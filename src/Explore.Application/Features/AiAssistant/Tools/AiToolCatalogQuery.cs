namespace Explore.Application.Features.AiAssistant.Tools;

public sealed record AiToolCatalogQuery(
    Guid TenantId,
    bool IsAuthenticated,
    AiToolCatalogPrincipalKind PrincipalKind,
    string? RoutePath,
    string? WorkflowScope,
    string? ContextScope,
    IReadOnlySet<string> AvailableHalLinkRels);
