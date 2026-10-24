using System;
using System.Collections.Generic;

namespace Piece_Launcher.Models
{
    public record ExploreProjectDetail(
        string Id,
        string Title,
        string Description,
        string Body,
        string Author,
        string? IconUrl,
        long Downloads,
        string ProjectType,
        string Source,
        IReadOnlyList<string> GameVersions,
        IReadOnlyList<string> Categories,
        DateTimeOffset DateModified,
        string? License,
        IReadOnlyList<string> Gallery
    );
}
