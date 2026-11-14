using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;

namespace Piece_Launcher.Services
{
    public interface IUpdateService
    {
        bool IsInstalled { get; }
        Task<UpdateInfo?> CheckForUpdatesAsync();
        async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var update = await CheckForUpdatesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return update;
        }

        Task DownloadAndRestartAsync(UpdateInfo updateInfo, IProgress<int> progress);
        async Task DownloadAndRestartAsync(UpdateInfo updateInfo, IProgress<int> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DownloadAndRestartAsync(updateInfo, progress);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}