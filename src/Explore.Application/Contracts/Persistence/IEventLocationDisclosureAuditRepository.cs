using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IEventLocationDisclosureAuditRepository
{
    Task<EventLocationDisclosureAudit> AppendAsync(
        EventLocationDisclosureAudit audit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<EventLocationDisclosureAudit>> GetByEventLocationAsync(
        Guid eventLocationId,
        CancellationToken cancellationToken);
}
