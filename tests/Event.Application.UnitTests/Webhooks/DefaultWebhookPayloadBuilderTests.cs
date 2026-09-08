using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Webhooks;

namespace Event.Application.UnitTests.Webhooks;

public sealed class DefaultWebhookPayloadBuilderTests
{
    private static readonly Guid MessageId = Guid.Parse("018f0000-0000-7000-8000-000000000999");
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid AggregateId = Guid.Parse("018f0000-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset OccurredAt = new(2026, 7, 2, 10, 0, 0, TimeSpan.Zero);

    private readonly DefaultWebhookPayloadBuilder _builder = new(new WebhookEventTypeRegistry());

    [Test]
    public async Task BuildAsync_ForPublishedEvent_CreatesStableEnvelopeHashAndRetention()
    {
        var context = CreateContext(
            WebhookEventNames.EventPublished,
            new Dictionary<string, object?>
            {
                ["eventId"] = AggregateId.ToString(),
                ["status"] = "Published",
                ["publicUrl"] = "https://example.org/events/example-event",
                ["internalNote"] = "must not be included"
            });

        var result = await _builder.BuildAsync(context, CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Envelope!.Id).IsEqualTo(MessageId);
        await Assert.That(result.Envelope.Type).IsEqualTo(WebhookEventNames.EventPublished);
        await Assert.That(result.Envelope.Version).IsEqualTo(1);
        await Assert.That(result.PayloadHash).IsNotNull();
        await Assert.That(result.PayloadHash!.Length).IsEqualTo(71);
        await Assert.That(result.PayloadHash).StartsWith("sha256:");
        await Assert.That(result.PayloadRetentionUntil).IsEqualTo(OccurredAt.AddDays(14));
        await Assert.That(PayloadJson(result)).Contains("publicUrl");
        await Assert.That(PayloadJson(result)).DoesNotContain("internalNote");

        using var parsed = JsonDocument.Parse(result.PayloadBytes!.Value);
        await Assert.That(parsed.RootElement.GetProperty("tenantId").GetGuid()).IsEqualTo(TenantId);
        await Assert.That(parsed.RootElement.GetProperty("data").GetProperty("eventId").GetString()).IsEqualTo(AggregateId.ToString());
    }

    [Test]
    public async Task BuildAsync_ForHeavyRedaction_DropsUnsafeFields()
    {
        var moderationRecordId = Guid.Parse("018f0000-0000-7000-8000-000000000010");
        var context = CreateContext(
            WebhookEventNames.EventHeavyRedacted,
            new Dictionary<string, object?>
            {
                ["moderationRecordId"] = moderationRecordId.ToString(),
                ["status"] = "HeavyRedacted",
                ["reportId"] = "018f0000-0000-7000-8000-000000000020",
                ["caseId"] = "018f0000-0000-7000-8000-000000000030",
                ["eventId"] = AggregateId.ToString(),
                ["title"] = "Unsafe Event Title",
                ["slug"] = "unsafe-event-title",
                ["publicUrl"] = "https://example.org/events/unsafe-event-title",
                ["imageUri"] = "s3://unsafe-bucket/object-key",
                ["sourceActorId"] = "018f0000-0000-7000-8000-000000000050",
                ["rawProviderError"] = "provider leaked unsafe content",
                ["moderatorFreeText"] = "unsafe moderator text"
            });

        var result = await _builder.BuildAsync(context, CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(PayloadJson(result)).Contains(moderationRecordId.ToString());
        await Assert.That(PayloadJson(result)).Contains("HeavyRedacted");
        await Assert.That(PayloadJson(result)).DoesNotContain(AggregateId.ToString());
        await Assert.That(PayloadJson(result)).DoesNotContain("Unsafe Event Title");
        await Assert.That(PayloadJson(result)).DoesNotContain("unsafe-event-title");
        await Assert.That(PayloadJson(result)).DoesNotContain("/events/");
        await Assert.That(PayloadJson(result)).DoesNotContain("s3://");
        await Assert.That(PayloadJson(result)).DoesNotContain("provider leaked unsafe content");
        await Assert.That(PayloadJson(result)).DoesNotContain("unsafe moderator text");
        await Assert.That(result.PayloadRetentionUntil).IsEqualTo(OccurredAt.AddDays(1));
    }

    [Test]
    public async Task BuildAsync_WhenRequiredFieldMissing_FailsClosed()
    {
        var context = CreateContext(
            WebhookEventNames.EventPublished,
            new Dictionary<string, object?>
            {
                ["eventId"] = AggregateId.ToString()
            });

        var result = await _builder.BuildAsync(context, CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("missing_required_payload_field");
        await Assert.That(result.PayloadBytes).IsNull();
    }

    [Test]
    public async Task BuildAsync_ForRegistrationCreatedWithoutShareConsent_OmitsAttendeePii()
    {
        var context = CreateContext(
            WebhookEventNames.RegistrationCreated,
            new Dictionary<string, object?>
            {
                ["registrationId"] = AggregateId.ToString(),
                ["eventId"] = AggregateId.ToString(),
                ["status"] = "Approved",
                ["consentToEmailShare"] = false
            });

        var result = await _builder.BuildAsync(context, CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Envelope!.Version).IsEqualTo(2);
        using var parsed = JsonDocument.Parse(result.PayloadBytes!.Value);
        var data = parsed.RootElement.GetProperty("data");
        await Assert.That(data.GetProperty("consentToEmailShare").GetBoolean()).IsFalse();
        await Assert.That(data.TryGetProperty("attendeeEmail", out _)).IsFalse();
    }

    [Test]
    public async Task BuildAsync_ForRegistrationCreatedWithShareConsent_IncludesAttendeePii()
    {
        var context = CreateContext(
            WebhookEventNames.RegistrationCreated,
            new Dictionary<string, object?>
            {
                ["registrationId"] = AggregateId.ToString(),
                ["eventId"] = AggregateId.ToString(),
                ["status"] = "Approved",
                ["consentToEmailShare"] = true,
                ["attendeeEmail"] = "attendee@example.test",
                ["attendeeFirstName"] = "Amina",
                ["attendeeLastName"] = "Rahman"
            });

        var result = await _builder.BuildAsync(context, CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Envelope!.Version).IsEqualTo(2);
        using var parsed = JsonDocument.Parse(result.PayloadBytes!.Value);
        var data = parsed.RootElement.GetProperty("data");
        await Assert.That(data.GetProperty("consentToEmailShare").GetBoolean()).IsTrue();
        await Assert.That(data.GetProperty("attendeeEmail").GetString()).IsEqualTo("attendee@example.test");
        await Assert.That(data.GetProperty("attendeeFirstName").GetString()).IsEqualTo("Amina");
        await Assert.That(data.GetProperty("attendeeLastName").GetString()).IsEqualTo("Rahman");
    }

    [Test]
    public async Task BuildAsync_ForRegistrationCreatedWithoutConsentField_FailsClosed()
    {
        var context = CreateContext(
            WebhookEventNames.RegistrationCreated,
            new Dictionary<string, object?>
            {
                ["registrationId"] = AggregateId.ToString(),
                ["eventId"] = AggregateId.ToString(),
                ["status"] = "Approved"
            });

        var result = await _builder.BuildAsync(context, CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("missing_required_payload_field");
    }

    [Test]
    public async Task BuildAsync_WhenEventTypeUnknown_FailsClosed()
    {
        var context = CreateContext(
            "unknown.event",
            new Dictionary<string, object?>
            {
                ["eventId"] = AggregateId.ToString()
            });

        var result = await _builder.BuildAsync(context, CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCategory).IsEqualTo("unknown_event_type");
        await Assert.That(result.Envelope).IsNull();
    }

    private static WebhookEventBuildContext CreateContext(
        string eventType,
        IReadOnlyDictionary<string, object?> data) =>
        new(
            MessageId,
            TenantId,
            eventType,
            MessageId.ToString(),
            "Event",
            AggregateId,
            OccurredAt,
            data);

    private static string PayloadJson(WebhookPayloadBuildResult result) =>
        Encoding.UTF8.GetString(result.PayloadBytes!.Value.Span);
}
