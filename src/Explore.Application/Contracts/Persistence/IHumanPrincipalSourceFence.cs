using System.Collections.Immutable;
using Explore.Application.Contracts.Identity;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Reads facts for the original resolved human, never a policy decision. Preparation is outside
/// a transaction. Acquisition borrows the selected Serializable owner after its source-set fence.
/// Provider projection, semantic comparison, resource facts and final admission belong to the caller.
/// </summary>
public interface IHumanPrincipalSourceFence
{
    Task<HumanPrincipalSourceFacts> ObserveAsync(CancellationToken cancellationToken = default);
    Task<IHumanPrincipalSourceLease> AcquireAsync(
        IPolicySourceSetLease sourceSet, CancellationToken cancellationToken = default);
}

public interface IHumanPrincipalSourceLease
{
    HumanPrincipalSourceFacts Facts { get; }
    /// <summary>
    /// Validates the original live owner and returns fresh facts, including its own persisted
    /// writes. The caller must rebuild and compare semantic policy inputs before committing.
    /// </summary>
    Task<HumanPrincipalSourceFacts> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable database observations. No role-success flag is an authorization grant. The four
/// permission lists retain query enumeration order and multiplicity. The remaining rows include
/// inactive/deleted candidates and must be projected using the selected provider's actual rules.
/// </summary>
public sealed record HumanPrincipalSourceFacts(
    Guid PrincipalId,
    Guid? SelectedTenantId,
    ImmutableArray<HumanPrincipalAccountFact> Users,
    ImmutableArray<HumanPrincipalBindingFact> Bindings,
    ImmutableArray<HumanPrincipalPlatformGrantFact> PlatformGrants,
    ImmutableArray<HumanPrincipalRoleFact> Roles,
    ImmutableArray<HumanPrincipalTenantMembershipFact> TenantMemberships,
    ImmutableArray<HumanPrincipalTenantGrantFact> TenantGrants,
    ImmutableArray<HumanPrincipalTenantFact> Tenants,
    ImmutableArray<HumanPrincipalOwnerMembershipFact> Organizations,
    ImmutableArray<HumanPrincipalOwnerMembershipFact> Groups,
    ImmutableArray<HumanPrincipalPermissionFact> Permissions,
    bool PermissionMappingsExist,
    ImmutableArray<Guid> EventCreateOrganizations,
    ImmutableArray<Guid> EventCreateGroups,
    ImmutableArray<Guid> EventFinanceOrganizations,
    ImmutableArray<Guid> EventFinanceGroups,
    HumanPrincipalLocalSessionFacts? LocalSession)
{
    public override string ToString() => nameof(HumanPrincipalSourceFacts);
}

public sealed record HumanPrincipalAccountFact(Guid Id, bool IsDeleted);
public sealed record HumanPrincipalBindingFact(Guid Id, Guid UserId, int AuthenticationProviderId, string ProviderKey)
{
    public override string ToString() => nameof(HumanPrincipalBindingFact);
}
public sealed record HumanPrincipalPlatformGrantFact(Guid Id, int RoleId);
public sealed record HumanPrincipalRoleFact(int Id, int RoleScopeId, string MasterCode, bool MatchesPlatformAdministratorCode);
public sealed record HumanPrincipalTenantMembershipFact(Guid Id, Guid TenantId, int StatusId, bool IsDeleted);
public sealed record HumanPrincipalTenantGrantFact(
    Guid Id, Guid TenantId, Guid TenantUserId, int RoleId, int RoleScopeId, DateTime? RevokedAt);
public sealed record HumanPrincipalTenantFact(Guid Id, int TenantStatusId, bool IsActiveState);
public sealed record HumanPrincipalOwnerMembershipFact(
    Guid Id, Guid TenantId, Guid PlacementId, Guid OwnerId, int RoleId,
    bool IsDeleted, bool PlacementIsDeleted, bool OwnerIsDeleted, int ApprovalStatusId);
public sealed record HumanPrincipalPermissionFact(
    int RoleId, int PermissionId, string MasterCode, bool IsActive,
    bool MatchesEventCreateCode, bool MatchesEventFinanceCode);

/// <summary>
/// Current co-located Local revocation inputs. Stamp comparison and password presence are facts,
/// not credentials. The original validated Local authority and authentic lifetime remain separate.
/// </summary>
public sealed record HumanPrincipalLocalSessionFacts(
    ImmutableArray<HumanPrincipalLocalAccountFact> Accounts,
    LocalCredentialStateMetadata? CredentialState,
    ImmutableArray<HumanPrincipalLocalOperationFact> Operations,
    ImmutableArray<HumanPrincipalPersonalActorFact> PersonalActors,
    bool? EmailDeliveryEnabled);

public sealed record HumanPrincipalLocalAccountFact(Guid Id, bool HasPassword, bool StampMatches, bool EmailConfirmed);
public sealed record HumanPrincipalLocalOperationFact(
    Guid Id, Guid LocalSubjectId, int Kind, int Stage, Guid PersonalActorId, Guid ExternalLoginId);
public sealed record HumanPrincipalPersonalActorFact(Guid Id, Guid? UserId, int ActorTypeId, bool IsDeleted, bool IsSuspended);
