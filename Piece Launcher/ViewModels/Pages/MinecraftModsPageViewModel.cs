using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Helpers;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CmlLib.Core;
using Piece_Launcher.Services.Impl;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class MinecraftModsPageViewModel : ViewModelBase
    {
        private const int PageSize = 20;

        private readonly IExploreService _modrinthService;
        private readonly IExploreService _curseForgeService;
        private readonly IVersionService _versionService;
        private readonly MinecraftPath _sharedMinecraftPath;
        private readonly LauncherConfig _config;
        private readonly string _defaultGameDirectory;
        private readonly ILoadingService _loadingService;
        private readonly MinecraftContentManager _contentManager = new();
        private readonly MinecraftModpackImportService _modpackImportService = new();
        private readonly MinecraftInstanceManagerService _instanceManagerService = new();
        private readonly AutoDismissMessage _statusMessageAutoDismiss = new();
        private readonly AutoDismissMessage _validationPopupAutoDismiss = new();
        private CancellationTokenSource? _debounceCts;
        private CancellationTokenSource? _requestCts;
        private int _searchVersion;
        private bool _suppressFilterSearch;
        private string? _selectedVersionName;

        public ObservableCollection<ExploreProject> SearchResults { get; } = new();
        public ObservableCollection<LocalizedChoice> SortOptions { get; } = new();
        public ObservableCollection<LocalizedChoice> TypeOptions { get; } = new();
        public ObservableCollection<LocalizedChoice> ManagerCategoryOptions { get; } = new();
        public ObservableCollection<MinecraftInstanceEntry> Instances { get; } = new();
        public ObservableCollection<string> InstanceVersions { get; } = new();
        public ObservableCollection<LocalizedChoice> InstanceLoaderOptions { get; } = new();
        public ObservableCollection<InstalledContentItem> InstalledContent { get; } = new();

        public event Action<MinecraftInstanceEntry?>? SelectedInstanceChanged;

        [ObservableProperty] private MinecraftInstanceEntry? _selectedInstance;
        [ObservableProperty] private bool _isManagerView;
        [ObservableProperty] private string _selectedManagerCategory = "all";
        [ObservableProperty] private InstalledContentItem? _pendingRemovalItem;
        [ObservableProperty] private MinecraftInstanceEntry? _pendingRemovalInstance;
        [ObservableProperty] private bool _showRemoveConfirmation;
        [ObservableProperty] private bool _isImportingModpack;
        [ObservableProperty] private bool _isInstanceManagerOpen;
        [ObservableProperty] private bool _isLoadingInstanceVersions;
        [ObservableProperty] private bool _isCreatingInstance;
        [ObservableProperty] private double _instanceInstallProgressValue;
        [ObservableProperty] private string? _instanceInstallProgressText;
        [ObservableProperty] private bool _isInstanceInstallProgressVisible;
        [ObservableProperty] private bool _isInstanceDownloadActive;
        [ObservableProperty] private bool _showDeleteInstanceConfirmation;
        [ObservableProperty] private MinecraftInstanceEntry? _pendingDeleteInstance;
        [ObservableProperty] private string _newInstanceName = string.Empty;
        [ObservableProperty] private string? _selectedNewInstanceVersion;
        [ObservableProperty] private string _selectedNewInstanceLoader = "Vanilla";

        public bool IsBrowseView => !IsManagerView;
        public bool CanCreateInstance => !IsCreatingInstance
            && !string.IsNullOrWhiteSpace(NewInstanceName)
            && !string.IsNullOrWhiteSpace(SelectedNewInstanceVersion);
        public string PendingDeleteInstanceName => PendingDeleteInstance?.Name ?? string.Empty;
        private string CurrentGameDirectory => SelectedInstance?.GameDirectory ?? _defaultGameDirectory;
        public bool HasInstalledContent => FilteredInstalledContent.Any();
        public IEnumerable<InstalledContentItem> FilteredInstalledContent
            => SelectedManagerCategory == "all"
                ? InstalledContent
                : InstalledContent.Where(item => item.Type switch
                {
                    InstalledContentType.Mod => SelectedManagerCategory == "mod",
                    InstalledContentType.ResourcePack => SelectedManagerCategory == "resourcepack",
                    InstalledContentType.Shader => SelectedManagerCategory == "shader",
                    InstalledContentType.DataPack => SelectedManagerCategory == "datapack",
                    _ => false
                });

        public LocalizedChoice? SelectedManagerCategoryOption
        {
            get => ManagerCategoryOptions.FirstOrDefault(option => option.Value == SelectedManagerCategory);
            set
            {
                if (value is not null)
                    SelectedManagerCategory = value.Value;
            }
        }

        [ObservableProperty] private string _searchQuery = "";
        [ObservableProperty] private string _selectedType = "mod"; // mod, resourcepack, shader, datapack
        [ObservableProperty] private string _selectedSort = "relevance"; // relevance, downloads, newest, updated
        [ObservableProperty] private string _selectedSource = "Modrinth";

        public bool IsModrinthSelected => SelectedSource == "Modrinth";
        public bool IsCurseForgeSelected => SelectedSource == "CurseForge";

        public LocalizedChoice? SelectedSortOption
        {
            get => SortOptions.FirstOrDefault(option => option.Value == SelectedSort);
            set
            {
                if (value is not null)
                    SelectedSort = value.Value;
            }
        }

        public LocalizedChoice? SelectedTypeOption
        {
            get => TypeOptions.FirstOrDefault(option => option.Value == SelectedType);
            set
            {
                if (value is not null)
                    SelectedType = value.Value;
            }
        }

        [ObservableProperty] private int _currentPage = 1;
        [ObservableProperty] private bool _hasNextPage;

        public bool HasPreviousPage => CurrentPage > 1;

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string? _statusMessage;
        [ObservableProperty] private bool _isInstallingProject;

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

        public MinecraftModsPageViewModel(
            IExploreService modrinthService,
            IExploreService curseForgeService,
            IVersionService versionService,
            LauncherConfig config,
            string gameDirectory,
            MinecraftPath sharedMinecraftPath)
        {
            _modrinthService = modrinthService;
            _curseForgeService = curseForgeService;
            _versionService = versionService;
            _sharedMinecraftPath = sharedMinecraftPath;
            _config = config;
            _defaultGameDirectory = gameDirectory;
            _loadingService = ServiceLocator.LoadingService;

            var defaultInstance = config.MinecraftInstances.FirstOrDefault(instance => instance.IsDefault);
            if (defaultInstance is null)
            {
                defaultInstance = new MinecraftInstanceEntry
                {
                    Id = "default",
                    Name = "Minecraft",
                    GameDirectory = gameDirectory,
                    IsDefault = true
                };
                config.MinecraftInstances.Insert(0, defaultInstance);
                config.Save();
            }

            foreach (var instance in config.MinecraftInstances)
                Instances.Add(instance);

            SelectedInstance = Instances.FirstOrDefault(instance =>
                instance.Id == config.LastSelectedMinecraftInstanceId) ?? defaultInstance;

            _config.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LauncherConfig.Language))
                    Dispatcher.UIThread.Post(() =>
                    {
                        RefreshLocalizedOptions();
                        RefreshManagerCategories();
                        RefreshInstanceLoaderOptions();
                        RefreshManagedContent();
                    });
            };
            RefreshLocalizedOptions();
            RefreshManagerCategories();
            RefreshInstanceLoaderOptions();
            RefreshManagedContent();
        }

        partial void OnSelectedInstanceChanged(MinecraftInstanceEntry? value)
        {
            if (value is not null)
            {
                _config.LastSelectedMinecraftInstanceId = value.Id;
                _config.Save();
            }

            SelectedInstanceChanged?.Invoke(value);
            RefreshManagedContent();
            if (IsActive && _config.ExploreOnlyCompatible)
            {
                CurrentPage = 1;
                _ = PerformSearchCore();
            }
        }

        partial void OnIsManagerViewChanged(bool value)
        {
            OnPropertyChanged(nameof(IsBrowseView));
            if (value)
                RefreshManagedContent();
        }

        partial void OnSelectedManagerCategoryChanged(string value)
        {
            OnPropertyChanged(nameof(FilteredInstalledContent));
            OnPropertyChanged(nameof(HasInstalledContent));
            OnPropertyChanged(nameof(SelectedManagerCategoryOption));
        }

        partial void OnPendingRemovalItemChanged(InstalledContentItem? value)
        {
            OnPropertyChanged(nameof(PendingRemovalName));
        }

        partial void OnPendingDeleteInstanceChanged(MinecraftInstanceEntry? value)
        {
            OnPropertyChanged(nameof(PendingDeleteInstanceName));
        }

        partial void OnNewInstanceNameChanged(string value)
        {
            OnPropertyChanged(nameof(CanCreateInstance));
        }

        partial void OnSelectedNewInstanceVersionChanged(string? value)
        {
            OnPropertyChanged(nameof(CanCreateInstance));
        }

        partial void OnIsCreatingInstanceChanged(bool value)
        {
            OnPropertyChanged(nameof(CanCreateInstance));
            RefreshStatusMessageDismissal();
        }

        partial void OnIsLoadingChanged(bool value) => RefreshStatusMessageDismissal();

        partial void OnIsImportingModpackChanged(bool value) => RefreshStatusMessageDismissal();

        partial void OnIsInstallingProjectChanged(bool value) => RefreshStatusMessageDismissal();

        partial void OnStatusMessageChanged(string? value) => RefreshStatusMessageDismissal();

        private void RefreshStatusMessageDismissal()
        {
            if (IsLoading || IsCreatingInstance || IsImportingModpack || IsInstallingProject)
            {
                _statusMessageAutoDismiss.Cancel();
                return;
            }

            _statusMessageAutoDismiss.Schedule(StatusMessage, () => StatusMessage, () => StatusMessage = null);
        }

        partial void OnSelectedNewInstanceLoaderChanged(string value)
        {
            OnPropertyChanged(nameof(SelectedNewInstanceLoaderOption));
        }

        public LocalizedChoice? SelectedNewInstanceLoaderOption
        {
            get => InstanceLoaderOptions.FirstOrDefault(option =>
                string.Equals(option.Value, SelectedNewInstanceLoader, StringComparison.OrdinalIgnoreCase));
            set
            {
                if (value is not null)
                    SelectedNewInstanceLoader = value.Value;
            }
        }

        public string PendingRemovalName => PendingRemovalItem?.Name ?? string.Empty;

        public void ShowModpackImportError(string message)
        {
            StatusMessage = LocalizationHelper.Format("Lang_ModsManager_ImportFailed", message);
        }

        public void AddImportedInstance(MinecraftInstanceEntry instance)
        {
            if (Instances.Any(existing => string.Equals(existing.Id, instance.Id, StringComparison.Ordinal)))
                return;

            Instances.Add(instance);
            SelectedInstance = instance;
        }

        [RelayCommand]
        private void OpenInstanceManager()
        {
            IsInstanceManagerOpen = true;
            StatusMessage = null;
            _ = LoadInstanceVersionsAsync();
        }

        [RelayCommand]
        private void CloseInstanceManager()
        {
            IsInstanceManagerOpen = false;
            ShowDeleteInstanceConfirmation = false;
            PendingDeleteInstance = null;
        }

        [RelayCommand]
        private async Task LoadInstanceVersions()
        {
            await LoadInstanceVersionsAsync();
        }

        private async Task LoadInstanceVersionsAsync()
        {
            IsLoadingInstanceVersions = true;
            try
            {
                var versions = await _versionService.GetAllVersionsAsync();
                InstanceVersions.Clear();
                foreach (var version in versions
                    .Where(version => string.Equals(version.Type, "release", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(version => version.ReleaseTime))
                {
                    InstanceVersions.Add(version.Name);
                }

                if (!string.IsNullOrWhiteSpace(SelectedInstance?.MinecraftVersion)
                    && InstanceVersions.Contains(SelectedInstance.MinecraftVersion))
                {
                    SelectedNewInstanceVersion = SelectedInstance.MinecraftVersion;
                }
                else if (string.IsNullOrWhiteSpace(SelectedNewInstanceVersion)
                    || !InstanceVersions.Contains(SelectedNewInstanceVersion))
                {
                    SelectedNewInstanceVersion = InstanceVersions.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_InstanceManager_LoadVersionsFailed", ex.Message);
            }
            finally
            {
                IsLoadingInstanceVersions = false;
            }
        }

        [RelayCommand]
        private async Task CreateMinecraftInstance()
        {
            if (string.IsNullOrWhiteSpace(NewInstanceName))
            {
                StatusMessage = LocalizationHelper.Get("Lang_InstanceManager_NameRequired");
                return;
            }
            if (string.IsNullOrWhiteSpace(SelectedNewInstanceVersion))
            {
                StatusMessage = LocalizationHelper.Get("Lang_InstanceManager_SelectVersion");
                return;
            }
            if (Instances.Any(instance => string.Equals(
                instance.Name.Trim(),
                NewInstanceName.Trim(),
                StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = LocalizationHelper.Get("Lang_InstanceManager_NameExists");
                return;
            }

            IsCreatingInstance = true;
            IsInstanceInstallProgressVisible = true;
            IsInstanceDownloadActive = false;
            InstanceInstallProgressValue = 0;
            InstanceInstallProgressText = SelectedNewInstanceLoader == "Vanilla"
                ? LocalizationHelper.Format("Lang_Versions_StartingGameInstall", SelectedNewInstanceVersion)
                : LocalizationHelper.Format("Lang_Progress_PreparingLoader", SelectedNewInstanceLoader);
            IDisposable? preparationLoading = null;
            try
            {
                var progress = new Progress<InstallProgress>(value =>
                {
                    if (!IsCreatingInstance)
                        return;
                    if (IsInstanceDownloadStage(value.Stage))
                    {
                        IsInstanceDownloadActive = true;
                        preparationLoading?.Dispose();
                        preparationLoading = null;
                    }
                    if (value.Percentage > 0)
                        IsInstanceInstallProgressVisible = true;
                    InstanceInstallProgressValue = value.Percentage;
                    InstanceInstallProgressText = $"{LocalizationHelper.GetInstallStage(value.Stage)} - {value.CurrentFile ?? string.Empty}";
                });
                void OnDownloadStarting() => preparationLoading = _loadingService.Begin();

                var instance = await _instanceManagerService.CreateAsync(
                    NewInstanceName,
                    SelectedNewInstanceVersion,
                    SelectedNewInstanceLoader,
                    _config.GameDirectory,
                    _sharedMinecraftPath,
                    progress,
                    OnDownloadStarting);
                _config.MinecraftInstances.Add(instance);
                Instances.Add(instance);
                _config.Save();
                SelectedInstance = instance;
                NewInstanceName = string.Empty;
                StatusMessage = LocalizationHelper.Format("Lang_InstanceManager_Created", instance.Name);
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_InstanceManager_CreateFailed", ex.Message);
            }
            finally
            {
                preparationLoading?.Dispose();
                IsCreatingInstance = false;
                IsInstanceInstallProgressVisible = false;
                IsInstanceDownloadActive = false;
                InstanceInstallProgressText = null;
            }
        }

        private static bool IsInstanceDownloadStage(string stage)
            => string.Equals(stage, "Downloading", StringComparison.OrdinalIgnoreCase)
                || stage.StartsWith("Downloading Minecraft", StringComparison.OrdinalIgnoreCase)
                || stage.StartsWith("Fetching ", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stage, "Installing Forge", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stage, "Installing NeoForge", StringComparison.OrdinalIgnoreCase);

        [RelayCommand]
        private void RequestDeleteInstance(MinecraftInstanceEntry instance)
        {
            if (instance.IsDefault)
                return;

            PendingDeleteInstance = instance;
            ShowDeleteInstanceConfirmation = true;
        }

        [RelayCommand]
        private void CancelDeleteInstance()
        {
            PendingDeleteInstance = null;
            ShowDeleteInstanceConfirmation = false;
        }

        [RelayCommand]
        private async Task ConfirmDeleteInstance()
        {
            var instance = PendingDeleteInstance;
            if (instance is null || instance.IsDefault)
                return;

            using var loading = _loadingService.Begin();
            try
            {
                await _instanceManagerService.DeleteAsync(instance, _config.GameDirectory);
                _config.MinecraftInstances.Remove(instance);
                Instances.Remove(instance);
                if (SelectedInstance?.Id == instance.Id)
                    SelectedInstance = Instances.FirstOrDefault(candidate => candidate.IsDefault);
                _config.Save();
                StatusMessage = LocalizationHelper.Format("Lang_InstanceManager_Deleted", instance.Name);
                CancelDeleteInstance();
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_InstanceManager_DeleteFailed", ex.Message);
            }
        }

        [RelayCommand]
        private void SelectInstance(MinecraftInstanceEntry instance)
        {
            SelectedInstance = instance;
            CloseInstanceManager();
        }

        public async Task ImportModpackAsync(Stream archiveStream, string archiveName)
        {
            if (IsImportingModpack)
                return;
            if (_curseForgeService is not CurseForgeExploreService curseForgeService)
                throw new InvalidOperationException("The CurseForge modpack service is not available.");

            using var loading = _loadingService.Begin();
            IsImportingModpack = true;
            StatusMessage = LocalizationHelper.Get("Lang_ModsManager_Importing");
            try
            {
                var progress = new Progress<string>(message => StatusMessage = message);
                var instance = await _modpackImportService.ImportAsync(
                    archiveStream,
                    archiveName,
                    _config.GameDirectory,
                    curseForgeService,
                    _sharedMinecraftPath,
                    progress);
                _config.MinecraftInstances.Add(instance);
                _config.Save();
                Instances.Add(instance);
                SelectedInstance = instance;
                var completeMessage = LocalizationHelper.Format("Lang_ModsManager_Imported", instance.Name);
                StatusMessage = completeMessage;
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_ModsManager_ImportFailed", ex.Message);
            }
            finally
            {
                IsImportingModpack = false;
            }
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
        partial void OnSelectedTypeChanged(string value)
        {
            OnPropertyChanged(nameof(SelectedTypeOption));
            if (_suppressFilterSearch)
                return;

            CurrentPage = 1;
            _ = PerformSearchCore();
        }

        partial void OnSelectedSortChanged(string value)
        {
            OnPropertyChanged(nameof(SelectedSortOption));
            CurrentPage = 1;
            _ = PerformSearchCore();
        }

        [RelayCommand]
        private void ShowBrowse()
        {
            IsManagerView = false;
        }

        [RelayCommand]
        private void ShowManager()
        {
            IsManagerView = true;
        }

        [RelayCommand]
        private void RefreshManagedContent()
        {
            InstalledContent.Clear();
            if (SelectedInstance is null || !Directory.Exists(SelectedInstance.GameDirectory))
            {
                OnPropertyChanged(nameof(FilteredInstalledContent));
                OnPropertyChanged(nameof(HasInstalledContent));
                return;
            }

            try
            {
                foreach (var item in _contentManager.Scan(SelectedInstance))
                    InstalledContent.Add(item);
                OnPropertyChanged(nameof(FilteredInstalledContent));
                OnPropertyChanged(nameof(HasInstalledContent));
            }
            catch (Exception ex)
            {
                InstalledContent.Clear();
                OnPropertyChanged(nameof(FilteredInstalledContent));
                OnPropertyChanged(nameof(HasInstalledContent));
                StatusMessage = LocalizationHelper.Format("Lang_ModsManager_ActionFailed", ex.Message);
            }
        }

        [RelayCommand]
        private async Task ToggleContent(InstalledContentItem item)
        {
            if (SelectedInstance is null)
                return;

            try
            {
                await _contentManager.SetEnabledAsync(SelectedInstance, item, !item.IsEnabled);
                RefreshManagedContent();
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_ModsManager_ActionFailed", ex.Message);
            }
        }

        [RelayCommand]
        private void RequestRemoveContent(InstalledContentItem item)
        {
            PendingRemovalItem = item;
            PendingRemovalInstance = SelectedInstance;
            ShowRemoveConfirmation = true;
        }

        [RelayCommand]
        private void CancelRemoveContent()
        {
            ShowRemoveConfirmation = false;
            PendingRemovalItem = null;
            PendingRemovalInstance = null;
        }

        [RelayCommand]
        private async Task ConfirmRemoveContent()
        {
            var instance = PendingRemovalInstance;
            var item = PendingRemovalItem;
            if (instance is null || item is null)
                return;

            try
            {
                await _contentManager.RemoveAsync(instance, item);
                StatusMessage = LocalizationHelper.Format("Lang_ModsManager_Removed", item.Name);
                ShowRemoveConfirmation = false;
                PendingRemovalItem = null;
                PendingRemovalInstance = null;
                RefreshManagedContent();
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_ModsManager_ActionFailed", ex.Message);
            }
        }

        [RelayCommand]
        private async Task UpdateManagedContent(InstalledContentItem item)
        {
            var instance = SelectedInstance;
            var record = item.ManagedRecord;
            if (instance is null || !item.IsManaged || record is null)
                return;

            using var loading = _loadingService.Begin();
            try
            {
                var target = instance.IsDefault
                    ? ExploreInstallHelper.ParseVersionName(_selectedVersionName)
                    : new InstallTarget(instance.MinecraftVersion, instance.Loader?.ToLowerInvariant());
                var service = GetExploreService(record.Source);
                var versions = await service.GetProjectVersionsAsync(record.ProjectId, target.GameVersion);
                var best = ExploreInstallHelper.PickBest(versions, target, record.ProjectType);
                if (best is null)
                {
                    StatusMessage = LocalizationHelper.Get("Lang_ModsManager_NoUpdateAvailable");
                    return;
                }

                var targetFolder = ExploreInstallHelper.GetTargetFolder(instance.GameDirectory, record.ProjectType);
                Directory.CreateDirectory(targetFolder);
                var fileName = ExploreInstallHelper.SafeFileName(best.Filename);
                var destination = Path.Combine(targetFolder, fileName + (item.IsEnabled ? string.Empty : ".disabled"));
                var oldPath = Path.GetFullPath(Path.Combine(
                    instance.GameDirectory,
                    item.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                var instanceRoot = Path.GetFullPath(instance.GameDirectory).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (!oldPath.StartsWith(instanceRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The selected content path is outside this instance.");
                if (!string.Equals(oldPath, destination, StringComparison.OrdinalIgnoreCase) && File.Exists(destination))
                    throw new IOException("Another installed item already uses the updated filename.");

                var temporaryPath = Path.Combine(targetFolder, $".{Guid.NewGuid():N}.download");
                try
                {
                    await service.DownloadFileAsync(best.DownloadUrl, temporaryPath);
                    File.Move(temporaryPath, destination, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }

                if (!string.Equals(oldPath, destination, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
                    File.Delete(oldPath);

                await _contentManager.RecordInstalledAsync(instance, record with
                {
                    RelativePath = Path.GetRelativePath(instance.GameDirectory, destination).Replace(Path.DirectorySeparatorChar, '/'),
                    VersionId = best.Id,
                    Name = record.Name
                }, item.RelativePath);
                StatusMessage = LocalizationHelper.Format("Lang_ModsManager_Updated", item.Name);
                RefreshManagedContent();
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_ModsManager_ActionFailed", ex.Message);
            }
        }

        partial void OnSelectedSourceChanged(string value)
        {
            OnPropertyChanged(nameof(IsModrinthSelected));
            OnPropertyChanged(nameof(IsCurseForgeSelected));

            _suppressFilterSearch = true;
            if (value == "CurseForge" && SelectedType != "mod")
                SelectedType = "mod";
            _suppressFilterSearch = false;

            RefreshLocalizedOptions();
            SearchResults.Clear();
            CurrentPage = 1;
            _ = PerformSearchCore();
        }

        private void RefreshLocalizedOptions()
        {
            SortOptions.Clear();
            SortOptions.Add(new LocalizedChoice("relevance", LocalizationHelper.Get("Lang_MinecraftMods_SortRelevance")));
            SortOptions.Add(new LocalizedChoice("downloads", LocalizationHelper.Get("Lang_MinecraftMods_SortDownloads")));
            SortOptions.Add(new LocalizedChoice("newest", LocalizationHelper.Get("Lang_MinecraftMods_SortNewest")));
            SortOptions.Add(new LocalizedChoice("updated", LocalizationHelper.Get("Lang_MinecraftMods_SortUpdated")));

            TypeOptions.Clear();
            TypeOptions.Add(new LocalizedChoice("mod", LocalizationHelper.Get("Lang_MinecraftMods_TypeMod")));
            if (SelectedSource == "Modrinth")
            {
                TypeOptions.Add(new LocalizedChoice("resourcepack", LocalizationHelper.Get("Lang_MinecraftMods_TypeResourcePack")));
                TypeOptions.Add(new LocalizedChoice("shader", LocalizationHelper.Get("Lang_MinecraftMods_TypeShader")));
                TypeOptions.Add(new LocalizedChoice("datapack", LocalizationHelper.Get("Lang_MinecraftMods_TypeDataPack")));
            }

            OnPropertyChanged(nameof(SelectedSortOption));
            OnPropertyChanged(nameof(SelectedTypeOption));
        }

        private void RefreshManagerCategories()
        {
            ManagerCategoryOptions.Clear();
            ManagerCategoryOptions.Add(new LocalizedChoice("all", LocalizationHelper.Get("Lang_ModsManager_All")));
            ManagerCategoryOptions.Add(new LocalizedChoice("mod", LocalizationHelper.Get("Lang_MinecraftMods_TypeMod")));
            ManagerCategoryOptions.Add(new LocalizedChoice("resourcepack", LocalizationHelper.Get("Lang_MinecraftMods_TypeResourcePack")));
            ManagerCategoryOptions.Add(new LocalizedChoice("shader", LocalizationHelper.Get("Lang_MinecraftMods_TypeShader")));
            ManagerCategoryOptions.Add(new LocalizedChoice("datapack", LocalizationHelper.Get("Lang_MinecraftMods_TypeDataPack")));
            OnPropertyChanged(nameof(SelectedManagerCategoryOption));
        }

        private void RefreshInstanceLoaderOptions()
        {
            InstanceLoaderOptions.Clear();
            InstanceLoaderOptions.Add(new LocalizedChoice("Vanilla", LocalizationHelper.Get("Lang_InstanceManager_Vanilla")));
            InstanceLoaderOptions.Add(new LocalizedChoice("Fabric", "Fabric"));
            InstanceLoaderOptions.Add(new LocalizedChoice("Forge", "Forge"));
            InstanceLoaderOptions.Add(new LocalizedChoice("NeoForge", "NeoForge"));
            InstanceLoaderOptions.Add(new LocalizedChoice("Quilt", "Quilt"));
            OnPropertyChanged(nameof(SelectedNewInstanceLoaderOption));
        }

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
            using var loading = _loadingService.Begin();
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

                var result = await GetExploreService(SelectedSource).SearchAsync(query, cts.Token);
                if (version != _searchVersion) return;

                SearchResults.Clear();
                foreach (var project in result.Projects)
                {
                    SearchResults.Add(project);
                }

                HasNextPage = CurrentPage * PageSize < result.TotalCount;

                if (SearchResults.Count == 0)
                {
                    StatusMessage = LocalizationHelper.Get("Lang_MinecraftMods_NoResults");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (version == _searchVersion)
                {
                    SearchResults.Clear();
                    HasNextPage = false;
                    StatusMessage = LocalizationHelper.Format("Lang_MinecraftMods_SearchFailed", ex.Message);
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
            var modsFolder = ExploreInstallHelper.GetTargetFolder(CurrentGameDirectory, "mod");
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
                StatusMessage = LocalizationHelper.Format("Lang_MinecraftMods_OpenFolderFailed", ex.Message);
            }
        }

        [RelayCommand]
        private async Task InstallProject(ExploreProject project)
        {
            using var loading = _loadingService.Begin();
            IsInstallingProject = true;
            StatusMessage = LocalizationHelper.Format("Lang_MinecraftMods_Downloading", project.Title);
            try
            {
                var target = ExploreInstallHelper.ParseVersionName(_selectedVersionName);
                var service = GetExploreService(project.Source);
                var versions = await service.GetProjectVersionsAsync(project.Id, target.GameVersion);
                var best = ExploreInstallHelper.PickBest(versions, target, project.ProjectType);

                if (best == null)
                {
                    StatusMessage = LocalizationHelper.Format("Lang_MinecraftMods_NoCompatibleProjectVersion", project.Title);
                    TriggerValidationPopup();
                    return;
                }

                string targetFolder = ExploreInstallHelper.GetTargetFolder(CurrentGameDirectory, project.ProjectType);
                Directory.CreateDirectory(targetFolder);

                string destPath = Path.Combine(targetFolder, ExploreInstallHelper.SafeFileName(best.Filename));
                var temporaryPath = Path.Combine(targetFolder, $".{Guid.NewGuid():N}.download");
                try
                {
                    await service.DownloadFileAsync(best.DownloadUrl, temporaryPath);
                    File.Move(temporaryPath, destPath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }

                var successMessage = LocalizationHelper.Format("Lang_MinecraftMods_Installed", best.Filename);
                var instance = SelectedInstance;
                if (instance is not null)
                {
                    await _contentManager.RecordInstalledAsync(instance, new InstalledContentRecord(
                        Path.GetRelativePath(instance.GameDirectory, destPath).Replace(Path.DirectorySeparatorChar, '/'),
                        project.Source,
                        project.Id,
                        best.Id,
                        project.Title,
                        project.ProjectType,
                        target.GameVersion,
                        target.Loader));
                    RefreshManagedContent();
                }
                StatusMessage = successMessage;
            }
            catch (Exception ex)
            {
                StatusMessage = LocalizationHelper.Format("Lang_MinecraftMods_InstallFailed", project.Title, ex.Message);
            }
            finally
            {
                IsInstallingProject = false;
            }
        }

        [RelayCommand]
        private async Task ViewProject(ExploreProject project)
        {
            var vm = new ModDetailPageViewModel(GetExploreService(project.Source), CurrentGameDirectory,
                onBack: () => IsDetailViewOpen = false,
                onValidationFailed: () => TriggerValidationPopup(),
                getSelectedVersionName: () => _selectedVersionName);

            DetailViewModel = vm;
            IsDetailViewOpen = true;

            await vm.LoadProjectAsync(project.Id);
        }

        [RelayCommand]
        private void SelectSource(string source)
        {
            if (source is "Modrinth" or "CurseForge")
                SelectedSource = source;
        }

        private IExploreService GetExploreService(string source)
            => source switch
            {
                "Modrinth" => _modrinthService,
                "CurseForge" => _curseForgeService,
                _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown mod source.")
            };

        private void TriggerValidationPopup()
        {
            ShowValidationPopup = true;
            _validationPopupAutoDismiss.Schedule(
                "validation",
                () => ShowValidationPopup ? "validation" : null,
                () => ShowValidationPopup = false);
        }
    }
}
