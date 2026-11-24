namespace ISLAMU.ReleaseEngineering;

public sealed record GitBlobReadResult(
    bool Found,
    byte[]? Bytes,
    string? Diagnostic);

public sealed record GitTreeListResult(
    bool IsValid,
    IReadOnlyList<string> Paths,
    string? Diagnostic);

/// <summary>
/// Read-only port over pinned Git commit trees and blobs. Implementations must never read working-tree files.
/// </summary>
public interface IGitObjectReader
{
    bool CommitExists(string fullOid);

    GitTreeListResult ListFiles(string fullOid, string? pathPrefix = null);

    GitBlobReadResult ReadBlob(string fullOid, string repositoryRelativePath);
}
