using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using Explore.Application.Responses;

namespace Explore.Application.Contracts.Identity;

/// <summary>
/// Opaque original invocation, not a role or credential grant. Only its scoped
/// preparation issuer may admit it, and only the Local physical owner settles it.
/// </summary>
public interface ILocalAdministrationInvocation
{
    Guid PrincipalId { get; }
}

public enum LocalAdministrationStage
{
    CreatePending,
    ReadProvisioning,
    Bind,
    Activate,
    Reset,
    Disclose
}

public interface ILocalAdministrationPreparation
{
    Task<ILocalAdministrationInvocation> PrepareAsync(object request, Guid administrator,
        CancellationToken cancellationToken);
    Task AcquireAsync(ILocalAdministrationInvocation invocation, object originalRequest,
        LocalAdministrationStage stage, CancellationToken cancellationToken);
    void Settle(ILocalAdministrationInvocation invocation, LocalAdministrationStage stage, bool committed);
}

/// <summary>
/// The real Local owner validates its private full reset postimage before returning
/// this non-secret projection. Callers cannot install an expected stamp or postimage.
/// </summary>
public interface ILocalAdministrationOwnerSource
{
    void RequireOwner(ILocalAdministrationInvocation invocation, LocalAdministrationStage stage);
    Task<LocalCredentialResetReceipt?> ValidateAsync(ILocalAdministrationInvocation invocation,
        CancellationToken cancellationToken);
}

/// <summary>
/// Human-only entry into existing Local lifecycle effects. Every call owns a separate
/// non-replaying physical transaction; no Application transaction is borrowed.
/// Bootstrap, worker and restricted replacement authority use their separate contracts.
/// </summary>
public interface ILocalHumanCredentialAdministration
{
    Task<LocalCredentialCreateResult> CreatePendingAsync(ILocalAdministrationInvocation invocation,
        CreateLocalIdentityCommand request, CancellationToken cancellationToken);
    Task<LocalCredentialResetResult> ResetAsync(ILocalAdministrationInvocation invocation,
        ResetLocalCredentialCommand request, CancellationToken cancellationToken);
    Task<LocalCredentialProvisioningSnapshot?> ReadProvisioningAsync(ILocalAdministrationInvocation invocation,
        object originalRequest, CancellationToken cancellationToken);
    Task<BaseCommandResponse<Guid>> BindAsync(ILocalAdministrationInvocation invocation, object originalRequest,
        LocalCredentialProvisioningSnapshot snapshot, CancellationToken cancellationToken);
    Task<LocalCredentialActivationOutcome> ActivateAsync(ILocalAdministrationInvocation invocation,
        object originalRequest, LocalCredentialProvisioningSnapshot snapshot, CancellationToken cancellationToken);
    Task<LocalCredentialOperationStatus?> ReadOperationAsync(ILocalAdministrationInvocation invocation,
        object originalRequest, CancellationToken cancellationToken);
    Task<LocalIdentityPage> ListAsync(ILocalAdministrationInvocation invocation,
        ListLocalIdentitiesQuery request, CancellationToken cancellationToken);
}
