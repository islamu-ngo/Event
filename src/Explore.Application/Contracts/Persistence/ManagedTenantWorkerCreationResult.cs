namespace Explore.Application.Contracts.Persistence;

/// <summary>Scalar completion facts from the original managed tenant graph owner; no presentation mapping or authority is carried.</summary>
public sealed record ManagedTenantWorkerCreationResult
{
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public Guid TenantUserId { get; init; }
    public Guid TenantUserProfileId { get; init; }
    public Guid UserActorId { get; init; }
    public Guid UserExternalLoginId { get; init; }
    public Guid TenantUserRoleGrantId { get; init; }
    public Guid TenantPlanAssignmentId { get; init; }
}
