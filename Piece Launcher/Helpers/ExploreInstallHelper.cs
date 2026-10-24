using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Piece_Launcher.Helpers
{
    public readonly record struct InstallTarget(string? GameVersion, string? Loader);

    public static class ExploreInstallHelper
    {
        private static readonly Regex NeoForgeRegex = new(@"neoforge-(\d+)\.(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex ReleaseRegex = new(@"(?<![\d.])1\.\d+(?:\.\d+)?(?![\d.])");
        private static readonly Regex SnapshotRegex = new(@"\d{2}w\d{2}[a-z]");

        public static InstallTarget ParseVersionName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new InstallTarget(null, null);

            string? loader = null;
            if (name.Contains("neoforge", StringComparison.OrdinalIgnoreCase)) loader = "neoforge";
            else if (name.Contains("forge", StringComparison.OrdinalIgnoreCase)) loader = "forge";
            else if (name.Contains("fabric", StringComparison.OrdinalIgnoreCase)) loader = "fabric";
            else if (name.Contains("quilt", StringComparison.OrdinalIgnoreCase)) loader = "quilt";

            string? gameVersion = null;
            var neo = NeoForgeRegex.Match(name);
            if (neo.Success)
            {
                var major = neo.Groups[1].Value;
                var minor = neo.Groups[2].Value;
                gameVersion = minor == "0" ? $"1.{major}" : $"1.{major}.{minor}";
            }
            else
            {
                var matches = ReleaseRegex.Matches(name);
                if (matches.Count > 0)
                {
                    var useLast = loader is "fabric" or "quilt";
                    gameVersion = (useLast ? matches[^1] : matches[0]).Value;
                }
                else
                {
                    var snapshot = SnapshotRegex.Match(name);
                    if (snapshot.Success) gameVersion = snapshot.Value;
                }
            }

            return new InstallTarget(gameVersion, loader);
        }

        public static string GetFolderName(string? projectType) => projectType switch
        {
            "resourcepack" => "resourcepacks",
            "shader" => "shaderpacks",
            "datapack" => "datapacks",
            _ => "mods"
        };

        public static string GetTargetFolder(LauncherConfig config, string? projectType)
            => Path.Combine(config.GameDirectory ?? "", GetFolderName(projectType));

        public static string SafeFileName(string filename)
        {
            var name = Path.GetFileName(filename);
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "download.jar" : name;
        }

        public static string? GetLoaderFilter(string? projectType, InstallTarget target)
            => (projectType ?? "mod") == "mod" ? target.Loader : null;

        public static bool IsCompatible(ExploreProjectVersion version, InstallTarget target, string? projectType)
        {
            if (string.IsNullOrEmpty(version.DownloadUrl)) return false;

            if (target.GameVersion != null &&
                !version.GameVersions.Contains(target.GameVersion, StringComparer.OrdinalIgnoreCase))
                return false;

            var loader = GetLoaderFilter(projectType, target);
            if (loader != null)
            {
                bool ok = version.Loaders.Any(l =>
                    l.Equals(loader, StringComparison.OrdinalIgnoreCase) ||
                    (loader == "quilt" && l.Equals("fabric", StringComparison.OrdinalIgnoreCase)));
                if (!ok) return false;
            }

            return true;
        }

        public static ExploreProjectVersion? PickBest(IEnumerable<ExploreProjectVersion> versions, InstallTarget target, string? projectType)
        {
            var compatible = versions.Where(v => IsCompatible(v, target, projectType)).ToList();
            return compatible.FirstOrDefault(v => v.VersionType == "release") ?? compatible.FirstOrDefault();
        }
    }
}
