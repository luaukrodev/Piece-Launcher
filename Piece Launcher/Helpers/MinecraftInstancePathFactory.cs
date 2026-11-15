using CmlLib.Core;

namespace Piece_Launcher.Helpers
{
    public static class MinecraftInstancePathFactory
    {
        public static MinecraftPath Create(string instanceDirectory, MinecraftPath sharedPath)
        {
            var path = new MinecraftPath(instanceDirectory)
            {
                Versions = sharedPath.Versions,
                Library = sharedPath.Library,
                Assets = sharedPath.Assets,
                Resource = sharedPath.Resource,
                Runtime = sharedPath.Runtime
            };
            return path;
        }
    }
}
