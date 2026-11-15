using Avalonia.Media.Imaging;
using Piece_Launcher.Helpers;
using Piece_Launcher.Models;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services
{
    public interface ISkinService
    {
        Task<Bitmap> GetSkinForProfileAsync(ProfileEntry profile, CancellationToken ct = default);
        void InvalidateCache(string profileId);
    }
}
