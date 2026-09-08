using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Services;

public sealed record EventLocationExactReadAuditRequest(
    Guid TenantId,
    Guid EventLocationId,
    Guid RequesterUserId,
    EventLocationExactReadPurposeEnum Purpose,
    bool WasAuthorized,
    Guid? CorrelationId = null,
    Guid? TraceId = null);

public interface IEventLocationExactReadAuditService
{
    Task RecordManyAsync(
        IReadOnlyCollection<EventLocationExactReadAuditRequest> requests,
        CancellationToken cancellationToken);
}
