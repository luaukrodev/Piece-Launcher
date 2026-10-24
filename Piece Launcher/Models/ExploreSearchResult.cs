using System.Collections.Generic;

namespace Piece_Launcher.Models
{
    public record ExploreSearchResult(
        IReadOnlyList<ExploreProject> Projects,
        int TotalCount,
        int Offset,
        int Limit
    );
}
