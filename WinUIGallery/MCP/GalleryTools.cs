// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using WinUIGallery.Helpers;
using WinUIGallery.Pages;

namespace WinUIGallery.MCP;

[McpServerToolType]
public sealed class GalleryTools
{
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
}
