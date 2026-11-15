using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Text.Json.Serialization;

namespace Piece_Launcher.Models
{
    public sealed class MinecraftInstanceEntry : ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        private string _name = string.Empty;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        public string GameDirectory { get; set; } = string.Empty;
        public string? MinecraftVersion { get; set; }
        public string? Loader { get; set; }
        public string? LoaderVersion { get; set; }
        public string? VersionId { get; set; }
        public string? SourceLauncher { get; set; }
        public string? ImportSourceDirectory { get; set; }
        public bool IsDefault { get; set; }
        public string? ModpackPlatform { get; set; }
        public string? ModpackProjectId { get; set; }
        public string? ModpackVersionId { get; set; }

        [JsonIgnore]
        public string Description => string.IsNullOrWhiteSpace(MinecraftVersion)
            ? Loader ?? string.Empty
            : $"{MinecraftVersion}{(string.IsNullOrWhiteSpace(Loader) ? string.Empty : $" · {Loader}")}";
    }
}
