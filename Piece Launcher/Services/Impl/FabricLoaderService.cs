using CmlLib.Core;
using CmlLib.Core.Installers;
using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public class FabricLoaderService : IModLoaderService
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
        private readonly MinecraftLauncher _launcher;

        public FabricLoaderService(MinecraftLauncher launcher)
        {
            _launcher = launcher;
        }

        public string LoaderName => "Fabric";

        public async Task<IReadOnlyList<LoaderVersionEntry>> GetAvailableVersionsAsync(string minecraftVersion, CancellationToken ct = default)
        {
            try
            {
                var url = $"https://meta.fabricmc.net/v2/versions/loader/{minecraftVersion}";
                var response = await _httpClient.GetStringAsync(url, ct);

                using var doc = JsonDocument.Parse(response);
                var list = new List<LoaderVersionEntry>();

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("loader", out var loader))
                    {
                        var version = loader.GetProperty("version").GetString() ?? "";
                        var stable = loader.TryGetProperty("stable", out var s) && s.GetBoolean();
                        bool isInstalled = IsFabricInstalled(minecraftVersion, version);

                        list.Add(new LoaderVersionEntry(version, minecraftVersion, stable, isInstalled));
                    }
                }

                return list;
            }
            catch
            {
                return Array.Empty<LoaderVersionEntry>();
            }
        }

        public async Task<string> InstallAsync(string minecraftVersion, string loaderVersion, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
        {
            // If no loaderVersion provided, get available and choose the first stable or latest
            if (string.IsNullOrEmpty(loaderVersion))
            {
                var available = await GetAvailableVersionsAsync(minecraftVersion, ct);
                var recommended = available.FirstOrDefault(v => v.IsRecommended) ?? available.FirstOrDefault();
                if (recommended == null)
                    throw new InvalidOperationException($"No Fabric loader versions found for Minecraft {minecraftVersion}");
                loaderVersion = recommended.Version;
            }

            var versionId = $"fabric-loader-{loaderVersion}-{minecraftVersion}";
            progress?.Report(new InstallProgress("Fetching Fabric profile", 10, versionId));

            // Download Fabric profile JSON
            var profileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{minecraftVersion}/{loaderVersion}/profile/json";
            var profileJson = await _httpClient.GetStringAsync(profileUrl, ct);

            // Save JSON to launcher versions folder
            var versionDir = Path.Combine(_launcher.MinecraftPath.Versions, versionId);
            Directory.CreateDirectory(versionDir);
            var jsonPath = Path.Combine(versionDir, $"{versionId}.json");
            await File.WriteAllTextAsync(jsonPath, profileJson, ct);

            progress?.Report(new InstallProgress("Downloading Minecraft and Fabric libraries", 20, versionId));

            // Track progress through MinecraftLauncher
            EventHandler<InstallerProgressChangedEventArgs> fileHandler = (s, e) =>
            {
                double pct = e.TotalTasks > 0 ? ((double)e.ProgressedTasks / e.TotalTasks) * 100.0 : 0;
                progress?.Report(new InstallProgress(e.EventType.ToString(), pct, e.Name));
            };

            EventHandler<ByteProgress> byteHandler = (s, e) =>
            {
                double pct = e.ToRatio() * 100.0;
                string sizeStr = e.TotalBytes > 0 
                    ? $"{e.ProgressedBytes / (1024 * 1024)}MB / {e.TotalBytes / (1024 * 1024)}MB"
                    : $"{e.ProgressedBytes / (1024 * 1024)}MB";
                progress?.Report(new InstallProgress("Downloading", pct, sizeStr));
            };

            try
            {
                _launcher.FileProgressChanged += fileHandler;
                _launcher.ByteProgressChanged += byteHandler;

                await _launcher.InstallAsync(versionId, ct);
                progress?.Report(new InstallProgress("Fabric Installed", 100, versionId));
                return versionId;
            }
            finally
            {
                _launcher.FileProgressChanged -= fileHandler;
                _launcher.ByteProgressChanged -= byteHandler;
            }
        }

        public Task<bool> IsInstalledAsync(string minecraftVersion, CancellationToken ct = default)
        {
            var versionsDir = new DirectoryInfo(_launcher.MinecraftPath.Versions);
            if (!versionsDir.Exists) return Task.FromResult(false);

            bool exists = versionsDir.GetDirectories().Any(d =>
                d.Name.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase) &&
                d.Name.EndsWith($"-{minecraftVersion}", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(d.FullName, $"{d.Name}.json")));

            return Task.FromResult(exists);
        }

        private bool IsFabricInstalled(string minecraftVersion, string loaderVersion)
        {
            var versionId = $"fabric-loader-{loaderVersion}-{minecraftVersion}";
            var jsonPath = Path.Combine(_launcher.MinecraftPath.Versions, versionId, $"{versionId}.json");
            return File.Exists(jsonPath);
        }
    }
}
