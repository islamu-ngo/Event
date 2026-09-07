using System.Text.Json;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Services;

public sealed class OutboxPayloadSnapshotContractTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset OccurredAt = new(UtcNow);

    [Test]
    public async Task PublishedPayload_RoundTripsFactorySnapshotAndCallerOwnedReplayId()
    {
        Guid messageId = Guid.CreateVersion7();
        Explore.Domain.Event @event = CreateEvent();

        OutboxMessage message = EventPublishedOutboxMessageFactory.CreateNotificationFanoutOutboxMessage(
            messageId,
            @event,
            OccurredAt);
        EventPublishedNotificationFanoutRequested payload = Deserialize<EventPublishedNotificationFanoutRequested>(message);
        string replayJson = JsonSerializer.Serialize(payload with { EventTitle = "Updated safe title" });
        EventPublishedNotificationFanoutRequested replay = JsonSerializer.Deserialize<EventPublishedNotificationFanoutRequested>(replayJson)!;

        await Assert.That(message.Id).IsEqualTo(messageId);
        await Assert.That(message.EventType).IsEqualTo(EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType);
        await Assert.That(payload.EventId).IsEqualTo(@event.Id);
        await Assert.That(payload.SourceActorId).IsEqualTo(@event.ActorId);
        await Assert.That(replay.EventTitle).IsEqualTo("Updated safe title");
        await Assert.That(replay with { EventTitle = payload.EventTitle }).IsEqualTo(payload);
    }

    [Test]
    public async Task LightModerationPayload_RoundTripsFactorySnapshotAndCallerOwnedReplayId()
    {
        Guid messageId = Guid.CreateVersion7();
        Explore.Domain.Event @event = CreateEvent();
        EventModerationRecord moderation = EventModerationRecord.CreateLightModeration(
            Guid.CreateVersion7(),
            @event.TenantId,
            @event.Id,
            Guid.CreateVersion7(),
            "policy_review",
            (int)EventStatusEnum.Published,
            correlationId: null,
            OccurredAt);

        OutboxMessage message = EventModerationOutboxMessageFactory.CreateLightModerationNotificationFanoutMessage(
            messageId,
            @event,
            moderation);
        EventLightModeratedNotificationFanoutRequested payload = Deserialize<EventLightModeratedNotificationFanoutRequested>(message);

        await Assert.That(message.Id).IsEqualTo(messageId);
        await Assert.That(message.EventType).IsEqualTo(EventModerationOutboxMessageFactory.EventLightModeratedNotificationFanoutRequestedEventType);
        await Assert.That(payload.ModerationRecordId).IsEqualTo(moderation.Id);
        await Assert.That(JsonSerializer.Deserialize<EventLightModeratedNotificationFanoutRequested>(
            JsonSerializer.Serialize(payload))).IsEqualTo(payload);
    }

    [Test]
    public async Task HeavyModerationPayload_PreservesVersionAndPrivacySafeReplayFacts()
    {
        Explore.Domain.Event @event = CreateEvent("PRIVATE-TITLE-CANARY");
        EventModerationRecord moderation = EventModerationRecord.CreateHeavyRedaction(
            Guid.CreateVersion7(),
            @event.TenantId,
            @event.Id,
            Guid.CreateVersion7(),
            "PRIVATE-REASON-CANARY",
            (int)EventStatusEnum.Published,
            "PRIVATE-CORRELATION-CANARY",
            OccurredAt);

        OutboxMessage message = EventModerationOutboxMessageFactory.CreateHeavyRedactionNotificationFanoutMessage(
            @event,
            moderation);
        EventHeavyRedactedNotificationFanoutPayloadParseResult replay =
            EventHeavyRedactedNotificationFanoutPayloadParser.Parse(message.Payload!);

        await Assert.That(message.Id.Version).IsEqualTo(7);
        await Assert.That(message.EventType).IsEqualTo(EventModerationOutboxMessageFactory.EventHeavyRedactedNotificationFanoutRequestedEventType);
        await Assert.That(replay.Request.Version).IsEqualTo(EventHeavyRedactedNotificationFanoutRequested.CurrentVersion);
        await Assert.That(replay.Request.TenantId).IsEqualTo(@event.TenantId);
        await Assert.That(replay.Request.ModerationRecordId).IsEqualTo(moderation.Id);
        await Assert.That(replay.WasLegacy).IsFalse();
        await Assert.That(GetPropertyNames(replay.CanonicalPayload)).IsEquivalentTo([
            "TenantId", "ModerationRecordId", "Version"]);
        await Assert.That(replay.CanonicalPayload).DoesNotContain("PRIVATE-TITLE-CANARY");
        await Assert.That(replay.CanonicalPayload).DoesNotContain("PRIVATE-REASON-CANARY");
        await Assert.That(replay.CanonicalPayload).DoesNotContain("PRIVATE-CORRELATION-CANARY");
    }

    [Test]
    public async Task ReportProviderPayload_PreservesProviderIdempotencyInputsAndSafeFields()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        EventReport report = EventReport.Create(
            tenantId,
            eventId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            EventReporterKind.AuthenticatedUser,
            EventReportSourceKind.UserReport,
            "spam",
            subcategoryCode: null,
            EventReportPriority.Normal,
            severityHint: null,
            reportCaseUpdatesConsent: true,
            reportFollowUpContactConsent: false,
            reporterLocale: "en",
            reporterIpHash: "PRIVATE-IP-HASH-CANARY",
            reporterUserAgentHash: "PRIVATE-UA-HASH-CANARY",
            UtcNow);
        EventReportCase reportCase = EventReportCase.Create(
            tenantId,
            report.Id,
            "default",
            EventReportPriority.Normal,
            slaDueAt: UtcNow.AddHours(48),
            UtcNow);

        OutboxMessage message = EventReportOutboxMessageFactory.CreateProviderSyncRequestedMessage(
            report,
            reportCase,
            "  correlation-123  ");
        EventReportProviderSyncRequested payload = Deserialize<EventReportProviderSyncRequested>(message);

        await Assert.That(message.Id.Version).IsEqualTo(7);
        await Assert.That(message.EventType).IsEqualTo(EventReportOutboxMessageFactory.EventReportProviderSyncRequestedEventType);
        await Assert.That(payload.ReportId).IsEqualTo(report.Id);
        await Assert.That(payload.CaseId).IsEqualTo(reportCase.Id);
        await Assert.That(payload.CaseConcurrencyStamp).IsEqualTo(reportCase.ConcurrencyStamp);
        await Assert.That(payload.CorrelationId).IsEqualTo("correlation-123");
        await Assert.That(JsonSerializer.Deserialize<EventReportProviderSyncRequested>(
            JsonSerializer.Serialize(payload))).IsEqualTo(payload);
        await Assert.That(GetPropertyNames(message.Payload!)).IsEquivalentTo([
            "TenantId", "ReportId", "EventId", "CaseId", "CaseConcurrencyStamp",
            "ReasonCode", "QueueCode", "SubmittedAtUtc", "CorrelationId"]);
        await Assert.That(message.Payload).DoesNotContain("PRIVATE-IP-HASH-CANARY");
        await Assert.That(message.Payload).DoesNotContain("PRIVATE-UA-HASH-CANARY");
    }

    [Test]
    [Arguments(RegistrationOrderStatusEnum.Confirmed, RegistrationOrderOutboxMessageFactory.ConfirmedEventType, true)]
    [Arguments(RegistrationOrderStatusEnum.Cancelled, RegistrationOrderOutboxMessageFactory.CancelledEventType, false)]
    [Arguments(RegistrationOrderStatusEnum.Rejected, RegistrationOrderOutboxMessageFactory.RejectedEventType, false)]
    public async Task RegistrationLifecyclePayload_PreservesTerminalEventAndReplayIdentity(
        RegistrationOrderStatusEnum status,
        string expectedEventType,
        bool admissionIssuanceRequested)
    {
        Guid messageId = Guid.CreateVersion7();
        RegistrationOrder order = CreateRegistrationOrder();

        OutboxMessage message = RegistrationOrderOutboxMessageFactory.Create(
            messageId,
            order,
            status,
            UtcNow,
            admissionCount: 3);
        RegistrationOrderLifecycleOutboxPayload payload = Deserialize<RegistrationOrderLifecycleOutboxPayload>(message);

        await Assert.That(message.Id).IsEqualTo(messageId);
        await Assert.That(message.EventType).IsEqualTo(expectedEventType);
        await Assert.That(payload.RegistrationOrderId).IsEqualTo(order.Id);
        await Assert.That(payload.StatusId).IsEqualTo((int)status);
        await Assert.That(payload.AdmissionCount).IsEqualTo(3);
        await Assert.That(payload.AdmissionIssuanceRequested).IsEqualTo(admissionIssuanceRequested);
        await Assert.That(JsonSerializer.Deserialize<RegistrationOrderLifecycleOutboxPayload>(
            JsonSerializer.Serialize(payload))).IsEqualTo(payload);
        await Assert.That(GetPropertyNames(message.Payload!)).IsEquivalentTo([
            "RegistrationOrderId", "EventId", "TenantId", "StatusId", "AdmissionCount", "AdmissionIssuanceRequested"]);
    }

    private static T Deserialize<T>(OutboxMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload!)
        ?? throw new JsonException($"{typeof(T).Name} payload was null.");

    private static string[] GetPropertyNames(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    }

    private static Explore.Domain.Event CreateEvent(string title = "Safe event title") => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = Guid.CreateVersion7(),
        Tenant = null!,
        Title = title,
        ActorId = Guid.CreateVersion7(),
        Actor = null!,
        VisibilityType = null!,
        EventStatus = null!,
        EventFormat = null!,
        FirstSessionStartUtc = OccurredAt.AddDays(1),
        LastSessionStartUtc = OccurredAt.AddDays(1).AddHours(2)
    };

    private static RegistrationOrder CreateRegistrationOrder() => RegistrationOrder.Create(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        purchaserActorId: null,
        BookingPartyTypeEnum.Individual,
        Guid.CreateVersion7(),
        RegistrationParticipationSnapshot.Create(
            Guid.CreateVersion7(),
            (int)ParticipationHandlingModeEnum.PlatformManaged,
            (int)AdvanceRegistrationObligationEnum.Required,
            (int)IdentityAccessModeEnum.AccountRequired,
            guestRecoveryPolicy: null),
        registrationWorkflowVersionId: null,
        guestAccessTokenHash: null,
        "USD",
        UtcNow,
        UtcNow.AddMinutes(15));
}
