using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public class ModrinthExploreService : IExploreService
    {
        private readonly HttpClient _httpClient;

        public ModrinthExploreService()
        {
            _httpClient = new HttpClient { BaseAddress = new Uri("https://api.modrinth.com/v2/") };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PieceLauncher/1.0.0");
        }

        public string SourceName => "Modrinth";

        public async Task<ExploreSearchResult> SearchAsync(ExploreSearchQuery query, CancellationToken ct = default)
        {
            var url = $"search?limit={query.Limit}&offset={query.Offset}&index={query.SortBy}";
            if (!string.IsNullOrEmpty(query.SearchText))
            {
                url += $"&query={Uri.EscapeDataString(query.SearchText)}";
            }

            var facets = new List<string>();
            if (!string.IsNullOrEmpty(query.ProjectType))
                facets.Add($"[\"project_type:{query.ProjectType}\"]");
            if (!string.IsNullOrEmpty(query.GameVersion))
                facets.Add($"[\"versions:{query.GameVersion}\"]");
            if (!string.IsNullOrEmpty(query.Loader))
                facets.Add($"[\"categories:{query.Loader}\"]");

            if (facets.Any())
            {
                var facetsJson = $"[{string.Join(",", facets)}]";
                url += $"&facets={Uri.EscapeDataString(facetsJson)}";
            }

            var response = await _httpClient.GetFromJsonAsync<ModrinthSearchResponse>(url, cancellationToken: ct);
            if (response == null) return new ExploreSearchResult(new List<ExploreProject>(), 0, query.Offset, query.Limit);

            var projects = response.Hits.Select(h => new ExploreProject(
                h.ProjectId,
                h.Title,
                h.Description,
                h.Author,
                h.IconUrl,
                h.Downloads,
                h.ProjectType,
                SourceName,
                h.Versions,
                h.Categories,
                h.DateModified
            )).ToList();

            return new ExploreSearchResult(projects, response.TotalHits, response.Offset, response.Limit);
        }

        public async Task<ExploreProjectDetail> GetProjectDetailAsync(string projectId, CancellationToken ct = default)
        {
            var p = await _httpClient.GetFromJsonAsync<ModrinthProject>($"project/{projectId}", cancellationToken: ct);
            if (p == null) throw new Exception("Project not found");

            var author = await GetAuthorAsync(projectId, ct);

            return new ExploreProjectDetail(
                p.Id,
                p.Title,
                p.Description,
                p.Body,
                author,
                p.IconUrl,
                p.Downloads,
                p.ProjectType,
                SourceName,
                p.GameVersions ?? new List<string>(),
                p.Categories ?? new List<string>(),
                p.Updated,
                p.License?.Name,
                p.Gallery?.Select(g => g.Url).ToList() ?? new List<string>()
            );
        }

        private async Task<string> GetAuthorAsync(string projectId, CancellationToken ct)
        {
            try
            {
                var members = await _httpClient.GetFromJsonAsync<List<ModrinthMember>>($"project/{projectId}/members", cancellationToken: ct);
                var owner = members?.FirstOrDefault(m => m.Role == "Owner") ?? members?.FirstOrDefault();
                return owner?.User?.Username ?? "Unknown Author";
            }
            catch (OperationCanceledException) { throw; }
            catch { return "Unknown Author"; }
        }

        public async Task<IReadOnlyList<ExploreProjectVersion>> GetProjectVersionsAsync(string projectId, string? gameVersion = null, string? loader = null, CancellationToken ct = default)
        {
            var url = $"project/{projectId}/version";

            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(gameVersion))
                queryParams.Add("game_versions=" + Uri.EscapeDataString("[\"" + gameVersion + "\"]"));
            if (!string.IsNullOrEmpty(loader))
                queryParams.Add("loaders=" + Uri.EscapeDataString("[\"" + loader + "\"]"));

            if (queryParams.Any())
                url += "?" + string.Join("&", queryParams);

            var versions = await _httpClient.GetFromJsonAsync<List<ModrinthVersion>>(url, cancellationToken: ct);
            if (versions == null) return new List<ExploreProjectVersion>();

            return versions.Select(v =>
            {
                var primaryFile = v.Files.FirstOrDefault(f => f.Primary) ?? v.Files.FirstOrDefault();
                return new ExploreProjectVersion(
                    v.Id,
                    v.ProjectId,
                    v.Name,
                    v.VersionNumber,
                    v.VersionType,
                    v.Changelog,
                    v.GameVersions,
                    v.Loaders,
                    v.DatePublished,
                    v.Downloads,
                    primaryFile?.Url ?? "",
                    primaryFile?.Filename ?? "",
                    primaryFile?.Size ?? 0
                );
            }).ToList();
        }

        public async Task DownloadFileAsync(string downloadUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1 && progress != null;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read, ct);
                totalRead += read;
                if (canReportProgress)
                {
                    progress!.Report((double)totalRead / totalBytes * 100);
                }
            }
        }

        // DTOs
        private class ModrinthSearchResponse
        {
            [JsonPropertyName("hits")] public List<ModrinthHit> Hits { get; set; } = new();
            [JsonPropertyName("offset")] public int Offset { get; set; }
            [JsonPropertyName("limit")] public int Limit { get; set; }
            [JsonPropertyName("total_hits")] public int TotalHits { get; set; }
        }

        private class ModrinthHit
        {
            [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("description")] public string Description { get; set; } = "";
            [JsonPropertyName("author")] public string Author { get; set; } = "";
            [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
            [JsonPropertyName("downloads")] public long Downloads { get; set; }
            [JsonPropertyName("project_type")] public string ProjectType { get; set; } = "";
            [JsonPropertyName("versions")] public List<string> Versions { get; set; } = new();
            [JsonPropertyName("categories")] public List<string> Categories { get; set; } = new();
            [JsonPropertyName("date_modified")] public DateTimeOffset DateModified { get; set; }
        }

        private class ModrinthProject
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("description")] public string Description { get; set; } = "";
            [JsonPropertyName("body")] public string Body { get; set; } = "";
            [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
            [JsonPropertyName("downloads")] public long Downloads { get; set; }
            [JsonPropertyName("project_type")] public string ProjectType { get; set; } = "";
            [JsonPropertyName("game_versions")] public List<string>? GameVersions { get; set; }
            [JsonPropertyName("categories")] public List<string>? Categories { get; set; }
            [JsonPropertyName("updated")] public DateTimeOffset Updated { get; set; }
            [JsonPropertyName("license")] public ModrinthLicense? License { get; set; }
            [JsonPropertyName("gallery")] public List<ModrinthGallery>? Gallery { get; set; }
        }

        private class ModrinthMember
        {
            [JsonPropertyName("role")] public string Role { get; set; } = "";
            [JsonPropertyName("user")] public ModrinthUser? User { get; set; }
        }

        private class ModrinthUser
        {
            [JsonPropertyName("username")] public string Username { get; set; } = "";
        }

        private class ModrinthGallery
        {
            [JsonPropertyName("url")] public string Url { get; set; } = "";
        }

        private class ModrinthLicense
        {
            [JsonPropertyName("name")] public string Name { get; set; } = "";
        }

        private class ModrinthVersion
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
            [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = "";
            [JsonPropertyName("version_type")] public string VersionType { get; set; } = "";
            [JsonPropertyName("changelog")] public string? Changelog { get; set; }
            [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = new();
            [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = new();
            [JsonPropertyName("date_published")] public DateTimeOffset DatePublished { get; set; }
            [JsonPropertyName("downloads")] public long Downloads { get; set; }
            [JsonPropertyName("files")] public List<ModrinthFile> Files { get; set; } = new();
        }

        private class ModrinthFile
        {
            [JsonPropertyName("url")] public string Url { get; set; } = "";
            [JsonPropertyName("filename")] public string Filename { get; set; } = "";
            [JsonPropertyName("primary")] public bool Primary { get; set; }
            [JsonPropertyName("size")] public long Size { get; set; }
        }
    }
}
