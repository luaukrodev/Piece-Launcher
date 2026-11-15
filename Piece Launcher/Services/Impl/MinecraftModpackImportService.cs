using CmlLib.Core;
using Piece_Launcher.Helpers;
using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public sealed class MinecraftModpackImportService
    {
        private const long MaximumArchiveBytes = 500L * 1024 * 1024;
        private const long MaximumImportedBytes = 4L * 1024 * 1024 * 1024;
        private const int MaximumArchiveEntries = 50_000;
        private const int MaximumResolvedCurseForgeFiles = 100;
        private static readonly HttpClient DownloadClient = new() { Timeout = TimeSpan.FromMinutes(10) };
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<MinecraftInstanceEntry> ImportAsync(
            Stream archiveStream,
            string archiveName,
            string instancesDirectory,
            CurseForgeExploreService curseForgeService,
            MinecraftPath sharedMinecraftPath,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var instanceId = Guid.NewGuid().ToString("N");
            var stagingDirectory = Path.Combine(instancesDirectory, $".import-{instanceId}");
            var finalDirectory = Path.Combine(instancesDirectory, $"{SanitizeName(Path.GetFileNameWithoutExtension(archiveName))}-{instanceId[..8]}");
            var archivePath = Path.Combine(Path.GetTempPath(), $"piece-launcher-{instanceId}.zip");

            Directory.CreateDirectory(instancesDirectory);
            try
            {
                await CopyInputArchiveAsync(archiveStream, archivePath, cancellationToken);
                MinecraftInstanceEntry instance;
                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    if (archive.Entries.Count > MaximumArchiveEntries)
                        throw new InvalidDataException("The modpack contains too many archive entries.");

                    Directory.CreateDirectory(stagingDirectory);
                    var budget = new ImportBudget();
                    var indexEntry = FindEntry(archive, "modrinth.index.json");
                    if (indexEntry is not null)
                    {
                        var index = await ReadManifestAsync<ModrinthIndex>(indexEntry, cancellationToken);
                        instance = await ImportModrinthAsync(
                            archive, index, stagingDirectory, archiveName, budget, sharedMinecraftPath, progress, cancellationToken);
                    }
                    else
                    {
                        var manifestEntry = FindEntry(archive, "manifest.json");
                        if (manifestEntry is null)
                            throw new InvalidDataException("The ZIP is not a supported Modrinth or CurseForge modpack.");

                        var manifest = await ReadManifestAsync<CurseForgeManifest>(manifestEntry, cancellationToken);
                        instance = await ImportCurseForgeAsync(
                            archive, manifest, stagingDirectory, archiveName, curseForgeService, budget, sharedMinecraftPath, progress, cancellationToken);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(archivePath);
                Directory.Move(stagingDirectory, finalDirectory);
                instance.GameDirectory = finalDirectory;
                instance.Id = instanceId;
                return instance;
            }
            catch
            {
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
                throw;
            }
            finally
            {
                if (File.Exists(archivePath))
                    File.Delete(archivePath);
            }
        }

        private static async Task<MinecraftInstanceEntry> ImportModrinthAsync(
            ZipArchive archive,
            ModrinthIndex index,
            string gameDirectory,
            string archiveName,
            ImportBudget budget,
            MinecraftPath sharedMinecraftPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            if (index.FormatVersion != 1 || !string.Equals(index.Game, "minecraft", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This Modrinth modpack format or game is not supported.");
            if (string.IsNullOrWhiteSpace(index.Dependencies?.Minecraft))
                throw new InvalidDataException("The Modrinth modpack does not specify a Minecraft version.");
            if (index.Files is null)
                throw new InvalidDataException("The Modrinth modpack file list is invalid.");

            var gameVersion = index.Dependencies.Minecraft;
            var (loaderName, loaderVersion) = GetModrinthLoader(index.Dependencies);
            var versionId = await InstallGameAsync(
                gameDirectory, gameVersion, loaderName, loaderVersion, sharedMinecraftPath, progress, cancellationToken);

            progress?.Report("Extracting modpack overrides...");
            await ExtractOverridesAsync(archive, "overrides/", gameDirectory, budget, cancellationToken);
            await ExtractOverridesAsync(archive, "client-overrides/", gameDirectory, budget, cancellationToken);

            foreach (var file in index.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(file.Environment?.Client, "unsupported", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (file.Downloads.Count == 0)
                    throw new InvalidDataException($"The modpack file '{file.Path}' has no download URL.");

                var destination = ResolveSafePath(gameDirectory, file.Path);
                var hashes = file.Hashes
                    .Where(pair => pair.Key is "sha1" or "sha512")
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                if (hashes.Count == 0)
                    throw new InvalidDataException($"The modpack file '{file.Path}' has no supported checksum.");

                progress?.Report($"Downloading {Path.GetFileName(file.Path)}...");
                await DownloadAndVerifyAsync(file.Downloads, destination, hashes, file.FileSize, budget, cancellationToken);
            }

            return new MinecraftInstanceEntry
            {
                Name = string.IsNullOrWhiteSpace(index.Name) ? Path.GetFileNameWithoutExtension(archiveName) : index.Name,
                MinecraftVersion = gameVersion,
                Loader = loaderName,
                LoaderVersion = loaderVersion,
                VersionId = versionId,
                ModpackPlatform = "Modrinth",
                ModpackVersionId = index.VersionId
            };
        }

        private static async Task<MinecraftInstanceEntry> ImportCurseForgeAsync(
            ZipArchive archive,
            CurseForgeManifest manifest,
            string gameDirectory,
            string archiveName,
            CurseForgeExploreService curseForgeService,
            ImportBudget budget,
            MinecraftPath sharedMinecraftPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            if (manifest.ManifestVersion is > 1 ||
                (!string.IsNullOrWhiteSpace(manifest.ManifestType) &&
                 !string.Equals(manifest.ManifestType, "minecraftModpack", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("This CurseForge modpack manifest format is not supported.");

            var gameVersion = manifest.Minecraft?.GameVersion;
            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new InvalidDataException("The CurseForge modpack does not specify a Minecraft version.");
            if (manifest.Files is null)
                throw new InvalidDataException("The CurseForge modpack file list is invalid.");

            var (loaderName, loaderVersion) = GetCurseForgeLoader(manifest.Minecraft?.ModLoaders ?? new List<CurseForgeLoader>());
            var versionId = await InstallGameAsync(
                gameDirectory, gameVersion, loaderName, loaderVersion, sharedMinecraftPath, progress, cancellationToken);

            progress?.Report("Resolving CurseForge modpack files...");
            var resolvedFiles = new List<CurseForgePackFileDownload>();
            foreach (var batch in manifest.Files.Chunk(MaximumResolvedCurseForgeFiles))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var references = batch.Select(file => new CurseForgePackFileReference(file.ProjectId, file.FileId)).ToList();
                resolvedFiles.AddRange(await curseForgeService.ResolvePackFilesAsync(references, cancellationToken));
            }

            var expectedCount = manifest.Files.Select(file => (file.ProjectId, file.FileId)).Distinct().Count();
            if (resolvedFiles.Select(file => (file.ProjectId, file.FileId)).Distinct().Count() != expectedCount)
                throw new InvalidDataException("One or more CurseForge modpack files cannot be downloaded. The project may not allow third-party distribution.");

            progress?.Report("Extracting modpack overrides...");
            var overrideDirectory = string.IsNullOrWhiteSpace(manifest.Overrides) ? "overrides" : manifest.Overrides.Trim('/');
            if (overrideDirectory.Split('/').Any(segment => segment is "" or "." or ".."))
                throw new InvalidDataException("The CurseForge modpack specifies an invalid overrides directory.");
            await ExtractOverridesAsync(archive, $"{overrideDirectory}/", gameDirectory, budget, cancellationToken);

            foreach (var file in resolvedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = ResolveSafePath(gameDirectory, Path.Combine("mods", file.FileName));
                var hashes = file.Hashes?
                    .Where(hash => hash.Algo == 1)
                    .ToDictionary(hash => "sha1", hash => hash.Value, StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                progress?.Report($"Downloading {file.FileName}...");
                await DownloadAndVerifyAsync(new[] { file.DownloadUrl }, destination, hashes, file.FileLength, budget, cancellationToken);
            }

            var stagingInstance = new MinecraftInstanceEntry
            {
                GameDirectory = gameDirectory,
                MinecraftVersion = gameVersion,
                Loader = loaderName
            };
            var contentManager = new MinecraftContentManager();
            foreach (var file in resolvedFiles)
            {
                await contentManager.RecordInstalledAsync(stagingInstance, new InstalledContentRecord(
                    Path.GetRelativePath(gameDirectory, Path.Combine(gameDirectory, "mods", file.FileName)).Replace(Path.DirectorySeparatorChar, '/'),
                    "CurseForge",
                    file.ProjectId.ToString(),
                    file.FileId.ToString(),
                    Path.GetFileNameWithoutExtension(file.FileName),
                    "mod",
                    gameVersion,
                    loaderName));
            }

            return new MinecraftInstanceEntry
            {
                Name = string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileNameWithoutExtension(archiveName) : manifest.Name,
                MinecraftVersion = gameVersion,
                Loader = loaderName,
                LoaderVersion = loaderVersion,
                VersionId = versionId,
                ModpackPlatform = "CurseForge",
                ModpackProjectId = manifest.ProjectId?.ToString(),
                ModpackVersionId = string.IsNullOrWhiteSpace(manifest.Version) ? manifest.VersionId?.ToString() : manifest.Version
            };
        }

        private static async Task<string> InstallGameAsync(
            string gameDirectory,
            string gameVersion,
            string? loaderName,
            string? loaderVersion,
            MinecraftPath sharedMinecraftPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report($"Installing Minecraft {gameVersion}...");
            var launcher = new MinecraftLauncher(
                MinecraftInstancePathFactory.Create(gameDirectory, sharedMinecraftPath));
            if (loaderName is null)
            {
                await launcher.InstallAsync(gameVersion, cancellationToken);
                return gameVersion;
            }

            IModLoaderService loaderService = loaderName switch
            {
                "Fabric" => new FabricLoaderService(launcher),
                "Forge" => new ForgeLoaderService(launcher),
                "NeoForge" => new NeoForgeLoaderService(launcher),
                "Quilt" => new QuiltLoaderService(launcher),
                _ => throw new InvalidDataException($"The mod loader '{loaderName}' is not supported.")
            };

            if (loaderName is "Forge" or "NeoForge")
            {
                var availableVersions = await loaderService.GetAvailableVersionsAsync(gameVersion, cancellationToken);
                var matchingVersion = availableVersions.FirstOrDefault(version =>
                    string.Equals(version.Version, loaderVersion, StringComparison.OrdinalIgnoreCase) ||
                    version.Version.EndsWith($"-{loaderVersion}", StringComparison.OrdinalIgnoreCase));
                if (matchingVersion is null)
                    throw new InvalidDataException($"The requested {loaderName} version '{loaderVersion}' is not available for Minecraft {gameVersion}.");
                loaderVersion = matchingVersion.Version;
            }

            progress?.Report($"Installing {loaderName}...");
            var versionId = await loaderService.InstallAsync(gameVersion, loaderVersion ?? string.Empty, ct: cancellationToken);
            return versionId;
        }

        private static (string? Name, string? Version) GetModrinthLoader(ModrinthDependencies dependencies)
        {
            var loaders = new[]
            {
                ("fabric-loader", "Fabric"),
                ("forge", "Forge"),
                ("neoforge", "NeoForge"),
                ("quilt-loader", "Quilt")
            };
            var found = loaders
                .Where(loader => dependencies.Values.TryGetValue(loader.Item1, out var value) && !string.IsNullOrWhiteSpace(value))
                .ToList();
            if (found.Count > 1)
                throw new InvalidDataException("The Modrinth modpack declares more than one mod loader.");
            if (found.Count == 0)
                return (null, null);

            var selected = found[0];
            dependencies.Values.TryGetValue(selected.Item1, out var version);
            return (selected.Item2, version);
        }

        private static (string? Name, string? Version) GetCurseForgeLoader(IReadOnlyList<CurseForgeLoader> loaders)
        {
            var primary = loaders.FirstOrDefault(loader => loader.Primary) ?? loaders.FirstOrDefault();
            if (primary is null)
                return (null, null);

            var separator = primary.Id.IndexOf('-');
            if (separator <= 0 || separator == primary.Id.Length - 1)
                throw new InvalidDataException($"The CurseForge mod loader '{primary.Id}' is invalid.");

            var name = primary.Id[..separator].ToLowerInvariant() switch
            {
                "fabric" => "Fabric",
                "forge" => "Forge",
                "neoforge" => "NeoForge",
                "quilt" => "Quilt",
                var unsupported => throw new InvalidDataException($"The mod loader '{unsupported}' is not supported.")
            };
            return (name, primary.Id[(separator + 1)..]);
        }

        private static async Task ExtractOverridesAsync(
            ZipArchive archive,
            string prefix,
            string destinationRoot,
            ImportBudget budget,
            CancellationToken cancellationToken)
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.FullName.Length == prefix.Length || entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    continue;
                if (IsSymbolicLink(entry))
                    throw new InvalidDataException($"The modpack contains an unsupported symbolic link: {entry.FullName}");

                var relativePath = entry.FullName[prefix.Length..];
                var destination = ResolveSafePath(destinationRoot, relativePath);
                await CopyArchiveEntryAsync(entry, destination, budget, cancellationToken);
            }
        }

        private static async Task CopyArchiveEntryAsync(
            ZipArchiveEntry entry,
            string destination,
            ImportBudget budget,
            CancellationToken cancellationToken)
        {
            if (entry.Length > MaximumImportedBytes)
                throw new InvalidDataException($"The modpack entry '{entry.FullName}' is too large.");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await CopyWithBudgetAsync(source, target, budget, cancellationToken);
        }

        private static async Task DownloadAndVerifyAsync(
            IEnumerable<string> urls,
            string destination,
            IReadOnlyDictionary<string, string> expectedHashes,
            long expectedSize,
            ImportBudget budget,
            CancellationToken cancellationToken)
        {
            if (expectedSize < 0 || expectedSize > MaximumImportedBytes)
                throw new InvalidDataException($"The modpack file '{Path.GetFileName(destination)}' has an invalid size.");

            Exception? lastFailure = null;
            foreach (var url in urls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidDataException("Modpack downloads must use HTTPS URLs.");

                var temporaryPath = destination + $".{Guid.NewGuid():N}.download";
                try
                {
                    using var response = await DownloadClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is > MaximumImportedBytes)
                        throw new InvalidDataException($"The modpack file '{Path.GetFileName(destination)}' is too large.");

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var hashes = await CopyWithBudgetAndHashAsync(source, target, budget, cancellationToken);

                    var fileLength = new FileInfo(temporaryPath).Length;
                    if (expectedSize > 0 && fileLength != expectedSize)
                        throw new InvalidDataException($"The downloaded file '{Path.GetFileName(destination)}' has an unexpected size.");
                    foreach (var expected in expectedHashes)
                    {
                        if (!hashes.TryGetValue(expected.Key, out var actual) ||
                            !string.Equals(actual, expected.Value, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"The checksum for '{Path.GetFileName(destination)}' does not match.");
                    }

                    File.Move(temporaryPath, destination, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    lastFailure = ex;
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }

            throw new InvalidOperationException(
                $"Could not download or verify '{Path.GetFileName(destination)}'.",
                lastFailure);
        }

        private static async Task CopyInputArchiveAsync(Stream source, string destination, CancellationToken cancellationToken)
        {
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                total += read;
                if (total > MaximumArchiveBytes)
                    throw new InvalidDataException("Modpack archives must be 500 MB or smaller.");
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        private static async Task CopyWithBudgetAsync(
            Stream source,
            Stream destination,
            ImportBudget budget,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                budget.Add(read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        private static async Task<Dictionary<string, string>> CopyWithBudgetAndHashAsync(
            Stream source,
            Stream destination,
            ImportBudget budget,
            CancellationToken cancellationToken)
        {
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                budget.Add(read);
                sha1.AppendData(buffer, 0, read);
                sha512.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sha1"] = Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant(),
                ["sha512"] = Convert.ToHexString(sha512.GetHashAndReset()).ToLowerInvariant()
            };
        }

        private static async Task<T> ReadManifestAsync<T>(ZipArchiveEntry entry, CancellationToken cancellationToken)
        {
            if (entry.Length > 2 * 1024 * 1024)
                throw new InvalidDataException("The modpack manifest is too large.");
            await using var stream = entry.Open();
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("The modpack manifest is invalid.");
        }

        private static ZipArchiveEntry? FindEntry(ZipArchive archive, string name)
            => archive.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase));

        private static string ResolveSafePath(string root, string relativePath)
        {
            var normalized = relativePath.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized.Split('/').Any(segment => segment is "" or "." or "..") ||
                (normalized.Length >= 2 && normalized[1] == ':'))
                throw new InvalidDataException($"The modpack contains an unsafe path: {relativePath}");

            var firstSegment = normalized.Split('/')[0];
            if (firstSegment.Equals(".piece-launcher", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("versions", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("libraries", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The modpack cannot replace launcher-managed content: {relativePath}");

            var fullRoot = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The modpack contains an unsafe path: {relativePath}");
            return fullPath;
        }

        private static bool IsSymbolicLink(ZipArchiveEntry entry)
        {
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            var windowsAttributes = entry.ExternalAttributes & 0xFFFF;
            return unixType == 0xA000 || (windowsAttributes & (int)FileAttributes.ReparsePoint) != 0;
        }

        private static string SanitizeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars().ToHashSet();
            var sanitized = new string(name.Select(character =>
                invalid.Contains(character) || char.IsControl(character) ? '-' : character).ToArray()).Trim(' ', '.');
            if (string.IsNullOrWhiteSpace(sanitized))
                return "Modpack";
            return sanitized.Length <= 64 ? sanitized : sanitized[..64];
        }

        private sealed class ImportBudget
        {
            private long _total;

            public void Add(int bytes)
            {
                _total += bytes;
                if (_total > MaximumImportedBytes)
                    throw new InvalidDataException("The modpack expands beyond the 4 GB import limit.");
            }
        }

        private sealed class ModrinthIndex
        {
            [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; }
            [JsonPropertyName("game")] public string Game { get; set; } = string.Empty;
            [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
            [JsonPropertyName("versionId")] public string VersionId { get; set; } = string.Empty;
            [JsonPropertyName("dependencies")] public ModrinthDependencies? Dependencies { get; set; }
            [JsonPropertyName("files")] public List<ModrinthFile> Files { get; set; } = new();
        }

        private sealed class ModrinthDependencies
        {
            [JsonPropertyName("minecraft")] public string? Minecraft { get; set; }
            [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement> Extensions { get; set; } = new();

            [JsonIgnore]
            public Dictionary<string, string> Values => Extensions
                .Where(pair => pair.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(pair => pair.Key, pair => pair.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ModrinthFile
        {
            [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
            [JsonPropertyName("hashes")] public Dictionary<string, string> Hashes { get; set; } = new();
            [JsonPropertyName("downloads")] public List<string> Downloads { get; set; } = new();
            [JsonPropertyName("fileSize")] public long FileSize { get; set; }
            [JsonPropertyName("env")] public ModrinthEnvironment? Environment { get; set; }
        }

        private sealed class ModrinthEnvironment
        {
            [JsonPropertyName("client")] public string? Client { get; set; }
        }

        private sealed class CurseForgeManifest
        {
            [JsonPropertyName("manifestType")] public string ManifestType { get; set; } = string.Empty;
            [JsonPropertyName("manifestVersion")] public int ManifestVersion { get; set; }
            [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
            [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
            [JsonPropertyName("versionId")] public int? VersionId { get; set; }
            [JsonPropertyName("projectId")] public int? ProjectId { get; set; }
            [JsonPropertyName("minecraft")] public CurseForgeMinecraft? Minecraft { get; set; }
            [JsonPropertyName("files")] public List<CurseForgeManifestFile> Files { get; set; } = new();
            [JsonPropertyName("overrides")] public string Overrides { get; set; } = "overrides";
        }

        private sealed class CurseForgeMinecraft
        {
            [JsonPropertyName("version")] public string? GameVersion { get; set; }
            [JsonPropertyName("modLoaders")] public List<CurseForgeLoader> ModLoaders { get; set; } = new();
        }

        private sealed class CurseForgeLoader
        {
            [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
            [JsonPropertyName("primary")] public bool Primary { get; set; }
        }

        private sealed class CurseForgeManifestFile
        {
            [JsonPropertyName("projectID")] public int ProjectId { get; set; }
            [JsonPropertyName("fileID")] public int FileId { get; set; }
        }
    }
}
