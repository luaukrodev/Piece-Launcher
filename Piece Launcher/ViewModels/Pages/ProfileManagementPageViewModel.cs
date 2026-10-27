using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using Piece_Launcher.Helpers;
using Piece_Launcher.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class ProfileManagementPageViewModel : ViewModelBase
    {
        private readonly ProfileEntry _profile;
        private readonly Action<ProfileEntry> _saveProfile;
        private readonly Action<ProfileEntry> _removeProfile;
        private readonly Action _goBack;
        private byte[]? _pendingSkin;

        public bool IsMicrosoftProfile => _profile.Type == ProfileType.Microsoft;
        public bool IsOfflineProfile => _profile.Type == ProfileType.Offline;
        public string ProfileId => _profile.Id;
        public bool CanSave => IsOfflineProfile && !string.IsNullOrWhiteSpace(ProfileName);
        public bool CanSaveSkin => IsOfflineProfile && HasPendingSkin;
        public bool HasSkinError => !string.IsNullOrWhiteSpace(SkinError);

        [ObservableProperty]
        private string _profileName;

        [ObservableProperty]
        private Bitmap _skinBitmap;

        [ObservableProperty]
        private Bitmap _headImage;

        [ObservableProperty]
        private bool _hasPendingSkin;

        [ObservableProperty]
        private bool _isLoading = true;

        [ObservableProperty]
        private string? _skinError;

        partial void OnProfileNameChanged(string value)
        {
            OnPropertyChanged(nameof(CanSave));
        }

        partial void OnHasPendingSkinChanged(bool value)
        {
            OnPropertyChanged(nameof(CanSaveSkin));
        }

        partial void OnSkinBitmapChanged(Bitmap value)
        {
            HeadImage = SkinTextureService.CreateHeadImage(value);
        }

        partial void OnSkinErrorChanged(string? value)
        {
            OnPropertyChanged(nameof(HasSkinError));
        }

        public ProfileManagementPageViewModel(
            ProfileEntry profile,
            Action<ProfileEntry> saveProfile,
            Action<ProfileEntry> removeProfile,
            Action goBack)
        {
            _profile = profile;
            _saveProfile = saveProfile;
            _removeProfile = removeProfile;
            _goBack = goBack;
            _profileName = profile.Name;
            _skinBitmap = SkinTextureService.GetDefaultSkin();
            _headImage = SkinTextureService.CreateHeadImage(_skinBitmap);
            _ = LoadProfileSkinAsync();
        }

        [RelayCommand]
        private void Save()
        {
            if (!IsOfflineProfile)
                return;

            var name = ProfileName.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            _profile.Name = name;
            _saveProfile(_profile);
            _goBack();
        }

        public void PreviewSkin(byte[] png)
        {
            _pendingSkin = null;
            HasPendingSkin = false;
            try
            {
                if (png.Length > 1024 * 1024)
                    throw new InvalidDataException("Minecraft skin PNG files must be 1 MB or smaller.");

                var skin = SkinTextureService.DecodeSkin(png);
                _pendingSkin = png;
                SkinBitmap = skin;
                HasPendingSkin = true;
                SkinError = null;
            }
            catch (Exception ex)
            {
                SkinError = ex.Message;
            }
        }

        [RelayCommand]
        private async Task SaveSkin()
        {
            if (!IsOfflineProfile || _pendingSkin == null)
                return;

            try
            {
                var path = SkinTextureService.GetSavedSkinPath(_profile.Id);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, _pendingSkin);

                _profile.SkinPath = path;
                _profile.HeadImage = SkinTextureService.CreateHeadImage(SkinBitmap);
                _saveProfile(_profile);
                _pendingSkin = null;
                HasPendingSkin = false;
                SkinError = null;
            }
            catch (Exception ex)
            {
                SkinError = ex.Message;
            }
        }

        public void ShowSkinError(string message)
        {
            SkinError = message;
        }

        [RelayCommand]
        private void Cancel()
        {
            _goBack();
        }

        [RelayCommand]
        private void RemoveProfile()
        {
            _removeProfile(_profile);
            _goBack();
        }

        private async Task LoadProfileSkinAsync()
        {
            try
            {
                SkinBitmap = await SkinTextureService.LoadProfileSkinAsync(_profile);
            }
            catch (Exception ex)
            {
                SkinBitmap = SkinTextureService.GetDefaultSkin();
                SkinError = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
