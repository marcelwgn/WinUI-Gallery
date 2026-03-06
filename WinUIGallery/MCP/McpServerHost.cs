// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace WinUIGallery.MCP;

public sealed class McpServerHost : IAsyncDisposable
{
    public const int Port = 5174;

    private WebApplication? _app;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new()
                {
                    Name = "WinUI Gallery",
                    Version = "1.0.0"
                };
                options.ServerInstructions =
                    "This MCP server is hosted inside a running WinUI Gallery application instance. " +
                    "IMPORTANT: Do NOT close and reopen the application between tool calls. " +
                    "The app must remain running for this MCP connection to stay alive. " +
                    "Reuse this single session for all interactions — closing the app terminates the MCP server.";
            })
            .WithHttpTransport()
            .WithToolsFromAssembly();

        _app = builder.Build();
        _app.Urls.Add($"http://localhost:{Port}");
        _app.MapMcp();
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
