using Explore.Diagnostic.Doctor.Infrastructure;

namespace Explore.Diagnostic.Doctor.Checks;

public sealed class McpDebugReadinessDoctorCheck(IDoctorFileSystem fileSystem, string repositoryRoot) : IDoctorCheck
{
    private static readonly string[] RequiredPaths =
    [
        "docs/internal/MCP_DEBUGGING.md",
        "docs/internal/adr/ADR-011-local-mcp-stdio-diagnostic-host.md",
        "Event.API.IntegrationTests/Features/McpProtocolContractTests.cs",
        "Event.API.IntegrationTests/Features/McpProjectedToolTests.cs",
        "Explore.Diagnostic/AiReplay/AiReplayScenarioCodes.cs",
        "Explore.Diagnostic/AiEvaluation/AiEvaluationReportGenerator.cs",
        "Explore.Diagnostic.UnitTests/AiReplay/AiReplayReportGeneratorTests.cs",
        "Explore.Diagnostic.UnitTests/AiEvaluation/AiEvaluationReportGeneratorTests.cs"
    ];

    public string Code => "mcp.debug-readiness";
    public DoctorCheckCategory Category => DoctorCheckCategory.Documentation;

    public Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var findings = new List<string>();
        foreach (var relativePath in RequiredPaths)
        {
            AddMissingPath(findings, relativePath);
        }

        AddDocumentationFindings(findings);
        AddGitIgnoreFindings(findings);

        if (findings.Count > 0)
        {
            return Task.FromResult(DoctorCheckResult.Warn(
                Code,
                Category,
                "MCP debug/test readiness has review items.",
                "Review redacted MCP debug docs, protocol tests, deterministic replay/evaluation coverage, local-secret ignore rules, and the stdio decision ADR before changing MCP client behavior.",
                "docs/internal/MCP_DEBUGGING.md",
                string.Join(", ", findings.Order(StringComparer.Ordinal))));
        }

        return Task.FromResult(DoctorCheckResult.Pass(
            Code,
            Category,
            "MCP debug/test readiness artifacts are present and review-first.",
            "Keep MCP debugging disabled-by-default, stateless, authenticated, redacted, proposal-first, and covered by protocol tests plus deterministic replay/evaluation evidence.",
            "docs/internal/MCP_DEBUGGING.md"));
    }

    private void AddDocumentationFindings(List<string> findings)
    {
        var docsPath = Path.Combine(repositoryRoot, "docs/internal/MCP_DEBUGGING.md");
        if (!fileSystem.FileExists(docsPath))
        {
            return;
        }

        var docs = fileSystem.ReadAllText(docsPath);
        AddMissingText(findings, docs, "McpProtocolContractTests", "docs:missing-protocol-harness");
        AddMissingText(findings, docs, "EventManagementMcpAuthenticatedReadTests", "docs:missing-event-management-harness");
        AddMissingText(findings, docs, "resources/templates/list", "docs:missing-resource-template-smoke");
        AddMissingText(findings, docs, "event_management_context", "docs:missing-event-management-context-smoke");
        AddMissingText(findings, docs, "propose_create_event_draft", "docs:missing-projected-tool-smoke");
        AddMissingText(findings, docs, "propose_update_event_draft", "docs:missing-event-management-proposal-smoke");
        AddMissingText(findings, docs, "redacted", "docs:missing-redaction-guidance");

        if (ContainsLikelySecret(docs))
        {
            findings.Add("docs:possible-secret");
        }
    }

    private void AddGitIgnoreFindings(List<string> findings)
    {
        var gitIgnorePath = Path.Combine(repositoryRoot, ".gitignore");
        if (!fileSystem.FileExists(gitIgnorePath))
        {
            findings.Add("missing:.gitignore");
            return;
        }

        var gitIgnore = fileSystem.ReadAllText(gitIgnorePath);
        AddMissingText(findings, gitIgnore, ".mcp.json", "gitignore:missing-mcp-json");
        AddMissingText(findings, gitIgnore, ".vscode/*", "gitignore:missing-vscode-local-config");
    }

    private void AddMissingPath(List<string> findings, string relativePath)
    {
        if (!fileSystem.FileExists(Path.Combine(repositoryRoot, relativePath)))
        {
            findings.Add($"missing:{relativePath}");
        }
    }

    private static void AddMissingText(
        List<string> findings,
        string content,
        string requiredText,
        string finding)
    {
        if (!content.Contains(requiredText, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(finding);
        }
    }

    private static bool ContainsLikelySecret(string content)
        => content.Contains("Bearer eyJ", StringComparison.Ordinal) ||
           content.Contains("sk-", StringComparison.Ordinal) ||
           content.Contains("OPENAI_API_KEY=", StringComparison.Ordinal);
}
