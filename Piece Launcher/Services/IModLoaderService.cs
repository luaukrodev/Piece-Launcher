using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services
{
    public interface IModLoaderService
    {
        string LoaderName { get; }
        Task<IReadOnlyList<LoaderVersionEntry>> GetAvailableVersionsAsync(string minecraftVersion, CancellationToken ct = default);
        Task<string> InstallAsync(string minecraftVersion, string loaderVersion, IProgress<InstallProgress>? progress = null, CancellationToken ct = default);
        Task<bool> IsInstalledAsync(string minecraftVersion, CancellationToken ct = default);
    }
}
