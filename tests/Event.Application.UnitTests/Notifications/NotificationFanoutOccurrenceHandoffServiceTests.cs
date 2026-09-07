// ABOUTME: Verifies durable fanout pointer validation and idempotent run handoff behavior.
// ABOUTME: Proves handoff never claims work and superseded occurrences complete as no-ops.

using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Notifications;

public sealed class NotificationFanoutOccurrenceHandoffServiceTests
{
    [Test]
    public async Task HandoffAsync_ReplayedPendingPointer_EnsuresOneRunWithoutClaiming()
    {
        var occurrence = CreateOccurrence();
        var occurrenceRepository = Substitute.For<INotificationFanoutOccurrenceRepository>();
        var runRepository = Substitute.For<INotificationFanoutRunRepository>();
        occurrenceRepository.GetByPointerAsync(
                Arg.Any<NotificationFanoutOccurrenceRequested>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(occurrence);
        var candidateRunIds = new List<Guid>();
        Guid durableRunId = Guid.CreateVersion7();
        runRepository.EnsurePendingOccurrenceRunAsync(
                occurrence.TenantId,
                occurrence.Id,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                candidateRunIds.Add(call.ArgAt<Guid>(2));
                return CreateRun(occurrence, durableRunId);
            });
        var service = new NotificationFanoutOccurrenceHandoffService(
            occurrenceRepository,
            runRepository);
        OutboxMessage message = NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence);

        await service.HandoffAsync(message);
        await service.HandoffAsync(message);

        await Assert.That(candidateRunIds).Count().IsEqualTo(2);
        await Assert.That(candidateRunIds.Distinct()).Count().IsEqualTo(2);
        await Assert.That(candidateRunIds).DoesNotContain(occurrence.Id);
        await runRepository.DidNotReceiveWithAnyArgs().TryClaimOccurrenceAsync(
            default,
            default,
            default!,
            default,
            default,
            default,
            default,
            default,
            default,
            default);
    }

    [Test]
    public async Task HandoffAsync_SupersededOccurrence_CompletesWithoutRun()
    {
        var occurrence = CreateOccurrence();
        occurrence.Supersede(Guid.CreateVersion7(), "newer_update", DateTime.UtcNow);
        var occurrenceRepository = Substitute.For<INotificationFanoutOccurrenceRepository>();
        var runRepository = Substitute.For<INotificationFanoutRunRepository>();
        occurrenceRepository.GetByPointerAsync(
                Arg.Any<NotificationFanoutOccurrenceRequested>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(occurrence);
        var service = new NotificationFanoutOccurrenceHandoffService(
            occurrenceRepository,
            runRepository);

        await service.HandoffAsync(NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence));

        await runRepository.DidNotReceiveWithAnyArgs().EnsurePendingOccurrenceRunAsync(
            default,
            default,
            default,
            default);
    }

    [Test]
    public async Task HandoffAsync_UnresolvedPointer_Throws()
    {
        var occurrence = CreateOccurrence();
        var service = new NotificationFanoutOccurrenceHandoffService(
            Substitute.For<INotificationFanoutOccurrenceRepository>(),
            Substitute.For<INotificationFanoutRunRepository>());

        await Assert.That(async () =>
                await service.HandoffAsync(NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task HandoffAsync_RepositoryReturnsWrongTenant_Throws()
    {
        var occurrence = CreateOccurrence();
        var wrongTenantOccurrence = CreateOccurrence(occurrence.Id, Guid.CreateVersion7());
        var occurrenceRepository = Substitute.For<INotificationFanoutOccurrenceRepository>();
        occurrenceRepository.GetByPointerAsync(
                Arg.Any<NotificationFanoutOccurrenceRequested>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(wrongTenantOccurrence);
        var service = new NotificationFanoutOccurrenceHandoffService(
            occurrenceRepository,
            Substitute.For<INotificationFanoutRunRepository>());

        await Assert.That(async () =>
                await service.HandoffAsync(NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task HandoffAsync_CorruptPointer_Throws()
    {
        var service = new NotificationFanoutOccurrenceHandoffService(
            Substitute.For<INotificationFanoutOccurrenceRepository>(),
            Substitute.For<INotificationFanoutRunRepository>());
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = nameof(NotificationFanoutOccurrence),
            AggregateId = Guid.CreateVersion7(),
            EventType = NotificationFanoutOccurrenceOutboxMessageFactory.EventType,
            Payload = "{}"
        };

        await Assert.That(async () => await service.HandoffAsync(message)).Throws<JsonException>();
    }

    [Test]
    public async Task HandoffAsync_MismatchedEnvelope_Throws()
    {
        var occurrence = CreateOccurrence();
        var occurrenceRepository = Substitute.For<INotificationFanoutOccurrenceRepository>();
        occurrenceRepository.GetByPointerAsync(
                Arg.Any<NotificationFanoutOccurrenceRequested>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(occurrence);
        var service = new NotificationFanoutOccurrenceHandoffService(
            occurrenceRepository,
            Substitute.For<INotificationFanoutRunRepository>());
        OutboxMessage message = NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence);
        message.AggregateId = Guid.CreateVersion7();
        OutboxMessage wrongEventType = NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence);
        wrongEventType.EventType = "WrongFanoutEvent";

        await Assert.That(async () => await service.HandoffAsync(message)).Throws<InvalidOperationException>();
        await Assert.That(async () => await service.HandoffAsync(wrongEventType)).Throws<InvalidOperationException>();
    }

    private static NotificationFanoutOccurrence CreateOccurrence(
        Guid? occurrenceId = null,
        Guid? tenantId = null)
    {
        DateTime occurredAt = DateTime.UtcNow;
        Guid eventId = Guid.CreateVersion7();
        return NotificationFanoutOccurrence.Create(
            occurrenceId ?? Guid.CreateVersion7(),
            tenantId ?? Guid.CreateVersion7(),
            eventId,
            sessionId: null,
            occurredAt,
            audienceCutoffAt: occurredAt,
            Guid.CreateVersion7(),
            "{\"fields\":[\"startTime\"]}",
            "{\"startTime\":\"2026-08-01T08:00:00Z\"}",
            "{\"startTime\":\"2026-08-01T09:00:00Z\"}",
            "event.updated",
            templateVersion: 1,
            (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            policyVersion: 1,
            priority: 30,
            notBefore: occurredAt,
            sourceType: "event",
            sourceId: eventId,
            coalescingKey: $"event:{eventId:N}:schedule",
            coalescingWindowEndsAt: occurredAt,
            emailDeliveryPolicyRevision: 0);
    }

    private static NotificationFanoutRun CreateRun(
        NotificationFanoutOccurrence occurrence,
        Guid runId) =>
        new()
        {
            Id = runId,
            TenantId = occurrence.TenantId,
            Tenant = null!,
            FanoutOccurrenceId = occurrence.Id,
            FanoutKind = "recipient_occurrence",
            NotificationEntityTypeId = (int)NotificationEntityTypeEnum.Event,
            NotificationEntityType = null!,
            EntityId = occurrence.EventId,
            SourceActorId = Guid.CreateVersion7(),
            SourceActor = null!,
            Status = "pending",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
}
