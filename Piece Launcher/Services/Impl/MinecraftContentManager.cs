using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public sealed class MinecraftContentManager
    {
        private const string MetadataDirectoryName = ".piece-launcher";
        private const string MetadataFileName = "managed-content.json";
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public IReadOnlyList<InstalledContentItem> Scan(MinecraftInstanceEntry instance)
        {
            var records = ReadRecords(instance);
            var items = new List<InstalledContentItem>();

            AddDirectoryItems(instance.GameDirectory, "mods", InstalledContentType.Mod, items, records);
            AddDirectoryItems(instance.GameDirectory, "resourcepacks", InstalledContentType.ResourcePack, items, records);
            AddDirectoryItems(instance.GameDirectory, "shaderpacks", InstalledContentType.Shader, items, records);
            AddDataPacks(instance.GameDirectory, items, records);

            return items.OrderBy(item => item.Type).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task SetEnabledAsync(MinecraftInstanceEntry instance, InstalledContentItem item, bool enabled)
        {
            if (item.IsEnabled == enabled)
                return;

            var sourcePath = ResolveItemPath(instance, item.RelativePath);
            var destinationPath = enabled
                ? sourcePath[..^".disabled".Length]
                : sourcePath + ".disabled";

            if (Directory.Exists(sourcePath))
                Directory.Move(sourcePath, destinationPath);
            else if (File.Exists(sourcePath))
                File.Move(sourcePath, destinationPath);
            else
                throw new FileNotFoundException("The selected item no longer exists.", sourcePath);

            var records = ReadRecords(instance);
            var record = records.FirstOrDefault(entry =>
                PathsEqual(entry.RelativePath, item.RelativePath));
            if (record is not null)
            {
                records.Remove(record);
                records.Add(record with { RelativePath = GetRelativePath(instance.GameDirectory, destinationPath) });
                await WriteRecordsAsync(instance, records);
            }
        }

        public async Task RemoveAsync(MinecraftInstanceEntry instance, InstalledContentItem item)
        {
            var itemPath = ResolveItemPath(instance, item.RelativePath);
            if (Directory.Exists(itemPath))
                Directory.Delete(itemPath, recursive: true);
            else if (File.Exists(itemPath))
                File.Delete(itemPath);
            else
                throw new FileNotFoundException("The selected item no longer exists.", itemPath);

            var records = ReadRecords(instance);
            records.RemoveAll(record => PathsEqual(record.RelativePath, item.RelativePath));
            await WriteRecordsAsync(instance, records);
        }

        public async Task RecordInstalledAsync(
            MinecraftInstanceEntry instance,
            InstalledContentRecord record,
            string? previousRelativePath = null)
        {
            var records = ReadRecords(instance);
            records.RemoveAll(existing =>
                PathsEqual(existing.RelativePath, record.RelativePath) ||
                (previousRelativePath is not null && PathsEqual(existing.RelativePath, previousRelativePath)));
            records.Add(record);
            await WriteRecordsAsync(instance, records);
        }

        private static void AddDirectoryItems(
            string gameDirectory,
            string directoryName,
            InstalledContentType type,
            ICollection<InstalledContentItem> items,
            IReadOnlyList<InstalledContentRecord> records)
        {
            var directoryPath = Path.Combine(gameDirectory, directoryName);
            if (!Directory.Exists(directoryPath))
                return;

            foreach (var path in Directory.EnumerateFileSystemEntries(directoryPath, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);
                var isDisabled = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                var activeName = isDisabled ? name[..^".disabled".Length] : name;
                var isDirectory = Directory.Exists(path);
                var valid = type switch
                {
                    InstalledContentType.Mod => !isDirectory && activeName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase),
                    InstalledContentType.ResourcePack or InstalledContentType.Shader =>
                        isDirectory || activeName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
                    _ => false
                };
                if (!valid)
                    continue;

                var relativePath = GetRelativePath(gameDirectory, path);
                var metadata = records.FirstOrDefault(record => PathsEqual(record.RelativePath, relativePath));
                items.Add(new InstalledContentItem(
                    activeName,
                    relativePath,
                    type,
                    !isDisabled,
                    GetItemSize(path),
                    metadata));
            }
        }

        private static void AddDataPacks(
            string gameDirectory,
            ICollection<InstalledContentItem> items,
            IReadOnlyList<InstalledContentRecord> records)
        {
            var roots = new List<string> { Path.Combine(gameDirectory, "datapacks") };
            var savesPath = Path.Combine(gameDirectory, "saves");
            if (Directory.Exists(savesPath))
            {
                roots.AddRange(Directory.EnumerateDirectories(savesPath)
                    .Select(world => Path.Combine(world, "datapacks")));
            }

            foreach (var root in roots.Where(Directory.Exists))
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(path);
                    var isDisabled = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    var activeName = isDisabled ? name[..^".disabled".Length] : name;
                    var isDirectory = Directory.Exists(path);
                    if (isDirectory && !File.Exists(Path.Combine(path, "pack.mcmeta")))
                        continue;
                    if (!isDirectory && !activeName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var relativePath = GetRelativePath(gameDirectory, path);
                    var metadata = records.FirstOrDefault(record => PathsEqual(record.RelativePath, relativePath));
                    items.Add(new InstalledContentItem(
                        activeName,
                        relativePath,
                        InstalledContentType.DataPack,
                        !isDisabled,
                        GetItemSize(path),
                        metadata));
                }
            }
        }

        private static List<InstalledContentRecord> ReadRecords(MinecraftInstanceEntry instance)
        {
            var path = GetMetadataPath(instance);
            if (!File.Exists(path))
                return new List<InstalledContentRecord>();

            return JsonSerializer.Deserialize<List<InstalledContentRecord>>(File.ReadAllText(path))
                ?? new List<InstalledContentRecord>();
        }

        private static async Task WriteRecordsAsync(
            MinecraftInstanceEntry instance,
            IReadOnlyList<InstalledContentRecord> records)
        {
            var path = GetMetadataPath(instance);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(records, JsonOptions));
        }

        private static string GetMetadataPath(MinecraftInstanceEntry instance)
            => Path.Combine(instance.GameDirectory, MetadataDirectoryName, MetadataFileName);

        private static string ResolveItemPath(MinecraftInstanceEntry instance, string relativePath)
        {
            var root = Path.GetFullPath(instance.GameDirectory);
            var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected content path is outside this instance.");
            return path;
        }

        private static string GetRelativePath(string root, string path)
            => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

        private static bool PathsEqual(string left, string right)
            => string.Equals(left.Replace('\\', '/'), right.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

        private static long GetItemSize(string path)
        {
            if (File.Exists(path))
                return new FileInfo(path).Length;
            if (!Directory.Exists(path))
                return 0;
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
    }
}
