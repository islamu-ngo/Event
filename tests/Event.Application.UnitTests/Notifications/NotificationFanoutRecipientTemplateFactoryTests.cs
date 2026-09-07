// ABOUTME: Verifies the closed fanout template set and immutable recipient rendering.
// ABOUTME: Covers strict contract drift, occurrence linkage, and snapshot-only location disclosure.

using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Services;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Notifications;

public sealed class NotificationFanoutRecipientTemplateFactoryTests
{
    private readonly NotificationFanoutRecipientTemplateFactory factory = new();

    [Test]
    [Arguments(NotificationFanoutRecipientTemplateFactory.EventCancelledTemplateKey, false, true)]
    [Arguments(NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey, false, false)]
    [Arguments(NotificationFanoutRecipientTemplateFactory.SessionCancelledTemplateKey, true, true)]
    [Arguments(NotificationFanoutRecipientTemplateFactory.SessionUpdatedTemplateKey, true, false)]
    public async Task VersionOneTemplateCreatesOccurrenceLinkedConfiguredChannels(
        string templateKey,
        bool sessionScoped,
        bool cancelled)
    {
        NotificationFanoutOccurrence occurrence = CreateOccurrence(templateKey, sessionScoped, cancelled);
        Guid recipientUserId = Guid.CreateVersion7();
        NotificationFanoutRecipientTemplate template = factory.Parse(occurrence);

        var request = factory.CreateMaterialization(
            occurrence,
            template,
            recipientUserId,
            "current-verified@example.test",
            emailPreferenceEnabled: true,
            emailSkipReason: null,
            locationAuthorization: null);

        await Assert.That(request.Intent.FanoutOccurrenceId).IsEqualTo(occurrence.Id);
        await Assert.That(request.Intent.DeduplicationKey).Contains(occurrence.Id.ToString("N"));
        await Assert.That(request.IntentId).IsNotEqualTo(Guid.Empty);
        await Assert.That(request.InAppNotificationId).IsNotNull();
        await Assert.That(request.InAppNotificationId).IsNotEqualTo(Guid.Empty);
        await Assert.That(request.InAppDeliveryId).IsNotNull();
        await Assert.That(request.InAppDeliveryId).IsNotEqualTo(Guid.Empty);
        await Assert.That(request.EmailDeliveryId).IsNotNull();
        await Assert.That(request.EmailDeliveryId).IsNotEqualTo(Guid.Empty);
        await Assert.That(request.MaterializedAt).IsNotNull();
        await Assert.That(request.MaterializedAt!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(request.InApp).IsNotNull();
        await Assert.That(request.Email).IsNotNull();
        await Assert.That(request.Email!.SourceType).IsEqualTo(NotificationFanoutRecipientTemplateFactory.OccurrenceSourceType);
        await Assert.That(request.Email.SourceId).IsEqualTo(occurrence.Id);
        await Assert.That(request.Email.Kind).IsEqualTo(cancelled ? EmailDispatchKind.EventCancelled : EmailDispatchKind.EventUpdated);
    }

    [Test]
    public async Task UnknownVersionPolicyKeyAndScopeFailClosed()
    {
        NotificationFanoutOccurrence unknownKey = CreateOccurrence("event.reminder", false, false);
        NotificationFanoutOccurrence unknownVersion = CreateOccurrence(
            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            false,
            false,
            templateVersion: 2);
        NotificationFanoutOccurrence wrongPolicy = CreateOccurrence(
            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            false,
            false,
            deliveryPolicyId: (int)NotificationDeliveryPolicyEnum.ReminderOptional);
        NotificationFanoutOccurrence wrongScope = CreateOccurrence(
            NotificationFanoutRecipientTemplateFactory.SessionUpdatedTemplateKey,
            false,
            false);

        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(factory.Parse(unknownKey)));
        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(factory.Parse(unknownVersion)));
        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(factory.Parse(wrongPolicy)));
        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(factory.Parse(wrongScope)));
    }

    [Test]
    public async Task MalformedAndUnknownSnapshotMembersFailClosed()
    {
        NotificationFanoutOccurrence malformed = CreateOccurrence(
            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            false,
            false,
            afterJson: "{");
        string valid = SnapshotJson(sessionScoped: false, "Immutable event", "Immutable venue");
        NotificationFanoutOccurrence extraMember = CreateOccurrence(
            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            false,
            false,
            afterJson: valid[..^1] + ",\"recipientEmail\":\"forbidden@example.test\"}");

        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(factory.Parse(malformed)));
        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(factory.Parse(extraMember)));
    }

    [Test]
    public async Task LocationMaskSelectsOnlyImmutableSnapshotValues()
    {
        NotificationFanoutOccurrence occurrence = CreateOccurrence(
            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            false,
            false);
        NotificationFanoutRecipientTemplate template = factory.Parse(occurrence);
        Guid recipientUserId = Guid.CreateVersion7();
        var authorization = new FanoutAttendeeLocationAuthorizationResult(
            occurrence.TenantId,
            occurrence.EventId,
            recipientUserId,
            template.After.Location!.EventLocationId,
            template.After.Location.RoomId,
            EventLocationDisclosureState.Available,
            ImmutableArray.Create(EventLocationDisclosureField.VenueName));

        var request = factory.CreateMaterialization(
            occurrence,
            template,
            recipientUserId,
            "verified@example.test",
            emailPreferenceEnabled: true,
            emailSkipReason: null,
            authorization);

        await Assert.That(request.Email!.PlainTextBody).Contains("Immutable venue");
        await Assert.That(request.Email.PlainTextBody).DoesNotContain("current database venue");
        await Assert.That(typeof(FanoutAttendeeLocationAuthorizationResult).GetProperties()
            .Any(property => property.Name is "Values" or "VenueName" or "StreetAddress")).IsFalse();
    }

    [Test]
    public async Task LocationAuthorizationForAnotherRecipientFailsClosed()
    {
        NotificationFanoutOccurrence occurrence = CreateOccurrence(
            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            false,
            false);
        NotificationFanoutRecipientTemplate template = factory.Parse(occurrence);
        Guid recipientUserId = Guid.CreateVersion7();
        var authorization = CreateAuthorization(
            occurrence,
            template.After.Location!,
            Guid.CreateVersion7());

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.FromResult(factory.CreateMaterialization(
            occurrence,
            template,
            recipientUserId,
            "verified@example.test",
            emailPreferenceEnabled: true,
            emailSkipReason: null,
            authorization)));
    }

    [Test]
    public async Task LocationAuthorizationForAnotherRoomFailsClosed()
    {
        NotificationFanoutOccurrence occurrence = CreateOccurrence(
            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            false,
            false);
        NotificationFanoutRecipientTemplate template = factory.Parse(occurrence);
        Guid recipientUserId = Guid.CreateVersion7();
        FanoutAttendeeLocationAuthorizationResult authorization = CreateAuthorization(
            occurrence,
            template.After.Location!,
            recipientUserId) with
        {
            RoomId = Guid.CreateVersion7()
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.FromResult(factory.CreateMaterialization(
            occurrence,
            template,
            recipientUserId,
            "verified@example.test",
            emailPreferenceEnabled: true,
            emailSkipReason: null,
            authorization)));
    }

    [Test]
    [Arguments("{\"fields\":[\"Title\"]}")]
    [Arguments("{\"fields\":[2]}")]
    public async Task FormerTitleChangeFieldFailsClosed(string changeSetJson)
    {
        NotificationFanoutOccurrence occurrence = CreateOccurrence(
            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            false,
            false,
            changeSetJson: changeSetJson);

        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(factory.Parse(occurrence)));
    }

    [Test]
    public async Task EventTimezoneSessionDisplayTimesAreCanonicalizedAndPairedBySessionId()
    {
        Guid firstId = Guid.Parse("01990000-0000-7000-8000-000000000001");
        Guid secondId = Guid.Parse("01990000-0000-7000-8000-000000000002");
        var firstBefore = new NotificationFanoutSessionDisplayTimeV1(
            firstId,
            "First",
            new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero),
            null);
        var secondBefore = new NotificationFanoutSessionDisplayTimeV1(
            secondId,
            "Second",
            new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero),
            null);
        var firstAfter = firstBefore with
        {
            StartsAt = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2))
        };
        var secondAfter = secondBefore with
        {
            StartsAt = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(1))
        };
        NotificationFanoutOccurrence occurrence = CreateOccurrence(
            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            sessionScoped: false,
            cancelled: false,
            afterJson: RawTimezoneSnapshot("Europe/Brussels", firstAfter, secondAfter),
            changeSetJson: NotificationFanoutTemplateJson.Serialize(new NotificationFanoutChangeSetV1([
                NotificationFanoutChangeField.Timezone])),
            beforeJson: RawTimezoneSnapshot("UTC", secondBefore, firstBefore));

        NotificationFanoutRecipientTemplate template = factory.Parse(occurrence);

        await Assert.That(template.Before.SessionDisplayTimes!.Select(value => value.SessionId))
            .IsEquivalentTo([firstId, secondId]);
        await Assert.That(template.Before.SessionDisplayTimes[0]).IsEqualTo(firstBefore);
        await Assert.That(template.After.SessionDisplayTimes![0]).IsEqualTo(firstAfter);
        await Assert.That(template.Before.SessionDisplayTimes[1]).IsEqualTo(secondBefore);
        await Assert.That(template.After.SessionDisplayTimes[1]).IsEqualTo(secondAfter);
        var materialization = factory.CreateMaterialization(
            occurrence,
            template,
            Guid.CreateVersion7(),
            "verified@example.test",
            emailPreferenceEnabled: true,
            emailSkipReason: null,
            locationAuthorization: null);
        await Assert.That(materialization.Email!.PlainTextBody).Contains("First");
        await Assert.That(materialization.Email.PlainTextBody).Contains("Second");
        await Assert.That(materialization.Email.PlainTextBody).DoesNotContain(" to ");
    }

    [Test]
    public async Task HalfEnrichedAndDefaultSessionDisplayPayloadsFailClosed()
    {
        Guid sessionId = Guid.Parse("01990000-0000-7000-8000-000000000003");
        var validBefore = new NotificationFanoutSessionDisplayTimeV1(
            sessionId,
            "Valid session",
            new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero),
            null);
        string timezoneChangeSet = NotificationFanoutTemplateJson.Serialize(new NotificationFanoutChangeSetV1([
            NotificationFanoutChangeField.Timezone]));
        NotificationFanoutOccurrence halfEnriched = CreateOccurrence(
            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            sessionScoped: false,
            cancelled: false,
            afterJson: SnapshotJson(sessionScoped: false, "Immutable event", "Immutable venue"),
            changeSetJson: timezoneChangeSet,
            beforeJson: RawTimezoneSnapshot("UTC", validBefore));
        var defaultEntry = new NotificationFanoutSessionDisplayTimeV1(
            sessionId,
            string.Empty,
            default,
            null);
        NotificationFanoutOccurrence invalidEntry = CreateOccurrence(
            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
            sessionScoped: false,
            cancelled: false,
            afterJson: RawTimezoneSnapshot("Europe/Brussels", defaultEntry),
            changeSetJson: timezoneChangeSet,
            beforeJson: RawTimezoneSnapshot("UTC", defaultEntry));

        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(factory.Parse(halfEnriched)));
        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(factory.Parse(invalidEntry)));
    }

    [Test]
    public async Task HeavyModerationCreatesRequiredLinklessChannelsWithoutConsultingPreference()
    {
        NotificationFanoutOccurrence occurrence = CreateHeavyModerationOccurrence();
        Guid recipientUserId = Guid.CreateVersion7();
        NotificationFanoutRecipientTemplate template = factory.Parse(occurrence);

        RecipientNotificationMaterialization materialization = factory.CreateMaterialization(
            occurrence,
            template,
            recipientUserId,
            "current-verified@example.test",
            emailPreferenceEnabled: false,
            emailSkipReason: null,
            locationAuthorization: null);

        await Assert.That(template.IsModerationAvailabilityRequired).IsTrue();
        await Assert.That(materialization.DeliveryPolicy).IsEqualTo(NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired);
        await Assert.That(materialization.InApp).IsNotNull();
        await Assert.That(materialization.InApp!.IsRequired).IsTrue();
        await Assert.That(materialization.InApp.NotificationEntityTypeId).IsNull();
        await Assert.That(materialization.InApp.EntityId).IsNull();
        await Assert.That(materialization.EmailRequired).IsTrue();
        await Assert.That(materialization.Email).IsNotNull();
        await Assert.That(materialization.Email!.Kind).IsEqualTo(EmailDispatchKind.ModerationAvailabilityRequired);
        await Assert.That(materialization.Email.RecipientEmail).IsEqualTo("current-verified@example.test");
        await Assert.That(materialization.PreferenceCategoryCode).IsNull();
        await Assert.That(materialization.EmailPreferenceEnabled).IsNull();
        await Assert.That(materialization.LinkAllowed).IsFalse();
        await Assert.That(materialization.InApp.Title).IsEqualTo(NotificationFanoutRecipientTemplateFactory.ModerationUnavailableTitle);
        await Assert.That(materialization.InApp.Body).IsEqualTo(NotificationFanoutRecipientTemplateFactory.ModerationUnavailableBody);
        await Assert.That(materialization.Email.Subject).IsEqualTo(NotificationFanoutRecipientTemplateFactory.ModerationUnavailableTitle);
        await Assert.That(materialization.Email.PlainTextBody).DoesNotContain("Immutable event");
        await Assert.That(materialization.IntentId).IsNotEqualTo(Guid.Empty);
        await Assert.That(materialization.InAppNotificationId).IsNotNull();
        await Assert.That(materialization.InAppNotificationId).IsNotEqualTo(Guid.Empty);
        await Assert.That(materialization.InAppDeliveryId).IsNotNull();
        await Assert.That(materialization.InAppDeliveryId).IsNotEqualTo(Guid.Empty);
        await Assert.That(materialization.EmailDeliveryId).IsNotNull();
        await Assert.That(materialization.EmailDeliveryId).IsNotEqualTo(Guid.Empty);
        await Assert.That(materialization.MaterializedAt).IsNotNull();
        await Assert.That(materialization.MaterializedAt!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
    }

    [Test]
    public async Task HeavyModerationWithoutVerifiedAddressCreatesTypedRequiredEmailSkip()
    {
        NotificationFanoutOccurrence occurrence = CreateHeavyModerationOccurrence();
        NotificationFanoutRecipientTemplate template = factory.Parse(occurrence);

        RecipientNotificationMaterialization materialization = factory.CreateMaterialization(
            occurrence,
            template,
            Guid.CreateVersion7(),
            verifiedEmail: null,
            emailPreferenceEnabled: false,
            emailSkipReason: RecipientEmailAddressResolver.RecipientEmailUnverified,
            locationAuthorization: null);

        await Assert.That(materialization.InApp!.IsRequired).IsTrue();
        await Assert.That(materialization.IncludeEmailChannel).IsTrue();
        await Assert.That(materialization.EmailRequired).IsTrue();
        await Assert.That(materialization.Email).IsNull();
        await Assert.That(materialization.EmailSkipReason).IsEqualTo(RecipientEmailAddressResolver.RecipientEmailUnverified);
    }

    [Test]
    public async Task HeavyModerationPayloadWithBusinessOrRecipientDataFailsClosed()
    {
        NotificationFanoutOccurrence occurrence = CreateHeavyModerationOccurrence(afterJson: "{\"eventTitle\":\"forbidden\"}");

        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(factory.Parse(occurrence)));
    }

    [Test]
    [Arguments("{\"title\":\"private-title-canary\"}", "{}", "{}")]
    [Arguments("{}", "{\"provider\":\"private-provider-canary\"}", "{}")]
    [Arguments("{}", "{}", "{\"storageKey\":\"private-storage-key-canary\"}")]
    public async Task HeavyModerationRejectsNonemptyEnrichmentAcrossEveryOccurrencePayload(
        string changeSetJson,
        string beforeJson,
        string afterJson)
    {
        NotificationFanoutOccurrence occurrence = CreateOccurrence(
            NotificationFanoutOccurrenceCoordinationPolicy.HeavyModerationUnavailableTemplateKey,
            sessionScoped: false,
            cancelled: false,
            deliveryPolicyId: (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired,
            afterJson: afterJson,
            changeSetJson: changeSetJson,
            beforeJson: beforeJson);

        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(factory.Parse(occurrence)));
    }

    [Test]
    public async Task HeavyModerationFullCanaryIsAbsentFromPointersMetadataAndRenderedChannels()
    {
        NotificationFanoutOccurrence occurrence = CreateHeavyModerationOccurrence();
        RecipientNotificationMaterialization materialization = factory.CreateMaterialization(
            occurrence,
            factory.Parse(occurrence),
            Guid.CreateVersion7(),
            "attendee-pii-canary@example.test",
            emailPreferenceEnabled: false,
            emailSkipReason: null,
            locationAuthorization: null);
        OutboxMessage pointer = NotificationFanoutOccurrenceOutboxMessageFactory.Create(occurrence);
        string exposedSurface = string.Join('\n',
            occurrence.ChangeSetJson,
            occurrence.SafeBeforeSnapshotJson,
            occurrence.SafeAfterSnapshotJson,
            pointer.Payload,
            materialization.Intent.SafePayloadReference,
            materialization.Intent.DeduplicationKey,
            materialization.Intent.CorrelationId,
            materialization.InApp!.Title,
            materialization.InApp.Body,
            materialization.InApp.NotificationEntityTypeId?.ToString(),
            materialization.InApp.EntityId,
            materialization.LinkAllowed.ToString(),
            materialization.Email!.Subject,
            materialization.Email.PlainTextBody,
            materialization.Email.HtmlBody);
        string[] forbiddenCanaries =
        [
            "private-title-canary",
            "private-slug-canary",
            "https://private.example/events/canary",
            "private-description-canary",
            "private-image-canary",
            "private-organizer-canary",
            "private-evidence-canary",
            "private-decision-note-canary",
            "private-reason-canary",
            "private-moderator-canary",
            "private-provider-canary",
            "private-storage-path-canary",
            "private-storage-key-canary",
            "private-raw-error-canary",
            "attendee-pii-canary@example.test"
        ];

        foreach (string canary in forbiddenCanaries)
        {
            await Assert.That(exposedSurface).DoesNotContain(canary);
        }

        await Assert.That(exposedSurface).DoesNotContain("unsubscribe", StringComparison.OrdinalIgnoreCase);
        await Assert.That(materialization.LinkAllowed).IsFalse();
        await Assert.That(materialization.InApp.NotificationEntityTypeId).IsNull();
        await Assert.That(materialization.InApp.EntityId).IsNull();
    }

    private static NotificationFanoutOccurrence CreateHeavyModerationOccurrence(string afterJson = "{}") =>
        CreateOccurrence(
            NotificationFanoutOccurrenceCoordinationPolicy.HeavyModerationUnavailableTemplateKey,
            sessionScoped: false,
            cancelled: false,
            deliveryPolicyId: (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired,
            afterJson: afterJson,
            changeSetJson: "{}",
            beforeJson: "{}");

    private static NotificationFanoutOccurrence CreateOccurrence(
        string templateKey,
        bool sessionScoped,
        bool cancelled,
        int templateVersion = 1,
        int deliveryPolicyId = (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
        string? afterJson = null,
        string? changeSetJson = null,
        string? beforeJson = null)
    {
        DateTime now = DateTime.UtcNow;
        return NotificationFanoutOccurrence.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            sessionScoped ? Guid.CreateVersion7() : null,
            now,
            now,
            Guid.CreateVersion7(),
            changeSetJson ?? NotificationFanoutTemplateJson.Serialize(new NotificationFanoutChangeSetV1(
                cancelled
                    ? [NotificationFanoutChangeField.Cancelled]
                    : [NotificationFanoutChangeField.StartTime, NotificationFanoutChangeField.Location])),
            beforeJson ?? SnapshotJson(sessionScoped, "Immutable event", "Old immutable venue"),
            afterJson ?? SnapshotJson(sessionScoped, "Immutable event", "Immutable venue"),
            templateKey,
            templateVersion,
            deliveryPolicyId,
            NotificationFanoutRecipientTemplateFactory.CurrentPolicyVersion,
            30,
            now,
            sessionScoped ? "event-session" : "event",
            Guid.CreateVersion7(),
            $"fanout:{Guid.NewGuid():N}",
            null,
            emailDeliveryPolicyRevision: 0);
    }

    private static string RawTimezoneSnapshot(
        string timezone,
        params NotificationFanoutSessionDisplayTimeV1[] sessions) =>
        JsonSerializer.Serialize(
            new NotificationFanoutSnapshotV1(
                "Immutable event",
                SessionTitle: null,
                StartsAt: null,
                EndsAt: null,
                Timezone: timezone,
                Location: null,
                SessionDisplayTimes: sessions),
            NotificationFanoutTemplateJsonContext.Default.NotificationFanoutSnapshotV1);

    private static FanoutAttendeeLocationAuthorizationResult CreateAuthorization(
        NotificationFanoutOccurrence occurrence,
        NotificationFanoutLocationSnapshotV1 location,
        Guid recipientUserId) => new(
            occurrence.TenantId,
            occurrence.EventId,
            recipientUserId,
            location.EventLocationId,
            location.RoomId,
            EventLocationDisclosureState.Available,
            ImmutableArray.Create(EventLocationDisclosureField.VenueName));

    private static string SnapshotJson(bool sessionScoped, string eventTitle, string venueName) =>
        NotificationFanoutTemplateJson.Serialize(new NotificationFanoutSnapshotV1(
            eventTitle,
            sessionScoped ? "Immutable session" : null,
            new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.FromHours(2)),
            "Europe/Brussels",
            new NotificationFanoutLocationSnapshotV1(
                Guid.Parse("0198a111-1111-7111-8111-111111111111"),
                null,
                "Belgium",
                "Brussels",
                venueName,
                null,
                "Immutable street 1",
                "1000")));
}
