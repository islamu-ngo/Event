namespace Explore.Application.Features.AiAssistant.Tools;

public sealed class AiToolCatalogService
{
    private readonly IAiToolContractRegistry _registry;

    public AiToolCatalogService()
        : this(AiToolContractRegistry.CreateDefault())
    {
    }

    public AiToolCatalogService(IAiToolContractRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public IReadOnlyList<AiToolCatalogItem> GetCatalog(AiToolCatalogQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.TenantId == Guid.Empty || !query.IsAuthenticated)
        {
            return [];
        }

        var availableHalLinkRels = NormalizeHalRels(query.AvailableHalLinkRels);
        return _registry.Definitions
            .Where(IsDiscoverable)
            .Where(definition => MatchesScope(definition.EffectiveAgentMetadata.Scopes.RouteScopes, query.RoutePath))
            .Where(definition => MatchesScope(definition.EffectiveAgentMetadata.Scopes.WorkflowScopes, query.WorkflowScope))
            .Where(definition => MatchesScope(definition.EffectiveAgentMetadata.Scopes.ContextScopes, query.ContextScope))
            .Select(definition => CreateItem(definition, availableHalLinkRels))
            .ToArray();
    }

    private static bool IsDiscoverable(AiToolDefinition definition)
        => definition.ExposeToProvider || definition.ExposeToMcp;

    private static AiToolCatalogItem CreateItem(AiToolDefinition definition, IReadOnlySet<string> availableHalLinkRels)
    {
        var metadata = definition.EffectiveAgentMetadata;
        var hasHalAffordance = string.IsNullOrWhiteSpace(metadata.RequiredHalLinkRel) ||
                               availableHalLinkRels.Contains(metadata.RequiredHalLinkRel.Trim());
        var availabilityCode = hasHalAffordance
            ? AiToolCatalogAvailabilityCodes.Available
            : AiToolCatalogAvailabilityCodes.MissingHalAffordance;
        var availabilityReason = hasHalAffordance
            ? metadata.AvailabilityReason
            : "Current API/HAL context does not expose the required affordance for this tool.";

        return new AiToolCatalogItem(
            definition.Kind,
            definition.Name,
            definition.DisplayName,
            metadata,
            CanRequestProposal: hasHalAffordance,
            ExecutionAuthorityGranted: false,
            availabilityCode,
            availabilityReason);
    }

    private static bool MatchesScope(IReadOnlySet<string> allowedScopes, string? requestedScope)
    {
        if (allowedScopes.Count == 0 || string.IsNullOrWhiteSpace(requestedScope))
        {
            return true;
        }

        return allowedScopes.Contains(requestedScope.Trim());
    }

    private static IReadOnlySet<string> NormalizeHalRels(IEnumerable<string>? halLinkRels)
    {
        if (halLinkRels is null)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return halLinkRels
            .Where(rel => !string.IsNullOrWhiteSpace(rel))
            .Select(rel => rel.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
