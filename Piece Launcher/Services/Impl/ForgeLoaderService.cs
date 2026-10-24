using CmlLib.Core;
using CmlLib.Core.Installer.Forge;
using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public class ForgeLoaderService : IModLoaderService
    {
        private readonly MinecraftLauncher _launcher;

        public ForgeLoaderService(MinecraftLauncher launcher)
        {
            _launcher = launcher;
        }

        public string LoaderName => "Forge";

        public async Task<IReadOnlyList<LoaderVersionEntry>> GetAvailableVersionsAsync(string minecraftVersion, CancellationToken ct = default)
        {
            try
            {
                var installer = new ForgeInstaller(_launcher);
                var versions = await installer.GetForgeVersions(minecraftVersion);
                return versions.Select(v => new LoaderVersionEntry(
                    v.ForgeVersionName ?? "",
                    minecraftVersion,
                    v.IsRecommendedVersion,
                    IsForgeInstalled(minecraftVersion, v.ForgeVersionName ?? "")
                )).ToList();
            }
            catch
            {
                return Array.Empty<LoaderVersionEntry>();
            }
        }

        public async Task<string> InstallAsync(string minecraftVersion, string loaderVersion, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
        {
            progress?.Report(new InstallProgress("Preparing Forge", 10, $"{minecraftVersion} - {loaderVersion}"));
            var installer = new ForgeInstaller(_launcher);
            
            // If loaderVersion is empty, pick recommended or latest
            if (string.IsNullOrEmpty(loaderVersion))
            {
                var available = await installer.GetForgeVersions(minecraftVersion);
                var recommended = available.FirstOrDefault(v => v.IsRecommendedVersion) ?? available.FirstOrDefault();
                if (recommended == null)
                    throw new InvalidOperationException($"No Forge versions found for Minecraft {minecraftVersion}");
                loaderVersion = recommended.ForgeVersionName ?? "";
            }

            progress?.Report(new InstallProgress("Installing Forge", 50, $"{loaderVersion}"));
            var result = await installer.Install(minecraftVersion, loaderVersion);
            progress?.Report(new InstallProgress("Forge Installed", 100, result));
            return result;
        }

        public Task<bool> IsInstalledAsync(string minecraftVersion, CancellationToken ct = default)
        {
            var versionsDir = new DirectoryInfo(_launcher.MinecraftPath.Versions);
            if (!versionsDir.Exists) return Task.FromResult(false);

            bool exists = versionsDir.GetDirectories().Any(d =>
                d.Name.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase) &&
                d.Name.Contains("forge", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(d.FullName, $"{d.Name}.json")));

            return Task.FromResult(exists);
        }

        private bool IsForgeInstalled(string minecraftVersion, string forgeVersion)
        {
            var versionsDir = new DirectoryInfo(_launcher.MinecraftPath.Versions);
            if (!versionsDir.Exists) return false;

            return versionsDir.GetDirectories().Any(d =>
                d.Name.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase) &&
                d.Name.Contains(forgeVersion, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(d.FullName, $"{d.Name}.json")));
        }
    }
}
