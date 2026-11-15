using CommunityToolkit.Mvvm.ComponentModel;

namespace Piece_Launcher.Models
{
    public sealed class InstalledVersionOption : ObservableObject
    {
        public string VersionId { get; }
        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            private set => SetProperty(ref _displayName, value);
        }

        public InstalledVersionOption(string versionId, string displayName)
        {
            VersionId = versionId;
            _displayName = displayName;
        }

        public void RefreshDisplayName(string displayName) => DisplayName = displayName;
    }
}
