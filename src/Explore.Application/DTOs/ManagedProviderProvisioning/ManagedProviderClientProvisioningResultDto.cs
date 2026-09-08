namespace Explore.Application.DTOs.ManagedProviderProvisioning;

public sealed record ManagedProviderClientProvisioningResultDto
{
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public Guid TenantUserId { get; init; }
    public Guid TenantUserProfileId { get; init; }
    public Guid UserActorId { get; init; }
    public Guid UserExternalLoginId { get; init; }
    public Guid TenantUserRoleGrantId { get; init; }
    public Guid? OrganizerId { get; init; }
    public Guid? OrganizerActorId { get; init; }
    public ManagedProviderOrganizerKindDto? OrganizerKind { get; init; }
    public Guid? OrganizerMembershipId { get; init; }
    public Guid? TenantPlanAssignmentId { get; init; }
}
