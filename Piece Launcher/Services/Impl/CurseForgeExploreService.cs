using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public class CurseForgeExploreService : IExploreService
    {
        private const int MinecraftGameId = 432;
        private const int MinecraftModsClassId = 6;
        private const int MaximumSearchOffset = 10_000;

        private const string DefaultProxyBaseAddress =
            "https://piece-launcher-curseforge-api.kro-dev.workers.dev/";
        private readonly HttpClient _apiClient;
        private readonly HttpClient _downloadClient = new();

        public CurseForgeExploreService()
        {
            var proxyBaseAddress = Environment.GetEnvironmentVariable("CURSEFORGE_PROXY_URL")?.Trim();
            if (string.IsNullOrWhiteSpace(proxyBaseAddress))
                proxyBaseAddress = DefaultProxyBaseAddress;

            _apiClient = new HttpClient
            {
                BaseAddress = new Uri($"{proxyBaseAddress.TrimEnd('/')}/v1/", UriKind.Absolute)
            };
        }

        public string SourceName => "CurseForge";

        public async Task<IReadOnlyList<CurseForgePackFileDownload>> ResolvePackFilesAsync(
            IReadOnlyList<CurseForgePackFileReference> files,
            CancellationToken ct = default)
        {
            if (files.Count is < 1 or > 100)
                throw new ArgumentOutOfRangeException(nameof(files), "Between 1 and 100 files can be resolved at once.");

            using var response = await _apiClient.PostAsJsonAsync(
                "modpacks/curseforge/files",
                new { files },
                cancellationToken: ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"CurseForge proxy returned HTTP {(int)response.StatusCode}: {error}",
                    null,
                    response.StatusCode);
            }

            var envelope = await response.Content.ReadFromJsonAsync<CurseForgeResponse<List<CurseForgePackFileDownload>>>(
                cancellationToken: ct);
            return envelope?.Data ?? throw new InvalidOperationException("CurseForge returned an empty modpack file response.");
        }

        public async Task<ExploreSearchResult> SearchAsync(ExploreSearchQuery query, CancellationToken ct = default)
        {
            EnsureSupportedProjectType(query.ProjectType);

            if (query.Offset >= MaximumSearchOffset)
                return new ExploreSearchResult(new List<ExploreProject>(), 0, query.Offset, query.Limit);

            var pageSize = Math.Min(Math.Clamp(query.Limit, 1, 50), MaximumSearchOffset - Math.Max(0, query.Offset));
            var parameters = new List<string>
            {
                $"gameId={MinecraftGameId}",
                $"classId={MinecraftModsClassId}",
                $"index={Math.Max(0, query.Offset)}",
                $"pageSize={pageSize}",
                $"sortField={GetSortField(query.SortBy)}",
                "sortOrder=desc"
            };

            if (!string.IsNullOrWhiteSpace(query.SearchText))
                parameters.Add($"searchFilter={Uri.EscapeDataString(query.SearchText)}");
            if (!string.IsNullOrWhiteSpace(query.GameVersion))
                parameters.Add($"gameVersion={Uri.EscapeDataString(query.GameVersion)}");

            if (!string.IsNullOrWhiteSpace(query.Loader) && !string.IsNullOrWhiteSpace(query.GameVersion)
                && TryGetLoaderType(query.Loader, out var loaderType))
            {
                parameters.Add($"modLoaderType={loaderType}");
            }

            var response = await GetResponseAsync<CurseForgeSearchResponse>(
                $"mods/search?{string.Join("&", parameters)}", ct);

            var projects = response.Data.Select(mod => ToProject(mod)).ToList();
            var totalCount = Math.Min(response.Pagination?.TotalCount ?? projects.Count, MaximumSearchOffset);
            return new ExploreSearchResult(projects, totalCount, query.Offset, pageSize);
        }

        public async Task<ExploreProjectDetail> GetProjectDetailAsync(string projectId, CancellationToken ct = default)
        {
            var id = ParseProjectId(projectId);
            var mod = await GetDataAsync<CurseForgeMod>($"mods/{id}", ct);

            return new ExploreProjectDetail(
                mod.Id.ToString(CultureInfo.InvariantCulture),
                mod.Name,
                mod.Summary,
                mod.Summary,
                mod.Authors.FirstOrDefault()?.Name ?? "Unknown Author",
                mod.Logo?.Url ?? mod.Logo?.ThumbnailUrl,
                mod.DownloadCount,
                "mod",
                SourceName,
                GetGameVersions(mod),
                GetCategories(mod.Categories),
                mod.DateModified,
                null,
                mod.Screenshots.Select(image => image.Url).OfType<string>()
                    .Where(url => !string.IsNullOrWhiteSpace(url)).ToList()
            );
        }

        public async Task<IReadOnlyList<ExploreProjectVersion>> GetProjectVersionsAsync(
            string projectId,
            string? gameVersion = null,
            string? loader = null,
            CancellationToken ct = default)
        {
            var id = ParseProjectId(projectId);
            var mod = await GetDataAsync<CurseForgeMod>($"mods/{id}", ct);
            if (!mod.AllowModDistribution)
            {
                throw new InvalidOperationException(
                    "CurseForge does not allow this mod to be distributed through third-party launchers.");
            }

            var parameters = new List<string> { "index=0", "pageSize=50" };
            if (!string.IsNullOrWhiteSpace(gameVersion))
                parameters.Add($"gameVersion={Uri.EscapeDataString(gameVersion)}");
            if (!string.IsNullOrWhiteSpace(loader) && TryGetLoaderType(loader, out var loaderType))
                parameters.Add($"modLoaderType={loaderType}");

            var files = await GetDataAsync<List<CurseForgeFile>>(
                $"mods/{id}/files?{string.Join("&", parameters)}", ct);

            return files
                .Where(file => file.IsAvailable)
                .Select(file => ToProjectVersion(file, id, loader))
                .ToList();
        }

        public async Task DownloadFileAsync(
            string downloadUrl,
            string destinationPath,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("CurseForge did not provide a secure download URL for this file.");
            }

            using var response = await _downloadClient.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(
                destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read, ct);
                totalRead += read;
                if (totalBytes > 0 && progress is not null)
                    progress.Report((double)totalRead / totalBytes * 100);
            }
        }

        private async Task<T> GetDataAsync<T>(string endpoint, CancellationToken ct) where T : class
        {
            var envelope = await GetResponseAsync<CurseForgeResponse<T>>(endpoint, ct);
            return envelope.Data ?? throw new InvalidOperationException("CurseForge returned an empty response.");
        }

        private async Task<T> GetResponseAsync<T>(string endpoint, CancellationToken ct) where T : class
        {
            using var response = await _apiClient.GetAsync(endpoint, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"CurseForge proxy returned HTTP {(int)response.StatusCode}: {error}",
                    null,
                    response.StatusCode);
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                ?? throw new InvalidOperationException("CurseForge returned an empty response.");
        }

        private static void EnsureSupportedProjectType(string? projectType)
        {
            if (!string.IsNullOrWhiteSpace(projectType)
                && !projectType.Equals("mod", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("CurseForge search currently supports Minecraft mods only.");
            }
        }

        private static int ParseProjectId(string projectId)
        {
            if (!int.TryParse(projectId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new ArgumentException("The CurseForge project id is invalid.", nameof(projectId));
            return id;
        }

        private static int GetSortField(string? sortBy) => sortBy switch
        {
            "downloads" => 6,
            "newest" => 11,
            "updated" => 3,
            _ => 2
        };

        private static bool TryGetLoaderType(string loader, out int loaderType)
        {
            loaderType = loader.ToLowerInvariant() switch
            {
                "forge" => 1,
                "fabric" => 4,
                "quilt" => 5,
                "neoforge" => 6,
                _ => 0
            };
            return loaderType != 0;
        }

        private ExploreProject ToProject(CurseForgeMod mod)
        {
            return new ExploreProject(
                mod.Id.ToString(CultureInfo.InvariantCulture),
                mod.Name,
                mod.Summary,
                mod.Authors.FirstOrDefault()?.Name ?? "Unknown Author",
                mod.Logo?.Url ?? mod.Logo?.ThumbnailUrl,
                mod.DownloadCount,
                "mod",
                SourceName,
                GetGameVersions(mod),
                GetCategories(mod.Categories),
                mod.DateModified
            );
        }

        private static ExploreProjectVersion ToProjectVersion(CurseForgeFile file, int projectId, string? loader)
        {
            var gameVersions = file.GameVersions ?? new List<string>();
            var loaders = string.IsNullOrWhiteSpace(loader)
                ? gameVersions.Where(IsLoaderName).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string> { loader };
            var filename = string.IsNullOrWhiteSpace(file.FileName) ? file.DisplayName : file.FileName;

            return new ExploreProjectVersion(
                file.Id.ToString(CultureInfo.InvariantCulture),
                projectId.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(file.DisplayName) ? filename : file.DisplayName,
                filename,
                file.ReleaseType switch { 1 => "release", 2 => "beta", 3 => "alpha", _ => "unknown" },
                null,
                gameVersions,
                loaders,
                file.FileDate,
                file.DownloadCount,
                file.DownloadUrl ?? string.Empty,
                filename,
                file.FileLength
            );
        }

        private static bool IsLoaderName(string value)
            => value.Equals("forge", StringComparison.OrdinalIgnoreCase)
                || value.Equals("fabric", StringComparison.OrdinalIgnoreCase)
                || value.Equals("quilt", StringComparison.OrdinalIgnoreCase)
                || value.Equals("neoforge", StringComparison.OrdinalIgnoreCase);

        private static IReadOnlyList<string> GetGameVersions(CurseForgeMod mod)
        {
            var versions = mod.LatestFilesIndexes
                .Select(file => file.GameVersion)
                .Concat(mod.LatestFiles.SelectMany(file => file.GameVersions ?? new List<string>()))
                .Where(version => !string.IsNullOrWhiteSpace(version) && !IsLoaderName(version));
            return versions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IReadOnlyList<string> GetCategories(List<CurseForgeCategory> categories)
            => categories.Where(category => !category.IsClass)
                .Select(category => category.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private sealed class CurseForgeResponse<T>
        {
            [JsonPropertyName("data")] public T? Data { get; set; }
        }

        private sealed class CurseForgeSearchResponse
        {
            [JsonPropertyName("data")] public List<CurseForgeMod> Data { get; set; } = new();
            [JsonPropertyName("pagination")] public CurseForgePagination? Pagination { get; set; }
        }

        private sealed class CurseForgePagination
        {
            [JsonPropertyName("totalCount")] public int TotalCount { get; set; }
        }

        private sealed class CurseForgeMod
        {
            [JsonPropertyName("id")] public int Id { get; set; }
            [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
            [JsonPropertyName("summary")] public string Summary { get; set; } = string.Empty;
            [JsonPropertyName("downloadCount")] public long DownloadCount { get; set; }
            [JsonPropertyName("dateModified")] public DateTimeOffset DateModified { get; set; }
            [JsonPropertyName("allowModDistribution")] public bool AllowModDistribution { get; set; }
            [JsonPropertyName("authors")] public List<CurseForgeAuthor> Authors { get; set; } = new();
            [JsonPropertyName("logo")] public CurseForgeImage? Logo { get; set; }
            [JsonPropertyName("screenshots")] public List<CurseForgeImage> Screenshots { get; set; } = new();
            [JsonPropertyName("categories")] public List<CurseForgeCategory> Categories { get; set; } = new();
            [JsonPropertyName("latestFilesIndexes")] public List<CurseForgeLatestFileIndex> LatestFilesIndexes { get; set; } = new();
            [JsonPropertyName("latestFiles")] public List<CurseForgeFile> LatestFiles { get; set; } = new();
        }

        private sealed class CurseForgeAuthor
        {
            [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        }

        private sealed class CurseForgeImage
        {
            [JsonPropertyName("url")] public string? Url { get; set; }
            [JsonPropertyName("thumbnailUrl")] public string? ThumbnailUrl { get; set; }
        }

        private sealed class CurseForgeCategory
        {
            [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
            [JsonPropertyName("isClass")] public bool IsClass { get; set; }
        }

        private sealed class CurseForgeLatestFileIndex
        {
            [JsonPropertyName("gameVersion")] public string GameVersion { get; set; } = string.Empty;
        }

        private sealed class CurseForgeFile
        {
            [JsonPropertyName("id")] public int Id { get; set; }
            [JsonPropertyName("isAvailable")] public bool IsAvailable { get; set; } = true;
            [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
            [JsonPropertyName("fileName")] public string FileName { get; set; } = string.Empty;
            [JsonPropertyName("releaseType")] public int ReleaseType { get; set; }
            [JsonPropertyName("fileDate")] public DateTimeOffset FileDate { get; set; }
            [JsonPropertyName("downloadCount")] public long DownloadCount { get; set; }
            [JsonPropertyName("fileLength")] public long FileLength { get; set; }
            [JsonPropertyName("downloadUrl")] public string? DownloadUrl { get; set; }
            [JsonPropertyName("gameVersions")] public List<string>? GameVersions { get; set; }
        }
    }
}
