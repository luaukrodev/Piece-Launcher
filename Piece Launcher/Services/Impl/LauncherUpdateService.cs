using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Piece_Launcher.Services.Impl
{
    public sealed class LauncherUpdateService : IUpdateService
    {
        private readonly UpdateManager _updateManager = new(
            new GithubSource("https://github.com/luaukrodev/Piece-Launcher", null, false));

        public bool IsInstalled => _updateManager.IsInstalled;

        public Task<UpdateInfo?> CheckForUpdatesAsync()
            => _updateManager.CheckForUpdatesAsync();

        public async Task DownloadAndRestartAsync(UpdateInfo updateInfo, IProgress<int> progress)
        {
            await _updateManager.DownloadUpdatesAsync(updateInfo, progress.Report);
            _updateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
        }
    }
}
