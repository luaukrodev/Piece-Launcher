using Avalonia;
using System;
using System.Globalization;

namespace Piece_Launcher.Helpers
{
    public static class LocalizationHelper
    {
        public static string Get(string key)
        {
            if (Application.Current is { } application
                && application.TryGetResource(key, null, out var resource)
                && resource is string text)
                return text;

            return key;
        }

        public static string Format(string key, params object?[] arguments) =>
            string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

        public static string GetInstallStage(string stage) => stage switch
        {
            "Initializing" => Get("Lang_Progress_Initializing"),
            "Downloading" => Get("Lang_Progress_Downloading"),
            "Completed" => Get("Lang_Progress_Completed"),
            "Preparing Forge" or "Preparing NeoForge" => Format("Lang_Progress_PreparingLoader", stage.Replace("Preparing ", "", StringComparison.Ordinal)),
            "Installing Forge" or "Installing NeoForge" => Format("Lang_Progress_InstallingLoader", stage.Replace("Installing ", "", StringComparison.Ordinal)),
            "Forge Installed" or "Fabric Installed" or "NeoForge Installed" or "Quilt Installed" =>
                Format("Lang_Progress_LoaderInstalled", stage.Replace(" Installed", "", StringComparison.Ordinal)),
            "Fetching Fabric profile" => Format("Lang_Progress_FetchingLoaderProfile", "Fabric"),
            "Fetching Quilt profile" => Format("Lang_Progress_FetchingLoaderProfile", "Quilt"),
            "Downloading Minecraft and Fabric libraries" => Format("Lang_Progress_DownloadingLoaderLibraries", "Fabric"),
            "Downloading Minecraft and Quilt libraries" => Format("Lang_Progress_DownloadingLoaderLibraries", "Quilt"),
            _ => stage
        };
    }
}
