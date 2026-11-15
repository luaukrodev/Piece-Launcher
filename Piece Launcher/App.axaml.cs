using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Piece_Launcher.ViewModels;
using Piece_Launcher.Views;
using Piece_Launcher.Models;
using Piece_Launcher.Services.Impl;
using DotNetEnv;
using System;
using System.Diagnostics;
using System.Linq;

namespace Piece_Launcher
{
    public partial class App : Application
    {
        private TrayIcon? _trayIcon;

        public override void Initialize()
        {
            Env.Load();
            AvaloniaXamlLoader.Load(this);
            _trayIcon = TrayIcon.GetIcons(this)?.FirstOrDefault();
            if (_trayIcon is null)
                return;

            _trayIcon.Clicked += OnTrayIconClicked;
            var menuItems = _trayIcon.Menu?.Items.OfType<NativeMenuItem>().ToArray();
            if (menuItems is { Length: > 0 })
                menuItems[0].Click += OnTrayOpenClicked;
            if (menuItems is { Length: > 1 })
                menuItems[1].Click += OnTrayExitClicked;
        }

        internal bool TryHideInTray(Window window)
        {
            if (!OperatingSystem.IsWindows() || _trayIcon is null)
                return false;

            _trayIcon.IsVisible = true;
            window.Hide();
            return true;
        }

        private void OnTrayIconClicked(object? sender, EventArgs e)
            => RestoreMainWindowFromTray();

        private void OnTrayOpenClicked(object? sender, EventArgs e)
            => RestoreMainWindowFromTray();

        private void OnTrayExitClicked(object? sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }

        private void RestoreMainWindowFromTray()
        {
            if (_trayIcon is null
                || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
                || desktop.MainWindow is not { } mainWindow)
                return;

            _trayIcon.IsVisible = false;
            mainWindow.Show();
            if (mainWindow.WindowState == WindowState.Minimized)
                mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var startupConfig = LauncherConfig.Load();
                new Services.Impl.AvaloniaLanguageService().ApplyLanguage(startupConfig.Language);

                var minecraft = new CmlLib.Core.MinecraftLauncher();
                var versionService = new CmlLibVersionService(minecraft);
                var updateService = new Services.Impl.LauncherUpdateService();
                var startupVm = new StartupWindowViewModel(
                    updateService,
                    startupConfig.CheckLauncherUpdates,
                    startupConfig,
                    versionService,
                    new MicrosoftAuthService());
                var startupWindow = new StartupWindow
                {
                    DataContext = startupVm,
                };

                var startupCompleted = false;
                startupVm.Completed += () =>
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        if (startupCompleted)
                            return;

                        startupCompleted = true;
                        try
                        {
                            var mainWindow = new MainWindow
                            {
                                DataContext = new MainWindowViewModel(),
                            };

                            await startupWindow.TransitionToMainWindowAsync(mainWindow);
                            desktop.MainWindow = mainWindow;
                            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                            mainWindow.Show();
                            startupWindow.Activate();
                            await startupWindow.FadeOutAndCloseAsync();
                            mainWindow.Activate();
                        }
                        catch (Exception exception)
                        {
                            Trace.WriteLine($"Unable to open the main launcher window: {exception}");
                            startupWindow.CancelTransition();
                            startupVm.ShowStartupFailure(exception);
                        }
                    });
                };

                desktop.MainWindow = startupWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
