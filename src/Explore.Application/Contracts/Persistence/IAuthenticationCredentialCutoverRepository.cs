using Explore.Domain.Secrets;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Value-free receipts and current-operation heads. Writes only flush into an existing
/// caller-owned transaction; callers own head guarding, commit and cleanup coordination.
/// </summary>
public interface IAuthenticationCredentialCutoverRepository
{
    Task<AuthenticationCredentialCutover?> GetAsync(Guid instanceId, Guid operationId,
        CancellationToken cancellationToken = default);
    Task<AuthenticationCredentialCutoverHead?> GetHeadAsync(Guid instanceId,
        CancellationToken cancellationToken = default);
    Task AddAsync(AuthenticationCredentialCutover operation,
        CancellationToken cancellationToken = default);
    Task AddHeadAsync(AuthenticationCredentialCutoverHead head,
        CancellationToken cancellationToken = default);
    Task SaveAsync(AuthenticationCredentialCutover operation, Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default);
    Task SaveHeadAsync(AuthenticationCredentialCutoverHead head, Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default);
}
