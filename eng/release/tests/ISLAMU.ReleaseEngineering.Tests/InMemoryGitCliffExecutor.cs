using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

internal sealed class InMemoryGitCliffExecutor : IGitCliffExecutor
{
    public Func<GitCliffContextExportRequest, GitCliffContextExportResult>? OnExportContext { get; set; }

    public Func<GitCliffRenderFromContextRequest, GitCliffRenderFromContextResult>? OnRenderFromContext { get; set; }

    public Func<GitCliffBumpVersionRequest, GitCliffBumpVersionResult>? OnSuggestBumpedVersion { get; set; }

    public List<GitCliffContextExportRequest> ExportRequests { get; } = [];

    public List<GitCliffRenderFromContextRequest> RenderRequests { get; } = [];

    public List<GitCliffBumpVersionRequest> BumpRequests { get; } = [];

    public GitCliffContextExportResult ExportContext(GitCliffContextExportRequest request)
    {
        ExportRequests.Add(request);
        return OnExportContext is not null
            ? OnExportContext(request)
            : new GitCliffContextExportResult(false, null, "git_cliff_stub_unconfigured");
    }

    public GitCliffRenderFromContextResult RenderFromContext(GitCliffRenderFromContextRequest request)
    {
        RenderRequests.Add(request);
        return OnRenderFromContext is not null
            ? OnRenderFromContext(request)
            : new GitCliffRenderFromContextResult(false, null, "git_cliff_stub_unconfigured");
    }

    public GitCliffBumpVersionResult SuggestBumpedVersion(GitCliffBumpVersionRequest request)
    {
        BumpRequests.Add(request);
        return OnSuggestBumpedVersion is not null
            ? OnSuggestBumpedVersion(request)
            : new GitCliffBumpVersionResult(false, null, null, "git_cliff_stub_unconfigured");
    }
}
