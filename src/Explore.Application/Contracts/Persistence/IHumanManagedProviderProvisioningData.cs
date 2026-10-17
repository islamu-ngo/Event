using Explore.Application.Authentication;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Observes one human provisioning request on its already selected idle Global owner.
/// This port neither chooses credentials nor opens an owning transaction.
/// </summary>
public interface IHumanManagedProviderProvisioningDataSource
{
    Task<IHumanManagedProviderProvisioningDataObservation> ObserveAsync(
        EnsureManagedProviderClientProvisionedCommand originalRequest,
        CancellationToken cancellationToken = default);
}

public interface IHumanManagedProviderProvisioningDataObservation
{
    Task<IHumanManagedProviderProvisioningData> AcquireAsync(
        EnsureManagedProviderClientProvisionedCommand originalRequest,
        IPolicySourceSetLease originalSourceSet,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One acquired, non-replaying physical transaction. Application orchestration must
/// fence AdministratorUserId against erasure before ApplyAsync for external accounts.
/// ValidateAsync belongs in the original owner's awaited terminal admission, before
/// its synchronous credential/support deadlines. Returned rows are not commit receipts.
/// </summary>
public interface IHumanManagedProviderProvisioningData
{
    /// <summary>A refusal requires whole-owner rollback; ApplyAsync must not be called.</summary>
    HumanManagedProviderProvisioningRefusal? Refusal { get; }
    bool IsReplay { get; }
    ProviderAccountKey AdministratorAccount { get; }
    Guid AdministratorUserId { get; }
    Task<HumanManagedProviderProvisioningCapacity> ReadActivationCapacityAsync(CancellationToken cancellationToken = default);
    Task<HumanManagedProviderProvisioningRows> ApplyAsync(CancellationToken cancellationToken = default);
    Task ValidateAsync(CancellationToken cancellationToken = default);
}

public sealed record HumanManagedProviderProvisioningCapacity(
    DeploymentMode DeploymentMode, int ActiveTenants, int ReservedTenants);

public sealed record HumanManagedProviderProvisioningRefusal(string? FailureCode, string Message, string Error);

/// <summary>Persisted graph identities, never a caller-supplied plan or settlement witness.</summary>
public sealed record HumanManagedProviderProvisioningRows(
    Guid TenantId,
    Guid UserId,
    Guid TenantUserId,
    Guid TenantUserProfileId,
    Guid UserActorId,
    Guid UserExternalLoginId,
    Guid TenantUserRoleGrantId,
    Guid? OrganizerId,
    Guid? OrganizerActorId,
    ManagedProviderOrganizerKind? OrganizerKind,
    Guid? OrganizerMembershipId,
    bool AlreadyProvisioned);
