using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class ExplorePageViewModel : ViewModelBase
    {
        private readonly IExploreService _modrinthService;
        private readonly LauncherConfig _config;
        private CancellationTokenSource? _searchCts;

        public ObservableCollection<ExploreProject> SearchResults { get; } = new();

        [ObservableProperty] private string _searchQuery = "";
        [ObservableProperty] private string _selectedType = "mod"; // mod, resourcepack, shader, datapack
        [ObservableProperty] private string _selectedSource = "Modrinth"; // All, Modrinth, CurseForge
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

        public ExplorePageViewModel(IExploreService modrinthService, LauncherConfig config)
        {
            _modrinthService = modrinthService;
            _config = config;
        }

        protected override async void OnActivated()
        {
            base.OnActivated();
            if (SearchResults.Count == 0 && !IsDetailViewOpen)
            {
                await PerformSearch();
            }
        }

        partial void OnSearchQueryChanged(string value) => DebouncedSearch();
        partial void OnSelectedTypeChanged(string value) { CurrentPage = 1; _ = PerformSearch(); }
        partial void OnSelectedSourceChanged(string value) { CurrentPage = 1; _ = PerformSearch(); }
        partial void OnSelectedSortChanged(string value) { CurrentPage = 1; _ = PerformSearch(); }
        partial void OnCurrentPageChanged(int value)
        {
            OnPropertyChanged(nameof(HasPreviousPage));
        }

        private void DebouncedSearch()
        {
            CurrentPage = 1;
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, token);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = PerformSearch(token));
                    }
                }
                catch (TaskCanceledException) { }
            }, token);
        }

        [RelayCommand]
        private async Task PerformSearch(CancellationToken ct = default)
        {
            IsLoading = true;
            StatusMessage = null;

            try
            {
                var query = new ExploreSearchQuery(
                    SearchText: SearchQuery,
                    ProjectType: SelectedType,
                    SortBy: SelectedSort,
                    Offset: (CurrentPage - 1) * 20,
                    Limit: 20
                );

                var result = await _modrinthService.SearchAsync(query, ct);
                
                SearchResults.Clear();
                foreach (var project in result.Projects)
                {
                    SearchResults.Add(project);
                }

                HasNextPage = result.Projects.Count == 20; // Assume we have next page if we got full limit

                if (SearchResults.Count == 0)
                {
                    StatusMessage = "No results found.";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                StatusMessage = $"Search failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task NextPage()
        {
            if (HasNextPage)
            {
                CurrentPage++;
                await PerformSearch();
            }
        }

        [RelayCommand]
        private async Task PreviousPage()
        {
            if (HasPreviousPage)
            {
                CurrentPage--;
                await PerformSearch();
            }
        }

        [RelayCommand]
        private async Task InstallProject(ExploreProject project)
        {
            StatusMessage = $"Downloading {project.Title}...";
            try
            {
                var versions = await _modrinthService.GetProjectVersionsAsync(project.Id);
                var latest = versions.FirstOrDefault();
                if (latest == null)
                {
                    StatusMessage = $"No versions found for {project.Title}.";
                    return;
                }
                
                string targetFolder = Path.Combine(_config.GameDirectory ?? "", "mods");
                if (!Directory.Exists(targetFolder))
                    Directory.CreateDirectory(targetFolder);
                    
                string destPath = Path.Combine(targetFolder, latest.Filename);
                await _modrinthService.DownloadFileAsync(latest.DownloadUrl, destPath);
                
                StatusMessage = $"Installed {latest.Filename} successfully!";
                
                // Clear message after 3 seconds
                _ = Task.Run(async () => {
                    await Task.Delay(3000);
                    if (StatusMessage?.Contains("successfully") == true)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusMessage = "");
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to install {project.Title}: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ViewProject(ExploreProject project)
        {
            var vm = new ModDetailPageViewModel(_modrinthService, _config,
                onBack: () => IsDetailViewOpen = false,
                onValidationFailed: () => TriggerValidationPopup());
            
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
