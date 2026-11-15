using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public sealed class LauncherDataImportService
    {
        private const long MaximumImportBytes = 8L * 1024 * 1024 * 1024;
        private const int MaximumImportFiles = 100_000;

        private static readonly string[] ImportDirectories =
        {
            "config",
            "defaultconfigs",
            "datapacks",
            "kubejs",
            "mods",
            "resourcepacks",
            "saves",
            "schematics",
            "screenshots",
            "shaderpacks",
            "versions"
        };

        private static readonly string[] ImportFiles =
        {
            "options.txt",
            "optionsof.txt",
            "optionsshaders.txt",
            "servers.dat"
        };

        public IReadOnlyList<LauncherImportSource> DiscoverSources()
        {
            var sources = new Dictionary<string, LauncherImportSource>(StringComparer.OrdinalIgnoreCase);
            var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourceFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            AddSource(Path.Combine(roaming, ".minecraft"), "Minecraft folder", sources, sourceNames, sourceFolders);

            var roots = new[]
            {
                (Path.Combine(roaming, "PrismLauncher", "instances"), "Prism Launcher"),
                (Path.Combine(local, "PrismLauncher", "instances"), "Prism Launcher"),
                (Path.Combine(roaming, "MultiMC", "instances"), "MultiMC"),
                (Path.Combine(local, "MultiMC", "instances"), "MultiMC"),
                (Path.Combine(roaming, "Badlion Client"), "Badlion Client"),
                (Path.Combine(local, "Badlion Client"), "Badlion Client"),
                (Path.Combine(roaming, "Lunar Client"), "Lunar Client"),
                (Path.Combine(profile, ".lunarclient"), "Lunar Client"),
                (Path.Combine(roaming, "TLauncher"), "TLauncher"),
                (Path.Combine(roaming, ".tlauncher"), "TLauncher"),
                (Path.Combine(local, "TLauncher"), "TLauncher")
            };

            foreach (var (root, launcherName) in roots)
            {
                foreach (var candidate in EnumerateCandidateDirectories(root, 3, 500))
                    AddSource(candidate, launcherName, sources, sourceNames, sourceFolders);
            }

            return sources.Values
                .OrderBy(source => source.LauncherName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(source => source.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        public async Task<MinecraftInstanceEntry> ImportAsync(
            LauncherImportSource source,
            string instancesDirectory,
            IProgress<InstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!Directory.Exists(source.GameDirectory))
                throw new DirectoryNotFoundException($"The selected Minecraft folder no longer exists: {source.GameDirectory}");

            var sourceDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.GameDirectory));
            var targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(instancesDirectory));
            if (IsSameOrChild(targetRoot, sourceDirectory) || IsSameOrChild(sourceDirectory, targetRoot))
                throw new InvalidOperationException("The selected source and Piece Launcher instance folders must be separate.");

            var files = GetImportFiles(sourceDirectory);
            if (files.Count == 0)
                throw new InvalidDataException("No Minecraft worlds, settings, mods, versions, or other supported data were found.");
            if (files.Count > MaximumImportFiles)
                throw new InvalidDataException($"The selected instance contains more than {MaximumImportFiles:N0} supported files.");

            var totalBytes = files.Sum(file => file.Length);
            if (totalBytes > MaximumImportBytes)
                throw new InvalidDataException("The selected Minecraft data exceeds the 8 GB import limit.");

            var id = Guid.NewGuid().ToString("N");
            var stagingDirectory = Path.Combine(targetRoot, $".import-{id}");
            var finalDirectory = Path.Combine(targetRoot, $"{SanitizeName(source.DisplayName)}-{id[..8]}");

            Directory.CreateDirectory(targetRoot);
            try
            {
                Directory.CreateDirectory(stagingDirectory);
                for (var index = 0; index < files.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var file = files[index];
                    var relativePath = Path.GetRelativePath(sourceDirectory, file.FullName);
                    var destination = Path.GetFullPath(Path.Combine(stagingDirectory, relativePath));
                    if (!IsSameOrChild(stagingDirectory, destination))
                        throw new InvalidDataException("A selected file resolved outside the new instance folder.");

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await CopyFileAsync(file.FullName, destination, cancellationToken);
                    progress?.Report(new InstallProgress(
                        "Importing",
                        (index + 1d) / files.Count * 100,
                        relativePath));
                }

                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(stagingDirectory, finalDirectory);

                var metadata = ReadInstanceMetadata(sourceDirectory, source.InstanceDirectory);
                return new MinecraftInstanceEntry
                {
                    Id = id,
                    Name = source.DisplayName,
                    GameDirectory = finalDirectory,
                    MinecraftVersion = metadata.MinecraftVersion,
                    Loader = metadata.Loader,
                    LoaderVersion = metadata.LoaderVersion,
                    VersionId = metadata.VersionId,
                    SourceLauncher = source.LauncherName,
                    ImportSourceDirectory = sourceDirectory
                };
            }
            catch
            {
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
                throw;
            }
        }

        public LauncherImportSource CreateSourceFromFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("Choose a Minecraft folder to import.", nameof(folderPath));

            var fullPath = Path.GetFullPath(folderPath);
            var gameDirectory = FindGameDirectory(fullPath)
                ?? throw new InvalidDataException("The selected folder is not a recognizable Minecraft game or instance folder.");
            var launcherName = GetLauncherName(fullPath);
            var folderName = new DirectoryInfo(fullPath).Name;
            return new LauncherImportSource(
                $"{launcherName} · {folderName}",
                launcherName,
                gameDirectory,
                fullPath);
        }

        private static IEnumerable<string> EnumerateCandidateDirectories(string root, int maximumDepth, int maximumDirectories)
        {
            if (!Directory.Exists(root))
                yield break;

            var pending = new Queue<(string Path, int Depth)>();
            pending.Enqueue((root, 0));
            var visited = 0;
            while (pending.Count > 0 && visited < maximumDirectories)
            {
                var (path, depth) = pending.Dequeue();
                visited++;
                yield return path;
                if (depth >= maximumDepth)
                    continue;

                string[] children;
                try
                {
                    children = Directory.GetDirectories(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var child in children)
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            pending.Enqueue((child, depth + 1));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        private static void AddSource(
            string candidate,
            string launcherName,
            IDictionary<string, LauncherImportSource> sources,
            ISet<string> sourceNames,
            ISet<string> sourceFolders)
        {
            var gameDirectory = FindGameDirectory(candidate);
            if (gameDirectory is null)
                return;

            var fullGameDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
            var instanceDirectory = Path.GetFullPath(candidate);
            var instanceName = new DirectoryInfo(instanceDirectory).Name;
            var displayName = string.Equals(instanceName, ".minecraft", StringComparison.OrdinalIgnoreCase)
                ? launcherName
                : $"{launcherName} · {instanceName}";
            var sourceName = NormalizeName($"{launcherName} {instanceName}");
            var fullInstanceDirectory = Path.TrimEndingDirectorySeparator(instanceDirectory);
            if (sources.ContainsKey(fullGameDirectory)
                || sourceNames.Contains(sourceName)
                || sourceFolders.Contains(fullGameDirectory)
                || sourceFolders.Contains(fullInstanceDirectory))
            {
                return;
            }

            sources.Add(fullGameDirectory, new LauncherImportSource(
                displayName,
                launcherName,
                fullGameDirectory,
                fullInstanceDirectory));
            sourceNames.Add(sourceName);
            sourceFolders.Add(fullGameDirectory);
            sourceFolders.Add(fullInstanceDirectory);
        }

        private static string NormalizeName(string name) =>
            string.Join(" ", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        private static string? FindGameDirectory(string path)
        {
            if (!Directory.Exists(path))
                return null;

            var candidates = new[]
            {
                path,
                Path.Combine(path, ".minecraft"),
                Path.Combine(path, "minecraft"),
                Path.Combine(path, "game")
            };
            return candidates.FirstOrDefault(IsMinecraftDirectory);
        }

        private static bool IsMinecraftDirectory(string path)
        {
            if (!Directory.Exists(path))
                return false;

            return ImportDirectories.Any(name => Directory.Exists(Path.Combine(path, name)))
                || ImportFiles.Any(name => File.Exists(Path.Combine(path, name)));
        }

        private static List<FileInfo> GetImportFiles(string sourceDirectory)
        {
            var files = new List<FileInfo>();
            foreach (var name in ImportFiles)
            {
                var path = Path.Combine(sourceDirectory, name);
                if (File.Exists(path) && !IsReparsePoint(path))
                    files.Add(new FileInfo(path));
            }

            foreach (var directoryName in ImportDirectories)
            {
                var directory = Path.Combine(sourceDirectory, directoryName);
                if (Directory.Exists(directory) && !IsReparsePoint(directory))
                    AddFilesRecursively(directory, files);
            }

            return files;
        }

        private static void AddFilesRecursively(string root, ICollection<FileInfo> files)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var file in Directory.GetFiles(directory))
                {
                    if (!IsReparsePoint(file))
                        files.Add(new FileInfo(file));
                }

                foreach (var child in Directory.GetDirectories(directory))
                {
                    if (!IsReparsePoint(child))
                        pending.Push(child);
                }
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (FileNotFoundException)
            {
                return true;
            }
        }

        private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
        {
            await using var input = new FileStream(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, cancellationToken);
        }

        private static (string? MinecraftVersion, string? Loader, string? LoaderVersion, string? VersionId)
            ReadInstanceMetadata(string gameDirectory, string instanceDirectory)
        {
            string? minecraftVersion = null;
            string? loader = null;
            string? loaderVersion = null;
            var metadataPath = Path.Combine(instanceDirectory, "mmc-pack.json");
            if (!File.Exists(metadataPath))
                metadataPath = Path.Combine(Path.GetDirectoryName(gameDirectory) ?? string.Empty, "mmc-pack.json");

            if (File.Exists(metadataPath))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
                    if (document.RootElement.TryGetProperty("components", out var components) &&
                        components.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var component in components.EnumerateArray())
                        {
                            var uid = component.TryGetProperty("uid", out var uidElement)
                                ? uidElement.GetString()
                                : null;
                            var version = component.TryGetProperty("version", out var versionElement)
                                ? versionElement.GetString()
                                : null;
                            if (string.IsNullOrWhiteSpace(version))
                                continue;

                            if (string.Equals(uid, "net.minecraft", StringComparison.OrdinalIgnoreCase))
                                minecraftVersion = version;
                            else if (uid is not null && GetLoader(uid) is { } componentLoader)
                            {
                                loader = componentLoader;
                                loaderVersion = version;
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                }
            }

            var versionId = FindVersionId(gameDirectory, minecraftVersion, ref loader, ref loaderVersion);
            if (minecraftVersion is null && versionId is not null)
            {
                var versionMetadata = Path.Combine(gameDirectory, "versions", versionId, $"{versionId}.json");
                if (File.Exists(versionMetadata))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(versionMetadata));
                        if (document.RootElement.TryGetProperty("inheritsFrom", out var inheritsFrom))
                            minecraftVersion = inheritsFrom.GetString();
                    }
                    catch (JsonException)
                    {
                    }
                }
            }

            return (minecraftVersion, loader, loaderVersion, versionId);
        }

        private static string? FindVersionId(
            string gameDirectory,
            string? requestedMinecraftVersion,
            ref string? loader,
            ref string? loaderVersion)
        {
            var versionsDirectory = Path.Combine(gameDirectory, "versions");
            if (!Directory.Exists(versionsDirectory))
                return null;

            var versionMetadata = Directory.GetDirectories(versionsDirectory)
                .Select(directory => Path.Combine(directory, $"{Path.GetFileName(directory)}.json"))
                .Where(File.Exists)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc);

            foreach (var file in versionMetadata)
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file.FullName));
                    var root = document.RootElement;
                    var id = root.TryGetProperty("id", out var idElement)
                        ? idElement.GetString()
                        : Path.GetFileNameWithoutExtension(file.Name);
                    var inheritsFrom = root.TryGetProperty("inheritsFrom", out var inheritsElement)
                        ? inheritsElement.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(requestedMinecraftVersion) &&
                        !string.Equals(requestedMinecraftVersion, inheritsFrom, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(requestedMinecraftVersion, id, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (id is null)
                        continue;

                    loader ??= GetLoader(id);
                    if (loaderVersion is null && loader is not null)
                    {
                        var loaderMarker = loader.ToLowerInvariant() switch
                        {
                            "fabric" => "fabric-loader-",
                            "forge" => "-forge-",
                            "neoforge" => "-neoforge-",
                            "quilt" => "quilt-loader-",
                            _ => string.Empty
                        };
                        var markerIndex = id.IndexOf(loaderMarker, StringComparison.OrdinalIgnoreCase);
                        if (markerIndex >= 0)
                            loaderVersion = id[(markerIndex + loaderMarker.Length)..];
                    }

                    return id;
                }
                catch (JsonException)
                {
                }
            }

            return null;
        }

        private static string? GetLoader(string value)
        {
            if (value.Contains("fabric", StringComparison.OrdinalIgnoreCase))
                return "Fabric";
            if (value.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
                return "NeoForge";
            if (value.Contains("forge", StringComparison.OrdinalIgnoreCase))
                return "Forge";
            if (value.Contains("quilt", StringComparison.OrdinalIgnoreCase))
                return "Quilt";
            return null;
        }

        private static string GetLauncherName(string path)
        {
            if (path.Contains("prism", StringComparison.OrdinalIgnoreCase))
                return "Prism Launcher";
            if (path.Contains("multimc", StringComparison.OrdinalIgnoreCase))
                return "MultiMC";
            if (path.Contains("badlion", StringComparison.OrdinalIgnoreCase))
                return "Badlion Client";
            if (path.Contains("lunar", StringComparison.OrdinalIgnoreCase))
                return "Lunar Client";
            if (path.Contains("tlauncher", StringComparison.OrdinalIgnoreCase))
                return "TLauncher";
            return "Minecraft";
        }

        private static bool IsSameOrChild(string root, string path)
        {
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(
                    normalizedRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }

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
