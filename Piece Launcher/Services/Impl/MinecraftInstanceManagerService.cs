using CmlLib.Core;
using Piece_Launcher.Helpers;
using Piece_Launcher.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public sealed class MinecraftInstanceManagerService
    {
        public async Task<MinecraftInstanceEntry> CreateAsync(
            string name,
            string minecraftVersion,
            string loader,
            string instancesDirectory,
            MinecraftPath sharedMinecraftPath,
            IProgress<InstallProgress>? progress = null,
            Action? onDownloadStarting = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An instance name is required.", nameof(name));
            if (string.IsNullOrWhiteSpace(minecraftVersion))
                throw new ArgumentException("A Minecraft version is required.", nameof(minecraftVersion));

            var instanceId = Guid.NewGuid().ToString("N");
            var stagingDirectory = Path.Combine(instancesDirectory, $".create-{instanceId}");
            var instanceDirectory = Path.Combine(
                instancesDirectory,
                $"{SanitizeName(name)}-{instanceId[..8]}");

            Directory.CreateDirectory(instancesDirectory);
            try
            {
                Directory.CreateDirectory(stagingDirectory);
                var launcher = new MinecraftLauncher(
                    MinecraftInstancePathFactory.Create(stagingDirectory, sharedMinecraftPath));
                string versionId;
                string? loaderVersion = null;
                if (string.Equals(loader, "Vanilla", StringComparison.OrdinalIgnoreCase))
                {
                    onDownloadStarting?.Invoke();
                    await CmlLibVersionService.InstallVersionAsync(
                        launcher,
                        minecraftVersion,
                        progress,
                        cancellationToken);
                    versionId = minecraftVersion;
                    loader = string.Empty;
                }
                else
                {
                    var service = CreateLoaderService(loader, launcher);
                    var availableVersions = await service.GetAvailableVersionsAsync(minecraftVersion, cancellationToken);
                    var selectedVersion = availableVersions.FirstOrDefault(version => version.IsRecommended)
                        ?? availableVersions.FirstOrDefault();
                    if (selectedVersion is null)
                        throw new InvalidOperationException($"No {loader} versions are available for Minecraft {minecraftVersion}.");

                    loaderVersion = selectedVersion.Version;
                    onDownloadStarting?.Invoke();
                    versionId = await service.InstallAsync(
                        minecraftVersion,
                        loaderVersion,
                        progress,
                        ct: cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(stagingDirectory, instanceDirectory);
                return new MinecraftInstanceEntry
                {
                    Id = instanceId,
                    Name = name.Trim(),
                    GameDirectory = instanceDirectory,
                    MinecraftVersion = minecraftVersion,
                    Loader = string.IsNullOrWhiteSpace(loader) ? null : loader,
                    LoaderVersion = loaderVersion,
                    VersionId = versionId
                };
            }
            catch
            {
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
                throw;
            }
        }

        public Task DeleteAsync(
            MinecraftInstanceEntry instance,
            string instancesDirectory,
            CancellationToken cancellationToken = default)
        {
            if (instance.IsDefault)
                throw new InvalidOperationException("The default Minecraft instance cannot be deleted.");

            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instancesDirectory));
            var instancePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instance.GameDirectory));
            var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (!instancePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected instance is outside the managed instances directory.");
            if (!string.Equals(Path.GetDirectoryName(instancePath), root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only managed instance folders can be deleted.");
            if (!Directory.Exists(instancePath))
                throw new DirectoryNotFoundException("The selected instance directory no longer exists.");

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Delete(instancePath, recursive: true);
            }, cancellationToken);
        }

        private static IModLoaderService CreateLoaderService(string loader, MinecraftLauncher launcher)
            => loader.ToLowerInvariant() switch
            {
                "fabric" => new FabricLoaderService(launcher),
                "forge" => new ForgeLoaderService(launcher),
                "neoforge" => new NeoForgeLoaderService(launcher),
                "quilt" => new QuiltLoaderService(launcher),
                _ => throw new ArgumentOutOfRangeException(nameof(loader), loader, "Unknown Minecraft mod loader.")
            };

        private static string SanitizeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Trim().Select(character =>
                Array.IndexOf(invalid, character) >= 0 || char.IsControl(character) ? '-' : character).ToArray())
                .Trim(' ', '.');
            if (string.IsNullOrWhiteSpace(sanitized))
                return "Minecraft";
            return sanitized.Length <= 64 ? sanitized : sanitized[..64];
        }
    }
}
