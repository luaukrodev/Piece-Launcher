using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class ModDetailPageViewModel : ViewModelBase
    {
        private readonly IExploreService _exploreService;
        private readonly LauncherConfig _config;
        private readonly Action _onBack;
        private readonly Action _onValidationFailed;

        [ObservableProperty] private ExploreProjectDetail? _project;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string? _errorMessage;
        
        // Tab Management
        [ObservableProperty] private int _selectedTabIndex = 0;

        public ObservableCollection<ExploreProjectVersion> Versions { get; } = new();
        public ObservableCollection<string> Gallery { get; } = new();

        public ModDetailPageViewModel(IExploreService exploreService, LauncherConfig config, Action onBack, Action onValidationFailed)
        {
            _exploreService = exploreService;
            _config = config;
            _onBack = onBack;
            _onValidationFailed = onValidationFailed;
        }

        public async Task LoadProjectAsync(string projectId)
        {
            IsLoading = true;
            ErrorMessage = null;
            try
            {
                Project = await _exploreService.GetProjectDetailAsync(projectId);
                
                Gallery.Clear();
                if (Project.Gallery != null)
                {
                    foreach (var image in Project.Gallery)
                    {
                        Gallery.Add(image);
                    }
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
            if (Versions.Count > 0)
            {
                var latest = Versions.First();
                await InstallVersion(latest);
            }
            else
            {
                ErrorMessage = "No versions available to install.";
            }
        }

        [RelayCommand]
        private async Task InstallVersion(ExploreProjectVersion version)
        {
            ErrorMessage = $"Installing {version.Name}...";
            try
            {
                string targetFolder = Path.Combine(_config.GameDirectory ?? "", "mods");
                if (!Directory.Exists(targetFolder))
                    Directory.CreateDirectory(targetFolder);

                string destPath = Path.Combine(targetFolder, version.Filename);
                await _exploreService.DownloadFileAsync(version.DownloadUrl, destPath);

                ErrorMessage = $"Installed {version.Filename} successfully!";
                
                // Clear message after 3 seconds
                _ = Task.Run(async () => {
                    await Task.Delay(3000);
                    if (ErrorMessage?.Contains("successfully") == true)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => ErrorMessage = null);
                });
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to install: {ex.Message}";
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
