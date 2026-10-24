using System;

namespace Piece_Launcher.Models
{
    public record VersionEntry(
        string Name,
        string Type,
        DateTimeOffset ReleaseTime,
        bool IsInstalled
    );
}
