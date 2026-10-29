using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;

namespace Piece_Launcher.Views.Pages
{
    public partial class SettingsPageView : UserControl
    {
        public SettingsPageView()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdateActiveSection();
        }

        private void OnSectionNavigationClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var sectionName = button.Tag as string
                ?? button.Name?.Replace("NavItem", string.Empty, StringComparison.Ordinal);
            if (string.IsNullOrEmpty(sectionName))
                return;

            var section = GetSection(sectionName);
            if (section is not null)
            {
                SetActiveNavigationItem(sectionName);
                Dispatcher.UIThread.Post(() =>
                {
                    var maxOffset = Math.Max(0, SettingsScroll.Extent.Height - SettingsScroll.Viewport.Height);
                    var targetOffset = Math.Clamp(section.Bounds.Y, 0, maxOffset);
                    SettingsScroll.Offset = new Vector(SettingsScroll.Offset.X, targetOffset);
                });
            }
        }

        private void OnSettingsScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            UpdateActiveSection();
        }

        private void UpdateActiveSection()
        {
            if (SettingsScroll is null
                || GeneralSection is null
                || AppearanceSection is null
                || GameSection is null
                || MemorySection is null
                || VersionsSection is null
                || UpdatesSection is null
                || AboutSection is null)
                return;

            var sections = new (string Name, Control Section)[]
            {
                ("General", GeneralSection),
                ("Appearance", AppearanceSection),
                ("Game", GameSection),
                ("Memory", MemorySection),
                ("Versions", VersionsSection),
                ("Updates", UpdatesSection),
                ("About", AboutSection)
            };

            var activeSection = sections[0].Name;
            foreach (var (name, section) in sections)
            {
                var position = section.TranslatePoint(new Point(0, 0), SettingsScroll);
                if (position is { Y: <= 120 })
                    activeSection = name;
            }

            var maxOffset = Math.Max(0, SettingsScroll.Extent.Height - SettingsScroll.Viewport.Height);
            if (SettingsScroll.Offset.Y >= maxOffset - 1)
                activeSection = "About";

            SetActiveNavigationItem(activeSection);
        }

        private Control? GetSection(string name) => name switch
        {
            "General" => GeneralSection,
            "Appearance" => AppearanceSection,
            "Game" => GameSection,
            "Memory" => MemorySection,
            "Versions" => VersionsSection,
            "Updates" => UpdatesSection,
            "About" => AboutSection,
            _ => null
        };

        private void SetActiveNavigationItem(string name)
        {
            if (GeneralNavItem is null
                || AppearanceNavItem is null
                || GameNavItem is null
                || MemoryNavItem is null
                || VersionsNavItem is null
                || UpdatesNavItem is null
                || AboutNavItem is null)
                return;

            GeneralNavItem.Classes.Set("SettingsNavActive", name == "General");
            AppearanceNavItem.Classes.Set("SettingsNavActive", name == "Appearance");
            GameNavItem.Classes.Set("SettingsNavActive", name == "Game");
            MemoryNavItem.Classes.Set("SettingsNavActive", name == "Memory");
            VersionsNavItem.Classes.Set("SettingsNavActive", name == "Versions");
            UpdatesNavItem.Classes.Set("SettingsNavActive", name == "Updates");
            AboutNavItem.Classes.Set("SettingsNavActive", name == "About");
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            SettingsScroll = this.FindControl<ScrollViewer>(nameof(SettingsScroll))
                ?? throw new InvalidOperationException("The settings scroll viewer was not found.");
            SectionContent = this.FindControl<StackPanel>(nameof(SectionContent))
                ?? throw new InvalidOperationException("The settings section content was not found.");
            GeneralSection = this.FindControl<Border>(nameof(GeneralSection))
                ?? throw new InvalidOperationException("The General settings section was not found.");
            AppearanceSection = this.FindControl<Border>(nameof(AppearanceSection))
                ?? throw new InvalidOperationException("The Appearance settings section was not found.");
            GameSection = this.FindControl<Border>(nameof(GameSection))
                ?? throw new InvalidOperationException("The Game settings section was not found.");
            MemorySection = this.FindControl<Border>(nameof(MemorySection))
                ?? throw new InvalidOperationException("The Memory settings section was not found.");
            VersionsSection = this.FindControl<Border>(nameof(VersionsSection))
                ?? throw new InvalidOperationException("The Versions settings section was not found.");
            UpdatesSection = this.FindControl<Border>(nameof(UpdatesSection))
                ?? throw new InvalidOperationException("The Updates settings section was not found.");
            AboutSection = this.FindControl<Border>(nameof(AboutSection))
                ?? throw new InvalidOperationException("The About settings section was not found.");
            GeneralNavItem = this.FindControl<Button>(nameof(GeneralNavItem))
                ?? throw new InvalidOperationException("The General navigation button was not found.");
            AppearanceNavItem = this.FindControl<Button>(nameof(AppearanceNavItem))
                ?? throw new InvalidOperationException("The Appearance navigation button was not found.");
            GameNavItem = this.FindControl<Button>(nameof(GameNavItem))
                ?? throw new InvalidOperationException("The Game navigation button was not found.");
            MemoryNavItem = this.FindControl<Button>(nameof(MemoryNavItem))
                ?? throw new InvalidOperationException("The Memory navigation button was not found.");
            VersionsNavItem = this.FindControl<Button>(nameof(VersionsNavItem))
                ?? throw new InvalidOperationException("The Versions navigation button was not found.");
            UpdatesNavItem = this.FindControl<Button>(nameof(UpdatesNavItem))
                ?? throw new InvalidOperationException("The Updates navigation button was not found.");
            AboutNavItem = this.FindControl<Button>(nameof(AboutNavItem))
                ?? throw new InvalidOperationException("The About navigation button was not found.");
        }
    }
}
