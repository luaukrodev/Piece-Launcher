namespace Piece_Launcher.Models
{
    public record ExploreSearchQuery(
        string? SearchText = null,
        string? ProjectType = null,
        string? GameVersion = null,
        string? Loader = null,
        string SortBy = "relevance",
        int Offset = 0,
        int Limit = 20
    );
}
