using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Piece_Launcher.Helpers
{
    public static class ImageAsyncHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly ConcurrentDictionary<string, Task<Bitmap?>> _cache = new();

        public static readonly AttachedProperty<string?> SourceUrlProperty =
            AvaloniaProperty.RegisterAttached<Image, string?>("SourceUrl", typeof(ImageAsyncHelper));

        static ImageAsyncHelper()
        {
            SourceUrlProperty.Changed.AddClassHandler<Image>(OnSourceUrlChanged);
        }

        public static string? GetSourceUrl(Image element)
        {
            return element.GetValue(SourceUrlProperty);
        }

        public static void SetSourceUrl(Image element, string? value)
        {
            element.SetValue(SourceUrlProperty, value);
        }

        private static async void OnSourceUrlChanged(Image sender, AvaloniaPropertyChangedEventArgs e)
        {
            var url = e.NewValue as string;
            sender.Source = null;

            if (string.IsNullOrWhiteSpace(url))
                return;

            try
            {
                var bitmap = await _cache.GetOrAdd(url, LoadBitmapAsync);
                if (bitmap == null)
                    _cache.TryRemove(url, out _);

                if (GetSourceUrl(sender) == url)
                    sender.Source = bitmap;
            }
            catch
            {
                sender.Source = null;
            }
        }

        private static async Task<Bitmap?> LoadBitmapAsync(string url)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                await using var responseStream = await response.Content.ReadAsStreamAsync();
                using var bitmapStream = new MemoryStream();
                await responseStream.CopyToAsync(bitmapStream);
                bitmapStream.Position = 0;
                return new Bitmap(bitmapStream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load image '{url}': {ex.Message}");
                return null;
            }
        }
    }
}
