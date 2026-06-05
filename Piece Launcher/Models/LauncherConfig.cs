using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Piece_Launcher.Models
{
    public enum ProfileType { Offline, Microsoft }

    public class ProfileEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Steve";

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ProfileType Type { get; set; } = ProfileType.Offline;

        public override string ToString() => Name;
    }

    public class LauncherConfig
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PieceLauncher");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        public string ClientId { get; set; } = string.Empty;
        public bool UseCustomClientId { get; set; }
        public List<ProfileEntry> Profiles { get; set; } = new();
        public string? LastSelectedVersion { get; set; }
        public string? LastSelectedProfileId { get; set; }

        // New settings
        public string Language { get; set; } = "Português";
        public string Theme { get; set; } = "Dark Orange";
        public string StartupPage { get; set; } = "Home";

        public bool LaunchOnStartup { get; set; } = false;
        public bool CheckLauncherUpdates { get; set; } = true;
        public bool KeepOpenWhilePlaying { get; set; } = true;
        public bool MinimizeToTray { get; set; } = false;
        public bool DiscordRichPresence { get; set; } = true;
        public bool EnableAnimations { get; set; } = true;
        public bool ReducedMotion { get; set; } = false;

        // Minecraft
        public string DefaultJava { get; set; } = @"C:\\Program Files\\Java\\jdk-21\\bin\\java.exe";
        public bool AutoDetectJava { get; set; } = true;
        public bool UseCustomJava { get; set; } = false;
        public string GameDirectory { get; set; } = @"D:\\PieceLauncher\\Instances";
        public string DownloadsDirectory { get; set; } = @"D:\\PieceLauncher\\Downloads";
        public bool VerifyBeforeLaunch { get; set; } = true;
        public bool VerifyAssets { get; set; } = true;
        public bool ShowMinecraftConsole { get; set; } = false;
        public bool ShowLauncherConsole { get; set; } = false;
        public bool CheckMissingDependencies { get; set; } = true;

        // Memory
        public bool AutoRam { get; set; } = true;
        public int RecommendedRamGb { get; set; } = 4;
        public int CustomRamGb { get; set; } = 4;
        public bool AutoAdjustForModded { get; set; } = true;
        public bool SafeModeLimit { get; set; } = true;
        public string SafeModeMaximum { get; set; } = "RAM / 2";
        public bool AdvancedRamMode { get; set; } = false;
        public string AdvancedMaximum { get; set; } = "100% RAM";

        // Performance
        public string PerformancePreset { get; set; } = "Balanced";

        // Versions
        public bool ShowSnapshots { get; set; } = false;
        public bool ShowBetas { get; set; } = false;
        public bool ShowAlphas { get; set; } = false;
        public bool ShowExperimental { get; set; } = false;
        public bool ShowOldVersions { get; set; } = false;
        public bool ShowInstalledOnly { get; set; } = true;
        public string DefaultLoader { get; set; } = "Vanilla";
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
        public string JavaArguments { get; set; } = "-XX:+UseG1GC";
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
                    return JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(ConfigPath))
                           ?? new LauncherConfig();
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