using System.Text;
using Explore.Application.Features.AiAssistant.Tools;

namespace Explore.Diagnostic.AgentInventory;

public sealed class AiAgentContractInventoryGenerator
{
    private const string ManualStart = "<!-- BEGIN MANUAL NOTES -->";
    private const string ManualEnd = "<!-- END MANUAL NOTES -->";
    private readonly IAiToolContractRegistry _registry;

    public AiAgentContractInventoryGenerator()
        : this(AiToolContractRegistry.CreateDefault())
    {
    }

    public AiAgentContractInventoryGenerator(IAiToolContractRegistry registry)
    {
        _registry = registry;
    }

    public string GenerateMarkdown(string? existingMarkdown = null)
    {
        var manualNotes = ExtractManualNotes(existingMarkdown);
        var builder = new StringBuilder();
        builder.AppendLine("ABOUTME: Generated AI agent contract inventory for registry-governed tools.");
        builder.AppendLine("ABOUTME: Lists tool metadata, approval posture, HAL requirements, and safe invariants without content-bearing AI data.");
        builder.AppendLine();
        builder.AppendLine("# AI Agent Contract Inventory");
        builder.AppendLine();
        builder.AppendLine("> Generated from `AiToolContractRegistry`. Do not add prompts, provider responses, raw tool payloads, tenant/user identifiers, endpoint URLs, API keys, model secrets, or private event content.");
        builder.AppendLine();
        builder.AppendLine("## Manual Notes");
        builder.AppendLine();
        builder.AppendLine(ManualStart);
        builder.AppendLine(manualNotes);
        builder.AppendLine(ManualEnd);
        builder.AppendLine();
        builder.AppendLine("## Global Invariants");
        builder.AppendLine();
        builder.AppendLine("- Registry catalog visibility is advisory and never grants execution authority.");
        builder.AppendLine("- Mutating tools remain proposal-first and require human confirmation before CQRS/MediatR commands execute.");
        builder.AppendLine("- UI mutation affordances must be gated by HAL link presence, not local role or claim inspection.");
        builder.AppendLine("- MCP adapters must use the same registry contracts and must not write repositories directly.");
        builder.AppendLine();
        builder.AppendLine("## Tool Inventory");
        builder.AppendLine();
        builder.AppendLine("| Tool | Kind | Risk | Approval | Confirmation | HAL rel | Routes | Workflows | Contexts | Authorization | Mapper/Executor | Provider | MCP |");
        builder.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var definition in _registry.Definitions.OrderBy(definition => definition.Name, StringComparer.Ordinal))
        {
            var metadata = definition.EffectiveAgentMetadata;
            builder.Append("| ")
                .Append(Escape(definition.DisplayName)).Append(" | ")
                .Append(definition.Kind).Append(" | ")
                .Append(metadata.RiskClass).Append(" | ")
                .Append(metadata.ApprovalMode).Append(" | ")
                .Append(definition.ConfirmationMode).Append(" | ")
                .Append(Escape(metadata.RequiredHalLinkRel ?? "n/a")).Append(" | ")
                .Append(Escape(Join(metadata.Scopes.RouteScopes))).Append(" | ")
                .Append(Escape(Join(metadata.Scopes.WorkflowScopes))).Append(" | ")
                .Append(Escape(Join(metadata.Scopes.ContextScopes))).Append(" | ")
                .Append(Escape(FormatAuthorization(definition.RequiredAuthorization))).Append(" | ")
                .Append(Escape(FormatMapperExecutor(definition))).Append(" | ")
                .Append(definition.ExposeToProvider ? "yes" : "no").Append(" | ")
                .Append(definition.ExposeToMcp ? "yes" : "no").AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Tool Instructions");
        builder.AppendLine();
        foreach (var definition in _registry.Definitions.OrderBy(definition => definition.Name, StringComparer.Ordinal))
        {
            var metadata = definition.EffectiveAgentMetadata;
            builder.AppendLine($"### {definition.Name}");
            builder.AppendLine();
            builder.AppendLine($"- Availability: {metadata.AvailabilityReason}");
            builder.AppendLine($"- Follow-up policy: {metadata.FollowUpPolicy}");
            builder.AppendLine($"- Safe action instructions: {metadata.SafeActionInstructions}");
            builder.AppendLine($"- Result card: {metadata.ResultPresentation.CardKind}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string ExtractManualNotes(string? existingMarkdown)
    {
        if (string.IsNullOrWhiteSpace(existingMarkdown))
        {
            return "_Add local reviewer notes here. This section is preserved by the generator._";
        }

        var start = existingMarkdown.IndexOf(ManualStart, StringComparison.Ordinal);
        var end = existingMarkdown.IndexOf(ManualEnd, StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            return "_Add local reviewer notes here. This section is preserved by the generator._";
        }

        start += ManualStart.Length;
        return existingMarkdown[start..end].Trim();
    }

    private static string Join(IReadOnlySet<string> values)
        => values.Count == 0 ? "all" : string.Join(", ", values.Order(StringComparer.OrdinalIgnoreCase));

    private static string FormatAuthorization(AiToolAuthorizationRequirement? requirement)
        => requirement is null ? "none" : $"{requirement.ResourceKind}:{requirement.Action}";

    private static string FormatMapperExecutor(AiToolDefinition definition)
        => definition.PayloadMapperType is null ? "registry only" : definition.PayloadMapperType.Name;

    private static string Escape(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace(Environment.NewLine, " ", StringComparison.Ordinal);
}
