using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.ProcessBuilder;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.Msal.OAuth;

namespace Piece_Launcher.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly MinecraftLauncher _minecraft = new();
        private readonly LauncherConfig _config;
        private JELoginHandler? _loginHandler;

        // Profiles
        public ObservableCollection<ProfileEntry> UserProfiles { get; } = new();

        [ObservableProperty] private ProfileEntry? _selectedProfile;

        partial void OnSelectedProfileChanged(ProfileEntry? value)
        {
            OnPropertyChanged(nameof(SelectedProfileType));
            SaveConfig();
        }

        // Expose config to bindings
        public LauncherConfig Config => _config;

        private void SaveSettings()
        {
            _config.Save();
            StatusMessage = "Settings saved.";
        }

        public string SelectedProfileType => SelectedProfile?.Type switch
        {
            ProfileType.Microsoft => "Online",
            ProfileType.Offline => "Offline",
            _ => "None"
        };

        // View navigation
        [ObservableProperty] private bool _isHomeView = true;
        [ObservableProperty] private bool _isProfilesView;
        [ObservableProperty] private bool _isDiscoverView;
        [ObservableProperty] private bool _isMapsView;
        [ObservableProperty] private bool _isVersionsView;
        [ObservableProperty] private bool _isSettingsView;

        // Add-profile form
        [ObservableProperty] private bool _showAddProfileForm;
        [ObservableProperty] private string _newProfileName = "Steve";

        // Client ID (optional — custom Azure apps need Mojang API approval)
        [ObservableProperty] private string _clientIdInput = string.Empty;
        [ObservableProperty] private bool _useCustomClientId;
        [ObservableProperty] private bool _showCustomClientIdSettings;

        // Versions
        public ObservableCollection<string> Versions { get; } = new();

        [ObservableProperty] private string? _selectedVersion;

        partial void OnSelectedVersionChanged(string? value)
        {
            if (value is null) return;
            _config.LastSelectedVersion = value;
            _config.Save();
        }

        [ObservableProperty] private bool _isLoadingVersions;

        // Status
        [ObservableProperty] private bool _isLoggingIn;
        [ObservableProperty] private string? _statusMessage;

        partial void OnStatusMessageChanged(string? value) =>
            OnPropertyChanged(nameof(HasStatusMessage));

        public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);
        public bool HasNoProfiles => !UserProfiles.Any();

        // Constructor
        public MainWindowViewModel()
        {
            _config = LauncherConfig.Load();

            UserProfiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoProfiles));

            foreach (var p in _config.Profiles)
                UserProfiles.Add(p);

            SelectedProfile =
                UserProfiles.FirstOrDefault(p => p.Id == _config.LastSelectedProfileId)
                ?? UserProfiles.FirstOrDefault();

            ClientIdInput = _config.ClientId;
            UseCustomClientId = _config.UseCustomClientId;

            // Restore startup page
            switch (_config.StartupPage)
            {
                case "Home": IsHomeView = true; break;
                case "Profiles": IsProfilesView = true; break;
                case "Discover": IsDiscoverView = true; break;
                case "Maps": IsMapsView = true; break;
                case "Versions": IsVersionsView = true; break;
                case "Settings": IsSettingsView = true; break;
                default: IsHomeView = true; break;
            }

            _ = LoadVersionsAsync();

            // Initialize commands
            NavigateHomeCommand = new RelayCommand(() => NavigateHome());
            NavigateProfilesCommand = new RelayCommand(() => NavigateProfiles());
            NavigateDiscoverCommand = new RelayCommand(() => NavigateDiscover());
            NavigateMapsCommand = new RelayCommand(() => NavigateMaps());
            NavigateVersionsCommand = new RelayCommand(() => NavigateVersions());
            NavigateSettingsCommand = new RelayCommand(() => NavigateSettings());

            OpenModrinthCommand = new RelayCommand(() => OpenModrinth());
            OpenCurseForgeCommand = new RelayCommand(() => OpenCurseForge());
            OpenLabrinthCommand = new RelayCommand(() => OpenLabrinth());

            InstallVersionCommand = new RelayCommand<string>(async (v) => await InstallVersion(v));
            InstallLoaderCommand = new RelayCommand<string>(async (l) => await InstallLoader(l));

            SaveSettingsCommand = new RelayCommand(() => SaveSettings());
        }

        // Command properties declared and initialized in constructor

        // Helpers

        private string GetAuthClientId()
        {
            if (UseCustomClientId && !string.IsNullOrWhiteSpace(_config.ClientId))
                return _config.ClientId.Trim();
            return LauncherDefaults.MicrosoftClientId;
        }

        private static string FormatAuthError(Exception ex)
        {
            if (ex.Message.Contains("403", StringComparison.OrdinalIgnoreCase))
                return "Sign-in failed: this Azure app is not approved for Minecraft login. "
                     + "Disable custom Client ID in settings, or register your app at "
                     + "https://help.minecraft.net/hc/en-us/articles/16254801392141";
            return $"Sign-in failed: {ex.Message}";
        }

        private async Task<JELoginHandler> GetLoginHandlerAsync()
        {
            if (_loginHandler is not null) return _loginHandler;

            var app = await MsalClientHelper.BuildApplicationWithCache(GetAuthClientId());
            _loginHandler = new JELoginHandlerBuilder()
                .WithOAuthProvider(new MsalCodeFlowProvider(app))
                .Build();
            return _loginHandler;
        }

        private async Task<MSession> GetSessionAsync()
        {
            if (SelectedProfile is null)
                return MSession.CreateOfflineSession("Steve");

            if (SelectedProfile.Type == ProfileType.Offline)
                return MSession.CreateOfflineSession(SelectedProfile.Name);

            var handler = await GetLoginHandlerAsync();
            try { return await handler.AuthenticateSilently().ConfigureAwait(true); }
            catch { return await handler.AuthenticateInteractively().ConfigureAwait(true); }
        }

        private void SaveConfig()
        {
            _config.Profiles = UserProfiles.ToList();
            _config.LastSelectedProfileId = SelectedProfile?.Id;
            _config.Save();
        }

        private async Task LoadVersionsAsync()
        {
            IsLoadingVersions = true;
            StatusMessage = null;
            try
            {
                var versions = await _minecraft.GetAllVersionsAsync();
                Versions.Clear();
                foreach (var v in versions)
                    Versions.Add(v.Name);

                SelectedVersion = _config.LastSelectedVersion is not null
                    ? Versions.FirstOrDefault(v => v == _config.LastSelectedVersion)
                      ?? Versions.FirstOrDefault()
                    : Versions.FirstOrDefault();

                if (!Versions.Any())
                    StatusMessage = "No versions found. Check your internet connection.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not load versions: {ex.Message}";
            }
            finally
            {
                IsLoadingVersions = false;
            }
        }

        // Commands
        [RelayCommand]
        private async Task LaunchGame()
        {
            if (SelectedProfile is null) { StatusMessage = "Create a profile first."; return; }
            if (SelectedVersion is null) { StatusMessage = "Select a version first."; return; }

            try
            {
                StatusMessage = "Starting...";
                var session = await GetSessionAsync();
                var option = new MLaunchOption { Session = session };
                var process = await _minecraft.InstallAndBuildProcessAsync(SelectedVersion, option);
                process.Start();
                StatusMessage = null;
            }
            catch (Exception ex) { StatusMessage = $"Launch failed: {ex.Message}"; }
        }

        // Command properties (single declarations)
        public RelayCommand NavigateHomeCommand { get; private set; }
        public RelayCommand NavigateProfilesCommand { get; private set; }
        public RelayCommand NavigateDiscoverCommand { get; private set; }
        public RelayCommand NavigateMapsCommand { get; private set; }
        public RelayCommand NavigateVersionsCommand { get; private set; }
        public RelayCommand NavigateSettingsCommand { get; private set; }

        public RelayCommand OpenModrinthCommand { get; private set; }
        public RelayCommand OpenCurseForgeCommand { get; private set; }
        public RelayCommand OpenLabrinthCommand { get; private set; }
        public RelayCommand<string> InstallVersionCommand { get; private set; }
        public RelayCommand<string> InstallLoaderCommand { get; private set; }
        public RelayCommand SaveSettingsCommand { get; private set; }

        private void NavigateHome() { IsHomeView = true; IsProfilesView = false; IsDiscoverView = false; IsMapsView = false; IsVersionsView = false; IsSettingsView = false; }

        private void NavigateProfiles() { IsHomeView = false; IsProfilesView = true; IsDiscoverView = false; IsMapsView = false; IsVersionsView = false; IsSettingsView = false; }

        private void NavigateDiscover()
        {
            IsHomeView = false; IsProfilesView = false; IsDiscoverView = true; IsMapsView = false; IsVersionsView = false; IsSettingsView = false;
        }

        private void NavigateMaps()
        {
            IsHomeView = false; IsProfilesView = false; IsDiscoverView = false; IsMapsView = true; IsVersionsView = false; IsSettingsView = false;
        }

        private void NavigateVersions()
        {
            IsHomeView = false; IsProfilesView = false; IsDiscoverView = false; IsMapsView = false; IsVersionsView = true; IsSettingsView = false;
        }

        private void NavigateSettings()
        {
            IsHomeView = false; IsProfilesView = false; IsDiscoverView = false; IsMapsView = false; IsVersionsView = false; IsSettingsView = true;
        }

        private void OpenModrinth() { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://modrinth.com") { UseShellExecute = true }); }

        private void OpenCurseForge() { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.curseforge.com/minecraft/mc-mods") { UseShellExecute = true }); }

        private void OpenLabrinth() { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://labrinth.gg") { UseShellExecute = true }); }

        // Placeholder for installing versions and loaders
        private async Task InstallVersion(string version)
        {
            if (string.IsNullOrEmpty(version)) return;
            StatusMessage = $"Installing {version}...";
            await Task.Delay(1200);
            StatusMessage = null;
            await LoadVersionsAsync();
        }

        private async Task InstallLoader(string loader)
        {
            if (string.IsNullOrEmpty(loader)) return;
            StatusMessage = $"Installing loader {loader}...";
            await Task.Delay(1200);
            StatusMessage = null;
        }

        [RelayCommand]
        private void ToggleAddForm()
        {
            ShowAddProfileForm = !ShowAddProfileForm;
            if (ShowAddProfileForm) NewProfileName = "Steve";
        }

        [RelayCommand]
        private void AddOfflineProfile()
        {
            var name = NewProfileName.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var profile = new ProfileEntry { Name = name, Type = ProfileType.Offline };
            UserProfiles.Add(profile);
            SelectedProfile = profile;
            ShowAddProfileForm = false;
            SaveConfig();
        }

        [RelayCommand]
        private async Task AddMicrosoftProfile()
        {
            if (UseCustomClientId && string.IsNullOrWhiteSpace(_config.ClientId))
            {
                StatusMessage = "Enter your Azure Client ID first, or disable custom Client ID.";
                return;
            }

            IsLoggingIn = true;
            StatusMessage = "Connecting to Microsoft...";
            try
            {
                var handler = await GetLoginHandlerAsync();
                MSession session;
                try { session = await handler.AuthenticateSilently().ConfigureAwait(true); }
                catch { session = await handler.AuthenticateInteractively().ConfigureAwait(true); }

                var old = UserProfiles.FirstOrDefault(p => p.Type == ProfileType.Microsoft);
                if (old is not null) UserProfiles.Remove(old);

                var profile = new ProfileEntry { Name = session.Username, Type = ProfileType.Microsoft };
                UserProfiles.Add(profile);
                SelectedProfile = profile;
                ShowAddProfileForm = false;
                StatusMessage = $"Signed in as {session.Username}";
                SaveConfig();
                await Task.Delay(5000);
                StatusMessage = null;
            }
            catch (OperationCanceledException) { StatusMessage = "Sign-in cancelled."; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AddMicrosoftProfile] {ex}");
                StatusMessage = FormatAuthError(ex);
            }
            finally { IsLoggingIn = false; }
        }

        [RelayCommand]
        private void RemoveProfile(ProfileEntry profile)
        {
            UserProfiles.Remove(profile);
            if (SelectedProfile == profile)
                SelectedProfile = UserProfiles.FirstOrDefault();
            SaveConfig();
        }
    }
}