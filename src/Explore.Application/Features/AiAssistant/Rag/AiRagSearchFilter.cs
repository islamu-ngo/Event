namespace Explore.Application.Features.AiAssistant.Rag;

public sealed record AiRagSearchFilter(
    Guid TenantId,
    IReadOnlySet<AiRagContentScope> AllowedScopes)
{
    public static AiRagSearchFilter ForTenant(Guid tenantId)
        => new(
            tenantId,
            new HashSet<AiRagContentScope>
            {
                AiRagContentScope.TenantPublicEventSummary,
                AiRagContentScope.GlobalPublicEventSummary
            });

    public AiRagIngestionValidationResult Validate()
    {
        if (TenantId == Guid.Empty)
        {
            return AiRagIngestionValidationResult.Failure(
                "rag_tenant_required",
                "AI RAG search filters must be tenant-bound.");
        }

        if (AllowedScopes.Count == 0 || AllowedScopes.Any(scope => !Enum.IsDefined(scope)))
        {
            return AiRagIngestionValidationResult.Failure(
                "rag_scope_not_allowed",
                "AI RAG search filters must use approved public-summary scopes.");
        }

        return AiRagIngestionValidationResult.Success();
    }
}
