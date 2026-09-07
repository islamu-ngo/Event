using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services;

public sealed class NotificationFanoutOccurrenceHandoffService(
    INotificationFanoutOccurrenceRepository occurrenceRepository,
    INotificationFanoutRunRepository runRepository)
{
    public async Task HandoffAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.Payload))
        {
            throw new JsonException("Fanout occurrence pointer payload is required.");
        }

        var pointer = NotificationFanoutOccurrenceOutboxMessageFactory.DeserializePointer(message.Payload);
        if (message.EventType != NotificationFanoutOccurrenceOutboxMessageFactory.EventType
            || message.AggregateType != nameof(NotificationFanoutOccurrence)
            || message.AggregateId != pointer.OccurrenceId)
        {
            throw new InvalidOperationException("Fanout occurrence outbox envelope is invalid.");
        }

        var occurrence = await occurrenceRepository.GetByPointerAsync(
            pointer,
            trackChanges: false,
            cancellationToken);
        if (occurrence is null
            || occurrence.Id != pointer.OccurrenceId
            || occurrence.TenantId != pointer.TenantId)
        {
            throw new InvalidOperationException("Fanout occurrence pointer could not be resolved.");
        }

        if (occurrence.State == NotificationFanoutOccurrenceState.Superseded)
        {
            return;
        }

        Guid runId = Guid.CreateVersion7();
        var run = await runRepository.EnsurePendingOccurrenceRunAsync(
            pointer.TenantId,
            pointer.OccurrenceId,
            runId,
            cancellationToken);
        if (run is not null)
        {
            return;
        }

        occurrence = await occurrenceRepository.GetByPointerAsync(
            pointer,
            trackChanges: false,
            cancellationToken);
        if (occurrence is not null
            && occurrence.Id == pointer.OccurrenceId
            && occurrence.TenantId == pointer.TenantId
            && occurrence.State == NotificationFanoutOccurrenceState.Superseded)
        {
            return;
        }

        throw new InvalidOperationException("Fanout occurrence could not be handed off.");
    }
}
