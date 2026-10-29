using CmlLib.Core;
using CmlLib.Core.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using Piece_Launcher.ViewModels.Pages;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Piece_Launcher.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly LauncherConfig _config;
        private readonly MinecraftLauncher _minecraft;
        private readonly IVersionService _versionService;
        private readonly Services.Impl.MicrosoftAuthService _microsoftAuthService;

        public ObservableCollection<ProfileEntry> UserProfiles { get; } = new();
        public ObservableCollection<string> InstalledVersions { get; } = new();
        public event Action? MinimizeLauncherRequested;
        public string LauncherVersion { get; } =
            typeof(MainWindowViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                .Split('+')[0]
            ?? throw new InvalidOperationException("The launcher version was not configured.");

        [ObservableProperty] private ProfileEntry? _selectedProfile;
        [ObservableProperty] private string? _selectedVersion;
        [ObservableProperty] private ViewModelBase _currentPage = null!;
        [ObservableProperty] private string? _statusMessage;
        [ObservableProperty] private bool _isLoadingVersions;
        [ObservableProperty] private bool _isLoggingIn;

        public bool CanPlay => SelectedProfile != null && !string.IsNullOrEmpty(SelectedVersion) && !IsLoadingVersions && !IsLoggingIn;

        public string SelectedProfileType => SelectedProfile?.Type switch
        {
            ProfileType.Microsoft => "Online",
            ProfileType.Offline => "Offline",
            _ => "None"
        };
        // Navigation Commands
        public RelayCommand NavigateHomeCommand { get; }
        public RelayCommand NavigateProfilesCommand { get; }
        public RelayCommand NavigateMinecraftModsCommand { get; }
        public RelayCommand NavigateCommunityCommand { get; }
        public RelayCommand NavigateVersionsCommand { get; }
        public RelayCommand NavigateSettingsCommand { get; }

        // Pages
        private readonly HomePageViewModel _homePage;
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

            _versionService = cmlVersionService;

            // Load profiles
            foreach (var p in _config.Profiles)
                UserProfiles.Add(p);

            _selectedProfile = UserProfiles.FirstOrDefault(p => p.Id == _config.LastSelectedProfileId) ?? UserProfiles.FirstOrDefault();

            // Initialize pages
            _profilesPage = new ProfilesPageViewModel(_config, UserProfiles, _selectedProfile, _microsoftAuthService);
            _homePage = new HomePageViewModel(_selectedProfile, _profilesPage);
            _versionsPage = new VersionsPageViewModel(_versionService, forgeService, fabricService, neoForgeService, quiltService, _config);
            _versionsPage.VersionInstalled += () => _ = LoadInstalledVersionsAsync();
            _minecraftModsPage = new MinecraftModsPageViewModel(modrinthExploreService, _config, _minecraft.MinecraftPath.BasePath);
            _communityPage = new CommunityPageViewModel();
            _settingsPage = new SettingsPageViewModel(
                _config, avaloniaThemeService, avaloniaLanguageService,
                new Services.Impl.LauncherUpdateService(), _minecraft.MinecraftPath.BasePath);

            // Apply initial theme and language
            avaloniaThemeService.ApplyTheme(_config.Theme);
            avaloniaLanguageService.ApplyLanguage(_config.Language);

            // Set initial page
            _currentPage = _config.StartupPage switch
            {
                "Profiles" => _profilesPage,
                "Versions" => _versionsPage,
                "Minecraft Mods" => _minecraftModsPage,
                "Discover" => _minecraftModsPage,
                "Community" => _communityPage,
                "Settings" => _settingsPage,
                _ => _homePage
            };
            _currentPage.IsActive = true;

            // Event subscriptions
            _profilesPage.ProfileSelected += (s, profile) => SelectedProfile = profile;
            _profilesPage.ManageProfileRequested += (_, profile) =>
            {
                var previousPage = CurrentPage;
                NavigateTo(new ProfileManagementPageViewModel(
                    profile,
                    _profilesPage.SaveProfile,
                    _profilesPage.DeleteProfile,
                    () => NavigateTo(previousPage)));
            };

            NavigateHomeCommand = new RelayCommand(() => NavigateTo(_homePage));
            NavigateProfilesCommand = new RelayCommand(() => NavigateTo(_profilesPage));
            NavigateMinecraftModsCommand = new RelayCommand(() => NavigateTo(_minecraftModsPage));
            NavigateCommunityCommand = new RelayCommand(() => NavigateTo(_communityPage));
            NavigateVersionsCommand = new RelayCommand(() => NavigateTo(_versionsPage));
            NavigateSettingsCommand = new RelayCommand(() => NavigateTo(_settingsPage));

            _ = LoadInstalledVersionsAsync();
        }

        private void NavigateTo(ViewModelBase page)
        {
            if (CurrentPage == page) return;
            CurrentPage.IsActive = false;
            CurrentPage = page;
            CurrentPage.IsActive = true;
        }

        partial void OnSelectedProfileChanged(ProfileEntry? value)
        {
            OnPropertyChanged(nameof(SelectedProfileType));
            OnPropertyChanged(nameof(CanPlay));
            _homePage.UpdateProfile(value);
            _config.LastSelectedProfileId = value?.Id;
            _config.Save();
        }

        partial void OnSelectedVersionChanged(string? value)
        {
            _minecraftModsPage.UpdateSelectedVersion(value);
            if (value != null)
            {
                _config.LastSelectedVersion = value;
                _config.Save();
            }
            OnPropertyChanged(nameof(CanPlay));
        }

        public async Task LoadInstalledVersionsAsync()
        {
            IsLoadingVersions = true;
            try
            {
                var installedNames = await _versionService.GetInstalledVersionNamesAsync();
                InstalledVersions.Clear();
                foreach (var name in installedNames)
                {
                    InstalledVersions.Add(name);
                }

                if (InstalledVersions.Contains(_config.LastSelectedVersion ?? ""))
                {
                    SelectedVersion = _config.LastSelectedVersion;
                }
                else
                {
                    SelectedVersion = InstalledVersions.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not load installed versions: {ex.Message}";
            }
            finally
            {
                IsLoadingVersions = false;
                OnPropertyChanged(nameof(CanPlay));
            }
        }

        [RelayCommand]
        private async Task LaunchGame()
        {
            if (SelectedProfile == null) { StatusMessage = "Select a profile first."; return; }
            if (string.IsNullOrEmpty(SelectedVersion)) { StatusMessage = "Select a version first."; return; }

            try
            {
                StatusMessage = "Starting...";
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

                var process = await _minecraft.InstallAndBuildProcessAsync(SelectedVersion, option);
                process.Start();
                StatusMessage = null;
                if (!_config.KeepOpenWhilePlaying)
                    MinimizeLauncherRequested?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Launch failed: {ex.Message}";
            }
        }
    }
}