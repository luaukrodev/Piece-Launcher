using System;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services
{
    public interface IUpdateService
    {
        bool IsInstalled { get; }
        Task<LauncherUpdateResult?> CheckForUpdatesAsync();
        async Task<LauncherUpdateResult?> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var update = await CheckForUpdatesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return update;
        }

        Task DownloadAndRestartAsync(LauncherUpdateResult updateInfo, IProgress<int> progress);
        async Task DownloadAndRestartAsync(LauncherUpdateResult updateInfo, IProgress<int> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DownloadAndRestartAsync(updateInfo, progress);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
