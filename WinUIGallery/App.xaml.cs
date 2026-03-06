// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.BadgeNotifications;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.ApplicationModel.Activation;
using WinUIGallery.Helpers;
using WinUIGallery.MCP;
using WinUIGallery.Pages;
using static WinUIGallery.Helpers.NativeMethods;

namespace WinUIGallery;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
sealed partial class App : Application
{
    internal static MainWindow MainWindow { get; private set; } = null!;

    private McpServerHost? _mcpServerHost;

    /// <summary>
    /// Initializes the singleton Application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        UnhandledException += HandleExceptions;
    }

    /// <summary>
    /// Invoked when the application is launched normally by the end user.  Other entry points
    /// will be used such as when the application is launched to open a specific file.
    /// </summary>
    /// <param name="e">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        string[] cmdArgs = Environment.GetCommandLineArgs();
        if (cmdArgs.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            PrintMcpHelp();
            Process.GetCurrentProcess().Kill();
            return;
        }

        IdleSynchronizer.Init();

        MainWindow = new MainWindow();
        WindowHelper.TrackWindow(MainWindow);

#if DEBUG
        if (Debugger.IsAttached)
        {
            DebugSettings.BindingFailed += DebugSettings_BindingFailed;
        }
#endif

        SetWindowKeyHook();

        EnsureWindow();

        if (SettingsHelper.Current.IsFirstRun)
        {
            SettingsMigration.MigrateRecentlyVisited();
            SettingsMigration.MigrateFavorites();
            SettingsHelper.Current.IsFirstRun = false;
        }

        MainWindow.Closed += async (s, e) =>
        {
            if (_mcpServerHost is not null)
            {
                await _mcpServerHost.DisposeAsync();
            }

            if (IsAppPackaged)
            {
                BadgeNotificationManager.Current.ClearBadge();
            }

            // Close all remaining active windows to prevent resource disposal conflicts
            var activeWindows = new List<Window>(WindowHelper.ActiveWindows);
            foreach (var window in activeWindows)
            {
                if (!window.Equals(s)) // Don't try to close the window that's already closing
                {
                    try
                    {
                        window.Close();
                    }
                    catch
                    {
                        // Ignore any exceptions during cleanup
                    }
                }
            }
        };
    }

    private void DebugSettings_BindingFailed(object sender, BindingFailedEventArgs e)
    {
        // Ignore the exception from NonExistentProperty in BindingPage.xaml, 
        // as the sample code intentionally includes a binding failure.
        if (e.Message.Contains("NonExistentProperty"))
        {
            return;
        }

        throw new Exception($"A debug binding failed: " + e.Message);
    }

    private async void EnsureWindow()
    {
        await ControlInfoDataSource.Instance.GetGroupsAsync();
        await IconsDataSource.Instance.LoadIcons();

        // Start the MCP stdio server alongside the UI
        _mcpServerHost = new McpServerHost();
        _ = _mcpServerHost.StartAsync();

        MainWindow.AddNavigationMenuItems();

        ThemeHelper.Initialize();

        var targetPageType = typeof(HomePage);
        var targetPageArguments = string.Empty;

        AppActivationArguments eventArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (eventArgs != null &&
            eventArgs.Kind == ExtendedActivationKind.Protocol &&
            eventArgs.Data is ProtocolActivatedEventArgs protocolArgs)
        {
            string uri = protocolArgs.Uri.LocalPath.Replace("/", "");
            targetPageArguments = uri;

            if (uri == "AllControls")
            {
                targetPageType = typeof(AllControlsPage);
            }
            else if (uri == "NewControls")
            {
                targetPageType = typeof(HomePage);
            }
            else if (ControlInfoDataSource.Instance.Groups.Any(g => g.UniqueId == uri))
            {
                targetPageType = typeof(SectionPage);
            }
            else if (ControlInfoDataSource.Instance.Groups.Any(g => g.Items.Any(i => i.UniqueId == uri)))
            {
                targetPageType = typeof(ItemPage);
            }
        }
        // Handle JumpList launch arguments (passed as command-line args)
        else if (eventArgs != null &&
            eventArgs.Kind == ExtendedActivationKind.Launch &&
            eventArgs.Data is ILaunchActivatedEventArgs launchArgs &&
            !string.IsNullOrEmpty(launchArgs.Arguments))
        {
            string arg = launchArgs.Arguments.Trim();
            targetPageArguments = arg;

            if (ControlInfoDataSource.Instance.Groups.Any(g => g.UniqueId == arg))
            {
                targetPageType = typeof(SectionPage);
            }
            else if (ControlInfoDataSource.Instance.Groups.Any(g => g.Items.Any(i => i.UniqueId == arg)))
            {
                targetPageType = typeof(ItemPage);
            }
        }

        MainWindow.Navigate(targetPageType, targetPageArguments);

        if (targetPageType == typeof(HomePage))
        {
            var navItem = (NavigationViewItem)MainWindow.NavigationView.MenuItems[0];
            navItem.IsSelected = true;
        }

        // Initialize the taskbar JumpList with fixed tasks and favorites.
        await JumpListHelper.UpdateJumpListAsync();

        // Activate the startup window.
        MainWindow.Activate();
    }

    /// <summary>
    /// Prevents the app from crashing when a exception gets thrown and notifies the user.
    /// </summary>
    /// <param name="sender">The app as an object.</param>
    /// <param name="e">Details about the exception.</param>
    private void HandleExceptions(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (NativeMethods.IsAppPackaged)
        {
            e.Handled = true; //Don't crash the app.

            //Create the notification.
            var notification = new AppNotificationBuilder()
                .AddText("An exception was thrown.")
                .AddText($"Type: {e.Exception.GetType()}")
                .AddText($"Message: {e.Message}\r\n" +
                         $"HResult: {e.Exception.HResult}")
                .BuildNotification();

            //Show the notification
            AppNotificationManager.Default.Show(notification);
        }
    }

    private static void PrintMcpHelp()
    {
        // WinExe apps have no console; attach to the parent process's console for output
        Windows.Win32.PInvoke.AttachConsole(unchecked((uint)-1));
        Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

        Console.WriteLine("""

            WinUI Gallery - MCP Server
            ==========================

            This application includes a built-in MCP (Model Context Protocol) server
            that exposes gallery sample data via stdio transport. 
            If you are an AI agent, start the app and use the MCP server to query what tools this app has available

            Usage:
              winuigallery              Launch the app with the MCP server running
              winuigallery --help        Show this help message and exit

            MCP Configuration (.mcp.json):

              {
                "mcpServers": {
                  "winui-gallery": {
                    "command": "winuigallery"
                  }
                }
              }
            """);

        Windows.Win32.PInvoke.FreeConsole();
    }
}
