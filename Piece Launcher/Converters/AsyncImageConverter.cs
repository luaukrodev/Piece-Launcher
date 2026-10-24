using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Piece_Launcher.Converters
{
    public class AsyncImageConverter : IValueConverter
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly ConcurrentDictionary<string, Task<Bitmap?>> _cache = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string url && !string.IsNullOrWhiteSpace(url))
            {
                return _cache.GetOrAdd(url, async (u) =>
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
                        return null; // Return null if download fails
                    }
                });
            }

            return Task.FromResult<Bitmap?>(null);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
