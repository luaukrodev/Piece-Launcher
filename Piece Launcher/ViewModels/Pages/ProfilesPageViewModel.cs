using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Models;
using System.Collections.ObjectModel;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class ProfilesPageViewModel : ViewModelBase
    {
        private readonly LauncherConfig _config;
        
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

        public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);
        
        // This relies on the fact that the collection reference doesn't change, 
        // just its contents. We need to raise property changed when it's empty.
        public bool HasNoProfiles => UserProfiles.Count == 0;

        public event EventHandler<ProfileEntry?>? ProfileSelected;

        public ProfilesPageViewModel(LauncherConfig config, ObservableCollection<ProfileEntry> sharedProfiles, ProfileEntry? currentSelected)
        {
            _config = config;
            UserProfiles = sharedProfiles;
            _selectedProfile = currentSelected;

            UserProfiles.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasNoProfiles));
        }

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
            UserProfiles.Add(profile);
            SelectedProfile = profile;
            ShowAddProfileForm = false;
        }

        [RelayCommand]
        private async Task AddMicrosoftProfile()
        {
            IsLoggingIn = true;
            StatusMessage = "Connecting to Microsoft... (Mocked in this viewmodel for now)";
            
            // In a real implementation, call Microsoft auth service
            await Task.Delay(2000);
            
            var profile = new ProfileEntry { Name = "OnlineUser", Type = ProfileType.Microsoft };
            UserProfiles.Add(profile);
            SelectedProfile = profile;
            ShowAddProfileForm = false;
            StatusMessage = "Signed in successfully.";
            
            IsLoggingIn = false;
            await Task.Delay(3000);
            StatusMessage = null;
        }

        [RelayCommand]
        private void RemoveProfile(ProfileEntry profile)
        {
            UserProfiles.Remove(profile);
            if (SelectedProfile == profile)
            {
                SelectedProfile = UserProfiles.FirstOrDefault();
            }
        }
    }
}
