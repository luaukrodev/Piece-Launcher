using Piece_Launcher.Helpers;
using System;

namespace Piece_Launcher.Models
{
    public enum InstalledContentType
    {
        Mod,
        ResourcePack,
        Shader,
        DataPack
    }

    public sealed record InstalledContentRecord(
        string RelativePath,
        string Source,
        string ProjectId,
        string VersionId,
        string Name,
        string ProjectType,
        string? GameVersion,
        string? Loader);

    public sealed record InstalledContentItem(
        string Name,
        string RelativePath,
        InstalledContentType Type,
        bool IsEnabled,
        long Size,
        InstalledContentRecord? ManagedRecord)
    {
        public bool IsManaged => ManagedRecord is not null
            && !string.IsNullOrWhiteSpace(ManagedRecord.ProjectId)
            && !string.IsNullOrWhiteSpace(ManagedRecord.Source);

        public string TypeLabel => LocalizationHelper.Get(Type switch
        {
            InstalledContentType.Mod => "Lang_MinecraftMods_TypeMod",
            InstalledContentType.ResourcePack => "Lang_MinecraftMods_TypeResourcePack",
            InstalledContentType.Shader => "Lang_MinecraftMods_TypeShader",
            InstalledContentType.DataPack => "Lang_MinecraftMods_TypeDataPack",
            _ => throw new ArgumentOutOfRangeException(nameof(Type), Type, "Unknown installed content type.")
        });
    }
}
