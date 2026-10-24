using CommunityToolkit.Mvvm.ComponentModel;
using Piece_Launcher.Models;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class HomePageViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ProfileEntry? _selectedProfile;

        // In a real scenario, this would likely share state or be passed in
        public HomePageViewModel(ProfileEntry? selectedProfile)
        {
            _selectedProfile = selectedProfile;
        }
        
        public void UpdateProfile(ProfileEntry? profile)
        {
            SelectedProfile = profile;
        }
    }
}
