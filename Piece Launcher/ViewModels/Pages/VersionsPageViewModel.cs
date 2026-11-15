using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using Piece_Launcher.Helpers;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class VersionsPageViewModel : ViewModelBase
    {
        private readonly IVersionService _versionService;
        private readonly IVersionManagerService _versionManagerService;
        private readonly IModLoaderService _forgeService;
        private readonly IModLoaderService _fabricService;
        private readonly IModLoaderService _neoForgeService;
        private readonly IModLoaderService _quiltService;
        private readonly LauncherConfig _config;
        private readonly ILoadingService _loadingService;
        private readonly AutoDismissMessage _statusMessageAutoDismiss = new();
        private string? _selectedVersionName;
        private string? _pendingDuplicateSourceVersionId;
        private VersionEntry? _pendingRenameVersion;
        private VersionEntry? _pendingDeleteVersion;
        private string? _versionBrowserRoot;

        public event Action? VersionInstalled;
        public event Action<string>? VersionSelected;

        // ─── Game Versions ───
        public ObservableCollection<VersionEntry> AllVersions { get; } = new();
        public ObservableCollection<VersionEntry> FilteredVersions { get; } = new();
        public ObservableCollection<VersionEntry> ManagedVersions { get; } = new();
        public bool HasManagedVersions => ManagedVersions.Count > 0;
        public bool HasSelectedManagedVersion => SelectedManagedVersion is not null;

        [ObservableProperty] private bool _isManageVersionsView;
        [ObservableProperty] private VersionEntry? _selectedManagedVersion;
        [ObservableProperty] private string _versionNameInput = string.Empty;
        [ObservableProperty] private string? _versionNameError;
        [ObservableProperty] private bool _isVersionNamePromptOpen;
        [ObservableProperty] private bool _isRenamePrompt;
        [ObservableProperty] private bool _isDuplicatePrompt;
        [ObservableProperty] private bool _isVersionOperationInProgress;
        [ObservableProperty] private bool _isDeleteVersionConfirmationOpen;
        [ObservableProperty] private string? _pendingDeleteVersionName;
        [ObservableProperty] private string? _currentVersionBrowserDirectory;
        [ObservableProperty] private bool _canNavigateVersionBrowserUp;
        [ObservableProperty] private bool _isVersionFileListEmpty = true;
        public ObservableCollection<VersionFileEntry> CurrentVersionFiles { get; } = new();

        [ObservableProperty] private bool _showSnapshots;
        [ObservableProperty] private bool _showBetas;
        [ObservableProperty] private bool _showAlphas;
        [ObservableProperty] private bool _showOldVersions;
        [ObservableProperty] private string _searchQuery = "";

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string? _statusMessage;
        [ObservableProperty] private double _installProgressValue;
        [ObservableProperty] private string? _installProgressText;
        [ObservableProperty] private bool _isInstalling;
        [ObservableProperty] private bool _isInstallProgressVisible;

        partial void OnStatusMessageChanged(string? value)
            => _statusMessageAutoDismiss.Schedule(value, () => StatusMessage, () => StatusMessage = null);

        // ─── Mod Loaders ───
        public ObservableCollection<string> MinecraftReleaseVersions { get; } = new();
        [ObservableProperty] private string? _selectedMinecraftVersion;

        [ObservableProperty] private string _selectedLoader = "Fabric";
        public bool IsFabricSelected => string.Equals(SelectedLoader, "Fabric", StringComparison.OrdinalIgnoreCase);
        public bool IsForgeSelected => string.Equals(SelectedLoader, "Forge", StringComparison.OrdinalIgnoreCase);
        public bool IsNeoForgeSelected => string.Equals(SelectedLoader, "NeoForge", StringComparison.OrdinalIgnoreCase);
        public bool IsQuiltSelected => string.Equals(SelectedLoader, "Quilt", StringComparison.OrdinalIgnoreCase);

        public ObservableCollection<LoaderVersionEntry> AvailableLoaderVersions { get; } = new();
        [ObservableProperty] private LoaderVersionEntry? _selectedLoaderVersionEntry;
        [ObservableProperty] private bool _isLoadingLoaderVersions;
        [ObservableProperty] private bool _isCurrentLoaderInstalled;

        public VersionsPageViewModel(
            IVersionService versionService,
            IVersionManagerService versionManagerService,
            IModLoaderService forgeService,
            IModLoaderService fabricService,
            IModLoaderService neoForgeService,
            IModLoaderService quiltService,
            LauncherConfig config)
        {
            _versionService = versionService;
            _versionManagerService = versionManagerService;
            _forgeService = forgeService;
            _fabricService = fabricService;
            _neoForgeService = neoForgeService;
            _quiltService = quiltService;
            _config = config;
            _loadingService = ServiceLocator.LoadingService;

            ShowSnapshots = config.ShowSnapshots;
            ShowBetas = config.ShowBetas;
            ShowAlphas = config.ShowAlphas;
            ShowOldVersions = config.ShowOldVersions;
            _selectedLoader = NormalizeModLoader(config.DefaultLoader) ?? "Fabric";
            _config.PropertyChanged += OnConfigPropertyChanged;
        }

        protected override async void OnActivated()
        {
            base.OnActivated();
            await LoadVersionsAsync();
        }

        partial void OnShowSnapshotsChanged(bool value)
        {
            _config.ShowSnapshots = value;
            ApplyFilters();
        }

        partial void OnShowBetasChanged(bool value)
        {
            _config.ShowBetas = value;
            ApplyFilters();
        }

        partial void OnShowAlphasChanged(bool value)
        {
            _config.ShowAlphas = value;
            ApplyFilters();
        }

        partial void OnShowOldVersionsChanged(bool value)
        {
            _config.ShowOldVersions = value;
            ApplyFilters();
        }
        partial void OnSearchQueryChanged(string value) => ApplyFilters();

        partial void OnSelectedManagedVersionChanged(VersionEntry? value)
        {
            OnPropertyChanged(nameof(HasSelectedManagedVersion));
            CurrentVersionFiles.Clear();
            _versionBrowserRoot = null;
            CurrentVersionBrowserDirectory = null;
            CanNavigateVersionBrowserUp = false;

            if (value is not null)
            {
                try
                {
                    _versionBrowserRoot = _versionManagerService.GetVersionDirectory(value.Name);
                    LoadVersionFiles(_versionBrowserRoot);
                }
                catch (Exception ex)
                {
                    StatusMessage = LocalizationHelper.Format("Lang_Versions_BrowseFailed", ex.Message);
                }
            }

            IsVersionFileListEmpty = CurrentVersionFiles.Count == 0;
        }

        partial void OnSelectedMinecraftVersionChanged(string? value)
        {
            _ = LoadLoaderVersionsAsync();
        }

        partial void OnSelectedLoaderChanged(string value)
        {
            _config.DefaultLoader = value;
            OnPropertyChanged(nameof(IsFabricSelected));
            OnPropertyChanged(nameof(IsForgeSelected));
            OnPropertyChanged(nameof(IsNeoForgeSelected));
            OnPropertyChanged(nameof(IsQuiltSelected));
            _ = LoadLoaderVersionsAsync();
        }

        private void OnConfigPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(LauncherConfig.ShowSnapshots):
                    ShowSnapshots = _config.ShowSnapshots;
                    break;
                case nameof(LauncherConfig.ShowBetas):
                    ShowBetas = _config.ShowBetas;
                    break;
                case nameof(LauncherConfig.ShowAlphas):
                    ShowAlphas = _config.ShowAlphas;
                    break;
                case nameof(LauncherConfig.ShowOldVersions):
                    ShowOldVersions = _config.ShowOldVersions;
                    break;
                case nameof(LauncherConfig.DefaultLoader):
                    if (NormalizeModLoader(_config.DefaultLoader) is { } configuredLoader)
                        SelectedLoader = configuredLoader;
                    break;
                case nameof(LauncherConfig.Language):
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var version in AllVersions)
                            version.RefreshDisplayType();
                    });
                    break;
                case nameof(LauncherConfig.VersionDisplayNames):
                    RefreshVersionDisplayNames();
                    break;
            }
        }

        public void RefreshVersionDisplayNames()
        {
            foreach (var version in AllVersions)
                version.RefreshDisplayName(_config.GetVersionDisplayName(version.Name));
        }

        [RelayCommand]
        private void ShowDownloadVersions() => IsManageVersionsView = false;

        [RelayCommand]
        private void ShowManageVersions() => IsManageVersionsView = true;

        [RelayCommand]
        private void RequestDuplicateVersion(VersionEntry version)
        {
            if (!version.IsInstalled)
                return;

            _pendingDuplicateSourceVersionId = version.Name;
            _pendingRenameVersion = null;
            IsDuplicatePrompt = true;
            IsRenamePrompt = false;
            VersionNameInput = GetNextAvailableDisplayName(version);
            VersionNameError = null;
            IsVersionNamePromptOpen = true;
        }

        [RelayCommand]
        private void RequestRenameVersion(VersionEntry version)
        {
            _pendingDuplicateSourceVersionId = null;
            _pendingRenameVersion = version;
            IsDuplicatePrompt = false;
            IsRenamePrompt = true;
            VersionNameInput = _config.GetVersionDisplayName(version.Name);
            VersionNameError = null;
            IsVersionNamePromptOpen = true;
        }

        [RelayCommand]
        private async Task ConfirmVersionName()
        {
            var displayName = VersionNameInput.Trim();
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 64 ||
                displayName.Any(char.IsControl))
            {
                VersionNameError = LocalizationHelper.Get("Lang_Versions_InvalidDisplayName");
                return;
            }

            var existingVersionId = _pendingRenameVersion?.Name;
            if (!IsDisplayNameAvailable(displayName, existingVersionId))
            {
                VersionNameError = LocalizationHelper.Format("Lang_Versions_DuplicateNameError", displayName);
                return;
            }

            VersionNameError = null;
            IsVersionOperationInProgress = true;
            using var loading = _loadingService.Begin();
            try
            {
                if (_pendingDuplicateSourceVersionId is { } sourceVersionId)
                {
                    var newVersionId = await _versionManagerService.CreateInstalledCopyAsync(sourceVersionId);
                    _config.SetVersionDisplayName(newVersionId, displayName);
                    await LoadVersionsAsync();
                    SelectedManagedVersion = ManagedVersions.FirstOrDefault(version =>
                        string.Equals(version.Name, newVersionId, StringComparison.OrdinalIgnoreCase));
                    StatusMessage = LocalizationHelper.Format("Lang_Versions_CopyCreated", displayName);
                    VersionInstalled?.Invoke();
                }
                else if (_pendingRenameVersion is { } version)
                {
                    _config.SetVersionDisplayName(version.Name, displayName);
                    StatusMessage = LocalizationHelper.Format("Lang_Versions_RenameSucceeded", displayName);
                }

                IsVersionNamePromptOpen = false;
                _pendingDuplicateSourceVersionId = null;
                _pendingRenameVersion = null;
            }
            catch (Exception ex)
            {
                VersionNameError = LocalizationHelper.Format("Lang_Versions_VersionOperationFailed", ex.Message);
            }
            finally
            {
                IsVersionOperationInProgress = false;
            }
        }

        [RelayCommand]
        private void CancelVersionNamePrompt()
        {
            IsVersionNamePromptOpen = false;
            VersionNameError = null;
            _pendingDuplicateSourceVersionId = null;
            _pendingRenameVersion = null;
        }

        [RelayCommand]
        private void RequestDeleteVersion(VersionEntry version)
        {
            _pendingDeleteVersion = version;
            PendingDeleteVersionName = version.DisplayName;
            IsDeleteVersionConfirmationOpen = true;
        }

        [RelayCommand]
        private void CancelDeleteVersion()
        {
            IsDeleteVersionConfirmationOpen = false;
            _pendingDeleteVersion = null;
            PendingDeleteVersionName = null;
        }

        [RelayCommand]
        private async Task ConfirmDeleteVersion()
        {
            if (_pendingDeleteVersion is not { } version)
                return;

            IsVersionOperationInProgress = true;
            using var loading = _loadingService.Begin();
            try
            {
                var instanceDependents = _config.MinecraftInstances
                    .Where(instance => string.Equals(
                        instance.VersionId,
                        version.Name,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(instance => instance.Name)
                    .ToArray();
                if (instanceDependents.Length > 0)
                    throw new VersionInUseException(instanceDependents);

                await _versionManagerService.DeleteVersionAsync(version.Name);
                _config.SetVersionDisplayName(version.Name, null);
                IsDeleteVersionConfirmationOpen = false;
                _pendingDeleteVersion = null;
                PendingDeleteVersionName = null;
                StatusMessage = LocalizationHelper.Format("Lang_Versions_DeleteSucceeded", version.DisplayName);
                await LoadVersionsAsync();
                VersionInstalled?.Invoke();
            }
            catch (VersionInUseException ex)
            {
                StatusMessage = LocalizationHelper.Format(
                    "Lang_Versions_DeleteBlocked",
                    string.Join(", ", ex.DependentVersions));
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_Versions_DeleteFailed", ex.Message);
            }
            finally
            {
                IsVersionOperationInProgress = false;
            }
        }

        [RelayCommand]
        private void OpenManagedVersionFolder(VersionEntry? version)
        {
            if (version is null)
                return;

            try
            {
                var path = _versionManagerService.GetVersionDirectory(version.Name);
                if (!Directory.Exists(path))
                    throw new DirectoryNotFoundException(path);

                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_Versions_OpenFolderFailed", ex.Message);
            }
        }

        [RelayCommand]
        private void OpenOrNavigateVersionFile(VersionFileEntry? entry)
        {
            if (entry is null || SelectedManagedVersion is null || _versionBrowserRoot is null)
                return;

            try
            {
                var parentDirectory = Path.GetDirectoryName(entry.FullPath)
                    ?? throw new InvalidOperationException("The selected item has no parent directory.");
                var entries = _versionManagerService.GetDirectoryEntries(
                    SelectedManagedVersion.Name,
                    parentDirectory);

                if (!entries.Any(item => string.Equals(item.FullPath, entry.FullPath, GetPathComparison())))
                    throw new InvalidOperationException("The selected file is outside the managed version.");

                if (entry.IsDirectory)
                {
                    LoadVersionFiles(entry.FullPath);
                }
                else
                {
                    Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_Versions_OpenFileFailed", ex.Message);
            }
        }

        [RelayCommand]
        private void NavigateVersionBrowserUp()
        {
            if (_versionBrowserRoot is null || CurrentVersionBrowserDirectory is null ||
                string.Equals(CurrentVersionBrowserDirectory, _versionBrowserRoot, GetPathComparison()))
            {
                return;
            }

            var parent = Directory.GetParent(CurrentVersionBrowserDirectory)?.FullName;
            if (parent is null)
                return;

            try
            {
                LoadVersionFiles(parent);
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_Versions_BrowseFailed", ex.Message);
            }
        }

        private void LoadVersionFiles(string directoryPath)
        {
            if (SelectedManagedVersion is null)
                return;

            var entries = _versionManagerService.GetDirectoryEntries(SelectedManagedVersion.Name, directoryPath);
            CurrentVersionFiles.Clear();
            foreach (var entry in entries)
                CurrentVersionFiles.Add(entry);

            CurrentVersionBrowserDirectory = directoryPath;
            CanNavigateVersionBrowserUp = _versionBrowserRoot is not null &&
                !string.Equals(directoryPath, _versionBrowserRoot, GetPathComparison());
            IsVersionFileListEmpty = CurrentVersionFiles.Count == 0;
        }

        private bool IsDisplayNameAvailable(string displayName, string? exceptVersionId)
            => ManagedVersions.All(version =>
                string.Equals(version.Name, exceptVersionId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    _config.GetVersionDisplayName(version.Name),
                    displayName,
                    StringComparison.OrdinalIgnoreCase));

        private string GetNextAvailableDisplayName(VersionEntry version)
        {
            var baseName = _config.GetVersionDisplayName(version.Name);
            var suffixStart = baseName.LastIndexOf('(');
            if (suffixStart > 0 && baseName.EndsWith(')') &&
                int.TryParse(
                    baseName.AsSpan(suffixStart + 1, baseName.Length - suffixStart - 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                baseName = baseName[..suffixStart].TrimEnd();
            }

            for (var suffix = 1; ; suffix++)
            {
                var candidate = $"{baseName}({suffix})";
                if (IsDisplayNameAvailable(candidate, null))
                    return candidate;
            }
        }

        private static StringComparison GetPathComparison()
            => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        [RelayCommand]
        public void SelectLoader(string loaderName)
        {
            if (NormalizeModLoader(loaderName) is { } normalizedLoader)
                SelectedLoader = normalizedLoader;
        }

        public async Task LoadVersionsAsync()
        {
            using var loading = _loadingService.Begin();
            IsLoading = true;
            try
            {
                var versions = await _versionService.GetAllVersionsAsync();
                var previousManagedVersion = SelectedManagedVersion?.Name;
                AllVersions.Clear();
                MinecraftReleaseVersions.Clear();
                ManagedVersions.Clear();

                foreach (var v in versions)
                {
                    v.RefreshDisplayName(_config.GetVersionDisplayName(v.Name));
                    AllVersions.Add(v);
                    if (v.IsInstalled)
                        ManagedVersions.Add(v);
                    if (v.Type.Equals("release", StringComparison.OrdinalIgnoreCase))
                    {
                        MinecraftReleaseVersions.Add(v.Name);
                    }
                }

                OnPropertyChanged(nameof(HasManagedVersions));
                SelectedManagedVersion = ManagedVersions.FirstOrDefault(v =>
                    string.Equals(v.Name, previousManagedVersion, StringComparison.OrdinalIgnoreCase))
                    ?? ManagedVersions.FirstOrDefault();
                UpdateSelectedVersion(_selectedVersionName);

                if (string.IsNullOrEmpty(SelectedMinecraftVersion) || !MinecraftReleaseVersions.Contains(SelectedMinecraftVersion))
                {
                    SelectedMinecraftVersion = MinecraftReleaseVersions.FirstOrDefault();
                }

                ApplyFilters();
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_Versions_LoadFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task LoadLoaderVersionsAsync()
        {
            if (string.IsNullOrEmpty(SelectedMinecraftVersion)) return;

            using var loading = _loadingService.Begin();
            IsLoadingLoaderVersions = true;
            AvailableLoaderVersions.Clear();
            SelectedLoaderVersionEntry = null;
            var service = GetServiceForLoader(SelectedLoader);
            if (service == null)
            {
                IsCurrentLoaderInstalled = false;
                IsLoadingLoaderVersions = false;
                return;
            }

            try
            {
                var versions = await service.GetAvailableVersionsAsync(SelectedMinecraftVersion);
                foreach (var v in versions)
                {
                    AvailableLoaderVersions.Add(v);
                }

                SelectedLoaderVersionEntry = AvailableLoaderVersions.FirstOrDefault(v => v.IsRecommended)
                    ?? AvailableLoaderVersions.FirstOrDefault();

                IsCurrentLoaderInstalled = await service.IsInstalledAsync(SelectedMinecraftVersion);
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_Versions_LoadLoaderVersionsFailed", SelectedLoader, ex.Message);
            }
            finally
            {
                IsLoadingLoaderVersions = false;
            }
        }

        private void ApplyFilters()
        {
            FilteredVersions.Clear();
            foreach (var v in AllVersions)
            {
                if (!string.IsNullOrEmpty(SearchQuery) && !v.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isMatch = v.Type.ToLowerInvariant() switch
                {
                    "release" => true,
                    "snapshot" => ShowSnapshots,
                    "old_beta" => ShowBetas,
                    "old_alpha" => ShowAlphas,
                    "custom" => true,
                    _ => ShowOldVersions
                };

                if (isMatch)
                {
                    FilteredVersions.Add(v);
                }
            }
        }

        public void UpdateSelectedVersion(string? versionName)
        {
            _selectedVersionName = versionName;
            foreach (var version in AllVersions)
                version.IsSelected = string.Equals(version.Name, versionName, StringComparison.OrdinalIgnoreCase);
        }

        [RelayCommand]
        private void ActivateVersion(VersionEntry version)
        {
            if (version.IsInstalled)
                VersionSelected?.Invoke(version.Name);
            else
                _ = InstallVersion(version.Name);
        }

        [RelayCommand]
        public async Task InstallVersion(string versionName)
        {
            if (IsInstalling) return;

            using var loading = _loadingService.Begin();
            IsInstalling = true;
            IsInstallProgressVisible = false;
            InstallProgressValue = 0;
            InstallProgressText = LocalizationHelper.Format("Lang_Versions_StartingGameInstall", versionName);

            var progress = new Progress<InstallProgress>(p =>
            {
                if (p.Percentage > 0)
                    IsInstallProgressVisible = true;
                InstallProgressValue = p.Percentage;
                InstallProgressText = $"{LocalizationHelper.GetInstallStage(p.Stage)} - {p.CurrentFile ?? ""}";
            });

            try
            {
                await _versionService.InstallVersionAsync(versionName, progress);
                StatusMessage = LocalizationHelper.Format("Lang_Versions_GameInstalled", versionName);
                await LoadVersionsAsync();
                VersionInstalled?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_Versions_GameInstallFailed", versionName, ex.Message);
            }
            finally
            {
                IsInstalling = false;
                IsInstallProgressVisible = false;
                InstallProgressText = null;
            }
        }

        [RelayCommand]
        public async Task InstallSelectedLoader()
        {
            if (IsInstalling || string.IsNullOrEmpty(SelectedMinecraftVersion)) return;

            var service = GetServiceForLoader(SelectedLoader);
            if (service == null) return;

            using var loading = _loadingService.Begin();
            var loaderVersion = SelectedLoaderVersionEntry?.Version ?? "";
            IsInstalling = true;
            IsInstallProgressVisible = false;
            InstallProgressValue = 0;
            InstallProgressText = LocalizationHelper.Format("Lang_Versions_StartingLoaderInstall", SelectedLoader, loaderVersion, SelectedMinecraftVersion);

            var progress = new Progress<InstallProgress>(p =>
            {
                if (p.Percentage > 0)
                    IsInstallProgressVisible = true;
                InstallProgressValue = p.Percentage;
                InstallProgressText = $"{LocalizationHelper.GetInstallStage(p.Stage)} - {p.CurrentFile ?? ""}";
            });

            try
            {
                var installedId = await service.InstallAsync(SelectedMinecraftVersion, loaderVersion, progress);
                StatusMessage = LocalizationHelper.Format("Lang_Versions_LoaderInstalled", SelectedLoader, installedId);
                await LoadVersionsAsync();
                await LoadLoaderVersionsAsync();
                VersionInstalled?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_Versions_LoaderInstallFailed", SelectedLoader, ex.Message);
            }
            finally
            {
                IsInstalling = false;
                IsInstallProgressVisible = false;
                InstallProgressText = null;
            }
        }

        private IModLoaderService? GetServiceForLoader(string loaderName) => loaderName switch
        {
            "Forge" => _forgeService,
            "Fabric" => _fabricService,
            "NeoForge" => _neoForgeService,
            "Quilt" => _quiltService,
            _ => null
        };

        private static string? NormalizeModLoader(string? loaderName)
        {
            if (string.Equals(loaderName, "Fabric", StringComparison.OrdinalIgnoreCase)) return "Fabric";
            if (string.Equals(loaderName, "Forge", StringComparison.OrdinalIgnoreCase)) return "Forge";
            if (string.Equals(loaderName, "NeoForge", StringComparison.OrdinalIgnoreCase)) return "NeoForge";
            if (string.Equals(loaderName, "Quilt", StringComparison.OrdinalIgnoreCase)) return "Quilt";
            return null;
        }
    }
}
