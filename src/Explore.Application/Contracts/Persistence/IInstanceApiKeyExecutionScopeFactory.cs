using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Acquires separate native owners without promoting the request connection.</summary>
public interface IInstanceApiKeyExecutionScopeFactory
{
    ValueTask<IInstanceApiKeyManagementScope> CreateReadAsync(
        Guid principalId, Guid? keyId, CancellationToken cancellationToken);

    ValueTask<IInstanceApiKeyManagementScope> CreateWriteAsync(
        Guid principalId, Guid keyId, CancellationToken cancellationToken);

    ValueTask<IInstanceApiKeyIssuanceScope> CreateIssuanceAsync(
        Guid principalId, string operationKey, CancellationToken cancellationToken);

    ValueTask<IPlatformApiKeyUsageScope> CreateReportAsync(
        Guid principalId, CancellationToken cancellationToken);
}

/// <summary>
/// Owns one original action and transaction. Execute acquires complete authority before
/// invoking the body and admits final sources before releasing its result. Lower effects
/// require this scope's private active action; possession of the scope grants nothing.
/// </summary>
public interface IInstanceApiKeyManagementScope : IAsyncDisposable
{
    Task<T> ExecuteAsync<T>(InstanceApiKeyAction action,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    /// <summary>Returns management metadata with credential hash and last-used IP redacted.</summary>
    Task<IReadOnlyList<ExternalApiKey>> ReadAsync(CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);

    /// <summary>Changes only policy fields on the scope's fixed target; read scopes reject it.</summary>
    Task UpdatePolicyAsync(
        string name, string scopes, DateTime? expiresAt, DateTime changedAt,
        CancellationToken cancellationToken);

    Task<bool> RevokeAsync(DateTime changedAt, CancellationToken cancellationToken);
}

public interface IInstanceApiKeyIssuanceScope : IAsyncDisposable
{
    IUnitOfWork UnitOfWork { get; }
    IPrivacyIdentityFenceAuthority PrivacyAuthority { get; }
    IExternalApiKeyIssuanceReceiptRepository Receipts { get; }

    Task<bool> IsAuthorizedForCommitAsync(CancellationToken cancellationToken);
}

public interface IPlatformApiKeyUsageScope : IAsyncDisposable
{
    /// <summary>Owns preparation, acquisition, read and final admission for this exact date range.</summary>
    Task<IReadOnlyList<TenantApiKeyUsageSummary>> ReadAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
