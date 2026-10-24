using System;
using System.Collections.Generic;

namespace Piece_Launcher.Models
{
    public record ExploreProjectVersion(
        string Id,
        string ProjectId,
        string Name,
        string VersionNumber,
        string VersionType,
        string? Changelog,
        IReadOnlyList<string> GameVersions,
        IReadOnlyList<string> Loaders,
        DateTimeOffset DatePublished,
        long Downloads,
        string DownloadUrl,
        string Filename,
        long FileSize
    );
}
