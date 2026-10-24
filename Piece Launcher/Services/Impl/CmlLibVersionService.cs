using CmlLib.Core;
using CmlLib.Core.Installers;
using CmlLib.Core.VersionMetadata;
using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public class CmlLibVersionService : IVersionService
    {
        private readonly MinecraftLauncher _launcher;

        public CmlLibVersionService(MinecraftLauncher launcher)
        {
            _launcher = launcher;
        }

        public async Task<IReadOnlyList<VersionEntry>> GetAllVersionsAsync(CancellationToken ct = default)
        {
            var installedNames = new HashSet<string>(await GetInstalledVersionNamesAsync(ct), StringComparer.OrdinalIgnoreCase);
            var metadata = await _launcher.GetAllVersionsAsync(ct);

            var list = new List<VersionEntry>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in metadata)
            {
                bool isInstalled = installedNames.Contains(v.Name);
                list.Add(new VersionEntry(
                    v.Name,
                    v.Type ?? "release",
                    v.ReleaseTime,
                    isInstalled
                ));
                seenNames.Add(v.Name);
            }

            // Include local versions that might not be in Mojang's manifest (custom/modloader versions)
            foreach (var installed in installedNames)
            {
                if (!seenNames.Contains(installed))
                {
                    list.Insert(0, new VersionEntry(
                        installed,
                        "custom",
                        DateTimeOffset.UtcNow,
                        true
                    ));
                }
            }

            return list;
        }

        public Task<IReadOnlyList<string>> GetInstalledVersionNamesAsync(CancellationToken ct = default)
        {
            var versionsDir = new DirectoryInfo(_launcher.MinecraftPath.Versions);
            if (!versionsDir.Exists)
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            var installed = versionsDir.GetDirectories()
                .Where(dir => File.Exists(Path.Combine(dir.FullName, $"{dir.Name}.json")))
                .Select(dir => dir.Name)
                .ToList();

            return Task.FromResult<IReadOnlyList<string>>(installed);
        }

        public async Task InstallVersionAsync(string versionName, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
        {
            progress?.Report(new InstallProgress("Initializing", 0, versionName));

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

                await _launcher.InstallAsync(versionName, ct);
                progress?.Report(new InstallProgress("Completed", 100, versionName));
            }
            finally
            {
                _launcher.FileProgressChanged -= fileHandler;
                _launcher.ByteProgressChanged -= byteHandler;
            }
        }
    }
}
