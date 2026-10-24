using System;
using System.Collections.Concurrent;
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
            
            if (string.IsNullOrWhiteSpace(url))
            {
                sender.Source = null;
                return;
            }

            try
            {
                var bitmap = await _cache.GetOrAdd(url, async (u) =>
                {
                    try
                    {
                        var response = await _httpClient.GetAsync(u);
                        response.EnsureSuccessStatusCode();
                        await using var stream = await response.Content.ReadAsStreamAsync();
                        return new Bitmap(stream);
                    }
                    catch
                    {
                        return null;
                    }
                });

                sender.Source = bitmap;
            }
            catch
            {
                sender.Source = null;
            }
        }
    }
}
