using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class VersionsPageViewModel : ViewModelBase
    {
        private readonly IVersionService _versionService;
        private readonly IModLoaderService _forgeService;
        private readonly IModLoaderService _fabricService;
        private readonly IModLoaderService _neoForgeService;
        private readonly IModLoaderService _quiltService;
        private readonly LauncherConfig _config;

        public event Action? VersionInstalled;

        // ─── Game Versions ───
        public ObservableCollection<VersionEntry> AllVersions { get; } = new();
        public ObservableCollection<VersionEntry> FilteredVersions { get; } = new();

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

        // ─── Mod Loaders ───
        public ObservableCollection<string> MinecraftReleaseVersions { get; } = new();
        [ObservableProperty] private string? _selectedMinecraftVersion;

        [ObservableProperty] private string _selectedLoader = "Vanilla";
        public bool IsVanillaSelected => string.Equals(SelectedLoader, "Vanilla", StringComparison.OrdinalIgnoreCase);
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
            IModLoaderService forgeService,
            IModLoaderService fabricService,
            IModLoaderService neoForgeService,
            IModLoaderService quiltService,
            LauncherConfig config)
        {
            _versionService = versionService;
            _forgeService = forgeService;
            _fabricService = fabricService;
            _neoForgeService = neoForgeService;
            _quiltService = quiltService;
            _config = config;

            ShowSnapshots = config.ShowSnapshots;
            ShowBetas = config.ShowBetas;
            ShowAlphas = config.ShowAlphas;
            ShowOldVersions = config.ShowOldVersions;
            SelectedLoader = config.DefaultLoader is "Vanilla" or "Fabric" or "Forge" or "NeoForge" or "Quilt"
                ? config.DefaultLoader
                : "Vanilla";
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

        partial void OnSelectedMinecraftVersionChanged(string? value)
        {
            _ = LoadLoaderVersionsAsync();
        }

        partial void OnSelectedLoaderChanged(string value)
        {
            _config.DefaultLoader = value;
            OnPropertyChanged(nameof(IsVanillaSelected));
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
                    SelectedLoader = _config.DefaultLoader;
                    break;
            }
        }

        [RelayCommand]
        public void SelectLoader(string loaderName)
        {
            SelectedLoader = loaderName;
        }

        public async Task LoadVersionsAsync()
        {
            IsLoading = true;
            try
            {
                var versions = await _versionService.GetAllVersionsAsync();
                AllVersions.Clear();
                MinecraftReleaseVersions.Clear();

                foreach (var v in versions)
                {
                    AllVersions.Add(v);
                    if (v.Type.Equals("release", StringComparison.OrdinalIgnoreCase))
                    {
                        MinecraftReleaseVersions.Add(v.Name);
                    }
                }

                if (string.IsNullOrEmpty(SelectedMinecraftVersion) || !MinecraftReleaseVersions.Contains(SelectedMinecraftVersion))
                {
                    SelectedMinecraftVersion = MinecraftReleaseVersions.FirstOrDefault();
                }

                ApplyFilters();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erro ao carregar versões: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task LoadLoaderVersionsAsync()
        {
            if (string.IsNullOrEmpty(SelectedMinecraftVersion)) return;

            IsLoadingLoaderVersions = true;
            AvailableLoaderVersions.Clear();
            SelectedLoaderVersionEntry = null;
            var service = GetServiceForLoader(SelectedLoader);
            if (service == null)
            {
                IsCurrentLoaderInstalled = IsVanillaSelected;
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
                StatusMessage = $"Erro ao buscar versões de {SelectedLoader}: {ex.Message}";
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

        [RelayCommand]
        public async Task InstallVersion(string versionName)
        {
            if (IsInstalling) return;

            IsInstalling = true;
            InstallProgressValue = 0;
            InstallProgressText = $"Iniciando download de {versionName}...";

            var progress = new Progress<InstallProgress>(p =>
            {
                InstallProgressValue = p.Percentage;
                InstallProgressText = $"{p.Stage} - {p.CurrentFile ?? ""}";
            });

            try
            {
                await _versionService.InstallVersionAsync(versionName, progress);
                StatusMessage = $"Versão {versionName} instalada com sucesso!";
                await LoadVersionsAsync();
                VersionInstalled?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Falha ao instalar {versionName}: {ex.Message}";
            }
            finally
            {
                IsInstalling = false;
                InstallProgressText = null;
            }
        }

        [RelayCommand]
        public async Task InstallSelectedLoader()
        {
            if (IsInstalling || string.IsNullOrEmpty(SelectedMinecraftVersion)) return;

            var service = GetServiceForLoader(SelectedLoader);
            if (service == null) return;

            var loaderVersion = SelectedLoaderVersionEntry?.Version ?? "";
            IsInstalling = true;
            InstallProgressValue = 0;
            InstallProgressText = $"Instalando {SelectedLoader} ({loaderVersion}) para Minecraft {SelectedMinecraftVersion}...";

            var progress = new Progress<InstallProgress>(p =>
            {
                InstallProgressValue = p.Percentage;
                InstallProgressText = $"{p.Stage} - {p.CurrentFile ?? ""}";
            });

            try
            {
                var installedId = await service.InstallAsync(SelectedMinecraftVersion, loaderVersion, progress);
                StatusMessage = $"{SelectedLoader} ({installedId}) instalado com sucesso!";
                await LoadVersionsAsync();
                await LoadLoaderVersionsAsync();
                VersionInstalled?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Falha ao instalar {SelectedLoader}: {ex.Message}";
            }
            finally
            {
                IsInstalling = false;
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
    }
}
