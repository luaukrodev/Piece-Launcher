using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Piece_Launcher.Models;
using Piece_Launcher.Services;
using System;
using System.ComponentModel;
using System.Reflection;

namespace Piece_Launcher.ViewModels.Pages
{
    public partial class SettingsPageViewModel : ViewModelBase
    {
        private readonly IThemeService _themeService;
        private readonly ILanguageService _languageService;

        public LauncherConfig Config { get; }
        public string LauncherVersion { get; } =
            typeof(SettingsPageViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                .Split('+')[0]
            ?? throw new InvalidOperationException("The launcher version was not configured.");

        // Collections for ComboBox ItemsSource — plain strings so SelectedItem binding works
        public string[] AvailableLanguages { get; } = new[] { "Português", "English", "Español", "Français", "Deutsch" };
        public string[] AvailableThemes { get; } = new[] { "DarkOrange", "Light", "DarkPurple", "MidnightBlue", "ForestGreen", "CrimsonRed" };
        public string[] AvailableStartupPages { get; } = new[] { "Home", "Profiles", "Versions", "Discover" };

        public SettingsPageViewModel(LauncherConfig config, IThemeService themeService, ILanguageService languageService)
        {
            Config = config;
            _themeService = themeService;
            _languageService = languageService;

            // React to property changes on Config so settings take effect immediately
            Config.PropertyChanged += OnConfigPropertyChanged;
        }

        private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LauncherConfig.Theme))
            {
                _themeService.ApplyTheme(Config.Theme);
            }
            else if (e.PropertyName == nameof(LauncherConfig.Language))
            {
                _languageService.ApplyLanguage(Config.Language);
            }

            // Auto-save whenever any observable setting changes
            Config.Save();
        }

        [RelayCommand]
        private void SaveSettings()
        {
            Config.Save();
            _themeService.ApplyTheme(Config.Theme);
        }

        [RelayCommand]
        private void ResetToDefaults()
        {
            var newConfig = new LauncherConfig();
            Config.Language = newConfig.Language;
            Config.Theme = newConfig.Theme;
            Config.StartupPage = newConfig.StartupPage;
            Config.LaunchOnStartup = newConfig.LaunchOnStartup;
            Config.KeepOpenWhilePlaying = newConfig.KeepOpenWhilePlaying;
            // Theme will be applied automatically via PropertyChanged handler
            Config.Save();
        }
    }
}
