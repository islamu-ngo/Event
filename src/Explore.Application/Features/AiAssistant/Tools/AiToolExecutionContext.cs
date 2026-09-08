namespace Explore.Application.Features.AiAssistant.Tools;

public sealed record AiToolExecutionContext(
    Guid TenantId,
    Guid UserId,
    IReadOnlySet<Guid> AllowedOrganizationIds,
    IReadOnlySet<Guid> AllowedGroupIds)
{
    public static AiToolExecutionContext Empty(Guid tenantId, Guid userId)
        => new(tenantId, userId, new HashSet<Guid>(), new HashSet<Guid>());
}
