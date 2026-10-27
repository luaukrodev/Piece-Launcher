using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Piece_Launcher.Models;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Piece_Launcher.Helpers
{
    public static class SkinTextureService
    {
        private static readonly HttpClient HttpClient = new();
        private static readonly Lazy<Bitmap> DefaultSkin = new(CreateDefaultSkin);

        public static Bitmap GetDefaultSkin() => DefaultSkin.Value;

        public static Bitmap CreateHeadImage(Bitmap skin)
        {
            var size = skin.PixelSize;
            using var staging = new WriteableBitmap(
                size, skin.Dpi, PixelFormats.Bgra8888, AlphaFormat.Unpremul);
            using var framebuffer = staging.Lock();
            skin.CopyPixels(framebuffer);

            var pixels = new byte[framebuffer.RowBytes * size.Height];
            System.Runtime.InteropServices.Marshal.Copy(framebuffer.Address, pixels, 0, pixels.Length);
            var head = new byte[8 * 8 * 4];
            for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                var source = (y + 8) * framebuffer.RowBytes + (x + 8) * 4;
                var target = (y * 8 + x) * 4;
                Array.Copy(pixels, source, head, target, 4);

                if (size.Height == 64)
                {
                    var overlay = (y + 8) * framebuffer.RowBytes + (x + 40) * 4;
                    var alpha = pixels[overlay + 3];
                    for (var channel = 0; channel < 3; channel++)
                        head[target + channel] = (byte)((pixels[overlay + channel] * alpha +
                            head[target + channel] * (255 - alpha) + 127) / 255);
                    head[target + 3] = (byte)(alpha + head[target + 3] * (255 - alpha) / 255);
                }
            }

            var enlargedHead = new byte[64 * 64 * 4];
            for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            for (var scaleY = 0; scaleY < 8; scaleY++)
            for (var scaleX = 0; scaleX < 8; scaleX++)
            {
                var source = (y * 8 + x) * 4;
                var target = ((y * 8 + scaleY) * 64 + x * 8 + scaleX) * 4;
                Array.Copy(head, source, enlargedHead, target, 4);
            }

            return CreateBitmap(enlargedHead, 64, 64);
        }

        public static Bitmap DecodeSkin(byte[] png)
        {
            if (png.Length < 8 ||
                !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                throw new InvalidDataException("Select a valid PNG file.");
            }

            using var stream = new MemoryStream(png, writable: false);
            var skin = new Bitmap(stream);
            if (skin.PixelSize.Width != 64 || skin.PixelSize.Height is not (32 or 64))
            {
                skin.Dispose();
                throw new InvalidDataException("Minecraft skins must be 64x64 or 64x32 PNG images.");
            }

            return skin;
        }

        public static async Task<Bitmap> LoadProfileSkinAsync(ProfileEntry profile)
        {
            if (profile.Type == ProfileType.Offline)
            {
                if (!string.IsNullOrWhiteSpace(profile.SkinPath) && File.Exists(profile.SkinPath))
                    return new Bitmap(profile.SkinPath);

                return GetDefaultSkin();
            }

            return await LoadMicrosoftSkinAsync(profile.Id);
        }

        public static string GetSavedSkinPath(string profileId)
        {
            var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(profileId)));
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PieceLauncher", "Skins", $"{id}.png");
        }

        private static async Task<Bitmap> LoadMicrosoftSkinAsync(string profileId)
        {
            var id = profileId.Replace("-", string.Empty, StringComparison.Ordinal);
            using var response = await HttpClient.GetAsync(
                $"https://sessionserver.mojang.com/session/minecraft/profile/{Uri.EscapeDataString(id)}");
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content.ReadAsStreamAsync();
            using var profileJson = await JsonDocument.ParseAsync(responseStream);
            var properties = profileJson.RootElement.GetProperty("properties");
            string? textureValue = null;
            foreach (var property in properties.EnumerateArray())
            {
                if (property.TryGetProperty("name", out var name) && name.GetString() == "textures")
                {
                    textureValue = property.GetProperty("value").GetString();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(textureValue))
                throw new InvalidDataException("The Minecraft profile did not include skin data.");

            using var textureJson = JsonDocument.Parse(Convert.FromBase64String(textureValue));
            if (!textureJson.RootElement.TryGetProperty("textures", out var textures) ||
                !textures.TryGetProperty("SKIN", out var skin) ||
                !skin.TryGetProperty("url", out var skinUrl))
            {
                throw new InvalidDataException("The Minecraft profile does not have a published skin.");
            }

            var png = await HttpClient.GetByteArrayAsync(skinUrl.GetString()
                ?? throw new InvalidDataException("The Minecraft profile returned an invalid skin URL."));
            return DecodeSkin(png);
        }

        private static Bitmap CreateDefaultSkin()
        {
            using var skinStream = AssetLoader.Open(new Uri("avares://Piece Launcher/Assets/steve.png"));
            using var memoryStream = new MemoryStream();
            skinStream.CopyTo(memoryStream);
            return DecodeSkin(memoryStream.ToArray());
        }

        private static Bitmap CreateBitmap(byte[] pixels, int width, int height)
        {
            var handle = System.Runtime.InteropServices.Marshal.AllocHGlobal(pixels.Length);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, handle, pixels.Length);
                return new Bitmap(
                    PixelFormats.Bgra8888,
                    AlphaFormat.Unpremul,
                    handle,
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    width * 4);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(handle);
            }
        }
    }
}
