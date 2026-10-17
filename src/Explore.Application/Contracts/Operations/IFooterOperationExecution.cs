using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Contracts.Operations;

/// <summary>
/// Owns the closed Footer human operation from original source preparation through physical
/// completion. The private active admission, not an actor ID or an ambient transaction,
/// authorizes the actual handler and its lower writer.
/// </summary>
public interface IFooterOperationExecution
{
    Task<T> ExecuteAsync<T>(object request, Func<CancellationToken, Task<T>> body,
        CancellationToken cancellationToken = default);
    Task<HumanCommitSubject?> RequireOwnedAsync(object request, CancellationToken cancellationToken = default);
    Task RequireWriteAsync(object entity, FooterWriteKind kind, CancellationToken cancellationToken = default);
    Task RequireGroupDeletionAsync(Guid groupId, CancellationToken cancellationToken = default);
}

public enum FooterWriteKind { Create, Update, Delete }

/// <summary>Only the request authentication adapter can produce an accepted setup read.</summary>
public interface IFooterSetupReadAuthority
{
    Task<IFooterSetupReadAdmission?> TryAcquireAsync(CancellationToken cancellationToken = default);
}

public interface IFooterSetupReadAdmission
{
    Task ValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Rejects borrowed import/email owners before constructing Footer consumers.</summary>
public interface IFooterExecutionScopeFactory
{
    ValueTask<IPolicyMutationExecutionScope> CreateAsync(PolicyMutationScope selection, CancellationToken cancellationToken);
    void RequireCurrentOwner(PolicyMutationScope selection);
}
