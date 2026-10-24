namespace Piece_Launcher.Models
{
    public record LoaderVersionEntry(
        string Version,
        string MinecraftVersion,
        bool IsRecommended,
        bool IsInstalled
    );
}
