using CmlLib.Core;
using CmlLib.Core.Installer.NeoForge;
using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public class NeoForgeLoaderService : IModLoaderService
    {
        private readonly MinecraftLauncher _launcher;

        public NeoForgeLoaderService(MinecraftLauncher launcher)
        {
            _launcher = launcher;
        }

        public string LoaderName => "NeoForge";

        public async Task<IReadOnlyList<LoaderVersionEntry>> GetAvailableVersionsAsync(string minecraftVersion, CancellationToken ct = default)
        {
            try
            {
                var installer = new NeoForgeInstaller(_launcher);
                var versions = await installer.GetNeoForgeVersions(minecraftVersion);
                return versions.Select(v => new LoaderVersionEntry(
                    v.NeoForgeVersionName ?? "",
                    minecraftVersion,
                    false,
                    IsNeoForgeInstalled(minecraftVersion, v.NeoForgeVersionName ?? "")
                )).ToList();
            }
            catch
            {
                return Array.Empty<LoaderVersionEntry>();
            }
        }

        public async Task<string> InstallAsync(string minecraftVersion, string loaderVersion, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
        {
            progress?.Report(new InstallProgress("Preparing NeoForge", 10, $"{minecraftVersion} - {loaderVersion}"));
            var installer = new NeoForgeInstaller(_launcher);

            progress?.Report(new InstallProgress("Installing NeoForge", 50, $"{loaderVersion}"));
            string installedVersionId = await installer.Install(minecraftVersion, loaderVersion);
            progress?.Report(new InstallProgress("NeoForge Installed", 100, installedVersionId));
            return installedVersionId;
        }

        public Task<bool> IsInstalledAsync(string minecraftVersion, CancellationToken ct = default)
        {
            var versionsDir = new DirectoryInfo(_launcher.MinecraftPath.Versions);
            if (!versionsDir.Exists) return Task.FromResult(false);

            bool exists = versionsDir.GetDirectories().Any(d =>
                d.Name.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase) &&
                d.Name.Contains("neoforge", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(d.FullName, $"{d.Name}.json")));

            return Task.FromResult(exists);
        }

        private bool IsNeoForgeInstalled(string minecraftVersion, string loaderVersion)
        {
            var versionsDir = new DirectoryInfo(_launcher.MinecraftPath.Versions);
            if (!versionsDir.Exists) return false;

            return versionsDir.GetDirectories().Any(d =>
                d.Name.Contains(minecraftVersion, StringComparison.OrdinalIgnoreCase) &&
                d.Name.Contains("neoforge", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(loaderVersion) || d.Name.Contains(loaderVersion, StringComparison.OrdinalIgnoreCase)) &&
                File.Exists(Path.Combine(d.FullName, $"{d.Name}.json")));
        }
    }
}
