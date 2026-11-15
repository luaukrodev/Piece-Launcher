using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Helpers;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class JavaEditionPageViewModel : ViewModelBase
    {
        private static readonly Regex ReleaseVersionPattern = new(@"^\d+\.\d+(?:\.\d+)?$", RegexOptions.Compiled);
        private static readonly Bitmap? JavaThumbnailAsset = LoadThumbnail("Version Images/Minecraft Java.png");
        private static readonly Bitmap? GenericThumbnailAsset = LoadThumbnail("Version Images/Generic.png");

        private readonly ProfilesPageViewModel _profilesPage;
        private readonly LauncherConfig _config;
        private readonly Action<ProfileEntry?> _selectProfile;
        private readonly Action<string?> _selectVersion;
        private readonly ICommand _launchGameCommand;
        private readonly ISkinService _skinService;
        private readonly ILoadingService _loadingService;
        private readonly AutoDismissMessage _launchStatusAutoDismiss = new();
        private CancellationTokenSource? _skinLoadCancellation;

        [ObservableProperty]
        private ProfileEntry? _selectedProfile;

        [ObservableProperty]
        private string? _selectedVersion;

        [ObservableProperty]
        private Bitmap? _skinBitmap;

        [ObservableProperty]
        private bool _isSkinLoading = true;

        [ObservableProperty]
        private bool _isLoadingVersions;

        [ObservableProperty]
        private bool _isLaunchingGame;

        [ObservableProperty]
        private bool _isGameRunning;

        [ObservableProperty]
        private string? _launchStatusMessage;

        [ObservableProperty]
        private bool _isGenericThumbnailActive;

        [ObservableProperty]
        private bool _isProfilePopupOpen;

        [ObservableProperty]
        private bool _isVersionPopupOpen;

        public ObservableCollection<ProfileEntry> UserProfiles { get; }
        public ObservableCollection<InstalledVersionOption> InstalledVersions { get; }
        public Bitmap? JavaThumbnail => JavaThumbnailAsset;
        public Bitmap? GenericThumbnail => GenericThumbnailAsset;
        public double JavaThumbnailOpacity => IsGenericThumbnailActive ? 0 : 1;
        public double GenericThumbnailOpacity => IsGenericThumbnailActive ? 1 : 0;
        public double SkinOpacity => IsSkinLoading ? 0 : 1;
        public bool HasSelectedProfile => SelectedProfile != null;
        public bool HasSelectedVersion => !string.IsNullOrWhiteSpace(SelectedVersion);
        public string SelectedVersionDisplayName => _config.GetVersionDisplayName(SelectedVersion);
        public bool CanPlay => HasSelectedProfile && HasSelectedVersion && !IsLoadingVersions
            && !IsLaunchingGame && !IsGameRunning;
        public string LaunchButtonText => LocalizationHelper.Get(
            IsLaunchingGame ? "Lang_Status_Launching"
            : IsGameRunning ? "Lang_Status_GameStarted"
            : "Lang_Global_Play");
        public bool HasLaunchStatusMessage => !string.IsNullOrWhiteSpace(LaunchStatusMessage);
        public string? SelectedProfileType => SelectedProfile?.Type switch
        {
            ProfileType.Microsoft => "Online",
            ProfileType.Offline => "Offline",
            _ => null
        };

        public ICommand SignInMicrosoftCommand => _profilesPage.AddMicrosoftProfileCommand;
        public ICommand LaunchGameCommand => _launchGameCommand;
        public string? SignInStatusMessage => _profilesPage.StatusMessage;
        public bool HasSignInStatusMessage => _profilesPage.HasStatusMessage;
        public Task InitialSkinLoadTask { get; }

        public JavaEditionPageViewModel(
            ProfileEntry? selectedProfile,
            ProfilesPageViewModel profilesPage,
            ObservableCollection<ProfileEntry> userProfiles,
            ObservableCollection<InstalledVersionOption> installedVersions,
            Action<ProfileEntry?> selectProfile,
            Action<string?> selectVersion,
            ICommand launchGameCommand,
            ISkinService skinService,
            ILoadingService loadingService,
            LauncherConfig config)
        {
            _selectedProfile = selectedProfile;
            _profilesPage = profilesPage;
            _config = config;
            UserProfiles = userProfiles;
            InstalledVersions = installedVersions;
            _selectProfile = selectProfile;
            _selectVersion = selectVersion;
            _launchGameCommand = launchGameCommand;
            _skinService = skinService;
            _loadingService = loadingService;
            _profilesPage.PropertyChanged += OnProfilesPagePropertyChanged;
            InitialSkinLoadTask = LoadSkinForProfileAsync(selectedProfile);
        }

        partial void OnSelectedProfileChanged(ProfileEntry? value)
        {
            OnPropertyChanged(nameof(HasSelectedProfile));
            OnPropertyChanged(nameof(SelectedProfileType));
            OnPropertyChanged(nameof(CanPlay));
            _ = LoadSkinForProfileAsync(value);
        }

        partial void OnSelectedVersionChanged(string? value)
        {
            IsGenericThumbnailActive = !string.IsNullOrWhiteSpace(value) && !IsReleaseVersion(value);
            OnPropertyChanged(nameof(SelectedVersionDisplayName));
            OnPropertyChanged(nameof(HasSelectedVersion));
            OnPropertyChanged(nameof(CanPlay));
        }

        partial void OnIsGenericThumbnailActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(JavaThumbnailOpacity));
            OnPropertyChanged(nameof(GenericThumbnailOpacity));
        }

        partial void OnIsSkinLoadingChanged(bool value)
            => OnPropertyChanged(nameof(SkinOpacity));

        partial void OnIsLoadingVersionsChanged(bool value)
            => OnPropertyChanged(nameof(CanPlay));

        partial void OnIsLaunchingGameChanged(bool value)
            => OnLaunchStateChanged();

        partial void OnIsGameRunningChanged(bool value)
            => OnLaunchStateChanged();

        partial void OnLaunchStatusMessageChanged(string? value)
        {
            OnPropertyChanged(nameof(HasLaunchStatusMessage));
            _launchStatusAutoDismiss.Schedule(
                value,
                () => LaunchStatusMessage,
                () => LaunchStatusMessage = null);
        }

        [RelayCommand]
        private void ToggleProfilePopup()
        {
            IsVersionPopupOpen = false;
            IsProfilePopupOpen = !IsProfilePopupOpen;
        }

        [RelayCommand]
        private void ToggleVersionPopup()
        {
            IsProfilePopupOpen = false;
            IsVersionPopupOpen = !IsVersionPopupOpen;
        }

        [RelayCommand]
        private void SelectProfile(ProfileEntry? profile)
            => ChooseProfile(profile);

        public void ChooseProfile(ProfileEntry? profile)
        {
            _selectProfile(profile);
            IsProfilePopupOpen = false;
        }

        [RelayCommand]
        private void SelectVersion(string? version)
            => ChooseVersion(version);

        public void ChooseVersion(string? versionId)
        {
            _selectVersion(versionId);
            IsVersionPopupOpen = false;
        }

        public void UpdateProfile(ProfileEntry? profile)
        {
            if (ReferenceEquals(SelectedProfile, profile))
                return;

            SelectedProfile = profile;
        }

        public void ReloadSelectedProfileSkin()
        {
            if (SelectedProfile is null)
            {
                _ = LoadSkinForProfileAsync(null);
                return;
            }

            _skinService.InvalidateCache(SelectedProfile.Id);
            _ = LoadSkinForProfileAsync(SelectedProfile);
        }

        public void UpdateSelectedVersion(string? version)
            => SelectedVersion = version;

        public void RefreshVersionDisplayNames()
        {
            foreach (var version in InstalledVersions)
                version.RefreshDisplayName(_config.GetVersionDisplayName(version.VersionId));
            OnPropertyChanged(nameof(SelectedVersionDisplayName));
        }

        public void UpdateLoadingVersions(bool value)
            => IsLoadingVersions = value;

        public void UpdateLaunchState(bool isLaunching, bool isRunning)
        {
            IsLaunchingGame = isLaunching;
            IsGameRunning = isRunning;
        }

        public void UpdateLaunchStatus(string? message)
            => LaunchStatusMessage = message;

        public void RefreshLaunchStateText()
            => OnPropertyChanged(nameof(LaunchButtonText));

        private void OnLaunchStateChanged()
        {
            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(LaunchButtonText));
        }

        private async Task LoadSkinForProfileAsync(ProfileEntry? profile)
        {
            _skinLoadCancellation?.Cancel();
            _skinLoadCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            _skinLoadCancellation = cancellation;
            IsSkinLoading = true;
            SkinBitmap = null;

            using var loading = _loadingService.Begin();
            try
            {
                var skin = profile is null
                    ? SkinTextureService.GetDefaultSkin()
                    : await _skinService.GetSkinForProfileAsync(profile, cancellation.Token);

                if (!cancellation.IsCancellationRequested)
                    SkinBitmap = skin;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"Could not load skin for the selected profile: {exception}");
                if (!cancellation.IsCancellationRequested)
                    SkinBitmap = SkinTextureService.GetDefaultSkin();
            }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                    IsSkinLoading = false;
            }
        }

        private void OnProfilesPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProfilesPageViewModel.StatusMessage))
            {
                OnPropertyChanged(nameof(SignInStatusMessage));
                OnPropertyChanged(nameof(HasSignInStatusMessage));
            }
        }

        private static bool IsReleaseVersion(string version)
        {
            var copyMarker = version.IndexOf("-copy-", StringComparison.OrdinalIgnoreCase);
            var baseVersion = copyMarker >= 0 ? version[..copyMarker] : version;
            return ReleaseVersionPattern.IsMatch(baseVersion);
        }

        private static Bitmap? LoadThumbnail(string assetPath)
        {
            try
            {
                var uri = new Uri($"avares://Piece Launcher/Assets/{assetPath}");
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"Thumbnail asset is unavailable at '{assetPath}': {exception.Message}");
                return null;
            }
        }
    }
}
