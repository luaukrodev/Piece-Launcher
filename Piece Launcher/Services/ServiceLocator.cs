using System;

namespace Piece_Launcher.Services
{
    public static class ServiceLocator
    {
        public static IVersionService VersionService { get; private set; } = null!;
        public static IExploreService ModrinthExploreService { get; private set; } = null!;
        public static IExploreService CurseForgeExploreService { get; private set; } = null!;
        public static IThemeService ThemeService { get; private set; } = null!;
        public static ILanguageService LanguageService { get; private set; } = null!;
        
        public static IModLoaderService ForgeLoaderService { get; private set; } = null!;
        public static IModLoaderService FabricLoaderService { get; private set; } = null!;
        public static IModLoaderService NeoForgeLoaderService { get; private set; } = null!;
        public static IModLoaderService QuiltLoaderService { get; private set; } = null!;

        public static void Initialize(
            IVersionService versionService,
            IExploreService modrinthService,
            IExploreService curseForgeService,
            IThemeService themeService,
            ILanguageService languageService,
            IModLoaderService forgeService,
            IModLoaderService fabricService,
            IModLoaderService neoForgeService,
            IModLoaderService quiltService)
        {
            VersionService = versionService;
            ModrinthExploreService = modrinthService;
            CurseForgeExploreService = curseForgeService;
            ThemeService = themeService;
            LanguageService = languageService;
            ForgeLoaderService = forgeService;
            FabricLoaderService = fabricService;
            NeoForgeLoaderService = neoForgeService;
            QuiltLoaderService = quiltService;
        }
    }
}
