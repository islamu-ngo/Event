using System.Text;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

internal sealed class InMemoryGitObjectReader : IGitObjectReader
{
    private readonly Dictionary<string, Dictionary<string, byte[]>> commits = new(StringComparer.Ordinal);

    public InMemoryGitObjectReader AddCommit(string fullOid, IDictionary<string, string>? textFiles = null)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (textFiles is not null)
        {
            foreach ((string path, string content) in textFiles)
            {
                files[path.Replace('\\', '/')] = Encoding.UTF8.GetBytes(content);
            }
        }

        commits[fullOid] = files;
        return this;
    }

    public bool CommitExists(string fullOid) => commits.ContainsKey(fullOid);

    public GitTreeListResult ListFiles(string fullOid, string? pathPrefix = null)
    {
        if (!commits.TryGetValue(fullOid, out Dictionary<string, byte[]>? files))
        {
            return new GitTreeListResult(false, [], "git_object_commit_missing");
        }

        string? normalizedPrefix = string.IsNullOrWhiteSpace(pathPrefix)
            ? null
            : pathPrefix.Replace('\\', '/').TrimEnd('/') + "/";

        string[] matched = files.Keys
            .Where(path => normalizedPrefix is null ||
                           string.Equals(path, pathPrefix!.Replace('\\', '/').TrimEnd('/'), StringComparison.Ordinal) ||
                           path.StartsWith(normalizedPrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new GitTreeListResult(true, matched, null);
    }

    public GitBlobReadResult ReadBlob(string fullOid, string repositoryRelativePath)
    {
        if (!commits.TryGetValue(fullOid, out Dictionary<string, byte[]>? files))
        {
            return new GitBlobReadResult(false, null, "git_object_commit_missing");
        }

        string normalized = repositoryRelativePath.Replace('\\', '/');
        return files.TryGetValue(normalized, out byte[]? bytes)
            ? new GitBlobReadResult(true, bytes, null)
            : new GitBlobReadResult(false, null, null);
    }
}
