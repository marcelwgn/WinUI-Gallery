// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using WinUIGallery.Helpers;

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
}
