using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Piece_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Piece_Launcher.Services.Impl
{
    public class AvaloniaThemeService : IThemeService
    {
        public IReadOnlyList<ThemeDefinition> AvailableThemes { get; } = new List<ThemeDefinition>
        {
            new ThemeDefinition("DarkOrange", "Dark Orange", true, new Dictionary<string, string>()),
            new ThemeDefinition("Light", "Light", false, new Dictionary<string, string>()),
            new ThemeDefinition("DarkPurple", "Dark Purple", true, new Dictionary<string, string>()),
            new ThemeDefinition("MidnightBlue", "Midnight Blue", true, new Dictionary<string, string>()),
            new ThemeDefinition("ForestGreen", "Forest Green", true, new Dictionary<string, string>()),
            new ThemeDefinition("CrimsonRed", "Crimson Red", true, new Dictionary<string, string>())
        };

        public ThemeDefinition CurrentTheme { get; private set; }

        public AvaloniaThemeService()
        {
            CurrentTheme = AvailableThemes[0];
        }

        public void ApplyTheme(string themeName)
        {
            var theme = AvailableThemes.FirstOrDefault(t => t.Name == themeName) ?? AvailableThemes[0];
            CurrentTheme = theme;

            var app = Application.Current;
            if (app == null) return;

            // Switch Avalonia theme variant (Light vs Dark) for FluentTheme controls
            app.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
            
            // Always replace the theme at index 0
            var dictionaries = app.Resources.MergedDictionaries;
            if (dictionaries != null && dictionaries.Count > 0)
            {
                var uri = new Uri($"avares://Piece%20Launcher/Themes/{theme.Name}Theme.axaml");
                var include = new ResourceInclude(new Uri("avares://Piece%20Launcher/")) { Source = uri };
                
                dictionaries[0] = include;
            }
        }

        public void ApplyCustomAccentColor(Color color)
        {
            Application.Current!.Resources["AccentColor"] = color;
        }
    }
}
