using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Helpers;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class ModDetailPageViewModel : ViewModelBase
    {
        private readonly IExploreService _exploreService;
        private readonly string _gameDirectory;
        private readonly Action _onBack;
        private readonly Action _onValidationFailed;
        private readonly Func<string?>? _getSelectedVersionName;
        private readonly ILoadingService _loadingService = ServiceLocator.LoadingService;
        private readonly AutoDismissMessage _errorMessageAutoDismiss = new();

        [ObservableProperty] private ExploreProjectDetail? _project;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _isInstallingContent;
        [ObservableProperty] private string? _errorMessage;
        [ObservableProperty] private string _supportedLoadersText = "";

        partial void OnErrorMessageChanged(string? value)
        {
            if (IsLoading || IsInstallingContent)
            {
                _errorMessageAutoDismiss.Cancel();
                return;
            }

            _errorMessageAutoDismiss.Schedule(value, () => ErrorMessage, () => ErrorMessage = null);
        }

        partial void OnIsLoadingChanged(bool value)
            => RefreshErrorMessageDismissal();

        partial void OnIsInstallingContentChanged(bool value)
            => RefreshErrorMessageDismissal();

        private void RefreshErrorMessageDismissal()
        {
            if (IsLoading || IsInstallingContent)
            {
                _errorMessageAutoDismiss.Cancel();
                return;
            }

            _errorMessageAutoDismiss.Schedule(ErrorMessage, () => ErrorMessage, () => ErrorMessage = null);
        }

        public bool HasSupportedLoaders => !string.IsNullOrWhiteSpace(SupportedLoadersText);

        partial void OnSupportedLoadersTextChanged(string value)
        {
            OnPropertyChanged(nameof(HasSupportedLoaders));
        }

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
            string gameDirectory,
            Action onBack,
            Action onValidationFailed,
            Func<string?>? getSelectedVersionName = null)
        {
            _exploreService = exploreService;
            _gameDirectory = gameDirectory;
            _onBack = onBack;
            _onValidationFailed = onValidationFailed;
            _getSelectedVersionName = getSelectedVersionName;
        }

        public async Task LoadProjectAsync(string projectId)
        {
            using var loading = _loadingService.Begin();
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
                ErrorMessage = LocalizationHelper.Format("Lang_ModDetail_LoadProjectFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadVersionsAsync(string projectId)
        {
            using var loading = _loadingService.Begin();
            Versions.Clear();
            SupportedLoadersText = "";
            try
            {
                var target = ExploreInstallHelper.ParseVersionName(_getSelectedVersionName?.Invoke());
                var versions = await _exploreService.GetProjectVersionsAsync(projectId, target.GameVersion);

                SupportedLoadersText = string.Join(", ", versions
                    .SelectMany(version => version.Loaders)
                    .Where(loader => !string.IsNullOrWhiteSpace(loader))
                    .Select(FormatLoaderName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(loader => loader, StringComparer.OrdinalIgnoreCase));

                foreach (var version in versions.Where(version =>
                    ExploreInstallHelper.IsCompatible(version, target, Project?.ProjectType)))
                {
                    Versions.Add(version);
                }

                if (Versions.Count == 0)
                    ErrorMessage = LocalizationHelper.Get("Lang_ModDetail_NoCompatibleVersion");
            }
            catch (Exception ex)
            {
                ErrorMessage = LocalizationHelper.Format("Lang_ModDetail_LoadVersionsFailed", ex.Message);
            }
        }

        private static string FormatLoaderName(string loader) => loader.ToLowerInvariant() switch
        {
            "neoforge" => "NeoForge",
            "fabric" => "Fabric",
            "forge" => "Forge",
            "quilt" => "Quilt",
            _ => loader
        };

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
                ErrorMessage = LocalizationHelper.Get("Lang_ModDetail_NoCompatibleVersion");
                return;
            }

            var target = ExploreInstallHelper.ParseVersionName(_getSelectedVersionName?.Invoke());
            var best = ExploreInstallHelper.PickBest(Versions, target, Project?.ProjectType);

            if (best == null)
            {
                ErrorMessage = LocalizationHelper.Get("Lang_ModDetail_NoCompatibleVersion");
                _onValidationFailed?.Invoke();
                return;
            }

            await InstallVersion(best);
        }

        [RelayCommand]
        private async Task InstallVersion(ExploreProjectVersion version)
        {
            var target = ExploreInstallHelper.ParseVersionName(_getSelectedVersionName?.Invoke());
            if (!ExploreInstallHelper.IsCompatible(version, target, Project?.ProjectType))
            {
                ErrorMessage = LocalizationHelper.Get("Lang_ModDetail_NoCompatibleVersion");
                _onValidationFailed?.Invoke();
                return;
            }

            using var loading = _loadingService.Begin();
            IsInstallingContent = true;
            ErrorMessage = LocalizationHelper.Format("Lang_ModDetail_Installing", version.Name);
            try
            {
                string targetFolder = ExploreInstallHelper.GetTargetFolder(_gameDirectory, Project?.ProjectType);
                Directory.CreateDirectory(targetFolder);

                string destPath = Path.Combine(targetFolder, ExploreInstallHelper.SafeFileName(version.Filename));
                var temporaryPath = Path.Combine(targetFolder, $".{Guid.NewGuid():N}.download");
                try
                {
                    await _exploreService.DownloadFileAsync(version.DownloadUrl, temporaryPath);
                    File.Move(temporaryPath, destPath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }

                var successMessage = LocalizationHelper.Format("Lang_ModDetail_Installed", version.Filename);
                ErrorMessage = successMessage;
            }
            catch (Exception ex)
            {
                ErrorMessage = LocalizationHelper.Format("Lang_ModDetail_InstallFailed", ex.Message);
            }
            finally
            {
                IsInstallingContent = false;
            }
        }

        [RelayCommand]
        private void SetSelectedTabIndex(string indexStr)
        {
            if (int.TryParse(indexStr, out var index))
                SelectedTabIndex = index;
        }
    }
}
