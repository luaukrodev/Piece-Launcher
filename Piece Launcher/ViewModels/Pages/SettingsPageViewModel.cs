using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Win32;
using Velopack;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class SettingsPageViewModel : ViewModelBase
    {
        private readonly IThemeService _themeService;
        private readonly ILanguageService _languageService;
        private readonly IUpdateService _updateService;
        private UpdateInfo? _availableUpdate;
        private bool _launchOnStartupRegistered;

        [ObservableProperty] private bool _isCheckingForUpdates;
        [ObservableProperty] private bool _isDownloadingUpdate;
        [ObservableProperty] private bool _isUpToDate;
        [ObservableProperty] private bool _hasUpdateCheckFailed;
        [ObservableProperty] private bool _hasUpdate;
        [ObservableProperty] private string? _updateError;
        [ObservableProperty] private string? _availableUpdateVersion;
        [ObservableProperty] private int _updateDownloadProgress;
        [ObservableProperty] private string? _settingsError;

        public LauncherConfig Config { get; }
        public string GameDirectory { get; }
        public string InvalidJavaPathMessage => GetLocalizedText("Lang_Settings_InvalidJavaPath");
        public bool IsUpdaterInstalled => _updateService.IsInstalled;
        public int MaxCustomRamGb => Config.SafeModeLimit
            ? Math.Max(1, SystemRamGb / 2)
            : SystemRamGb;
        private int SystemRamGb => Math.Max(
            1,
            (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024 / 1024));
        public string LauncherVersion { get; } =
            typeof(SettingsPageViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                .Split('+')[0]
            ?? throw new InvalidOperationException("The launcher version was not configured.");

        // Collections for ComboBox ItemsSource — plain strings so SelectedItem binding works
        public string[] AvailableLanguages { get; } = new[] { "Português", "English", "Español", "Français", "Deutsch" };
        public string[] AvailableThemes { get; } = new[] { "DarkOrange", "Light", "DarkPurple", "MidnightBlue", "ForestGreen", "CrimsonRed" };
        public string[] AvailableStartupPages { get; } = new[] { "Home", "Profiles", "Versions", "Minecraft Mods", "Community" };
        public string[] AvailableDefaultLoaders { get; } = new[] { "Vanilla", "Fabric", "Forge", "NeoForge", "Quilt" };

        public SettingsPageViewModel(
            LauncherConfig config,
            IThemeService themeService,
            ILanguageService languageService,
            IUpdateService updateService,
            string gameDirectory)
        {
            Config = config;
            _themeService = themeService;
            _languageService = languageService;
            _updateService = updateService;
            GameDirectory = gameDirectory;

            if (Config.CustomRamGb > MaxCustomRamGb)
                Config.CustomRamGb = MaxCustomRamGb;

            // React to property changes on Config so settings take effect immediately
            Config.PropertyChanged += OnConfigPropertyChanged;
            try
            {
                ApplyLaunchOnStartup(Config.LaunchOnStartup);
                _launchOnStartupRegistered = Config.LaunchOnStartup;
            }
            catch (Exception ex)
            {
                SettingsError = ex.Message;
            }

            if (Config.CheckLauncherUpdates && IsUpdaterInstalled)
                _ = CheckForUpdatesAsync();
        }

        private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LauncherConfig.Theme))
            {
                _themeService.ApplyTheme(Config.Theme);
            }
            else if (e.PropertyName == nameof(LauncherConfig.Language))
            {
                _languageService.ApplyLanguage(Config.Language);
            }
            else if (e.PropertyName == nameof(LauncherConfig.CheckLauncherUpdates) && Config.CheckLauncherUpdates)
            {
                _ = CheckForUpdatesAsync();
            }
            else if (e.PropertyName == nameof(LauncherConfig.LaunchOnStartup))
            {
                try
                {
                    ApplyLaunchOnStartup(Config.LaunchOnStartup);
                    _launchOnStartupRegistered = Config.LaunchOnStartup;
                    SettingsError = null;
                }
                catch (Exception ex)
                {
                    SettingsError = ex.Message;
                    if (Config.LaunchOnStartup != _launchOnStartupRegistered)
                    {
                        Config.LaunchOnStartup = _launchOnStartupRegistered;
                        SettingsError = ex.Message;
                    }
                }
            }
            else if (e.PropertyName == nameof(LauncherConfig.SafeModeLimit))
            {
                OnPropertyChanged(nameof(MaxCustomRamGb));
                if (Config.CustomRamGb > MaxCustomRamGb)
                    Config.CustomRamGb = MaxCustomRamGb;
            }

            // Auto-save whenever any observable setting changes
            Config.Save();
        }

        private static void ApplyLaunchOnStartup(bool enabled)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException(GetLocalizedText("Lang_Settings_StartupWindowsOnly"));

            using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")
                ?? throw new InvalidOperationException(GetLocalizedText("Lang_Settings_StartupRegistryUnavailable"));

            if (enabled)
            {
                var executablePath = Environment.ProcessPath
                    ?? throw new InvalidOperationException(GetLocalizedText("Lang_Settings_LauncherPathUnavailable"));
                runKey.SetValue("Piece Launcher", $"\"{executablePath}\"");
            }
            else
            {
                runKey.DeleteValue("Piece Launcher", false);
            }
        }

        private static string GetLocalizedText(string key)
        {
            if (Application.Current is { } application
                && application.TryGetResource(key, null, out var resource)
                && resource is string text)
                return text;

            return key;
        }

        [RelayCommand]
        private void OpenGameDirectory()
        {
            OpenDirectory(GameDirectory);
        }

        [RelayCommand]
        private void OpenLauncherDataDirectory()
        {
            OpenDirectory(LauncherConfig.DataDirectory);
        }

        [RelayCommand]
        private void OpenExternalLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                SettingsError = null;
            }
            catch (Exception ex)
            {
                SettingsError = ex.Message;
            }
        }

        private void OpenDirectory(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                SettingsError = null;
            }
            catch (Exception ex)
            {
                SettingsError = ex.Message;
            }
        }

        [RelayCommand]
        private async Task CheckForUpdates()
        {
            await CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            if (!IsUpdaterInstalled || IsCheckingForUpdates || IsDownloadingUpdate)
                return;

            IsCheckingForUpdates = true;
            IsUpToDate = false;
            HasUpdateCheckFailed = false;
            HasUpdate = false;
            UpdateError = null;
            AvailableUpdateVersion = null;
            _availableUpdate = null;

            try
            {
                _availableUpdate = await _updateService.CheckForUpdatesAsync();
                if (_availableUpdate == null)
                {
                    IsUpToDate = true;
                    return;
                }

                AvailableUpdateVersion = _availableUpdate.TargetFullRelease.Version.ToString();
                HasUpdate = true;
            }
            catch (Exception ex)
            {
                UpdateError = ex.Message;
                HasUpdateCheckFailed = true;
            }
            finally
            {
                IsCheckingForUpdates = false;
            }
        }

        [RelayCommand]
        private async Task DownloadAndInstallUpdate()
        {
            if (_availableUpdate == null || IsDownloadingUpdate)
                return;

            IsDownloadingUpdate = true;
            HasUpdateCheckFailed = false;
            UpdateDownloadProgress = 0;

            try
            {
                var progress = new Progress<int>(value => UpdateDownloadProgress = value);
                await _updateService.DownloadAndRestartAsync(_availableUpdate, progress);
            }
            catch (Exception ex)
            {
                UpdateError = ex.Message;
                HasUpdateCheckFailed = true;
            }
            finally
            {
                IsDownloadingUpdate = false;
            }
        }

        [RelayCommand]
        private void SaveSettings()
        {
            Config.Save();
            _themeService.ApplyTheme(Config.Theme);
        }

        [RelayCommand]
        private void ResetToDefaults()
        {
            var newConfig = new LauncherConfig();
            Config.Language = newConfig.Language;
            Config.Theme = newConfig.Theme;
            Config.StartupPage = newConfig.StartupPage;
            Config.LaunchOnStartup = newConfig.LaunchOnStartup;
            Config.CheckLauncherUpdates = newConfig.CheckLauncherUpdates;
            Config.KeepOpenWhilePlaying = newConfig.KeepOpenWhilePlaying;
            Config.AutoDetectJava = newConfig.AutoDetectJava;
            Config.UseCustomJava = newConfig.UseCustomJava;
            Config.DefaultJava = newConfig.DefaultJava;
            Config.JavaArguments = newConfig.JavaArguments;
            Config.AutoRam = newConfig.AutoRam;
            Config.RecommendedRamGb = newConfig.RecommendedRamGb;
            Config.CustomRamGb = Math.Min(newConfig.CustomRamGb, MaxCustomRamGb);
            Config.SafeModeLimit = newConfig.SafeModeLimit;
            Config.ShowSnapshots = newConfig.ShowSnapshots;
            Config.ShowBetas = newConfig.ShowBetas;
            Config.ShowAlphas = newConfig.ShowAlphas;
            Config.ShowOldVersions = newConfig.ShowOldVersions;
            Config.DefaultLoader = newConfig.DefaultLoader;
            // Theme will be applied automatically via PropertyChanged handler
            Config.Save();
        }
    }
}
