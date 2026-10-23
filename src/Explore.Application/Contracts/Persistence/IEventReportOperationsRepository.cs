using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Returns cross-tenant operational counts without exposing report content.</summary>
public interface IEventReportOperationsRepository
{
    Task<int> CountExternalLinksBySyncStateAsync(EventReportSyncState state, CancellationToken cancellationToken);
    Task<int> CountExternalLinksBySyncStateBeforeAsync(EventReportSyncState state, DateTime before, CancellationToken cancellationToken);
}
