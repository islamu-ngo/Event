// ABOUTME: Unit tests for composite outbox dispatch routing to internal side-effect handlers.
// ABOUTME: Verifies retired broker events fail closed while local fanout and provider sync still route.

using System.Text.Json;
using System.Diagnostics.Metrics;
using Explore.Application.Caching;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Handlers.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Features.Management.Handlers.Commands;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Services;
using Explore.Application.Services.Registration;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Services.Moderation;
using Explore.Persistence;
using Explore.Persistence.Services;
using Explore.Tests.Shared.Telemetry;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class CompositeOutboxMessageDispatcherTests
{
    [Test]
    public async Task DispatchAsync_WithFanoutOccurrencePointer_RoutesToDurableHandoff()
    {
        var occurrence = CreateOccurrence();
        var occurrenceRepository = Substitute.For<INotificationFanoutOccurrenceRepository>();
        var runRepository = Substitute.For<INotificationFanoutRunRepository>();
        occurrenceRepository.GetByPointerAsync(
                Arg.Any<NotificationFanoutOccurrenceRequested>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(occurrence);
        runRepository.EnsurePendingOccurrenceRunAsync(
                occurrence.TenantId,
                occurrence.Id,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateRun(occurrence));
        var dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            occurrenceRepository: occurrenceRepository,
            runRepository: runRepository);

        await dispatcher.DispatchAsync(NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence));

        await runRepository.Received(1).EnsurePendingOccurrenceRunAsync(
            occurrence.TenantId,
            occurrence.Id,
            Arg.Is<Guid>(runId => runId != Guid.Empty && runId != occurrence.Id),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_WithInternalFanoutEvent_RoutesToFanoutServiceOnly()
    {
        var fanoutService = Substitute.For<IEventPublishedNotificationFanoutService>();
        var moderationFanoutService = Substitute.For<IEventModerationNotificationFanoutService>();
        var dispatcher = CreateDispatcher(fanoutService, moderationFanoutService);
        var request = new EventPublishedNotificationFanoutRequested
        {
            TenantId = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            EventTitle = "Community Iftar",
            SourceActorId = Guid.NewGuid(),
            StartDate = DateTimeOffset.UtcNow,
            PublishedAt = DateTimeOffset.UtcNow
        };

        await dispatcher.DispatchAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateType = "Event",
            AggregateId = request.EventId,
            EventType = PublishEventCommandHandler.EventPublishedNotificationFanoutRequestedEventType,
            Payload = JsonSerializer.Serialize(request)
        });

        await fanoutService.Received(1).FanoutAsync(
            Arg.Is<EventPublishedNotificationFanoutRequested>(payload =>
                payload.TenantId == request.TenantId
                && payload.EventId == request.EventId
                && payload.SourceActorId == request.SourceActorId),
            Arg.Any<CancellationToken>());
        await moderationFanoutService.DidNotReceiveWithAnyArgs().FanoutLightModerationAsync(default!, default);
        await moderationFanoutService.DidNotReceiveWithAnyArgs().FanoutHeavyRedactionAsync(default!, default);
    }

    [Test]
    public async Task DispatchAsync_WithEventReportProviderSyncEvent_RoutesToReportProviderSyncDispatcherOnly()
    {
        var fanoutService = Substitute.For<IEventPublishedNotificationFanoutService>();
        var moderationFanoutService = Substitute.For<IEventModerationNotificationFanoutService>();
        var reportProviderSyncDispatcher = Substitute.For<IReportProviderSyncDispatcher>();
        var dispatcher = CreateDispatcher(
            fanoutService,
            moderationFanoutService,
            reportProviderSyncDispatcher);
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateType = "EventReport",
            AggregateId = Guid.NewGuid(),
            EventType = EventReportOutboxMessageFactory.EventReportProviderSyncRequestedEventType,
            Payload = "{}"
        };

        await dispatcher.DispatchAsync(message);

        await reportProviderSyncDispatcher.Received(1).DispatchAsync(
            Arg.Is<OutboxMessage>(payload => payload.Id == message.Id),
            Arg.Any<CancellationToken>());
        await fanoutService.DidNotReceiveWithAnyArgs().FanoutAsync(default!, default);
        await moderationFanoutService.DidNotReceiveWithAnyArgs().FanoutLightModerationAsync(default!, default);
        await moderationFanoutService.DidNotReceiveWithAnyArgs().FanoutHeavyRedactionAsync(default!, default);
    }

    [Test]
    public async Task DispatchAsync_WithLightModerationFanoutEvent_RoutesToModerationFanoutServiceOnly()
    {
        var fanoutService = Substitute.For<IEventPublishedNotificationFanoutService>();
        var moderationFanoutService = Substitute.For<IEventModerationNotificationFanoutService>();
        var dispatcher = CreateDispatcher(fanoutService, moderationFanoutService);
        var request = new EventLightModeratedNotificationFanoutRequested
        {
            TenantId = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            ModerationRecordId = Guid.NewGuid(),
            EventTitle = "Community Iftar",
            SourceActorId = Guid.NewGuid(),
            ModeratedAt = DateTimeOffset.UtcNow
        };

        await dispatcher.DispatchAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateType = "Event",
            AggregateId = request.EventId,
            EventType = EventModerationOutboxMessageFactory.EventLightModeratedNotificationFanoutRequestedEventType,
            Payload = JsonSerializer.Serialize(request)
        });

        await moderationFanoutService.Received(1).FanoutLightModerationAsync(
            Arg.Is<EventLightModeratedNotificationFanoutRequested>(payload =>
                payload.TenantId == request.TenantId
                && payload.EventId == request.EventId
                && payload.ModerationRecordId == request.ModerationRecordId),
            Arg.Any<CancellationToken>());
        await fanoutService.DidNotReceiveWithAnyArgs().FanoutAsync(default!, default);
        await moderationFanoutService.DidNotReceiveWithAnyArgs().FanoutHeavyRedactionAsync(default!, default);
    }

    [Test]
    public async Task DispatchAsync_WithHeavyRedactionFanoutEvent_RoutesToModerationFanoutServiceOnly()
    {
        var fanoutService = Substitute.For<IEventPublishedNotificationFanoutService>();
        var moderationFanoutService = Substitute.For<IEventModerationNotificationFanoutService>();
        var dispatcher = CreateDispatcher(fanoutService, moderationFanoutService);
        var request = new EventHeavyRedactedNotificationFanoutRequested
        {
            TenantId = Guid.NewGuid(),
            ModerationRecordId = Guid.NewGuid(),
            Version = EventHeavyRedactedNotificationFanoutRequested.CurrentVersion
        };

        await dispatcher.DispatchAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateType = "Event",
            AggregateId = Guid.NewGuid(),
            EventType = EventModerationOutboxMessageFactory.EventHeavyRedactedNotificationFanoutRequestedEventType,
            Payload = JsonSerializer.Serialize(request)
        });

        await moderationFanoutService.Received(1).FanoutHeavyRedactionAsync(
            Arg.Is<EventHeavyRedactedNotificationFanoutRequested>(payload =>
                payload.TenantId == request.TenantId
                && payload.ModerationRecordId == request.ModerationRecordId
                && payload.Version == request.Version),
            Arg.Any<CancellationToken>());
        await moderationFanoutService.DidNotReceiveWithAnyArgs().FanoutLightModerationAsync(default!, default);
        await fanoutService.DidNotReceiveWithAnyArgs().FanoutAsync(default!, default);
    }

    [Test]
    public async Task DispatchAsync_WithHistoricalHeavyPayload_ScrubsAtRestBeforeCanonicalDispatch()
    {
        var moderationFanoutService = Substitute.For<IEventModerationNotificationFanoutService>();
        var outboxRepository = Substitute.For<IOutboxRepository>();
        outboxRepository.TryReplaceProcessingPayloadAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            moderationFanoutService,
            outboxRepository: outboxRepository);
        Guid tenantId = Guid.CreateVersion7();
        Guid moderationRecordId = Guid.CreateVersion7();
        string historicalPayload = JsonSerializer.Serialize(new
        {
            TenantId = tenantId,
            ModerationRecordId = moderationRecordId,
            SourceActorId = Guid.CreateVersion7(),
            RedactedAt = DateTimeOffset.UtcNow
        });
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = "Event",
            EventType = EventModerationOutboxMessageFactory.EventHeavyRedactedNotificationFanoutRequestedEventType,
            Payload = historicalPayload
        };

        await dispatcher.DispatchAsync(message);

        await outboxRepository.Received(1).TryReplaceProcessingPayloadAsync(
            message.Id,
            historicalPayload,
            Arg.Is<string>(payload => payload.Contains("Version", StringComparison.Ordinal)
                && !payload.Contains("SourceActorId", StringComparison.Ordinal)
                && !payload.Contains("RedactedAt", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await moderationFanoutService.Received(1).FanoutHeavyRedactionAsync(
            Arg.Is<EventHeavyRedactedNotificationFanoutRequested>(payload => payload.TenantId == tenantId
                && payload.ModerationRecordId == moderationRecordId
                && payload.Version == EventHeavyRedactedNotificationFanoutRequested.CurrentVersion),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_WithUnknownHeavyPayloadMember_ScrubsAndRejectsWithoutFanout()
    {
        var moderationFanoutService = Substitute.For<IEventModerationNotificationFanoutService>();
        var outboxRepository = Substitute.For<IOutboxRepository>();
        outboxRepository.TryReplaceProcessingPayloadAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            moderationFanoutService,
            outboxRepository: outboxRepository);
        string unsafePayload = JsonSerializer.Serialize(new
        {
            TenantId = Guid.CreateVersion7(),
            ModerationRecordId = Guid.CreateVersion7(),
            Version = EventHeavyRedactedNotificationFanoutRequested.CurrentVersion,
            EventTitle = "private-title-canary",
            EventSlug = "private-slug-canary",
            EventUrl = "https://private.example/events/canary",
            Description = "private-description-canary",
            ImageUrl = "private-image-canary",
            OrganizerId = "private-organizer-canary",
            Evidence = "private-evidence-canary",
            DecisionNote = "private-decision-note-canary",
            ReasonCode = "private-reason-canary",
            ModeratorUserId = "private-moderator-canary",
            Provider = "private-provider-canary",
            StoragePath = "private-storage-path-canary",
            StorageKey = "private-storage-key-canary",
            RawError = "private-raw-error-canary",
            RecipientEmail = "attendee-pii-canary@example.test"
        });
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = "Event",
            EventType = EventModerationOutboxMessageFactory.EventHeavyRedactedNotificationFanoutRequestedEventType,
            Payload = unsafePayload
        };

        await Assert.That(async () => await dispatcher.DispatchAsync(message)).Throws<JsonException>();

        await outboxRepository.Received(1).TryReplaceProcessingPayloadAsync(
            message.Id,
            unsafePayload,
            EventHeavyRedactedNotificationFanoutPayloadParser.SafeInvalidPayload,
            Arg.Any<CancellationToken>());
        await moderationFanoutService.DidNotReceiveWithAnyArgs().FanoutHeavyRedactionAsync(default!, default);
    }

    [Test]
    public async Task DispatchAsync_WithRetiredEventPublishedEvent_Throws()
    {
        var fanoutService = Substitute.For<IEventPublishedNotificationFanoutService>();
        var moderationFanoutService = Substitute.For<IEventModerationNotificationFanoutService>();
        var dispatcher = CreateDispatcher(fanoutService, moderationFanoutService);

        await Assert.That(async () => await dispatcher.DispatchAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateType = "Event",
            AggregateId = Guid.NewGuid(),
            EventType = "EventPublished",
            Payload = "{}"
        })).Throws<InvalidOperationException>();
        await fanoutService.DidNotReceiveWithAnyArgs().FanoutAsync(default!, default);
        await moderationFanoutService.DidNotReceiveWithAnyArgs().FanoutLightModerationAsync(default!, default);
        await moderationFanoutService.DidNotReceiveWithAnyArgs().FanoutHeavyRedactionAsync(default!, default);
    }

    [Test]
    public async Task DispatchAsync_WithUnknownEventType_Throws()
    {
        var dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>());

        await Assert.That(async () => await dispatcher.DispatchAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateType = "Event",
            AggregateId = Guid.NewGuid(),
            EventType = "UnknownEvent",
            Payload = "{}"
        })).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReconcileDeadLetterAsync_WithManagedTenantPointer_RoutesTerminalCommand()
    {
        var mediator = Substitute.For<IMediator>();
        var dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            mediator: mediator);
        Guid operationId = Guid.CreateVersion7();

        await dispatcher.ReconcileDeadLetterAsync(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = nameof(ManagedTenantProvisioningOperation),
            AggregateId = operationId,
            EventType = ManagedTenantProvisioningOutboxEvents.ProcessRequested
        });

        await mediator.Received(1).Send(
            Arg.Is<ReconcileManagedTenantProvisioningDeadLetterCommand>(command =>
                command.OperationId == operationId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    [Category("EventLocationPrivacy")]
    [Arguments(LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType)]
    [Arguments(LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType)]
    [Arguments(LocationPrivacyCorrectionDispatcher.GovernanceCorrectionEventType)]
    public async Task DispatchAsync_WithLocationPrivacyEvent_RoutesToCorrectionDispatcher(string eventType)
    {
        var cache = new RecordingHybridCache();
        CompositeOutboxMessageDispatcher dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            cache: cache);

        await dispatcher.DispatchAsync(CreateLocationPrivacyMessage(eventType));

        await Assert.That(cache.RemovedTags).Contains(CacheTags.EventLocations);
    }

    [Test]
    [Category("EventLocationPrivacy")]
    public async Task ReconcileDeadLetterAsync_WithLocationPrivacyEvent_ReplaysCorrectionDispatcher()
    {
        var cache = new RecordingHybridCache();
        CompositeOutboxMessageDispatcher dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            cache: cache);

        await dispatcher.ReconcileDeadLetterAsync(CreateLocationPrivacyMessage(
            LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType));

        await Assert.That(cache.RemovedTags).Contains(CacheTags.EventLocations);
    }

    [Test]
    public async Task DispatchAsync_WithPrivacyErasureCacheWork_ClearsUserAndSharedEventCaches()
    {
        var cache = new RecordingHybridCache();
        CompositeOutboxMessageDispatcher dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            cache: cache);
        Guid subjectId = Guid.CreateVersion7();

        await dispatcher.DispatchAsync(PrivacyErasureCacheInvalidationOutboxMessageFactory.Create(
            Guid.CreateVersion7(),
            subjectId,
            DateTime.UtcNow));

        await Assert.That(cache.RemovedKeys).Contains($"user:detail:{subjectId}");
        await Assert.That(cache.RemovedTags).Contains(CacheTags.Events);
        await Assert.That(cache.RemovedTags).Contains(CacheTags.EventLists);
        await Assert.That(cache.RemovedTags).Contains(CacheTags.EventDetails);
        await Assert.That(cache.RemovedTags).Contains(CacheTags.EventLocations);
    }

    [Test]
    public async Task ReconcileDeadLetterAsync_WithPrivacyErasureCacheWork_ReplaysInvalidation()
    {
        var cache = new RecordingHybridCache();
        CompositeOutboxMessageDispatcher dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            cache: cache);
        Guid subjectId = Guid.CreateVersion7();

        await dispatcher.ReconcileDeadLetterAsync(
            PrivacyErasureCacheInvalidationOutboxMessageFactory.Create(
                Guid.CreateVersion7(),
                subjectId,
                DateTime.UtcNow));

        await Assert.That(cache.RemovedKeys).Contains($"user:detail:{subjectId}");
        await Assert.That(cache.RemovedTags).Contains(CacheTags.EventLocations);
    }

    [Test]
    public async Task DispatchAsync_WithPrivacyErasurePayload_RejectsClosedEnvelope()
    {
        CompositeOutboxMessageDispatcher dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>());
        OutboxMessage message = PrivacyErasureCacheInvalidationOutboxMessageFactory.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTime.UtcNow);
        message.Payload = "{}";

        await Assert.That(async () => await dispatcher.DispatchAsync(message))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DispatchAsync_WithConfigurationManifestEffects_RoutesByOperationIdentity()
    {
        var manifestDispatcher =
            Substitute.For<IConfigurationManifestEffectDispatcher>();
        CompositeOutboxMessageDispatcher dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            manifestEffectDispatcher: manifestDispatcher);
        Guid operationId = Guid.CreateVersion7();
        OutboxMessage message = ConfigurationManifestEffectOutbox.Create(
            Guid.CreateVersion7(),
            operationId,
            DateTime.UtcNow);

        await dispatcher.DispatchAsync(message);

        await manifestDispatcher.Received(1).DispatchAsync(
            operationId,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DispatchAsync_WithConfigurationImportEffects_RoutesClaimedMessage()
    {
        var importDelivery = new CapturingImportEffectDelivery();
        CompositeOutboxMessageDispatcher dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            importEffectDelivery: importDelivery);
        OutboxMessage message = ConfigurationImportEffectOutbox.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTime.UtcNow);

        await dispatcher.DispatchAsync(message);

        await Assert.That(importDelivery.Message).IsSameReferenceAs(message);
    }

    [Test]
    [Category("EventLocationPrivacy")]
    public async Task ReconcileDeadLetterAsync_WithUnsupportedEventType_Throws()
    {
        CompositeOutboxMessageDispatcher dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>());

        await Assert.That(async () => await dispatcher.ReconcileDeadLetterAsync(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = "Unknown",
            AggregateId = Guid.CreateVersion7(),
            EventType = "UnknownEvent"
        })).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReconcileDeadLetterAsync_WithRecoveryDelivery_RoutesIdempotentHandler()
    {
        var recoveryHandler = Substitute.For<IAdmissionRecoveryDeliveryOutboxHandler>();
        CompositeOutboxMessageDispatcher dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            admissionRecoveryHandler: recoveryHandler);
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = nameof(AdmissionRecoveryCapability),
            AggregateId = Guid.CreateVersion7(),
            EventType = AdmissionRecoveryDeliveryEvents.RecoveryDeliveryRequested,
            Payload = "{}"
        };

        await dispatcher.ReconcileDeadLetterAsync(message);

        await recoveryHandler.Received(1).HandleAsync(
            message,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CancelledRegistrationOrderOutboxInvokesTenantBoundAdmissionRevocation()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        IAdmissionRevocationService admissionRevocation =
            Substitute.For<IAdmissionRevocationService>();
        admissionRevocation.ReconcileAsync(
                Arg.Any<AdmissionRevocationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdmissionRevocationResult(
                AdmissionRevocationOutcome.Applied, [], []));
        CompositeOutboxMessageDispatcher dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            admissionRevocationService: admissionRevocation);
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = nameof(RegistrationOrder),
            AggregateId = orderId,
            EventType = RegistrationOrderOutboxMessageFactory.CancelledEventType,
            Payload = JsonSerializer.Serialize(new RegistrationOrderLifecycleOutboxPayload(
                orderId,
                eventId,
                tenantId,
                (int)RegistrationOrderStatusEnum.Cancelled,
                0,
                false))
        };

        await dispatcher.DispatchAsync(message);

        await admissionRevocation.Received(1).ReconcileAsync(
            Arg.Is<AdmissionRevocationRequest>(request =>
                request.TenantId == tenantId &&
                request.RegistrationOrderId == orderId &&
                request.Reason == AdmissionRevocationService.OrderCancellationReason &&
                request.RefundAllocations.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EventCancellationOutboxInvokesBoundedAdmissionDrain()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        IAdmissionEventCancellationService eventCancellation =
            Substitute.For<IAdmissionEventCancellationService>();
        CompositeOutboxMessageDispatcher dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            admissionEventCancellationService: eventCancellation);
        OutboxMessage message =
            AdmissionRevocationOutboxMessageFactory.CreateEventCancellation(
                tenantId, eventId, DateTime.UtcNow);

        await dispatcher.DispatchAsync(message);

        await eventCancellation.Received(1).ReconcileAsync(
            message.Id,
            tenantId,
            eventId,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SuccessfulRefundDispatchInvokesAdmissionReconciliationBeforeOutboxCompletion()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid paymentAttemptId = Guid.CreateVersion7();
        PaidOrderAcceptanceSnapshot acceptance = RefundAcceptance(tenantId);
        RefundAttempt attempt = RefundAttempt.Create(
            Guid.CreateVersion7(),
            tenantId,
            paymentAttemptId,
            acceptance,
            "acct_example",
            "pi_example",
            $"refund:{Guid.CreateVersion7():N}",
            500,
            DateTime.UtcNow.AddMinutes(-2));
        IRefundAttemptRepository refunds = Substitute.For<IRefundAttemptRepository>();
        refunds.GetByIdAsync(tenantId, attempt.Id, Arg.Any<CancellationToken>())
            .Returns(attempt);
        IRefundCreator creator = Substitute.For<IRefundCreator>();
        creator.CreateAsync(Arg.Any<RefundCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(RefundProviderResult.Observed(
                new RefundProviderObservation(
                    "re_example",
                    "pi_example",
                    RefundProviderStatus.Succeeded,
                    500,
                    "EUR",
                    0),
                "req_example"));
        IAdmissionRefundRevocationService admissionRefund =
            Substitute.For<IAdmissionRefundRevocationService>();
        admissionRefund.ReconcileSucceededAsync(
                tenantId,
                attempt.Id,
                Arg.Any<CancellationToken>())
            .Returns(new AdmissionRevocationResult(
                AdmissionRevocationOutcome.Applied, [], []));
        CompositeOutboxMessageDispatcher dispatcher = CreateDispatcher(
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            refundRepository: refunds,
            refundCreator: creator,
            admissionRefundRevocationService: admissionRefund);

        await dispatcher.DispatchAsync(
            RefundOutboxMessageFactory.CreateDispatch(attempt, DateTime.UtcNow));

        await admissionRefund.Received(1).ReconcileSucceededAsync(
            tenantId,
            attempt.Id,
            Arg.Any<CancellationToken>());
    }

    private static CompositeOutboxMessageDispatcher CreateDispatcher(
        IEventPublishedNotificationFanoutService fanoutService,
        IEventModerationNotificationFanoutService moderationFanoutService,
        IReportProviderSyncDispatcher? reportProviderSyncDispatcher = null,
        IMediator? mediator = null,
        INotificationFanoutOccurrenceRepository? occurrenceRepository = null,
        INotificationFanoutRunRepository? runRepository = null,
        IOutboxRepository? outboxRepository = null,
        HybridCache? cache = null,
        IAdmissionCredentialDeliveryOutboxHandler? admissionDeliveryHandler = null,
        IAdmissionRecoveryDeliveryOutboxHandler? admissionRecoveryHandler = null,
        IAdmissionRevocationService? admissionRevocationService = null,
        IAdmissionRefundRevocationService? admissionRefundRevocationService = null,
        IAdmissionEventCancellationService? admissionEventCancellationService = null,
        IRefundAttemptRepository? refundRepository = null,
        IRefundCreator? refundCreator = null,
        IConfigurationManifestEffectDispatcher? manifestEffectDispatcher = null,
        IConfigurationImportEffectDelivery? importEffectDelivery = null)
    {
        HybridCache selectedCache = cache ?? new RecordingHybridCache();
        var correctionPlanner = Substitute.For<IAtprotoLocationPrivacyCorrectionPlanner>();
        refundRepository ??= Substitute.For<IRefundAttemptRepository>();
        var refundCampaignRepository = Substitute.For<IRefundCampaignRepository>();
        refundCreator ??= Substitute.For<IRefundCreator>();
        var refundRetriever = Substitute.For<IRefundRetriever>();
        correctionPlanner.PlanLocationPrivacyCorrectionAsync(
                Arg.Any<AtprotoLocationPrivacyCorrectionInput>(),
                Arg.Any<CancellationToken>())
            .Returns(AtprotoPublicationPlanningResult.Skipped("correction_already_planned"));
        return new CompositeOutboxMessageDispatcher(
            new NotificationFanoutOccurrenceHandoffService(
                occurrenceRepository ?? Substitute.For<INotificationFanoutOccurrenceRepository>(),
                runRepository ?? Substitute.For<INotificationFanoutRunRepository>()),
            fanoutService,
            moderationFanoutService,
            reportProviderSyncDispatcher ?? Substitute.For<IReportProviderSyncDispatcher>(),
            new LocationPrivacyCorrectionDispatcher(
                selectedCache,
                correctionPlanner,
                EventLocationPrivacyMetricsFactory.Create()),
            new PrivacyErasureCacheInvalidationDispatcher(selectedCache),
            admissionDeliveryHandler ?? Substitute.For<IAdmissionCredentialDeliveryOutboxHandler>(),
            Substitute.For<IAdmissionRecoveryRequestOutboxHandler>(),
            admissionRecoveryHandler ?? Substitute.For<IAdmissionRecoveryDeliveryOutboxHandler>(),
            outboxRepository ?? Substitute.For<IOutboxRepository>(),
            refundCampaignRepository,
            new RefundCampaignProcessor(
                refundCampaignRepository, refundRepository,
                Substitute.For<IRegistrationMaterialChangeChoiceRepository>(),
                Substitute.For<IRegistrationPaymentAttemptRepository>(), TimeProvider.System),
            new RefundDispatchService(refundRepository, refundCreator, TimeProvider.System),
            new RefundReconciliationService(refundRepository, refundCreator, refundRetriever, TimeProvider.System),
            new RegistrationPaymentCancellationService(
                Substitute.For<IRegistrationPaymentAttemptRepository>(), refundRepository,
                refundCampaignRepository, Substitute.For<IPaymentCancellationProvider>(), TimeProvider.System),
            admissionRevocationService ?? Substitute.For<IAdmissionRevocationService>(),
            admissionRefundRevocationService ?? Substitute.For<IAdmissionRefundRevocationService>(),
            admissionEventCancellationService ?? Substitute.For<IAdmissionEventCancellationService>(),
            CreateMetrics(),
            TimeProvider.System,
            mediator ?? Substitute.For<IMediator>(),
            NullLogger<CompositeOutboxMessageDispatcher>.Instance,
            manifestEffectDispatcher
                ?? Substitute.For<IConfigurationManifestEffectDispatcher>(),
            importEffectDelivery
                ?? Substitute.For<IConfigurationImportEffectDelivery>());
    }

    private static PaidOrderAcceptanceSnapshot RefundAcceptance(Guid tenantId) =>
        PaidOrderAcceptanceSnapshot.Create(
            Guid.CreateVersion7(),
            tenantId,
            tenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "composition-1",
            "disclosure-1",
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
            Guid.CreateVersion7(),
            "Example Organizer",
            PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "Community Events", "Community Events ASBL",
                "registered_organization", "BE", null, "contact@example.test", "https://example.test/legal",
                "https://example.test/terms", "https://example.test/privacy"),
            PaidCheckoutOperatorDisclosure.Create(
                Guid.CreateVersion7(),
                "Example Operator",
                false,
                "https://events.example.test",
                "BE",
                "https://events.example.test",
                "https://events.example.test/legal",
                "https://events.example.test/terms",
                "https://events.example.test/privacy",
                "complaints@example.test",
                "Trust and Safety",
                "Payments Operations",
                "Dispute Operations",
                "Payment Reconciliation",
                "approved"),
            PaidOrderDeliverySnapshot.Create(
                new DateTimeOffset(2026, 9, 10, 17, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero),
                "Europe/Brussels"),
            "EUR",
            500,
            0,
            0,
            500,
            Guid.CreateVersion7(),
            7,
            "Refunds follow accepted policy v7.",
            "en-GB",
            "support@example.test",
            PaidCheckoutProviderDisclosure.Create(
                "stripe",
                "OrganizerDirect",
                "direct-charge",
                "EXAMPLE EVENT",
                "test",
                "instance-operator"),
            [PaidOrderAcceptanceLineFact.Create(
                Guid.CreateVersion7(), "Admission", 1, 500, 0, 500)],
            DateTime.UtcNow.AddHours(-1),
            organizerPaymentProviderConnectionId: Guid.CreateVersion7(),
            connectPlatformId: "platform-test",
            externalAccountId: "acct_example",
            merchantCountryCode: "BE");

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }

    private static NotificationFanoutOccurrence CreateOccurrence()
    {
        DateTime occurredAt = DateTime.UtcNow;
        Guid eventId = Guid.CreateVersion7();
        return NotificationFanoutOccurrence.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
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
            (int)Explore.Domain.Enums.NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            policyVersion: 1,
            priority: 30,
            notBefore: occurredAt,
            sourceType: "event",
            sourceId: eventId,
            coalescingKey: $"event:{eventId:N}:schedule",
            coalescingWindowEndsAt: occurredAt,
            emailDeliveryPolicyRevision: 0);
    }

    private static NotificationFanoutRun CreateRun(NotificationFanoutOccurrence occurrence) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = occurrence.TenantId,
            Tenant = null!,
            FanoutOccurrenceId = occurrence.Id,
            FanoutKind = "recipient_occurrence",
            NotificationEntityTypeId = (int)Explore.Domain.Enums.NotificationEntityTypeEnum.Event,
            NotificationEntityType = null!,
            EntityId = occurrence.EventId,
            SourceActorId = Guid.CreateVersion7(),
            SourceActor = null!,
            Status = "pending",
            ConcurrencyStamp = Guid.CreateVersion7()
        };

    private static OutboxMessage CreateLocationPrivacyMessage(string eventType)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid eventLocationId = Guid.CreateVersion7();
        Guid locationId = Guid.CreateVersion7();
        object payload = eventType switch
        {
            LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType => new
            {
                SchemaVersion = 1,
                IntentId = Guid.CreateVersion7(),
                AuthoritySequence = 1,
                LocationId = locationId,
                LocationVersion = Guid.CreateVersion7()
            },
            LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType => new
            {
                SchemaVersion = 1,
                IntentId = Guid.CreateVersion7(),
                AuthoritySequence = 1,
                TenantId = tenantId,
                EventId = eventId,
                EventLocationId = eventLocationId,
                LocationId = (Guid?)null,
                PolicyVersion = 1
            },
            LocationPrivacyCorrectionDispatcher.GovernanceCorrectionEventType => new
            {
                SchemaVersion = 1,
                TenantId = tenantId,
                EventId = eventId,
                EventLocationId = eventLocationId,
                PolicyVersion = 2
            },
            _ => throw new ArgumentOutOfRangeException(nameof(eventType))
        };

        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = eventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
                ? nameof(Location)
                : nameof(EventLocation),
            AggregateId = eventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
                ? locationId
                : eventLocationId,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    private sealed class CapturingImportEffectDelivery
        : IConfigurationImportEffectDelivery
    {
        public OutboxMessage? Message { get; private set; }

        public Task DrainPendingAsync(
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeliverAsync(
            Guid outboxMessageId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DispatchClaimedAsync(
            OutboxMessage message,
            CancellationToken cancellationToken)
        {
            Message = message;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHybridCache : HybridCache
    {
        public List<string> RemovedKeys { get; } = [];
        public List<string> RemovedTags { get; } = [];

        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => factory(state, cancellationToken);

        public override ValueTask RemoveAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            RemovedKeys.Add(key);
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveByTagAsync(
            string tag,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemovedTags.Add(tag);
            return ValueTask.CompletedTask;
        }

        public override ValueTask SetAsync<T>(
            string key,
            T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
