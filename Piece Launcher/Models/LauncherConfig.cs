using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Piece_Launcher.Models
{
    public enum ProfileType { Offline, Microsoft }

    public class ProfileEntry : ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        private string _name = "Steve";
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        public string? SkinPath { get; set; }

        [JsonIgnore]
        private Bitmap? _headImage;
        [JsonIgnore]
        public Bitmap? HeadImage { get => _headImage; set => SetProperty(ref _headImage, value); }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ProfileType Type { get; set; } = ProfileType.Offline;

        public override string ToString() => Name;
    }

    public class LauncherConfig : ObservableObject
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PieceLauncher");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
        public static string DataDirectory => ConfigDir;

        public string ClientId { get; set; } = string.Empty;
        public bool UseCustomClientId { get; set; }
        public List<ProfileEntry> Profiles { get; set; } = new();
        public string? LastSelectedVersion { get; set; }
        public string? LastSelectedProfileId { get; set; }

        // New settings (Observable)
        private string _language = "English";
        public string Language { get => _language; set => SetProperty(ref _language, value); }
        
        private string _theme = "DarkOrange";
        public string Theme { get => _theme; set => SetProperty(ref _theme, value); }
        
        private string _startupPage = "Home";
        public string StartupPage { get => _startupPage; set => SetProperty(ref _startupPage, value); }

        private bool _launchOnStartup = false;
        public bool LaunchOnStartup { get => _launchOnStartup; set => SetProperty(ref _launchOnStartup, value); }
        
        private bool _checkLauncherUpdates = true;
        public bool CheckLauncherUpdates { get => _checkLauncherUpdates; set => SetProperty(ref _checkLauncherUpdates, value); }
        
        private bool _keepOpenWhilePlaying = true;
        public bool KeepOpenWhilePlaying { get => _keepOpenWhilePlaying; set => SetProperty(ref _keepOpenWhilePlaying, value); }
        
        public bool MinimizeToTray { get; set; } = false;
        public bool DiscordRichPresence { get; set; } = true;
        public bool EnableAnimations { get; set; } = true;
        public bool ReducedMotion { get; set; } = false;

        // Minecraft
        private string _defaultJava = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java", "jdk-21", "bin", "java.exe");
        public string DefaultJava { get => _defaultJava; set => SetProperty(ref _defaultJava, value); }

        private bool _autoDetectJava = true;
        public bool AutoDetectJava
        {
            get => _autoDetectJava;
            set
            {
                if (SetProperty(ref _autoDetectJava, value))
                    UseCustomJava = !value;
            }
        }

        private bool _useCustomJava;
        public bool UseCustomJava
        {
            get => _useCustomJava;
            set
            {
                if (SetProperty(ref _useCustomJava, value))
                    AutoDetectJava = !value;
            }
        }

        private string _gameDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PieceLauncher", "Instances");
        public string GameDirectory { get => _gameDirectory; set => SetProperty(ref _gameDirectory, value); }

        private string _downloadsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PieceLauncher", "Downloads");
        public string DownloadsDirectory { get => _downloadsDirectory; set => SetProperty(ref _downloadsDirectory, value); }
        public bool VerifyBeforeLaunch { get; set; } = true;
        public bool VerifyAssets { get; set; } = true;
        public bool ShowMinecraftConsole { get; set; } = false;
        public bool ShowLauncherConsole { get; set; } = false;
        public bool CheckMissingDependencies { get; set; } = true;

        // Memory
        private bool _autoRam = true;
        public bool AutoRam { get => _autoRam; set => SetProperty(ref _autoRam, value); }

        private int _recommendedRamGb = 4;
        public int RecommendedRamGb { get => _recommendedRamGb; set => SetProperty(ref _recommendedRamGb, value); }

        private int _customRamGb = 4;
        public int CustomRamGb { get => _customRamGb; set => SetProperty(ref _customRamGb, value); }
        public bool AutoAdjustForModded { get; set; } = true;
        private bool _safeModeLimit = true;
        public bool SafeModeLimit { get => _safeModeLimit; set => SetProperty(ref _safeModeLimit, value); }
        public string SafeModeMaximum { get; set; } = "RAM / 2";
        public bool AdvancedRamMode { get; set; } = false;
        public string AdvancedMaximum { get; set; } = "100% RAM";

        // Performance
        public string PerformancePreset { get; set; } = "Balanced";

        // Versions
        private bool _showSnapshots;
        public bool ShowSnapshots { get => _showSnapshots; set => SetProperty(ref _showSnapshots, value); }

        private bool _showBetas;
        public bool ShowBetas { get => _showBetas; set => SetProperty(ref _showBetas, value); }

        private bool _showAlphas;
        public bool ShowAlphas { get => _showAlphas; set => SetProperty(ref _showAlphas, value); }
        public bool ShowExperimental { get; set; } = false;
        private bool _showOldVersions;
        public bool ShowOldVersions { get => _showOldVersions; set => SetProperty(ref _showOldVersions, value); }
        public bool ShowInstalledOnly { get; set; } = true;
        private string _defaultLoader = "Vanilla";
        public string DefaultLoader { get => _defaultLoader; set => SetProperty(ref _defaultLoader, value); }
        public bool ShowOnlyCompatibleMods { get; set; } = true;
        public bool AutoInstallDependencies { get; set; } = true;
        public bool SuggestCompatibleVersions { get; set; } = true;

        // Downloads
        public int MaxSimultaneousDownloads { get; set; } = 3;
        public string DownloadSpeedLimit { get; set; } = "Unlimited";
        public bool PauseDownloadsWhilePlaying { get; set; } = true;
        public bool ResumeInterrupted { get; set; } = true;
        public bool VerifyDownloads { get; set; } = true;
        public bool RetryFailedDownloads { get; set; } = true;

        // Explore
        public bool ExploreModrinth { get; set; } = true;
        public bool ExploreCurseForge { get; set; } = true;
        public bool ExploreOnlyCompatible { get; set; } = true;
        public bool ExploreAutoInstallDeps { get; set; } = true;
        public bool ExploreHideNSFW { get; set; } = false;

        // Community
        public bool CommunityEnableNotifications { get; set; } = true;
        public bool CommunityShowFeaturedMaps { get; set; } = true;
        public bool CommunityShowCreatorUpdates { get; set; } = true;
        public bool CommunityShowNews { get; set; } = true;
        public bool CommunityAllowComments { get; set; } = true;
        public bool CommunityShowMature { get; set; } = false;

        // Accounts
        public bool RememberLogin { get; set; } = true;
        public bool AutoLogin { get; set; } = false;
        public bool AskPasswordBeforeLaunch { get; set; } = false;
        public bool AllowMultipleAccounts { get; set; } = false;

        // Backups
        public bool EnableAutomaticBackups { get; set; } = true;
        public string BackupFrequency { get; set; } = "Daily";
        public int BackupMaximum { get; set; } = 10;
        public bool BackupProfiles { get; set; } = true;
        public bool BackupSaves { get; set; } = true;
        public bool BackupMods { get; set; } = true;
        public bool BackupResourcePacks { get; set; } = true;
        public bool BackupSettings { get; set; } = true;

        // Advanced
        private string _javaArguments = "-XX:+UseG1GC";
        public string JavaArguments { get => _javaArguments; set => SetProperty(ref _javaArguments, value); }
        public bool EnableDebugLogs { get; set; } = false;
        public bool EnableDeveloperMode { get; set; } = false;
        public bool ShowApiRequests { get; set; } = false;
        public bool ForceSoftwareRendering { get; set; } = false;
        public bool EnableExperimentalFeatures { get; set; } = false;
        public bool EnableBetaFeatures { get; set; } = false;

        public static LauncherConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var config = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(ConfigPath))
                                 ?? new LauncherConfig();
                    var migrated = false;

                    if (config.GameDirectory == @"D:\\PieceLauncher\\Instances")
                    {
                        config.GameDirectory = new LauncherConfig().GameDirectory;
                        migrated = true;
                    }

                    if (config.DownloadsDirectory == @"D:\\PieceLauncher\\Downloads")
                    {
                        config.DownloadsDirectory = new LauncherConfig().DownloadsDirectory;
                        migrated = true;
                    }

                    if (config.DefaultJava == @"C:\\Program Files\\Java\\jdk-21\\bin\\java.exe")
                    {
                        config.DefaultJava = new LauncherConfig().DefaultJava;
                        migrated = true;
                    }

                    if (migrated)
                        config.Save();

                    return config;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Config Load] {ex.Message}");
            }
            return new LauncherConfig();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ConfigPath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Config Save] {ex.Message}");
            }
        }
    }
}