using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventLocationExactReadAuditRepository
{
    const int MaximumBatchSize = 256;

    Task<EventLocationExactReadAudit> AppendAsync(
        EventLocationExactReadAudit audit,
        CancellationToken cancellationToken);
    Task AppendManyAsync(
        IReadOnlyCollection<EventLocationExactReadAudit> audits,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<EventLocationExactReadAudit>> GetByEventLocationsAsync(
        IReadOnlyCollection<Guid> eventLocationIds,
        CancellationToken cancellationToken);
}
