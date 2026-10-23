using Explore.Application.Features.InstanceOnboarding.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Exposes accounting aggregates, never cross-tenant storage or counter entities.</summary>
public interface IInstanceStorageAdministration
{
    Task<IReadOnlyList<InstanceStorageProviderTotals>> ReadUsageAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<InstanceStorageProviderTotals>> RecalculateUsageAsync(
        RecalculateInstanceStorageUsageCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<InstanceStorageProviderTotals>> RecalculateUsageAsync(
        RecalculateInstanceStorageUsageDuringSetupCommand command, CancellationToken cancellationToken);
}

public sealed record InstanceStorageProviderTotals(
    string Provider, long UsedBytes, long ReservedBytes, long QuarantinedBytes,
    long ObjectCount, DateTime? LastRecalculatedAt);

/// <summary>Exact persisted accounting scope; contains no object identity or private metadata.</summary>
public sealed record InstanceStorageAccountingScope(Guid TenantId, string Provider);
