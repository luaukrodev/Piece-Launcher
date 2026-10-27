using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Helpers;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using System.Collections.ObjectModel;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class ProfilesPageViewModel : ViewModelBase
    {
        private readonly LauncherConfig _config;
        private readonly IMicrosoftAuthService _microsoftAuthService;
        
        public ObservableCollection<ProfileEntry> UserProfiles { get; }

        [ObservableProperty]
        private ProfileEntry? _selectedProfile;

        [ObservableProperty]
        private bool _showAddProfileForm;

        [ObservableProperty]
        private string _newProfileName = "Steve";

        [ObservableProperty]
        private string? _statusMessage;

        [ObservableProperty]
        private bool _isLoggingIn;

        [ObservableProperty]
        private bool _isLoadingProfiles;

        private int _pendingSkinLoads;

        public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);
        
        // This relies on the fact that the collection reference doesn't change, 
        // just its contents. We need to raise property changed when it's empty.
        public bool HasNoProfiles => UserProfiles.Count == 0;

        public event EventHandler<ProfileEntry?>? ProfileSelected;
        public event EventHandler<ProfileEntry>? ManageProfileRequested;
        public ProfilesPageViewModel(
            LauncherConfig config,
            ObservableCollection<ProfileEntry> sharedProfiles,
            ProfileEntry? currentSelected,
            IMicrosoftAuthService microsoftAuthService)
        {
            _config = config;
            _microsoftAuthService = microsoftAuthService;
            UserProfiles = sharedProfiles;
            _selectedProfile = currentSelected;

            UserProfiles.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasNoProfiles));
                SaveProfiles();
            };

            foreach (var profile in UserProfiles)
            {
                profile.HeadImage = SkinTextureService.CreateHeadImage(SkinTextureService.GetDefaultSkin());
                LoadHeadImage(profile);
            }
        }

        partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatusMessage));

        partial void OnSelectedProfileChanged(ProfileEntry? value)
        {
            ProfileSelected?.Invoke(this, value);
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
            profile.HeadImage = SkinTextureService.CreateHeadImage(SkinTextureService.GetDefaultSkin());
            UserProfiles.Add(profile);
            LoadHeadImage(profile);
            SelectedProfile = profile;
            ShowAddProfileForm = false;
        }

        [RelayCommand]
        private async Task AddMicrosoftProfile()
        {
            IsLoggingIn = true;
            StatusMessage = "Connecting to Microsoft...";
            try
            {
                var session = await _microsoftAuthService.SignInAsync();
                if (string.IsNullOrWhiteSpace(session.UUID) || string.IsNullOrWhiteSpace(session.Username))
                    throw new InvalidOperationException("Microsoft authentication returned an incomplete Minecraft profile.");

                var profile = UserProfiles.FirstOrDefault(candidate =>
                    candidate.Type == ProfileType.Microsoft &&
                    string.Equals(candidate.Id, session.UUID, StringComparison.OrdinalIgnoreCase));

                if (profile == null)
                {
                    profile = new ProfileEntry
                    {
                        Id = session.UUID,
                        Name = session.Username,
                        Type = ProfileType.Microsoft
                    };
                    profile.HeadImage = SkinTextureService.CreateHeadImage(SkinTextureService.GetDefaultSkin());
                    UserProfiles.Add(profile);
                    LoadHeadImage(profile);
                }

                SelectedProfile = profile;
                ShowAddProfileForm = false;
                StatusMessage = "Signed in successfully.";
                _ = ClearStatusMessageAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Microsoft sign-in failed: {ex.Message}";
            }
            finally
            {
                IsLoggingIn = false;
            }
        }

        [RelayCommand]
        private void RemoveProfile(ProfileEntry profile)
        {
            DeleteProfile(profile);
        }

        public void DeleteProfile(ProfileEntry profile)
        {
            UserProfiles.Remove(profile);
            if (SelectedProfile == profile)
            {
                SelectedProfile = UserProfiles.FirstOrDefault();
            }
        }

        [RelayCommand]
        private void ManageProfile(ProfileEntry profile)
        {
            ManageProfileRequested?.Invoke(this, profile);
        }

        public void SaveProfile(ProfileEntry profile)
        {
            if (!UserProfiles.Contains(profile))
                return;

            SaveProfiles();
        }

        private void SaveProfiles()
        {
            _config.Profiles = UserProfiles.ToList();
            _config.Save();
        }

        private async Task ClearStatusMessageAsync()
        {
            await Task.Delay(3000);
            if (StatusMessage == "Signed in successfully.")
                StatusMessage = null;
        }

        private void LoadHeadImage(ProfileEntry profile)
        {
            _pendingSkinLoads++;
            IsLoadingProfiles = true;
            _ = LoadHeadImageAsync(profile);
        }

        private async Task LoadHeadImageAsync(ProfileEntry profile)
        {
            try
            {
                var skin = await SkinTextureService.LoadProfileSkinAsync(profile);
                profile.HeadImage = SkinTextureService.CreateHeadImage(skin);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not load skin for profile '{profile.Name}': {ex.Message}");
                profile.HeadImage = SkinTextureService.CreateHeadImage(SkinTextureService.GetDefaultSkin());
            }
            finally
            {
                _pendingSkinLoads--;
                IsLoadingProfiles = _pendingSkinLoads > 0;
            }
        }
    }
}
