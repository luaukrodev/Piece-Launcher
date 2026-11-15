using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Helpers;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using Piece_Launcher.Services.Impl;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using CmlLib.Core;
using Microsoft.Win32;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class SettingsPageViewModel : ViewModelBase
    {
        private readonly IThemeService _themeService;
        private readonly ILanguageService _languageService;
        private readonly IUpdateService _updateService;
        private readonly MinecraftPath _sharedMinecraftPath;
        private readonly Action<MinecraftInstanceEntry> _onInstanceImported;
        private readonly LauncherDataImportService _launcherDataImportService = new();
        private readonly AutoDismissMessage _launcherImportStatusAutoDismiss = new();
        private readonly ILoadingService _loadingService = ServiceLocator.LoadingService;
        private LauncherUpdateResult? _availableUpdate;
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
        [ObservableProperty] private bool _isDiscoveringImportSources;
        [ObservableProperty] private bool _isImportingLauncherData;
        [ObservableProperty] private double _launcherImportProgress;
        [ObservableProperty] private string? _launcherImportStatus;
        public ObservableCollection<LauncherImportSource> ImportSources { get; } = new();
        public ObservableCollection<LauncherImportSource> SelectedImportSources { get; } = new();
        public bool HasImportSources => ImportSources.Count > 0;
        public bool HasSelectedImportSources => SelectedImportSources.Count > 0;

        partial void OnLauncherImportStatusChanged(string? value)
            => RefreshLauncherImportStatusDismissal();

        partial void OnIsImportingLauncherDataChanged(bool value)
            => RefreshLauncherImportStatusDismissal();

        private void RefreshLauncherImportStatusDismissal()
        {
            if (IsImportingLauncherData)
            {
                _launcherImportStatusAutoDismiss.Cancel();
                return;
            }

            _launcherImportStatusAutoDismiss.Schedule(
                LauncherImportStatus,
                () => LauncherImportStatus,
                () => LauncherImportStatus = null);
        }

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
        public string[] AvailableLanguages { get; } = new[]
        {
            "Português", "English", "Español", "Français", "Deutsch",
            "Italiano", "日本語", "简体中文", "한국어", "Русский"
        };
        public ObservableCollection<string> AvailableThemes { get; } = new();
        public string SelectedTheme
        {
            get
            {
                var theme = ThemeOptions.FirstOrDefault(option =>
                    string.Equals(option.Name, Config.Theme, StringComparison.OrdinalIgnoreCase));
                return GetLocalizedText(theme.ResourceKey ?? ThemeOptions[0].ResourceKey);
            }
            set
            {
                var theme = ThemeOptions.FirstOrDefault(option =>
                    string.Equals(GetLocalizedText(option.ResourceKey), value, StringComparison.CurrentCulture));
                if (theme.Name != null)
                    Config.Theme = theme.Name;
            }
        }
        private static readonly (string Name, string ResourceKey)[] ThemeOptions =
        {
            ("DarkOrange", "Lang_Settings_DarkOrangeTheme"),
            ("DarkPurple", "Lang_Settings_DarkPurpleTheme"),
            ("CrimsonRed", "Lang_Settings_CrimsonRedTheme"),
            ("ForestGreen", "Lang_Settings_ForestGreenTheme"),
            ("MidnightBlue", "Lang_Settings_MidnightBlueTheme"),
            ("OceanTeal", "Lang_Settings_OceanTealTheme"),
            ("Sakura", "Lang_Settings_SakuraTheme"),
            ("DesertSand", "Lang_Settings_DesertSandTheme"),
            ("Monochrome", "Lang_Settings_MonochromeTheme"),
            ("Light", "Lang_Settings_LightTheme")
        };
        private static readonly (string Value, string ResourceKey)[] StartupPages =
        {
            ("Minecraft: Java Edition", "Lang_Nav_JavaEdition"),
            ("Profiles", "Lang_Nav_Profiles"),
            ("Versions", "Lang_Nav_Versions"),
            ("Minecraft Mods", "Lang_Nav_MinecraftMods"),
            ("Community", "Lang_Nav_Community")
        };

        public ObservableCollection<string> AvailableStartupPages { get; } = new();
        public string SelectedStartupPage
        {
            get => GetLocalizedText(StartupPages.FirstOrDefault(page =>
                string.Equals(page.Value, Config.StartupPage, StringComparison.OrdinalIgnoreCase)).ResourceKey
                ?? StartupPages[0].ResourceKey);
            set
            {
                var option = StartupPages.FirstOrDefault(page =>
                    string.Equals(GetLocalizedText(page.ResourceKey), value, StringComparison.CurrentCulture));
                if (option.Value != null)
                    Config.StartupPage = option.Value;
            }
        }
        public string[] AvailableDefaultLoaders { get; } = new[] { "Vanilla", "Fabric", "Forge", "NeoForge", "Quilt" };

        public SettingsPageViewModel(
            LauncherConfig config,
            IThemeService themeService,
            ILanguageService languageService,
            IUpdateService updateService,
            string gameDirectory,
            MinecraftPath sharedMinecraftPath,
            Action<MinecraftInstanceEntry> onInstanceImported)
        {
            Config = config;
            _themeService = themeService;
            _languageService = languageService;
            _updateService = updateService;
            GameDirectory = gameDirectory;
            _sharedMinecraftPath = sharedMinecraftPath;
            _onInstanceImported = onInstanceImported;
            SelectedImportSources.CollectionChanged += (_, _) =>
                OnPropertyChanged(nameof(HasSelectedImportSources));
            RefreshStartupPages();

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
                OnPropertyChanged(nameof(SelectedTheme));
            }
            else if (e.PropertyName == nameof(LauncherConfig.Language))
            {
                _languageService.ApplyLanguage(Config.Language);
                RefreshStartupPages();
            }
            else if (e.PropertyName == nameof(LauncherConfig.StartupPage))
            {
                OnPropertyChanged(nameof(SelectedStartupPage));
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

        private void RefreshStartupPages()
        {
            AvailableStartupPages.Clear();
            foreach (var page in StartupPages)
                AvailableStartupPages.Add(GetLocalizedText(page.ResourceKey));

            AvailableThemes.Clear();
            foreach (var theme in ThemeOptions)
                AvailableThemes.Add(GetLocalizedText(theme.ResourceKey));

            OnPropertyChanged(nameof(SelectedStartupPage));
            OnPropertyChanged(nameof(SelectedTheme));
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

            using var loading = _loadingService.Begin();
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

                AvailableUpdateVersion = _availableUpdate.Version;
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

            using var loading = _loadingService.Begin();
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
        private async Task DiscoverLauncherInstalls()
        {
            if (IsDiscoveringImportSources || IsImportingLauncherData)
                return;

            IsDiscoveringImportSources = true;
            LauncherImportStatus = null;
            try
            {
                var sources = await Task.Run(_launcherDataImportService.DiscoverSources);
                ImportSources.Clear();
                SelectedImportSources.Clear();
                foreach (var source in sources)
                    ImportSources.Add(source);

                OnPropertyChanged(nameof(HasImportSources));
                LauncherImportStatus = sources.Count == 0
                    ? GetLocalizedText("Lang_Settings_NoImportSources")
                    : LocalizationHelper.Format("Lang_Settings_ImportSourcesFound", sources.Count);
            }
            catch (Exception exception)
            {
                LauncherImportStatus = LocalizationHelper.Format(
                    "Lang_Settings_ImportFailed",
                    exception.Message);
            }
            finally
            {
                IsDiscoveringImportSources = false;
            }
        }

        [RelayCommand]
        private async Task ImportSelectedLauncherData()
        {
            var sources = SelectedImportSources.ToArray();
            if (sources.Length == 0)
            {
                LauncherImportStatus = GetLocalizedText("Lang_Settings_SelectImportSource");
                return;
            }

            await ImportLauncherDataAsync(sources, showBatchSummary: sources.Length > 1);
        }

        [RelayCommand]
        private async Task ImportAllLauncherData()
        {
            await ImportLauncherDataAsync(ImportSources.ToArray(), showBatchSummary: true);
        }

        public void SetSelectedImportSources(IEnumerable<LauncherImportSource> sources)
        {
            SelectedImportSources.Clear();
            foreach (var source in sources)
                SelectedImportSources.Add(source);
        }

        public async Task ImportLauncherDataAsync(string folderPath)
        {
            try
            {
                var source = _launcherDataImportService.CreateSourceFromFolder(folderPath);
                await ImportLauncherDataAsync(new[] { source }, showBatchSummary: false);
            }
            catch (Exception exception)
            {
                LauncherImportStatus = LocalizationHelper.Format(
                    "Lang_Settings_ImportFailed",
                    exception.Message);
            }
        }

        public void ShowLauncherImportError(string message)
        {
            LauncherImportStatus = LocalizationHelper.Format("Lang_Settings_ImportFailed", message);
        }

        private async Task ImportLauncherDataAsync(
            IReadOnlyList<LauncherImportSource> sources,
            bool showBatchSummary)
        {
            if (sources.Count == 0)
            {
                LauncherImportStatus = GetLocalizedText("Lang_Settings_NoImportSources");
                return;
            }
            if (IsImportingLauncherData)
                return;

            IsImportingLauncherData = true;
            LauncherImportProgress = 0;
            LauncherImportStatus = GetLocalizedText("Lang_Settings_Importing");
            var failures = new List<string>();
            var importedCount = 0;
            var duplicateCount = 0;
            try
            {
                for (var index = 0; index < sources.Count; index++)
                {
                    var source = sources[index];
                    var sourceDirectory = Path.TrimEndingDirectorySeparator(
                        Path.GetFullPath(source.GameDirectory));
                    if (Config.MinecraftInstances.Any(instance =>
                            !string.IsNullOrWhiteSpace(instance.ImportSourceDirectory)
                            && string.Equals(
                                Path.TrimEndingDirectorySeparator(
                                    Path.GetFullPath(instance.ImportSourceDirectory)),
                                sourceDirectory,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        duplicateCount++;
                        continue;
                    }

                    LauncherImportProgress = 0;
                    LauncherImportStatus = showBatchSummary
                        ? LocalizationHelper.Format(
                            "Lang_Settings_ImportingSource",
                            index + 1,
                            sources.Count,
                            source.DisplayName)
                        : GetLocalizedText("Lang_Settings_Importing");
                    try
                    {
                        var progress = new Progress<InstallProgress>(value =>
                        {
                            LauncherImportProgress = value.Percentage;
                            LauncherImportStatus = value.CurrentFile is null
                                ? GetLocalizedText("Lang_Settings_Importing")
                                : LocalizationHelper.Format("Lang_Settings_ImportingFile", value.CurrentFile);
                        });
                        var instance = await _launcherDataImportService.ImportAsync(
                            source,
                            Config.GameDirectory,
                            progress);
                        Config.MinecraftInstances.Add(instance);
                        Config.Save();
                        _onInstanceImported(instance);
                        importedCount++;
                    }
                    catch (Exception exception)
                    {
                        failures.Add($"{source.DisplayName}: {exception.Message}");
                    }
                }

                LauncherImportProgress = 100;
                if (!showBatchSummary && duplicateCount > 0)
                {
                    LauncherImportStatus = GetLocalizedText("Lang_Settings_ImportAlreadyExists");
                }
                else if (showBatchSummary && (duplicateCount > 0 || failures.Count > 0))
                {
                    LauncherImportStatus = LocalizationHelper.Format(
                        "Lang_Settings_ImportBatchResult",
                        importedCount,
                        duplicateCount,
                        failures.Count,
                        string.Join(Environment.NewLine, failures));
                }
                else if (failures.Count > 0)
                {
                    LauncherImportStatus = LocalizationHelper.Format("Lang_Settings_ImportFailed", failures[0]);
                }
                else if (showBatchSummary)
                {
                    LauncherImportStatus = LocalizationHelper.Format(
                        "Lang_Settings_ImportAllComplete",
                        importedCount);
                }
                else
                {
                    LauncherImportStatus = LocalizationHelper.Format(
                        "Lang_Settings_ImportComplete",
                        sources[0].DisplayName);
                }
            }
            finally
            {
                IsImportingLauncherData = false;
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
            Config.MinimizeToTray = newConfig.MinimizeToTray;
            Config.AutoDetectJava = newConfig.AutoDetectJava;
            Config.UseCustomJava = newConfig.UseCustomJava;
            Config.DefaultJava = newConfig.DefaultJava;
            Config.JavaArguments = newConfig.JavaArguments;
            Config.ShowMinecraftConsole = newConfig.ShowMinecraftConsole;
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
