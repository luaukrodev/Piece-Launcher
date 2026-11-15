using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public sealed class MinecraftVersionManagerService : IVersionManagerService
    {
        private readonly string _versionsDirectory;

        public MinecraftVersionManagerService(string versionsDirectory)
        {
            _versionsDirectory = Path.GetFullPath(versionsDirectory);
        }

        public string GetVersionDirectory(string versionId)
        {
            if (string.IsNullOrWhiteSpace(versionId) ||
                versionId is "." or ".." ||
                !string.Equals(Path.GetFileName(versionId), versionId, StringComparison.Ordinal))
            {
                throw new ArgumentException("The version identifier is invalid.", nameof(versionId));
            }

            var root = Path.GetFullPath(_versionsDirectory);
            var directory = Path.GetFullPath(Path.Combine(root, versionId));
            if (!IsSameOrChildPath(root, directory) ||
                string.Equals(root, directory, GetPathComparison()))
            {
                throw new InvalidOperationException("The version directory is outside the managed versions folder.");
            }

            if (Directory.Exists(directory) &&
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Linked version directories cannot be managed.");
            }

            return directory;
        }

        public IReadOnlyList<VersionFileEntry> GetDirectoryEntries(string versionId, string directoryPath)
        {
            var root = GetVersionDirectory(versionId);
            var directory = Path.GetFullPath(directoryPath);
            if (!IsSameOrChildPath(root, directory) || !Directory.Exists(directory))
                throw new InvalidOperationException("The selected directory is outside the managed version.");

            return Directory.EnumerateFileSystemEntries(directory)
                .Select(path => new FileInfo(path))
                .Where(entry => (entry.Attributes & FileAttributes.ReparsePoint) == 0)
                .Select(entry =>
                {
                    var isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                    return new VersionFileEntry(
                        entry.Name,
                        entry.FullName,
                        isDirectory,
                        isDirectory ? 0 : entry.Length);
                })
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public Task<string> CreateInstalledCopyAsync(string sourceVersionId, CancellationToken ct = default)
            => Task.Run(() => CreateInstalledCopy(sourceVersionId, ct), ct);

        public Task DeleteVersionAsync(string versionId, CancellationToken ct = default)
            => Task.Run(() => DeleteVersion(versionId, ct), ct);

        private string CreateInstalledCopy(string sourceVersionId, CancellationToken ct)
        {
            var sourceDirectory = GetVersionDirectory(sourceVersionId);
            if (!Directory.Exists(sourceDirectory) ||
                !CmlLibVersionService.IsVersionDownloaded(new DirectoryInfo(sourceDirectory)))
            {
                throw new InvalidOperationException("The source version is not installed.");
            }

            var sourceMetadataPath = Path.Combine(sourceDirectory, $"{sourceVersionId}.json");
            if (!File.Exists(sourceMetadataPath))
                throw new InvalidOperationException("The source version metadata file is missing.");

            var targetVersionId = $"{sourceVersionId}-copy-{Guid.NewGuid():N}";
            var targetDirectory = GetVersionDirectory(targetVersionId);
            var stagingDirectory = Path.Combine(_versionsDirectory, $".copy-{Guid.NewGuid():N}");

            try
            {
                CopyDirectory(sourceDirectory, stagingDirectory, sourceVersionId, targetVersionId, ct);
                ct.ThrowIfCancellationRequested();
                Directory.Move(stagingDirectory, targetDirectory);
                return targetVersionId;
            }
            catch (Exception exception)
            {
                try
                {
                    if (Directory.Exists(stagingDirectory))
                        Directory.Delete(stagingDirectory, recursive: true);
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(
                        "Creating a version copy failed and its temporary directory could not be removed.",
                        exception,
                        cleanupException);
                }

                throw;
            }
        }

        private void DeleteVersion(string versionId, CancellationToken ct)
        {
            var directory = GetVersionDirectory(versionId);
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"The installed version '{versionId}' was not found.");

            ct.ThrowIfCancellationRequested();
            var dependents = FindInheritedVersions(versionId);
            if (dependents.Count > 0)
            {
                throw new VersionInUseException(dependents);
            }

            Directory.Delete(directory, recursive: true);
        }

        private List<string> FindInheritedVersions(string versionId)
        {
            var dependents = new List<string>();
            if (!Directory.Exists(_versionsDirectory))
                return dependents;

            foreach (var directory in Directory.EnumerateDirectories(_versionsDirectory))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    continue;

                var name = Path.GetFileName(directory);
                if (string.Equals(name, versionId, StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(".copy-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var metadataPath = Path.Combine(directory, $"{name}.json");
                if (!File.Exists(metadataPath))
                    continue;

                try
                {
                    var metadata = JsonNode.Parse(File.ReadAllText(metadataPath));
                    if (string.Equals(
                            metadata?["inheritsFrom"]?.GetValue<string>(),
                            versionId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        dependents.Add(name);
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Invalid local metadata cannot be used to establish a dependency.
                }
                catch (InvalidOperationException)
                {
                    // Invalid local metadata cannot be used to establish a dependency.
                }
            }

            return dependents;
        }

        private static void CopyDirectory(
            string sourceDirectory,
            string destinationDirectory,
            string sourceVersionId,
            string targetVersionId,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if ((File.GetAttributes(sourceDirectory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Version folders containing linked directories cannot be copied.");

            Directory.CreateDirectory(destinationDirectory);

            foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
            {
                ct.ThrowIfCancellationRequested();
                if ((File.GetAttributes(sourceFile) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Version files containing links cannot be copied.");

                var fileName = Path.GetFileName(sourceFile);
                var targetFileName = string.Equals(fileName, $"{sourceVersionId}.json", StringComparison.OrdinalIgnoreCase)
                    ? $"{targetVersionId}.json"
                    : string.Equals(fileName, $"{sourceVersionId}.jar", StringComparison.OrdinalIgnoreCase)
                        ? $"{targetVersionId}.jar"
                        : fileName;
                var targetFile = Path.Combine(destinationDirectory, targetFileName);

                if (string.Equals(fileName, $"{sourceVersionId}.json", StringComparison.OrdinalIgnoreCase))
                {
                    var metadata = JsonNode.Parse(File.ReadAllText(sourceFile)) as JsonObject
                        ?? throw new InvalidDataException("The source version metadata is invalid.");
                    if (metadata["id"] is null)
                        throw new InvalidDataException("The source version metadata has no identifier.");

                    metadata["id"] = targetVersionId;
                    File.WriteAllText(targetFile, metadata.ToJsonString());
                }
                else
                {
                    File.Copy(sourceFile, targetFile);
                }
            }

            foreach (var sourceSubdirectory in Directory.EnumerateDirectories(sourceDirectory))
            {
                ct.ThrowIfCancellationRequested();
                var targetSubdirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceSubdirectory));
                CopyDirectory(sourceSubdirectory, targetSubdirectory, sourceVersionId, targetVersionId, ct);
            }
        }

        private static bool IsSameOrChildPath(string root, string candidate)
        {
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            return string.Equals(normalizedRoot, normalizedCandidate, GetPathComparison()) ||
                   normalizedCandidate.StartsWith(
                       normalizedRoot + Path.DirectorySeparatorChar,
                       GetPathComparison());
        }

        private static StringComparison GetPathComparison()
            => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}
