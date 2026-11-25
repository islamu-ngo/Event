namespace ISLAMU.ReleaseEngineering;

public sealed record GitCliffToolConfiguration(
    string LockPath,
    string ToolDirectory,
    string ConfigPath,
    string Platform,
    TimeSpan Timeout);

public sealed record GitCliffContextExportRequest(
    GitCliffToolConfiguration Tool,
    string RepositoryRoot,
    string Tag,
    string? CommitRange);

public sealed record GitCliffContextExportResult(
    bool IsValid,
    string? RawContextJson,
    string? Diagnostic);

public sealed record GitCliffRenderFromContextRequest(
    GitCliffToolConfiguration Tool,
    string WorkingDirectory,
    string RenderingContextJson);

public sealed record GitCliffRenderFromContextResult(
    bool IsValid,
    string? Markdown,
    string? Diagnostic);

public sealed record GitCliffBumpVersionRequest(
    GitCliffToolConfiguration Tool,
    string RepositoryRoot,
    string? CommitRange);

public sealed record GitCliffBumpVersionResult(
    bool IsValid,
    string? SuggestedVersion,
    string? RawSuggestedVersion,
    string? Diagnostic);

/// <summary>
/// Process boundary for invoking a lock-verified <c>git-cliff</c> executable in offline <c>--no-exec</c> mode.
/// </summary>
public interface IGitCliffExecutor
{
    GitCliffContextExportResult ExportContext(GitCliffContextExportRequest request);

    GitCliffRenderFromContextResult RenderFromContext(GitCliffRenderFromContextRequest request);

    GitCliffBumpVersionResult SuggestBumpedVersion(GitCliffBumpVersionRequest request);
}
