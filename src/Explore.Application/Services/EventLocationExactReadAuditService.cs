using System.Diagnostics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;

namespace Explore.Application.Services;

public sealed class EventLocationExactReadAuditService(
    IEventLocationExactReadAuditRepository repository,
    TimeProvider timeProvider) : IEventLocationExactReadAuditService
{
    public async Task RecordManyAsync(
        IReadOnlyCollection<EventLocationExactReadAuditRequest> requests,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count > IEventLocationExactReadAuditRepository.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requests),
                $"Exact-read audit batches cannot exceed {IEventLocationExactReadAuditRepository.MaximumBatchSize} records.");
        }

        if (requests.Count == 0)
        {
            return;
        }

        DateTime occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        Guid? activityTraceId = GetActivityTraceId();
        Guid fallbackCorrelationId = Guid.CreateVersion7();
        var audits = new EventLocationExactReadAudit[requests.Count];
        var index = 0;

        foreach (EventLocationExactReadAuditRequest request in requests)
        {
            Guid? correlationId = request.CorrelationId;
            Guid? traceId = request.TraceId ?? activityTraceId;
            if (correlationId is null && traceId is null)
            {
                correlationId = fallbackCorrelationId;
            }

            audits[index++] = EventLocationExactReadAudit.Create(
                request.TenantId,
                request.EventLocationId,
                request.RequesterUserId,
                request.Purpose,
                request.WasAuthorized,
                occurredAtUtc,
                correlationId,
                traceId);
        }

        await repository.AppendManyAsync(audits, cancellationToken);
    }

    private static Guid? GetActivityTraceId()
    {
        string? value = Activity.Current?.TraceId.ToString();
        return Guid.TryParseExact(value, "N", out Guid traceId) && traceId != Guid.Empty
            ? traceId
            : null;
    }
}
