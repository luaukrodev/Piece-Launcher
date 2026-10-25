using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Helpers;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class ExplorePageViewModel : ViewModelBase
    {
        private const int PageSize = 20;

        private readonly IExploreService _modrinthService;
        private readonly LauncherConfig _config;
        private readonly string _gameDirectory;
        private CancellationTokenSource? _debounceCts;
        private CancellationTokenSource? _requestCts;
        private int _searchVersion;
        private string? _selectedVersionName;

        public ObservableCollection<ExploreProject> SearchResults { get; } = new();

        [ObservableProperty] private string _searchQuery = "";
        [ObservableProperty] private string _selectedType = "mod"; // mod, resourcepack, shader, datapack
        [ObservableProperty] private string _selectedSort = "relevance"; // relevance, downloads, newest, updated

        [ObservableProperty] private int _currentPage = 1;
        [ObservableProperty] private bool _hasNextPage;

        public bool HasPreviousPage => CurrentPage > 1;

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string? _statusMessage;

        // Navigation state
        [ObservableProperty] private bool _isDetailViewOpen;
        [ObservableProperty] private ModDetailPageViewModel? _detailViewModel;

        public bool IsMasterViewOpen => !IsDetailViewOpen;

        partial void OnIsDetailViewOpenChanged(bool value)
        {
            OnPropertyChanged(nameof(IsMasterViewOpen));
        }

        // Popup state
        [ObservableProperty] private bool _showValidationPopup;

        public ExplorePageViewModel(IExploreService modrinthService, LauncherConfig config, string gameDirectory)
        {
            _modrinthService = modrinthService;
            _config = config;
            _gameDirectory = gameDirectory;
        }

        public void UpdateSelectedVersion(string? versionName)
        {
            if (_selectedVersionName == versionName)
                return;

            _selectedVersionName = versionName;
            if (IsActive && _config.ExploreOnlyCompatible)
            {
                CurrentPage = 1;
                _ = PerformSearchCore();
            }
        }

        protected override async void OnActivated()
        {
            base.OnActivated();
            if (SearchResults.Count == 0 && !IsDetailViewOpen)
            {
                await PerformSearchCore();
            }
        }

        partial void OnSearchQueryChanged(string value) => DebouncedSearch();
        partial void OnSelectedTypeChanged(string value) { CurrentPage = 1; _ = PerformSearchCore(); }
        partial void OnSelectedSortChanged(string value) { CurrentPage = 1; _ = PerformSearchCore(); }
        partial void OnCurrentPageChanged(int value)
        {
            OnPropertyChanged(nameof(HasPreviousPage));
        }

        private async void DebouncedSearch()
        {
            CurrentPage = 1;
            _debounceCts?.Cancel();
            var cts = _debounceCts = new CancellationTokenSource();

            try
            {
                await Task.Delay(300, cts.Token);
                await PerformSearchCore();
            }
            catch (OperationCanceledException) { }
        }

        [RelayCommand]
        private Task PerformSearch()
        {
            CurrentPage = 1;
            return PerformSearchCore();
        }

        private async Task PerformSearchCore()
        {
            _requestCts?.Cancel();
            var cts = _requestCts = new CancellationTokenSource();
            var version = ++_searchVersion;

            IsLoading = true;
            StatusMessage = null;

            try
            {
                var target = ExploreInstallHelper.ParseVersionName(_selectedVersionName);
                var filterByCompatibility = _config.ExploreOnlyCompatible;
                var query = new ExploreSearchQuery(
                    SearchText: SearchQuery,
                    ProjectType: SelectedType,
                    GameVersion: filterByCompatibility ? target.GameVersion : null,
                    Loader: filterByCompatibility
                        ? ExploreInstallHelper.GetLoaderFilter(SelectedType, target)
                        : null,
                    SortBy: SelectedSort,
                    Offset: (CurrentPage - 1) * PageSize,
                    Limit: PageSize
                );

                var result = await _modrinthService.SearchAsync(query, cts.Token);
                if (version != _searchVersion) return;

                SearchResults.Clear();
                foreach (var project in result.Projects)
                {
                    SearchResults.Add(project);
                }

                HasNextPage = CurrentPage * PageSize < result.TotalCount;

                if (SearchResults.Count == 0)
                {
                    StatusMessage = "No results found.";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (version == _searchVersion)
                {
                    SearchResults.Clear();
                    HasNextPage = false;
                    StatusMessage = $"Search failed: {ex.Message}";
                }
            }
            finally
            {
                if (version == _searchVersion)
                    IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task NextPage()
        {
            if (HasNextPage)
            {
                CurrentPage++;
                await PerformSearchCore();
            }
        }

        [RelayCommand]
        private async Task PreviousPage()
        {
            if (HasPreviousPage)
            {
                CurrentPage--;
                await PerformSearchCore();
            }
        }

        [RelayCommand]
        private void OpenModsFolder()
        {
            var modsFolder = ExploreInstallHelper.GetTargetFolder(_gameDirectory, "mod");
            try
            {
                Directory.CreateDirectory(modsFolder);

                var startInfo = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open",
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add(modsFolder);
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not open mods folder: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task InstallProject(ExploreProject project)
        {
            StatusMessage = $"Downloading {project.Title}...";
            try
            {
                var target = ExploreInstallHelper.ParseVersionName(_selectedVersionName);
                var versions = await _modrinthService.GetProjectVersionsAsync(project.Id, target.GameVersion);
                var best = ExploreInstallHelper.PickBest(versions, target, project.ProjectType);

                if (best == null)
                {
                    StatusMessage = $"No compatible version of {project.Title} found.";
                    TriggerValidationPopup();
                    return;
                }

                string targetFolder = ExploreInstallHelper.GetTargetFolder(_gameDirectory, project.ProjectType);
                Directory.CreateDirectory(targetFolder);

                string destPath = Path.Combine(targetFolder, ExploreInstallHelper.SafeFileName(best.Filename));
                var temporaryPath = Path.Combine(targetFolder, $".{Guid.NewGuid():N}.download");
                try
                {
                    await _modrinthService.DownloadFileAsync(best.DownloadUrl, temporaryPath);
                    File.Move(temporaryPath, destPath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }

                StatusMessage = $"Installed {best.Filename} successfully!";

                _ = ClearSuccessMessageAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to install {project.Title}: {ex.Message}";
            }
        }

        private async Task ClearSuccessMessageAsync()
        {
            await Task.Delay(3000);
            if (StatusMessage?.Contains("successfully") == true)
                StatusMessage = "";
        }

        [RelayCommand]
        private async Task ViewProject(ExploreProject project)
        {
            var vm = new ModDetailPageViewModel(_modrinthService, _gameDirectory,
                onBack: () => IsDetailViewOpen = false,
                onValidationFailed: () => TriggerValidationPopup(),
                getSelectedVersionName: () => _selectedVersionName);

            DetailViewModel = vm;
            IsDetailViewOpen = true;

            await vm.LoadProjectAsync(project.Id);
        }

        private async void TriggerValidationPopup()
        {
            ShowValidationPopup = true;
            await Task.Delay(5000);
            ShowValidationPopup = false;
        }
    }
}
