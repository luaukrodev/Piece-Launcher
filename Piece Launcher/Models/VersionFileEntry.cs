using System;

namespace Piece_Launcher.Models
{
    public sealed class VersionFileEntry
    {
        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public bool IsJarFile => !IsDirectory && Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
        public bool IsJsonFile => !IsDirectory && Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        public bool IsOtherFile => !IsDirectory && !IsJarFile && !IsJsonFile;
        public string SizeText { get; }

        public VersionFileEntry(string name, string fullPath, bool isDirectory, long size)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
            SizeText = isDirectory ? string.Empty : FormatSize(size);
        }

        private static string FormatSize(long size)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double value = size;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value:0.#} {units[unit]}";
        }
    }
}
