using System;
using System.Collections.Generic;

namespace Piece_Launcher.Models
{
    public record ExploreProject(
        string Id,
        string Title,
        string Description,
        string Author,
        string? IconUrl,
        long Downloads,
        string ProjectType,
        string Source,
        IReadOnlyList<string> GameVersions,
        IReadOnlyList<string> Categories,
        DateTimeOffset DateModified
    );
}
