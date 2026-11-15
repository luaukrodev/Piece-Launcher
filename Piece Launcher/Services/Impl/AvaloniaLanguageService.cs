using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using System;
using System.Collections.Generic;

namespace Piece_Launcher.Services.Impl
{
    public class AvaloniaLanguageService : ILanguageService
    {
        private static readonly Dictionary<string, string> LanguageFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Italiano"] = "Italian",
            ["日本語"] = "Japanese",
            ["简体中文"] = "ChineseSimplified",
            ["한국어"] = "Korean",
            ["Русский"] = "Russian"
        };

        public string CurrentLanguage { get; private set; } = "English";

        public void ApplyLanguage(string languageName)
        {
            CurrentLanguage = languageName;

            var app = Application.Current;
            if (app == null) return;

            var dictionaries = app.Resources.MergedDictionaries;
            if (dictionaries == null)
                return;

            for (var i = dictionaries.Count - 1; i >= 0; i--)
            {
                if (dictionaries[i] is ResourceInclude resourceInclude &&
                    resourceInclude.Source?.OriginalString.Contains("/Langs/", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (resourceInclude.Source.OriginalString.EndsWith("/English.axaml", StringComparison.OrdinalIgnoreCase))
                        continue;

                    dictionaries.RemoveAt(i);
                }
            }

            var englishIndex = -1;
            for (var i = 0; i < dictionaries.Count; i++)
            {
                if (dictionaries[i] is ResourceInclude resourceInclude &&
                    resourceInclude.Source?.OriginalString.EndsWith("/English.axaml", StringComparison.OrdinalIgnoreCase) == true)
                {
                    englishIndex = i;
                    break;
                }
            }

            if (englishIndex < 0)
            {
                dictionaries.Add(CreateLanguageInclude("English"));
                englishIndex = dictionaries.Count - 1;
            }

            if (string.Equals(languageName, "English", StringComparison.OrdinalIgnoreCase))
                return;

            var fileName = LanguageFiles.TryGetValue(languageName, out var mappedFileName)
                ? mappedFileName
                : languageName;
            dictionaries.Insert(englishIndex + 1, CreateLanguageInclude(fileName));
        }

        private static ResourceInclude CreateLanguageInclude(string fileName)
        {
            var uri = new Uri($"avares://Piece%20Launcher/Langs/{fileName}.axaml");
            return new ResourceInclude(new Uri("avares://Piece%20Launcher/")) { Source = uri };
        }
    }
}
