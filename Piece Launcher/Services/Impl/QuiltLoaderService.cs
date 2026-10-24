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
    public class QuiltLoaderService : IModLoaderService
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
        private readonly MinecraftLauncher _launcher;

        public QuiltLoaderService(MinecraftLauncher launcher)
        {
            _launcher = launcher;
        }

        public string LoaderName => "Quilt";

        public async Task<IReadOnlyList<LoaderVersionEntry>> GetAvailableVersionsAsync(string minecraftVersion, CancellationToken ct = default)
        {
            try
            {
                var url = $"https://meta.quiltmc.org/v3/versions/loader/{minecraftVersion}";
                var response = await _httpClient.GetStringAsync(url, ct);

                using var doc = JsonDocument.Parse(response);
                var list = new List<LoaderVersionEntry>();

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("loader", out var loader))
                    {
                        var version = loader.GetProperty("version").GetString() ?? "";
                        var stable = !version.Contains("beta", StringComparison.OrdinalIgnoreCase) && 
                                     !version.Contains("alpha", StringComparison.OrdinalIgnoreCase);
                        bool isInstalled = IsQuiltInstalled(minecraftVersion, version);

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
            if (string.IsNullOrEmpty(loaderVersion))
            {
                var available = await GetAvailableVersionsAsync(minecraftVersion, ct);
                var recommended = available.FirstOrDefault(v => v.IsRecommended) ?? available.FirstOrDefault();
                if (recommended == null)
                    throw new InvalidOperationException($"No Quilt loader versions found for Minecraft {minecraftVersion}");
                loaderVersion = recommended.Version;
            }

            var versionId = $"quilt-loader-{loaderVersion}-{minecraftVersion}";
            progress?.Report(new InstallProgress("Fetching Quilt profile", 10, versionId));

            // Download Quilt profile JSON
            var profileUrl = $"https://meta.quiltmc.org/v3/versions/loader/{minecraftVersion}/{loaderVersion}/profile/json";
            var profileJson = await _httpClient.GetStringAsync(profileUrl, ct);

            // Save JSON to launcher versions folder
            var versionDir = Path.Combine(_launcher.MinecraftPath.Versions, versionId);
            Directory.CreateDirectory(versionDir);
            var jsonPath = Path.Combine(versionDir, $"{versionId}.json");
            await File.WriteAllTextAsync(jsonPath, profileJson, ct);

            progress?.Report(new InstallProgress("Downloading Minecraft and Quilt libraries", 20, versionId));

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
                progress?.Report(new InstallProgress("Quilt Installed", 100, versionId));
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
                d.Name.StartsWith("quilt-loader-", StringComparison.OrdinalIgnoreCase) &&
                d.Name.EndsWith($"-{minecraftVersion}", StringComparison.OrdinalIgnoreCase) &&
                CmlLibVersionService.IsVersionDownloaded(d));

            return Task.FromResult(exists);
        }

        private bool IsQuiltInstalled(string minecraftVersion, string loaderVersion)
        {
            var versionId = $"quilt-loader-{loaderVersion}-{minecraftVersion}";
            var versionDir = new DirectoryInfo(Path.Combine(_launcher.MinecraftPath.Versions, versionId));
            return versionDir.Exists && CmlLibVersionService.IsVersionDownloaded(versionDir);
        }
    }
}
