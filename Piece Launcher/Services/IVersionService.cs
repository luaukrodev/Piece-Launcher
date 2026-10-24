using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services
{
    public interface IVersionService
    {
        Task<IReadOnlyList<VersionEntry>> GetAllVersionsAsync(CancellationToken ct = default);
        Task<IReadOnlyList<string>> GetInstalledVersionNamesAsync(CancellationToken ct = default);
        Task InstallVersionAsync(string versionName, IProgress<InstallProgress>? progress = null, CancellationToken ct = default);
    }
}
