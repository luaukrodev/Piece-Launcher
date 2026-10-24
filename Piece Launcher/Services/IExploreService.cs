using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services
{
    public interface IExploreService
    {
        string SourceName { get; }
        Task<ExploreSearchResult> SearchAsync(ExploreSearchQuery query, CancellationToken ct = default);
        Task<ExploreProjectDetail> GetProjectDetailAsync(string projectId, CancellationToken ct = default);
        Task<IReadOnlyList<ExploreProjectVersion>> GetProjectVersionsAsync(string projectId, string? gameVersion = null, string? loader = null, CancellationToken ct = default);
        Task DownloadFileAsync(string downloadUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default);
    }
}
