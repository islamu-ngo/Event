using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Original custody metadata only; provider configuration and subscription payload stay tenant-local.</summary>
public sealed record RegistrationProviderSubscriptionClaim(
    Guid StateId,
    Guid TenantId,
    Guid RegistrationProviderBindingId,
    Guid LeaseToken,
    long ProcessingGeneration,
    DateTime LeaseExpiresAt);

public interface IRegistrationProviderSubscriptionClaimQueue
{
    Task<IReadOnlyList<RegistrationProviderSubscriptionClaim>> ClaimAsync(
        RegistrationProviderSubscriptionOperation operation,
        int batchSize,
        DateTime observedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);
}

public interface IRegistrationProviderSubscriptionExecutionScopeFactory
{
    ValueTask<IRegistrationProviderSubscriptionExecutionScope> CreateAsync(
        Guid tenantId, CancellationToken cancellationToken);
}

public interface IRegistrationProviderSubscriptionExecutionScope : IAsyncDisposable
{
    Task<bool> ProcessAsync(
        RegistrationProviderSubscriptionClaim claim,
        RegistrationProviderSubscriptionOperation operation,
        CancellationToken cancellationToken);
}

/// <summary>Abort the owning local transaction; never translate lost custody into a successful checkpoint.</summary>
public sealed class RegistrationProviderSubscriptionClaimLostException()
    : InvalidOperationException("Subscription claim custody is no longer current.");
