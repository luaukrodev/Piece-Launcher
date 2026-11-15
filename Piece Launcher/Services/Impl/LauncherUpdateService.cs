using System;
using System.Runtime.CompilerServices;
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
        private const string InstallerFileName = "PieceLauncher-win-Setup.exe";

        private static readonly ConditionalWeakTable<LauncherUpdateResult, UpdateInfo> VelopackUpdates = new();

        private readonly UpdateManager _updateManager = new(
            new GithubSource($"https://github.com/{RepoOwner}/{RepoName}", null, true));

        public bool IsInstalled => _updateManager.IsInstalled;

        public Task<LauncherUpdateResult?> CheckForUpdatesAsync()
            => CheckForUpdatesAsync(CancellationToken.None);

        public async Task<LauncherUpdateResult?> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            UpdateInfo? updateInfo;
            try
            {
                updateInfo = await _updateManager.CheckForUpdatesAsync();
                cancellationToken.ThrowIfCancellationRequested();
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

            if (updateInfo is null)
                return null;

            var version = updateInfo.TargetFullRelease.Version.ToString();
            var tag = Uri.EscapeDataString($"v{version}");
            var result = new LauncherUpdateResult(
                version,
                $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{tag}/{InstallerFileName}",
                InstallerFileName);
            VelopackUpdates.Add(result, updateInfo);
            return result;
        }

        public Task DownloadAndRestartAsync(LauncherUpdateResult updateInfo, IProgress<int> progress)
            => DownloadAndRestartAsync(updateInfo, progress, CancellationToken.None);

        public async Task DownloadAndRestartAsync(
            LauncherUpdateResult updateInfo,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(updateInfo);
            ArgumentNullException.ThrowIfNull(progress);

            if (!VelopackUpdates.TryGetValue(updateInfo, out var velopackUpdateInfo))
                throw new InvalidOperationException("Update details are unavailable. Check for updates again.");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _updateManager.DownloadUpdatesAsync(
                    velopackUpdateInfo,
                    progress.Report,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _updateManager.ApplyUpdatesAndRestart(velopackUpdateInfo.TargetFullRelease);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Unable to download and apply launcher update {updateInfo.Version}: {exception.Message}",
                    exception);
            }
        }
    }
}
