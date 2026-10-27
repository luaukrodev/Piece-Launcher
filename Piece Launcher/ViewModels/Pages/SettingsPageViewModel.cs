using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using System;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using Velopack;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class SettingsPageViewModel : ViewModelBase
    {
        private readonly IThemeService _themeService;
        private readonly ILanguageService _languageService;
        private readonly IUpdateService _updateService;
        private UpdateInfo? _availableUpdate;

        [ObservableProperty] private bool _isCheckingForUpdates;
        [ObservableProperty] private bool _isDownloadingUpdate;
        [ObservableProperty] private bool _isUpToDate;
        [ObservableProperty] private bool _hasUpdateCheckFailed;
        [ObservableProperty] private bool _hasUpdate;
        [ObservableProperty] private string? _updateError;
        [ObservableProperty] private string? _availableUpdateVersion;
        [ObservableProperty] private int _updateDownloadProgress;

        public LauncherConfig Config { get; }
        public bool IsUpdaterInstalled => _updateService.IsInstalled;
        public string LauncherVersion { get; } =
            typeof(SettingsPageViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                .Split('+')[0]
            ?? throw new InvalidOperationException("The launcher version was not configured.");

        // Collections for ComboBox ItemsSource — plain strings so SelectedItem binding works
        public string[] AvailableLanguages { get; } = new[] { "Português", "English", "Español", "Français", "Deutsch" };
        public string[] AvailableThemes { get; } = new[] { "DarkOrange", "Light", "DarkPurple", "MidnightBlue", "ForestGreen", "CrimsonRed" };
        public string[] AvailableStartupPages { get; } = new[] { "Home", "Profiles", "Versions", "Discover" };

        public SettingsPageViewModel(
            LauncherConfig config,
            IThemeService themeService,
            ILanguageService languageService,
            IUpdateService updateService)
        {
            Config = config;
            _themeService = themeService;
            _languageService = languageService;
            _updateService = updateService;

            // React to property changes on Config so settings take effect immediately
            Config.PropertyChanged += OnConfigPropertyChanged;

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

            // Auto-save whenever any observable setting changes
            Config.Save();
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
            // Theme will be applied automatically via PropertyChanged handler
            Config.Save();
        }
    }
}
