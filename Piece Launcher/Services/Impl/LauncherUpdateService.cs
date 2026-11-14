using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Piece_Launcher.Services.Impl
{
    public sealed class LauncherUpdateService : IUpdateService
    {
        private const string RepoOwner = "luaukrodev";
        private const string RepoName = "Piece-Launcher";

        private readonly UpdateManager _updateManager = new(
            new GithubSource($"https://github.com/{RepoOwner}/{RepoName}", null, true));

        public bool IsInstalled => _updateManager.IsInstalled;

        public Task<UpdateInfo?> CheckForUpdatesAsync()
            => CheckForUpdatesAsync(CancellationToken.None);

        public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var updateInfo = await _updateManager.CheckForUpdatesAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return updateInfo;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Unable to check for launcher updates: {exception.Message}", exception);
            }
        }

        public Task DownloadAndRestartAsync(UpdateInfo updateInfo, IProgress<int> progress)
            => DownloadAndRestartAsync(updateInfo, progress, CancellationToken.None);

        public async Task DownloadAndRestartAsync(
            UpdateInfo updateInfo,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(updateInfo);
            ArgumentNullException.ThrowIfNull(progress);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _updateManager.DownloadUpdatesAsync(updateInfo, progress.Report, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _updateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Unable to download and apply launcher update: {exception.Message}", exception);
            }
        }
    }
}