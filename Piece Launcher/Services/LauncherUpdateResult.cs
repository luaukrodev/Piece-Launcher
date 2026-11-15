using System;

namespace Piece_Launcher.Services
{
    public sealed record LauncherUpdateResult(
        string Version,
        string InstallerDownloadUrl,
        string FileName)
    {
        public string DisplayVersion => Version.TrimStart('v', 'V');
    }
}
