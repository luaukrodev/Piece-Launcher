using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Piece_Launcher.Converters
{
    public class CompactDownloadCountConverter : IValueConverter
    {
        private static readonly string[] Suffixes = { "", "k", "mi", "bi" };

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not long downloads)
                return value?.ToString() ?? string.Empty;

            decimal count = downloads;
            var suffixIndex = 0;
            while (count >= 1000 && suffixIndex < Suffixes.Length - 1)
            {
                count /= 1000;
                suffixIndex++;
            }

            count = decimal.Round(count, 1, MidpointRounding.AwayFromZero);
            if (count >= 1000 && suffixIndex < Suffixes.Length - 1)
            {
                count /= 1000;
                suffixIndex++;
            }

            return count.ToString("0.#", culture) + Suffixes[suffixIndex];
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
