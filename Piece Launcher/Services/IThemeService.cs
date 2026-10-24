using Avalonia.Media;
using Piece_Launcher.Models;
using System.Collections.Generic;

namespace Piece_Launcher.Services
{
    public interface IThemeService
    {
        IReadOnlyList<ThemeDefinition> AvailableThemes { get; }
        ThemeDefinition CurrentTheme { get; }
        void ApplyTheme(string themeName);
        void ApplyCustomAccentColor(Color color);
    }
}
