namespace Piece_Launcher.Models
{
    public sealed record LauncherImportSource(
        string DisplayName,
        string LauncherName,
        string GameDirectory,
        string InstanceDirectory);
}
