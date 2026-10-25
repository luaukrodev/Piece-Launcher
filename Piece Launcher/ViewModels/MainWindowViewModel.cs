using CmlLib.Core;
using CmlLib.Core.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using Piece_Launcher.ViewModels.Pages;
using System;
using System.Collections.ObjectModel;
using System.Linq;
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
        public RelayCommand NavigateDiscoverCommand { get; }
        public RelayCommand NavigateMapsCommand { get; }
        public RelayCommand NavigateVersionsCommand { get; }
        public RelayCommand NavigateSettingsCommand { get; }

        // Pages
        private readonly HomePageViewModel _homePage;
        private readonly ProfilesPageViewModel _profilesPage;
        private readonly VersionsPageViewModel _versionsPage;
        private readonly ExplorePageViewModel _explorePage;
        private readonly MapsPageViewModel _mapsPage;
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
            _versionsPage = new VersionsPageViewModel(_versionService, forgeService, fabricService, neoForgeService, quiltService);
            _versionsPage.VersionInstalled += () => _ = LoadInstalledVersionsAsync();
            _explorePage = new ExplorePageViewModel(modrinthExploreService, _config, _minecraft.MinecraftPath.BasePath);
            _mapsPage = new MapsPageViewModel();
            _settingsPage = new SettingsPageViewModel(_config, avaloniaThemeService, avaloniaLanguageService);

            // Apply initial theme and language
            avaloniaThemeService.ApplyTheme(_config.Theme);
            avaloniaLanguageService.ApplyLanguage(_config.Language);

            // Set initial page
            _currentPage = _config.StartupPage switch
            {
                "Profiles" => _profilesPage,
                "Versions" => _versionsPage,
                "Discover" => _explorePage,
                "Settings" => _settingsPage,
                _ => _homePage
            };
            _currentPage.IsActive = true;

            // Event subscriptions
            _profilesPage.ProfileSelected += (s, profile) => SelectedProfile = profile;

            NavigateHomeCommand = new RelayCommand(() => NavigateTo(_homePage));
            NavigateProfilesCommand = new RelayCommand(() => NavigateTo(_profilesPage));
            NavigateDiscoverCommand = new RelayCommand(() => NavigateTo(_explorePage));
            NavigateMapsCommand = new RelayCommand(() => NavigateTo(_mapsPage));
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
            _explorePage.UpdateSelectedVersion(value);
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
                var process = await _minecraft.InstallAndBuildProcessAsync(SelectedVersion, option);
                process.Start();
                StatusMessage = null;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Launch failed: {ex.Message}";
            }
        }
    }
}