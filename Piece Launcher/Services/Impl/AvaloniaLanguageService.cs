using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using System;
using System.Linq;

namespace Piece_Launcher.Services.Impl
{
    public class AvaloniaLanguageService : ILanguageService
    {
        public string CurrentLanguage { get; private set; } = "English";

        public void ApplyLanguage(string languageName)
        {
            CurrentLanguage = languageName;

            var app = Application.Current;
            if (app == null) return;

            // Always replace the language at index 1
            var dictionaries = app.Resources.MergedDictionaries;
            if (dictionaries != null && dictionaries.Count > 1)
            {
                var uri = new Uri($"avares://Piece Launcher/Langs/{languageName}.axaml");
                var include = new ResourceInclude(new Uri("avares://Piece Launcher/App.axaml")) { Source = uri };
                
                dictionaries[1] = include;
            }
        }
    }
}
