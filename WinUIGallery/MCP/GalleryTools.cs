// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ModelContextProtocol.Server;
using Windows.Storage;
using WinUIGallery.Controls;
using WinUIGallery.Helpers;
using WinUIGallery.Pages;

namespace WinUIGallery.MCP;

[McpServerToolType]
public sealed class GalleryTools
{
    private static readonly Regex SubstitutionPattern = new(@"\$\(([^\)]+)\)");

    [McpServerTool(Name = "list_samples")]
    [Description("Lists all available WinUI Gallery samples grouped by category. Returns sample metadata including UniqueId, Title, Description, and status flags.")]
    public async Task<string> ListSamples()
    {
        var groups = await ControlInfoDataSource.Instance.GetGroupsAsync();

        var samples = groups.SelectMany(group => group.Items.Select(item => new Dictionary<string, object>
        {
            ["UniqueId"] = item.UniqueId,
            ["Title"] = item.Title,
            ["Subtitle"] = item.Subtitle,
            ["Description"] = item.Description,
            ["Group"] = group.Title,
            ["IsNew"] = item.IsNew,
            ["IsUpdated"] = item.IsUpdated,
            ["IsPreview"] = item.IsPreview
        })).ToList();

        return JsonSerializer.Serialize(samples);
    }

    [McpServerTool(Name = "open_sample")]
    [Description("Opens a WinUI Gallery sample page by control name (UniqueId). Use list_samples to discover available control names.")]
    public async Task<string> OpenSample(
        [Description("The UniqueId of the control to open (e.g. \"Button\", \"TextBox\", \"NavigationView\").")] string controlName)
    {
        var item = await ControlInfoDataSource.GetItemAsync(controlName);
        if (item is null)
        {
            return $"Control '{controlName}' not found. Use list_samples to see available controls.";
        }

        var tcs = new TaskCompletionSource<bool>();

        App.MainWindow.dispatcherQueue.TryEnqueue(() =>
        {
            App.MainWindow.Navigate(typeof(ItemPage), item.UniqueId);
            tcs.SetResult(true);
        });

        await tcs.Task;
        return $"Navigated to '{item.Title}' sample page.";
    }

    [McpServerTool(Name = "list_page_samples")]
    [Description("Lists all code samples on a control's sample page. Returns each sample's header text. Use open_sample first, or this tool will navigate automatically.")]
    public async Task<string> ListPageSamples(
        [Description("The UniqueId of the control (e.g. \"Button\", \"TextBox\").")] string controlName)
    {
        var item = await ControlInfoDataSource.GetItemAsync(controlName);
        if (item is null)
        {
            return $"Control '{controlName}' not found. Use list_samples to see available controls.";
        }

        return await RunOnUIThreadAsync(async () =>
        {
            var examples = await NavigateAndFindExamplesAsync(item.UniqueId);

            var samples = examples.Select((ex, i) => new Dictionary<string, object>
            {
                ["index"] = i,
                ["headerText"] = ex.HeaderText ?? string.Empty
            }).ToList();

            return JsonSerializer.Serialize(samples);
        });
    }

    [McpServerTool(Name = "get_sample_code")]
    [Description("Gets the XAML and/or C# source code for a specific sample on a control page, identified by its header text.")]
    public async Task<string> GetSampleCode(
        [Description("The UniqueId of the control (e.g. \"Button\").")] string controlName,
        [Description("The header text of the sample to retrieve code for. Use list_page_samples to see available headers.")] string sampleHeader)
    {
        var item = await ControlInfoDataSource.GetItemAsync(controlName);
        if (item is null)
        {
            return $"Control '{controlName}' not found. Use list_samples to see available controls.";
        }

        return await RunOnUIThreadAsync(async () =>
        {
            var examples = await NavigateAndFindExamplesAsync(item.UniqueId);

            var target = examples.FirstOrDefault(ex =>
                string.Equals(ex.HeaderText, sampleHeader, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                return $"Sample with header '{sampleHeader}' not found on the '{item.Title}' page. Use list_page_samples to see available samples.";
            }

            string? xamlCode = await ResolveSourceCodeAsync(target.Xaml, target.XamlSource);
            string? csharpCode = await ResolveSourceCodeAsync(target.CSharp, target.CSharpSource);

            if (target.Substitutions is { Count: > 0 })
            {
                if (xamlCode is not null)
                {
                    xamlCode = ApplySubstitutions(xamlCode, target.Substitutions);
                }
                if (csharpCode is not null)
                {
                    csharpCode = ApplySubstitutions(csharpCode, target.Substitutions);
                }
            }

            var result = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(xamlCode))
            {
                result["xaml"] = xamlCode;
            }
            if (!string.IsNullOrEmpty(csharpCode))
            {
                result["csharp"] = csharpCode;
            }

            if (result.Count == 0)
            {
                return $"No source code available for sample '{sampleHeader}'.";
            }

            return JsonSerializer.Serialize(result);
        });
    }

    private static Task<T> RunOnUIThreadAsync<T>(Func<Task<T>> func)
    {
        var tcs = new TaskCompletionSource<T>();
        App.MainWindow.dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                tcs.SetResult(await func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private static async Task<List<ControlExample>> NavigateAndFindExamplesAsync(string uniqueId)
    {
        App.MainWindow.Navigate(typeof(ItemPage), uniqueId);

        // Poll until ControlExample instances appear in the visual tree
        List<ControlExample> examples = [];
        for (int attempt = 0; attempt < 50; attempt++)
        {
            await Task.Delay(100);
            examples = FindDescendants<ControlExample>(App.MainWindow.Content as DependencyObject).ToList();
            if (examples.Count > 0)
            {
                break;
            }
        }

        return examples;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }
            foreach (T descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static async Task<string?> ResolveSourceCodeAsync(string? inlineCode, string? sourceFile)
    {
        if (!string.IsNullOrEmpty(inlineCode))
        {
            return inlineCode.TrimStart('\n').TrimEnd();
        }

        if (string.IsNullOrEmpty(sourceFile) || !sourceFile.EndsWith("txt"))
        {
            return null;
        }

        try
        {
            StorageFile file;
            if (NativeMethods.IsAppPackaged)
            {
                Uri sourceUri = new(new Uri("ms-appx:///Samples/SampleCode/"), sourceFile);
                file = await StorageFile.GetFileFromApplicationUriAsync(sourceUri);
            }
            else
            {
                string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Samples", "SampleCode", sourceFile));
                file = await StorageFile.GetFileFromPathAsync(path);
            }

            string content = await FileIO.ReadTextAsync(file);
            return content.TrimStart('\n').TrimEnd();
        }
        catch
        {
            return null;
        }
    }

    private static string ApplySubstitutions(string code, IList<ControlExampleSubstitution> substitutions)
    {
        return SubstitutionPattern.Replace(code, match =>
        {
            foreach (ControlExampleSubstitution sub in substitutions)
            {
                if (sub.Key == match.Groups[1].Value)
                {
                    return sub.ValueAsString();
                }
            }
            return match.Value;
        });
    }
}
