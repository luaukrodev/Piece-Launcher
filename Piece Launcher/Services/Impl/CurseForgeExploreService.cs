using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public class CurseForgeExploreService : IExploreService
    {
        public string SourceName => "CurseForge";

        public Task<ExploreSearchResult> SearchAsync(ExploreSearchQuery query, CancellationToken ct = default)
        {
            // Placeholder: Not implemented due to lack of API key
            return Task.FromResult(new ExploreSearchResult(new List<ExploreProject>(), 0, query.Offset, query.Limit));
        }

        public Task<ExploreProjectDetail> GetProjectDetailAsync(string projectId, CancellationToken ct = default)
        {
            throw new NotImplementedException("CurseForge API requires an API key.");
        }

        public Task<IReadOnlyList<ExploreProjectVersion>> GetProjectVersionsAsync(string projectId, string? gameVersion = null, string? loader = null, CancellationToken ct = default)
        {
            throw new NotImplementedException("CurseForge API requires an API key.");
        }

        public Task DownloadFileAsync(string downloadUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            throw new NotImplementedException("CurseForge API requires an API key.");
        }
    }
}
