using Piece_Launcher.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Piece_Launcher.Services
{
    public interface IVersionManagerService
    {
        string GetVersionDirectory(string versionId);
        IReadOnlyList<VersionFileEntry> GetDirectoryEntries(string versionId, string directoryPath);
        Task<string> CreateInstalledCopyAsync(string sourceVersionId, CancellationToken ct = default);
        Task DeleteVersionAsync(string versionId, CancellationToken ct = default);
    }

    public sealed class VersionInUseException : System.InvalidOperationException
    {
        public IReadOnlyList<string> DependentVersions { get; }

        public VersionInUseException(IReadOnlyList<string> dependentVersions)
            : base("The version is required by one or more installed versions.")
        {
            DependentVersions = dependentVersions;
        }
    }
}
