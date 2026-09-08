using Explore.Application.Features.AiAssistant.Tools;
using Explore.Diagnostic.Doctor.Infrastructure;

namespace Explore.Diagnostic.Doctor.Checks;

public sealed class AiToolReadinessDoctorCheck : IDoctorCheck
{
    private readonly IDoctorFileSystem _fileSystem;
    private readonly string _repositoryRoot;
    private readonly IAiToolContractRegistry _registry;

    public AiToolReadinessDoctorCheck(IDoctorFileSystem fileSystem, string repositoryRoot)
        : this(fileSystem, repositoryRoot, AiToolContractRegistry.CreateDefault())
    {
    }

    public AiToolReadinessDoctorCheck(
        IDoctorFileSystem fileSystem,
        string repositoryRoot,
        IAiToolContractRegistry registry)
    {
        _fileSystem = fileSystem;
        _repositoryRoot = repositoryRoot;
        _registry = registry;
    }

    public string Code => "ai-tools.readiness";
    public DoctorCheckCategory Category => DoctorCheckCategory.Documentation;

    public Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var findings = new List<string>();
        foreach (var definition in _registry.Definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.JsonSchema))
            {
                findings.Add($"{definition.Name}:missing-schema");
            }

            if (definition.PayloadMapperType is null)
            {
                findings.Add($"{definition.Name}:missing-mapper");
            }

            if (string.IsNullOrWhiteSpace(definition.EffectiveAgentMetadata.RequiredHalLinkRel))
            {
                findings.Add($"{definition.Name}:missing-hal-rel");
            }
        }

        AddMissingPath(findings, "docs/internal/AI_AGENT_CONTRACT_INVENTORY.md");
        AddMissingPath(findings, "docs/internal/AI_RAG_FOUNDATION.md");
        AddMissingPath(findings, "docs/internal/AI_AGENT_EXPERIENCE_HARDENING.md");
        AddMissingPath(findings, "Event.Application.UnitTests/Features/AiAssistant/Context/AiSafeDataContextSummaryPolicyTests.cs");
        AddMissingPath(findings, "Event.Application.UnitTests/Features/AiAssistant/Plans/AiProposedPlanValidatorTests.cs");
        AddMissingPath(findings, "Event.Application.UnitTests/Features/AiAssistant/Tools/CreateEventDraftAiToolDefinitionTests.cs");
        AddMissingPath(findings, "Event.Application.UnitTests/Features/AiAssistant/Tools/AiToolPayloadGuardTests.cs");
        AddMissingPath(findings, "Explore.Diagnostic.UnitTests/AiReplay/AiReplayReportGeneratorTests.cs");

        if (findings.Count > 0)
        {
            return Task.FromResult(DoctorCheckResult.Warn(
                Code,
                Category,
                "AI tool readiness has review items.",
                "Review missing registry schema, mapper, HAL rel, tests, generated inventory, docs, and OpenAPI/client regeneration evidence before exposing new tools.",
                "docs/internal/AI_AGENT_CONTRACT_INVENTORY.md",
                string.Join(", ", findings.Order(StringComparer.Ordinal))));
        }

        return Task.FromResult(DoctorCheckResult.Pass(
            Code,
            Category,
            "AI tool readiness artifacts are present for registered tools.",
            "Keep readiness evidence current whenever registry tools, HAL rels, tests, docs, or generated inventories change.",
            "docs/internal/AI_AGENT_CONTRACT_INVENTORY.md"));
    }

    private void AddMissingPath(List<string> findings, string relativePath)
    {
        if (!_fileSystem.FileExists(Path.Combine(_repositoryRoot, relativePath)))
        {
            findings.Add($"missing:{relativePath}");
        }
    }
}
