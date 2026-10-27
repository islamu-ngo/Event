namespace Explore.Application.Contracts.Persistence;

/// <summary>Coordinates of the complete graph committed by the admitted worker owner.</summary>
public sealed record ManagedTenantWorkerCreationOutcome(
    Guid TenantId,
    Guid UserId,
    Guid UserActorId,
    Guid UserExternalLoginId,
    Guid TenantUserId,
    Guid TenantUserProfileId,
    Guid TenantUserRoleGrantId,
    Guid TenantPlanAssignmentId);
