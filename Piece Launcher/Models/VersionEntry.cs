using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Piece_Launcher.Helpers;
using System;
using System.Diagnostics;

namespace Piece_Launcher.Models
{
    public sealed class VersionEntry : ObservableObject
    {
        private static readonly Bitmap? FabricIcon = LoadIcon("Fabric.png");
        private static readonly Bitmap? ForgeIcon = LoadIcon("Forge.png");
        private static readonly Bitmap? NeoForgeIcon = LoadIcon("NeoForge.png");
        private static readonly Bitmap? QuiltIcon = LoadIcon("Quilt.png");

        private bool _isSelected;

        public string Name { get; }
        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            private set => SetProperty(ref _displayName, value);
        }
        public bool HasCustomDisplayName => !string.Equals(Name, DisplayName, StringComparison.Ordinal);
        public string Type { get; }
        public string DisplayType => Type.ToLowerInvariant() switch
        {
            "release" => LocalizationHelper.Get("Lang_Versions_TypeRelease"),
            "snapshot" => LocalizationHelper.Get("Lang_Versions_TypeSnapshot"),
            "old_beta" => LocalizationHelper.Get("Lang_Versions_TypeBeta"),
            "old_alpha" => LocalizationHelper.Get("Lang_Versions_TypeAlpha"),
            "custom" => LocalizationHelper.Get("Lang_Versions_TypeCustom"),
            _ => Type
        };
        public DateTimeOffset ReleaseTime { get; }
        public bool IsInstalled { get; }
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public Bitmap? LoaderIcon => GetLoaderIcon(Name);

        public VersionEntry(string name, string type, DateTimeOffset releaseTime, bool isInstalled)
        {
            Name = name;
            _displayName = name;
            Type = type;
            ReleaseTime = releaseTime;
            IsInstalled = isInstalled;
        }

        public void RefreshDisplayType() => OnPropertyChanged(nameof(DisplayType));

        public void RefreshDisplayName(string displayName)
        {
            if (DisplayName == displayName)
                return;

            DisplayName = displayName;
            OnPropertyChanged(nameof(HasCustomDisplayName));
        }

        private static Bitmap? GetLoaderIcon(string name)
        {
            if (name.Contains("neoforge", StringComparison.OrdinalIgnoreCase)) return NeoForgeIcon;
            if (name.Contains("fabric", StringComparison.OrdinalIgnoreCase)) return FabricIcon;
            if (name.Contains("forge", StringComparison.OrdinalIgnoreCase)) return ForgeIcon;
            if (name.Contains("quilt", StringComparison.OrdinalIgnoreCase)) return QuiltIcon;
            return null;
        }

        private static Bitmap? LoadIcon(string fileName)
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri($"avares://Piece Launcher/Assets/{fileName}"));
                return new Bitmap(stream);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"Loader icon 'Assets/{fileName}' is missing or invalid: {exception.Message}");
                return null;
            }
        }
    }
}
