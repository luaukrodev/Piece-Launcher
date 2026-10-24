using System.Collections.Generic;

namespace Piece_Launcher.Models
{
    public record ThemeDefinition(
        string Name,
        string DisplayName,
        bool IsDark,
        Dictionary<string, string> ColorTokens
    );
}
