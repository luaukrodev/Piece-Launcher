using System.Collections.Generic;

namespace Piece_Launcher.Models
{
    public sealed record CurseForgePackFileReference(int ProjectId, int FileId);

    public sealed record CurseForgePackFileDownload(
        int ProjectId,
        int FileId,
        string FileName,
        string DownloadUrl,
        long FileLength,
        IReadOnlyList<CurseForgeFileHash>? Hashes);

    public sealed record CurseForgeFileHash(int Algo, string Value);
}
