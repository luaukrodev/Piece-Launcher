using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Helpers;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class ModDetailPageViewModel : ViewModelBase
    {
        private readonly IExploreService _exploreService;
        private readonly LauncherConfig _config;
        private readonly Action _onBack;
        private readonly Action _onValidationFailed;
        private readonly Func<string?>? _getSelectedVersionName;

        [ObservableProperty] private ExploreProjectDetail? _project;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string? _errorMessage;

        // Tab Management
        [ObservableProperty] private int _selectedTabIndex = 0;

        public bool IsDescriptionTab => SelectedTabIndex == 0;
        public bool IsGalleryTab => SelectedTabIndex == 1;
        public bool IsVersionsTab => SelectedTabIndex == 2;

        partial void OnSelectedTabIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsDescriptionTab));
            OnPropertyChanged(nameof(IsGalleryTab));
            OnPropertyChanged(nameof(IsVersionsTab));
        }

        public ObservableCollection<ExploreProjectVersion> Versions { get; } = new();
        public ObservableCollection<string> Gallery { get; } = new();

        public ModDetailPageViewModel(
            IExploreService exploreService,
            LauncherConfig config,
            Action onBack,
            Action onValidationFailed,
            Func<string?>? getSelectedVersionName = null)
        {
            _exploreService = exploreService;
            _config = config;
            _onBack = onBack;
            _onValidationFailed = onValidationFailed;
            _getSelectedVersionName = getSelectedVersionName;
        }

        public async Task LoadProjectAsync(string projectId)
        {
            IsLoading = true;
            ErrorMessage = null;
            try
            {
                Project = await _exploreService.GetProjectDetailAsync(projectId);

                Gallery.Clear();
                foreach (var image in Project.Gallery)
                {
                    Gallery.Add(image);
                }

                await LoadVersionsAsync(projectId);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to load project details: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadVersionsAsync(string projectId)
        {
            try
            {
                var versions = await _exploreService.GetProjectVersionsAsync(projectId);
                Versions.Clear();
                foreach (var v in versions)
                {
                    Versions.Add(v);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to load versions: {ex.Message}";
            }
        }

        [RelayCommand]
        private void GoBack()
        {
            _onBack?.Invoke();
        }

        [RelayCommand]
        private async Task InstallLatest()
        {
            if (Versions.Count == 0)
            {
                ErrorMessage = "No versions available to install.";
                return;
            }

            var target = ExploreInstallHelper.ParseVersionName(_getSelectedVersionName?.Invoke());
            var best = ExploreInstallHelper.PickBest(Versions, target, Project?.ProjectType);

            if (best == null)
            {
                ErrorMessage = "No compatible version found for the selected Minecraft version.";
                _onValidationFailed?.Invoke();
                return;
            }

            await InstallVersion(best);
        }

        [RelayCommand]
        private async Task InstallVersion(ExploreProjectVersion version)
        {
            ErrorMessage = $"Installing {version.Name}...";
            try
            {
                string targetFolder = ExploreInstallHelper.GetTargetFolder(_config, Project?.ProjectType);
                Directory.CreateDirectory(targetFolder);

                string destPath = Path.Combine(targetFolder, ExploreInstallHelper.SafeFileName(version.Filename));
                await _exploreService.DownloadFileAsync(version.DownloadUrl, destPath);

                ErrorMessage = $"Installed {version.Filename} successfully!";

                _ = ClearSuccessMessageAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to install: {ex.Message}";
            }
        }

        private async Task ClearSuccessMessageAsync()
        {
            await Task.Delay(3000);
            if (ErrorMessage?.Contains("successfully") == true)
                ErrorMessage = null;
        }

        [RelayCommand]
        private void SetSelectedTabIndex(string indexStr)
        {
            if (int.TryParse(indexStr, out var index))
                SelectedTabIndex = index;
        }
    }
}
