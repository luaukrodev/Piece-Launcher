using Avalonia.Media.Imaging;
using Piece_Launcher.Helpers;
using Piece_Launcher.Models;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services.Impl
{
    public sealed class SkinService : ISkinService
    {
        private readonly ConcurrentDictionary<string, Lazy<Task<Bitmap>>> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public Task<Bitmap> GetSkinForProfileAsync(ProfileEntry profile, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(profile);

            var skinTask = _cache.GetOrAdd(
                profile.Id,
                _ => new Lazy<Task<Bitmap>>(() => LoadSkinOrDefaultAsync(profile))).Value;

            return ct.CanBeCanceled ? skinTask.WaitAsync(ct) : skinTask;
        }

        public void InvalidateCache(string profileId)
        {
            if (!string.IsNullOrWhiteSpace(profileId))
                _cache.TryRemove(profileId, out _);
        }

        private static async Task<Bitmap> LoadSkinOrDefaultAsync(ProfileEntry profile)
        {
            try
            {
                return await SkinTextureService.LoadProfileSkinAsync(profile);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"Could not load skin for profile '{profile.Name}': {exception.Message}");
                return SkinTextureService.GetDefaultSkin();
            }
        }
    }
}
