using System.Collections.Immutable;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Captures targets on the already selected human execution scope before its transaction.</summary>
public interface IHumanEventAssignmentSourceFactory
{
    IHumanEventAssignmentSourceFence Create(Guid tenantId, IReadOnlyCollection<Guid> eventIds);
}

/// <summary>
/// Database facts for one originally selected human, tenant and event set, not an authorization
/// decision. Observe before any transaction/provider evaluation. Acquire borrows the original
/// Serializable owner after its actual policy source-set lease and, when used, principal lease.
/// </summary>
public interface IHumanEventAssignmentSourceFence
{
    Task<HumanEventAssignmentSourceFacts> ObserveAsync(CancellationToken cancellationToken = default);
    Task<IHumanEventAssignmentSourceLease> AcquireAsync(
        IPolicySourceSetLease sourceSet, CancellationToken cancellationToken = default);
}

public interface IHumanEventAssignmentSourceLease
{
    HumanEventAssignmentSourceFacts Facts { get; }

    /// <summary>
    /// Reenters policy and assignment capabilities and returns fresh persisted facts, including
    /// own writes. Does not mutate Facts, replay provider decisions, or validate credential/PDP
    /// lifetime. The parent compares original semantic inputs and checks authentic lifetimes
    /// after its last blocking operation, before commit.
    /// </summary>
    Task<HumanEventAssignmentSourceFacts> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Complete positive and negative candidates. Empty collections are observations, not errors.
/// Rows and event targets retain enumeration order; inactive/deleted candidates remain present.
/// No credential, provider decision, or arbitrary PDP revision is represented here.
/// </summary>
public sealed record HumanEventAssignmentSourceFacts(
    Guid PrincipalId,
    Guid TenantId,
    ImmutableArray<Guid> EventIds,
    ImmutableArray<HumanAssignmentEventFact> Events,
    ImmutableArray<HumanEventAssignmentFact> Assignments,
    ImmutableArray<HumanAssignmentRoleFact> Roles,
    ImmutableArray<HumanAssignmentPermissionFact> Permissions,
    ImmutableArray<HumanAssignmentActorFact> Actors,
    ImmutableArray<HumanAssignmentTenantMemberFact> TenantMembers,
    ImmutableArray<HumanAssignmentOwnerMemberFact> Organizations,
    ImmutableArray<HumanAssignmentOwnerMemberFact> Groups,
    ImmutableArray<HumanAssignmentProvenanceFact> ProvenanceTypes)
{
    public const int MaximumEvents = 128;
    public override string ToString() => nameof(HumanEventAssignmentSourceFacts);
}

public sealed record HumanAssignmentEventFact(
    Guid Id, Guid TenantId, Guid ActorId, Guid? OrganizerActorId, int EventProvenanceTypeId,
    Guid? SubmittedByUserId, bool IsDeleted, Guid ConcurrencyStamp);

public sealed record HumanEventAssignmentFact(
    Guid Id, Guid TenantId, Guid EventId, Guid UserId, int RoleId, int Status,
    DateTime StartsAtUtc, DateTime? ExpiresAtUtc, DateTime? RevokedAtUtc, long Version);

public sealed record HumanAssignmentRoleFact(int Id, string MasterCode);
public sealed record HumanAssignmentPermissionFact(int RoleId, int PermissionId, string MasterCode, bool IsActive);
public sealed record HumanAssignmentActorFact(
    Guid Id, Guid? UserId, Guid? OrganizationId, Guid? GroupId, bool IsDeleted, bool IsSuspended);
public sealed record HumanAssignmentTenantMemberFact(Guid Id, Guid? ActorId, int StatusId, bool IsDeleted);
public sealed record HumanAssignmentOwnerMemberFact(
    Guid Id, Guid PlacementId, Guid OwnerId, int RoleId, bool IsDeleted, bool PlacementIsDeleted,
    bool IsSuspended, bool IsOrganizerEligible, int ApprovalStatusId);
public sealed record HumanAssignmentProvenanceFact(int Id, string MasterCode);
