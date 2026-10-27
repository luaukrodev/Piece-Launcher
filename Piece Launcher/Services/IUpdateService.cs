using System;
using System.Threading.Tasks;
using Velopack;

namespace Piece_Launcher.Services
{
    public interface IUpdateService
    {
        bool IsInstalled { get; }
        Task<UpdateInfo?> CheckForUpdatesAsync();
        Task DownloadAndRestartAsync(UpdateInfo updateInfo, IProgress<int> progress);
    }
}
