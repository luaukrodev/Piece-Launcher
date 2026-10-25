using CommunityToolkit.Mvvm.ComponentModel;
using Piece_Launcher.Models;
using System.ComponentModel;
using System.Windows.Input;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class HomePageViewModel : ViewModelBase
    {
        private readonly ProfilesPageViewModel _profilesPage;

        [ObservableProperty]
        private ProfileEntry? _selectedProfile;

        public ICommand SignInMicrosoftCommand => _profilesPage.AddMicrosoftProfileCommand;
        public string? SignInStatusMessage => _profilesPage.StatusMessage;
        public bool HasSignInStatusMessage => _profilesPage.HasStatusMessage;

        public HomePageViewModel(ProfileEntry? selectedProfile, ProfilesPageViewModel profilesPage)
        {
            _selectedProfile = selectedProfile;
            _profilesPage = profilesPage;
            _profilesPage.PropertyChanged += OnProfilesPagePropertyChanged;
        }
        
        public void UpdateProfile(ProfileEntry? profile)
        {
            SelectedProfile = profile;
        }

        private void OnProfilesPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProfilesPageViewModel.StatusMessage))
            {
                OnPropertyChanged(nameof(SignInStatusMessage));
                OnPropertyChanged(nameof(HasSignInStatusMessage));
            }
        }
    }
}
