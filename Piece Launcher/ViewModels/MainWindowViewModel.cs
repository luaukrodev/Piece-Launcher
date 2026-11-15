using CmlLib.Core;
using CmlLib.Core.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Models;
using Piece_Launcher.Helpers;
using Piece_Launcher.Services;
using Piece_Launcher.ViewModels.Pages;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Piece_Launcher.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private static readonly TimeSpan GameWindowDetectionTimeout = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan GameWindowCheckInterval = TimeSpan.FromMilliseconds(500);

        private readonly LauncherConfig _config;
        private readonly MinecraftLauncher _minecraft;
        private readonly IVersionService _versionService;
        private readonly Services.Impl.MicrosoftAuthService _microsoftAuthService;
        private bool _suppressDefaultLoadingSpinnerUntilIdle;
        private bool _isLaunchingGame;
        private bool _isGameRunning;
        private string? _selectedInstanceVersionId;

        public ObservableCollection<ProfileEntry> UserProfiles { get; } = new();
        public ObservableCollection<InstalledVersionOption> InstalledVersions { get; } = new();
        public event Action? MinimizeLauncherRequested;
        public string LauncherVersion { get; } =
            typeof(MainWindowViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                .Split('+')[0]
            ?? throw new InvalidOperationException("The launcher version was not configured.");

        public string FormattedLauncherVersion => LocalizationHelper.Get("Lang_Settings_Version") + " " + LauncherVersion;
        public bool MinimizeToTray => _config.MinimizeToTray;

        [ObservableProperty] private ProfileEntry? _selectedProfile;
        [ObservableProperty] private string? _selectedVersion;
        [ObservableProperty] private ViewModelBase _currentPage = null!;
        [ObservableProperty] private string? _statusMessage;
        [ObservableProperty] private bool _isLoadingVersions;
        [ObservableProperty] private bool _isLoggingIn;

        // Startup Cover (covers client area, min 900ms, fades out 800ms)
        [ObservableProperty] private bool _isStartupCoverVisible = true;
        [ObservableProperty] private double _startupCoverOpacity = 1.0;

        // Universal Loading Overlay (covers Main area only, 150ms in, 250ms out)
        [ObservableProperty] private bool _isLoadingOverlayActive = false;
        [ObservableProperty] private double _loadingOverlayOpacity = 0.0;

        public ILoadingService LoadingService => ServiceLocator.LoadingService;
        public bool IsDefaultLoadingSpinnerVisible => IsLoadingOverlayActive
            && !_versionsPage.IsInstallProgressVisible
            && !_suppressDefaultLoadingSpinnerUntilIdle;
        public bool IsVersionInstallProgressVisible => _versionsPage.IsInstalling && _versionsPage.IsInstallProgressVisible;
        public double VersionInstallProgressValue => _versionsPage.InstallProgressValue;
        public string? VersionInstallProgressText => _versionsPage.InstallProgressText;

        public bool CanPlay => SelectedProfile != null && !string.IsNullOrEmpty(SelectedVersion)
            && !IsLoadingVersions && !IsLoggingIn && !_isLaunchingGame && !_isGameRunning;

        public bool IsJavaEditionActive => CurrentPage is JavaEditionPageViewModel;
        public bool IsProfilesActive => CurrentPage is ProfilesPageViewModel || CurrentPage is ProfileManagementPageViewModel;
        public bool IsVersionsActive => CurrentPage is VersionsPageViewModel;
        public bool IsMinecraftModsActive => CurrentPage is MinecraftModsPageViewModel || CurrentPage is ModDetailPageViewModel;
        public bool IsCommunityActive => CurrentPage is CommunityPageViewModel;
        public bool IsSettingsActive => CurrentPage is SettingsPageViewModel;

        public string SelectedProfileType => SelectedProfile?.Type switch
        {
            ProfileType.Microsoft => "Online",
            ProfileType.Offline => "Offline",
            _ => "None"
        };
        // Navigation Commands
        public RelayCommand NavigateJavaEditionCommand { get; }
        public RelayCommand NavigateProfilesCommand { get; }
        public RelayCommand NavigateMinecraftModsCommand { get; }
        public RelayCommand NavigateCommunityCommand { get; }
        public RelayCommand NavigateVersionsCommand { get; }
        public RelayCommand NavigateSettingsCommand { get; }

        // Pages
        private readonly JavaEditionPageViewModel _javaEditionPage;
        private readonly ProfilesPageViewModel _profilesPage;
        private readonly VersionsPageViewModel _versionsPage;
        private readonly MinecraftModsPageViewModel _minecraftModsPage;
        private readonly CommunityPageViewModel _communityPage;
        private readonly SettingsPageViewModel _settingsPage;

        public MainWindowViewModel()
        {
            _config = LauncherConfig.Load();
            _minecraft = new MinecraftLauncher();
            _microsoftAuthService = new Services.Impl.MicrosoftAuthService();

            // In a real DI setup, these would be injected.
            var cmlVersionService = new Services.Impl.CmlLibVersionService(_minecraft);
            var modrinthExploreService = new Services.Impl.ModrinthExploreService();
            var curseForgeExploreService = new Services.Impl.CurseForgeExploreService();
            var avaloniaThemeService = new Services.Impl.AvaloniaThemeService();
            var avaloniaLanguageService = new Services.Impl.AvaloniaLanguageService();
            var forgeService = new Services.Impl.ForgeLoaderService(_minecraft);
            var fabricService = new Services.Impl.FabricLoaderService(_minecraft);
            var neoForgeService = new Services.Impl.NeoForgeLoaderService(_minecraft);
            var quiltService = new Services.Impl.QuiltLoaderService(_minecraft);

            ServiceLocator.Initialize(
                cmlVersionService, modrinthExploreService, curseForgeExploreService,
                avaloniaThemeService, avaloniaLanguageService, forgeService, fabricService, neoForgeService, quiltService);
            avaloniaLanguageService.ApplyLanguage(_config.Language);

            _versionService = cmlVersionService;

            // Load profiles
            foreach (var p in _config.Profiles)
                UserProfiles.Add(p);

            _selectedProfile = UserProfiles.FirstOrDefault(p => p.Id == _config.LastSelectedProfileId) ?? UserProfiles.FirstOrDefault();

            // Initialize pages
            _profilesPage = new ProfilesPageViewModel(_config, UserProfiles, _selectedProfile, _microsoftAuthService);
            _javaEditionPage = new JavaEditionPageViewModel(
                _selectedProfile,
                _profilesPage,
                UserProfiles,
                InstalledVersions,
                profile => SelectedProfile = profile,
                version => SelectedVersion = version,
                LaunchGameCommand,
                ServiceLocator.SkinService,
                LoadingService,
                _config);
            var versionManagerService = new Services.Impl.MinecraftVersionManagerService(_minecraft.MinecraftPath.Versions);
            _versionsPage = new VersionsPageViewModel(
                _versionService, versionManagerService, forgeService, fabricService, neoForgeService, quiltService, _config);
            _versionsPage.PropertyChanged += OnVersionsPagePropertyChanged;
            _versionsPage.VersionInstalled += () => _ = LoadInstalledVersionsAsync();
            _versionsPage.VersionSelected += name => SelectedVersion = name;
            _minecraftModsPage = new MinecraftModsPageViewModel(
                modrinthExploreService,
                curseForgeExploreService,
                _versionService,
                _config,
                _minecraft.MinecraftPath.BasePath,
                _minecraft.MinecraftPath);
            _minecraftModsPage.SelectedInstanceChanged += OnMinecraftInstanceSelected;
            _communityPage = new CommunityPageViewModel();
            _settingsPage = new SettingsPageViewModel(
                _config, avaloniaThemeService, avaloniaLanguageService,
                new Services.Impl.LauncherUpdateService(),
                _minecraft.MinecraftPath.BasePath,
                _minecraft.MinecraftPath,
                _minecraftModsPage.AddImportedInstance);
            _config.PropertyChanged += OnLauncherConfigPropertyChanged;

            // Apply initial theme and language
            avaloniaThemeService.ApplyTheme(_config.Theme);

            // Set initial page
            _currentPage = _config.StartupPage switch
            {
                "Profiles" => _profilesPage,
                "Versions" => _versionsPage,
                "Minecraft Mods" => _minecraftModsPage,
                "Discover" => _minecraftModsPage,
                "Community" => _communityPage,
                "Settings" => _settingsPage,
                "Minecraft: Java Edition" => _javaEditionPage,
                "Home" => _javaEditionPage,
                _ => _javaEditionPage
            };
            _currentPage.IsActive = true;

            // Event subscriptions
            _profilesPage.ProfileSelected += (s, profile) => SelectedProfile = profile;
            _profilesPage.ManageProfileRequested += (_, profile) =>
            {
                var previousPage = CurrentPage;
                NavigateTo(new ProfileManagementPageViewModel(
                    profile,
                    SaveProfileAndRefreshSkin,
                    _profilesPage.DeleteProfile,
                    () => NavigateTo(previousPage)));
            };

            NavigateJavaEditionCommand = new RelayCommand(() => NavigateTo(_javaEditionPage));
            NavigateProfilesCommand = new RelayCommand(() => NavigateTo(_profilesPage));
            NavigateMinecraftModsCommand = new RelayCommand(() => NavigateTo(_minecraftModsPage));
            NavigateCommunityCommand = new RelayCommand(() => NavigateTo(_communityPage));
            NavigateVersionsCommand = new RelayCommand(() => NavigateTo(_versionsPage));
            NavigateSettingsCommand = new RelayCommand(() => NavigateTo(_settingsPage));

            UpdateActiveTabProperties();
            LoadingService.PropertyChanged += OnLoadingServicePropertyChanged;
            var startupInitialization = Task.WhenAll(
                LoadInstalledVersionsAsync(),
                _javaEditionPage.InitialSkinLoadTask);
            _ = RunStartupCoverSequenceAsync(startupInitialization);
        }

        private async Task RunStartupCoverSequenceAsync(Task startupInitialization)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await startupInitialization;
            var remainingMinimumTime = 900 - (int)stopwatch.ElapsedMilliseconds;
            if (remainingMinimumTime > 0)
                await Task.Delay(remainingMinimumTime);

            StartupCoverOpacity = 0.0;
            await Task.Delay(800);
            IsStartupCoverVisible = false;
        }

        private async void OnLoadingServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ILoadingService.IsLoading))
            {
                if (LoadingService.IsLoading)
                {
                    IsLoadingOverlayActive = true;
                    LoadingOverlayOpacity = 1.0;
                }
                else
                {
                    LoadingOverlayOpacity = 0.0;
                    await Task.Delay(250);
                    if (!LoadingService.IsLoading)
                    {
                        _suppressDefaultLoadingSpinnerUntilIdle = false;
                        IsLoadingOverlayActive = false;
                    }
                }
            }
        }

        private void NavigateTo(ViewModelBase page)
        {
            if (CurrentPage == page) return;
            CurrentPage.IsActive = false;
            CurrentPage = page;
            CurrentPage.IsActive = true;
            UpdateActiveTabProperties();
        }

        private void UpdateActiveTabProperties()
        {
            OnPropertyChanged(nameof(IsJavaEditionActive));
            OnPropertyChanged(nameof(IsProfilesActive));
            OnPropertyChanged(nameof(IsVersionsActive));
            OnPropertyChanged(nameof(IsMinecraftModsActive));
            OnPropertyChanged(nameof(IsCommunityActive));
            OnPropertyChanged(nameof(IsSettingsActive));
        }

        private void OnLauncherConfigPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LauncherConfig.Language))
            {
                OnPropertyChanged(nameof(FormattedLauncherVersion));
                _javaEditionPage.RefreshLaunchStateText();
            }
            else if (e.PropertyName == nameof(LauncherConfig.VersionDisplayNames))
            {
                _javaEditionPage.RefreshVersionDisplayNames();
                _versionsPage.RefreshVersionDisplayNames();
            }
        }

        private void OnVersionsPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VersionsPageViewModel.IsInstallProgressVisible))
            {
                if (_versionsPage.IsInstallProgressVisible)
                    _suppressDefaultLoadingSpinnerUntilIdle = true;
                else if (!LoadingService.IsLoading)
                    _suppressDefaultLoadingSpinnerUntilIdle = false;
            }

            if (e.PropertyName is nameof(VersionsPageViewModel.IsInstalling)
                or nameof(VersionsPageViewModel.IsInstallProgressVisible)
                or nameof(VersionsPageViewModel.InstallProgressValue)
                or nameof(VersionsPageViewModel.InstallProgressText))
            {
                OnPropertyChanged(nameof(IsVersionInstallProgressVisible));
                OnPropertyChanged(nameof(IsDefaultLoadingSpinnerVisible));
                OnPropertyChanged(nameof(VersionInstallProgressValue));
                OnPropertyChanged(nameof(VersionInstallProgressText));
            }
        }

        partial void OnIsLoadingOverlayActiveChanged(bool value)
        {
            if (!value)
                _suppressDefaultLoadingSpinnerUntilIdle = false;
            OnPropertyChanged(nameof(IsDefaultLoadingSpinnerVisible));
        }

        partial void OnSelectedProfileChanged(ProfileEntry? value)
        {
            OnPropertyChanged(nameof(SelectedProfileType));
            OnPropertyChanged(nameof(CanPlay));
            _javaEditionPage.UpdateProfile(value);
            _config.LastSelectedProfileId = value?.Id;
            _config.Save();
        }

        partial void OnSelectedVersionChanged(string? value)
        {
            _javaEditionPage.UpdateSelectedVersion(value);
            _versionsPage.UpdateSelectedVersion(value);
            _minecraftModsPage.UpdateSelectedVersion(value);
            if (value != null && _minecraftModsPage.SelectedInstance?.IsDefault != false)
            {
                _config.LastSelectedVersion = value;
                _config.Save();
            }
            OnPropertyChanged(nameof(CanPlay));
        }

        private void SaveProfileAndRefreshSkin(ProfileEntry profile)
        {
            _profilesPage.SaveProfile(profile);
            ServiceLocator.SkinService.InvalidateCache(profile.Id);
            if (SelectedProfile?.Id == profile.Id)
                _javaEditionPage.ReloadSelectedProfileSkin();
        }

        public async Task LoadInstalledVersionsAsync()
        {
            IsLoadingVersions = true;
            _javaEditionPage.UpdateLoadingVersions(true);
            try
            {
                var installedNames = await _versionService.GetInstalledVersionNamesAsync();
                InstalledVersions.Clear();
                foreach (var name in installedNames)
                {
                    InstalledVersions.Add(new InstalledVersionOption(name, _config.GetVersionDisplayName(name)));
                }

                var selectedInstance = _minecraftModsPage.SelectedInstance;
                if (selectedInstance is { IsDefault: false, VersionId: { Length: > 0 } instanceVersion })
                {
                    if (!InstalledVersions.Any(version => string.Equals(version.VersionId, instanceVersion, StringComparison.OrdinalIgnoreCase)))
                    {
                        InstalledVersions.Add(new InstalledVersionOption(
                            instanceVersion,
                            _config.GetVersionDisplayName(instanceVersion)));
                        _selectedInstanceVersionId = instanceVersion;
                    }
                    SelectedVersion = instanceVersion;
                }
                else if (InstalledVersions.Any(version =>
                             string.Equals(version.VersionId, _config.LastSelectedVersion, StringComparison.OrdinalIgnoreCase)))
                {
                    SelectedVersion = _config.LastSelectedVersion;
                }
                else
                {
                    SelectedVersion = InstalledVersions.FirstOrDefault()?.VersionId;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_Status_LoadInstalledVersionsFailed", ex.Message);
            }
            finally
            {
                IsLoadingVersions = false;
                _javaEditionPage.UpdateLoadingVersions(false);
                OnPropertyChanged(nameof(CanPlay));
            }
        }

        [RelayCommand]
        private async Task LaunchGame()
        {
            if (_isLaunchingGame || _isGameRunning)
                return;

            if (SelectedProfile == null)
            {
                StatusMessage = LocalizationHelper.Get("Lang_Status_SelectProfile");
                _javaEditionPage.UpdateLaunchStatus(StatusMessage);
                return;
            }
            var selectedInstance = _minecraftModsPage.SelectedInstance;
            var versionToLaunch = selectedInstance is { IsDefault: false, VersionId: { Length: > 0 } instanceVersion }
                ? instanceVersion
                : SelectedVersion;
            if (string.IsNullOrEmpty(versionToLaunch))
            {
                StatusMessage = LocalizationHelper.Get("Lang_Status_SelectVersion");
                _javaEditionPage.UpdateLaunchStatus(StatusMessage);
                return;
            }

            _isLaunchingGame = true;
            UpdateGameLaunchState();
            _javaEditionPage.UpdateLaunchStatus(null);
            Process? process = null;
            var processStarted = false;
            var processMonitorStarted = false;
            try
            {
                StatusMessage = LocalizationHelper.Get("Lang_Status_Launching");
                var session = SelectedProfile.Type == ProfileType.Microsoft
                    ? await _microsoftAuthService.AuthenticateProfileAsync(SelectedProfile.Id)
                    : MSession.CreateOfflineSession(SelectedProfile.Name);
                var option = new CmlLib.Core.ProcessBuilder.MLaunchOption { Session = session };
                if (!_config.AutoDetectJava)
                {
                    if (string.IsNullOrWhiteSpace(_config.DefaultJava) || !File.Exists(_config.DefaultJava))
                        throw new FileNotFoundException(_settingsPage.InvalidJavaPathMessage, _config.DefaultJava);

                    option.JavaPath = _config.DefaultJava;
                }

                var systemRamGb = Math.Max(
                    1,
                    (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024 / 1024));
                var maximumRamGb = _config.AutoRam
                    ? Math.Min(_config.RecommendedRamGb, _config.SafeModeLimit ? Math.Max(1, systemRamGb / 2) : systemRamGb)
                    : Math.Min(_config.CustomRamGb, _config.SafeModeLimit ? Math.Max(1, systemRamGb / 2) : systemRamGb);
                option.MaximumRamMb = Math.Max(1, maximumRamGb) * 1024;
                option.ExtraJvmArguments = string.IsNullOrWhiteSpace(_config.JavaArguments)
                    ? Array.Empty<CmlLib.Core.ProcessBuilder.MArgument>()
                    : new[] { CmlLib.Core.ProcessBuilder.MArgument.FromCommandLine(_config.JavaArguments) };

                var instanceLauncher = selectedInstance is { IsDefault: false }
                    ? CreateInstanceLauncher(selectedInstance, versionToLaunch)
                    : _minecraft;
                option.Path = instanceLauncher.MinecraftPath;
                process = await instanceLauncher.InstallAndBuildProcessAsync(versionToLaunch, option);
                if (_config.ShowMinecraftConsole)
                    ConfigureMinecraftConsole(process);
                if (!process.Start())
                    throw new InvalidOperationException(LocalizationHelper.Get("Lang_Status_GameExitedEarly"));
                processStarted = true;

                var detectionDeadline = DateTime.UtcNow + GameWindowDetectionTimeout;
                while (DateTime.UtcNow < detectionDeadline)
                {
                    process.Refresh();
                    if (process.HasExited)
                        throw new InvalidOperationException(LocalizationHelper.Get("Lang_Status_GameExitedEarly"));

                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        _isLaunchingGame = false;
                        _isGameRunning = true;
                        UpdateGameLaunchState();
                        StatusMessage = null;
                        _javaEditionPage.UpdateLaunchStatus(null);
                        _ = MonitorGameProcessAsync(process);
                        processMonitorStarted = true;
                        process = null;
                        if (!_config.KeepOpenWhilePlaying)
                            MinimizeLauncherRequested?.Invoke();
                        return;
                    }

                    await Task.Delay(GameWindowCheckInterval);
                }

                throw new TimeoutException(LocalizationHelper.Get("Lang_Status_GameWindowTimeout"));
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_Status_LaunchFailed", ex.Message);
                _javaEditionPage.UpdateLaunchStatus(StatusMessage);
                if (process is not null && processStarted)
                {
                    var processToStop = process;
                    try
                    {
                        processToStop.Refresh();
                        if (!processToStop.HasExited)
                        {
                            processToStop.Kill(entireProcessTree: true);
                            _ = MonitorGameProcessAsync(processToStop);
                            processMonitorStarted = true;
                            process = null;
                        }
                    }
                    catch (Exception stopException)
                    {
                        Trace.WriteLine($"Could not stop the Minecraft process after launch failure: {stopException}");
                        _ = MonitorGameProcessAsync(processToStop);
                        processMonitorStarted = true;
                        process = null;
                    }
                }
            }
            finally
            {
                if (process is not null)
                    process.Dispose();

                if (!processMonitorStarted)
                {
                    _isLaunchingGame = false;
                    _isGameRunning = false;
                    UpdateGameLaunchState();
                }
            }
        }

        private void UpdateGameLaunchState()
        {
            _javaEditionPage.UpdateLaunchState(_isLaunchingGame, _isGameRunning);
            OnPropertyChanged(nameof(CanPlay));
        }

        private static void ConfigureMinecraftConsole(Process process)
        {
            if (!OperatingSystem.IsWindows())
                return;

            var javaPath = process.StartInfo.FileName;
            if (string.Equals(Path.GetFileName(javaPath), "javaw.exe", StringComparison.OrdinalIgnoreCase))
            {
                var javaDirectory = Path.GetDirectoryName(javaPath);
                var consoleJavaPath = string.IsNullOrWhiteSpace(javaDirectory)
                    ? "java.exe"
                    : Path.Combine(javaDirectory, "java.exe");
                if (!string.IsNullOrWhiteSpace(javaDirectory) && !File.Exists(consoleJavaPath))
                    throw new FileNotFoundException(
                        LocalizationHelper.Get("Lang_Status_ConsoleJavaMissing"),
                        consoleJavaPath);

                process.StartInfo.FileName = consoleJavaPath;
            }

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = false;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
        }

        private async Task MonitorGameProcessAsync(Process process)
        {
            var hasExited = false;
            try
            {
                await process.WaitForExitAsync();
                hasExited = true;
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"Could not monitor the Minecraft process: {exception}");
                try
                {
                    hasExited = process.HasExited;
                }
                catch (Exception stateException)
                {
                    Trace.WriteLine($"Could not determine whether the Minecraft process is still running: {stateException}");
                }
            }
            finally
            {
                if (hasExited)
                {
                    process.Dispose();
                    _isLaunchingGame = false;
                    _isGameRunning = false;
                    UpdateGameLaunchState();
                }
            }
        }

        private MinecraftLauncher CreateInstanceLauncher(MinecraftInstanceEntry instance, string versionId)
        {
            var path = MinecraftInstancePathFactory.Create(instance.GameDirectory, _minecraft.MinecraftPath);
            var sharedVersionMetadata = Path.Combine(path.Versions, versionId, $"{versionId}.json");
            var legacyVersionMetadata = Path.Combine(instance.GameDirectory, "versions", versionId, $"{versionId}.json");
            if (!File.Exists(sharedVersionMetadata) && File.Exists(legacyVersionMetadata))
                path.Versions = Path.Combine(instance.GameDirectory, "versions");

            return new MinecraftLauncher(path);
        }

        private void OnMinecraftInstanceSelected(MinecraftInstanceEntry? instance)
        {
            if (_selectedInstanceVersionId is not null)
            {
                var previousInstanceVersion = InstalledVersions.FirstOrDefault(version =>
                    string.Equals(version.VersionId, _selectedInstanceVersionId, StringComparison.OrdinalIgnoreCase));
                if (previousInstanceVersion is not null)
                    InstalledVersions.Remove(previousInstanceVersion);
                _selectedInstanceVersionId = null;
            }

            if (instance is { IsDefault: false, VersionId: { Length: > 0 } versionId })
            {
                if (!InstalledVersions.Any(version =>
                        string.Equals(version.VersionId, versionId, StringComparison.OrdinalIgnoreCase)))
                {
                    InstalledVersions.Add(new InstalledVersionOption(
                        versionId,
                        _config.GetVersionDisplayName(versionId)));
                    _selectedInstanceVersionId = versionId;
                }
                SelectedVersion = versionId;
                return;
            }

            SelectedVersion = InstalledVersions.Any(version =>
                    string.Equals(version.VersionId, _config.LastSelectedVersion, StringComparison.OrdinalIgnoreCase))
                ? _config.LastSelectedVersion
                : InstalledVersions.FirstOrDefault()?.VersionId;
        }
    }
}
