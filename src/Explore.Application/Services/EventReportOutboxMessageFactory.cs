using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Models.InternalEvents;
using Explore.Domain;

namespace Explore.Application.Services;

public static class EventReportOutboxMessageFactory
{
    private const string EventReportAggregateType = "EventReport";
    public const string EventReportProviderSyncRequestedEventType = "EventReportProviderSyncRequested";

    public static OutboxMessage CreateProviderSyncRequestedMessage(
        EventReport report,
        EventReportCase reportCase,
        string? correlationId)
    {
        // PostgreSQL retains microseconds. Canonicalize before both durable records
        // and their JSON are created so a round-trip does not change source identity.
        report.CreatedAt = new DateTime(
            report.CreatedAt.Ticks - report.CreatedAt.Ticks % TimeSpan.TicksPerMicrosecond,
            report.CreatedAt.Kind);
        var payload = new EventReportProviderSyncRequested
        {
            TenantId = report.TenantId,
            ReportId = report.Id,
            EventId = report.EventId,
            CaseId = reportCase.Id,
            CaseConcurrencyStamp = reportCase.ConcurrencyStamp,
            ReasonCode = report.ReasonCode,
            QueueCode = reportCase.QueueCode,
            SubmittedAtUtc = report.CreatedAt,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim()
        };

        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = EventReportAggregateType,
            AggregateId = report.Id,
            EventType = EventReportProviderSyncRequestedEventType,
            Payload = JsonSerializer.Serialize(payload),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = report.CreatedAt,
            MaxRetries = 5
        };
        report.RetainProviderSubmission(
            message.Id,
            reportCase,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message.Payload))));
        return message;
    }
}
