using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Webhooks;
using Explore.Domain;

namespace Event.Application.UnitTests.Webhooks;

public sealed class WebhookMessageExactByteAuthorityTests
{
    private static readonly Guid MessageId = Guid.Parse("018f0000-0000-7000-8000-000000000999");
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid AggregateId = Guid.Parse("018f0000-0000-7000-8000-000000000001");
    private static readonly DateTime CreatedAt = new(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task PublicContract_ExposesNoProviderOrJsonPayloadState()
    {
        var type = typeof(WebhookMessage);

        foreach (var removedProperty in new[]
                 {
                     "PayloadJson", "ProviderMode", "ProviderMessageId", "Status", "PublishedAt"
                 })
        {
            await Assert.That(type.GetProperty(removedProperty)).IsNull();
        }

        foreach (var immutableProperty in new[]
                 {
                     nameof(WebhookMessage.Id),
                     nameof(WebhookMessage.EventType),
                     nameof(WebhookMessage.EventId),
                     nameof(WebhookMessage.AggregateKind),
                     nameof(WebhookMessage.AggregateId),
                     nameof(WebhookMessage.PayloadHash),
                     nameof(WebhookMessage.PayloadRetentionUntil)
                 })
        {
            var setter = type.GetProperty(immutableProperty)!.SetMethod;
            await Assert.That(setter is null || !setter.IsPublic).IsTrue();
        }
    }

    [Test]
    public async Task Create_OwnsUuidV7MessageIdentity()
    {
        var create = typeof(WebhookMessage).GetMethods()
            .Single(method => method.Name == nameof(WebhookMessage.Create));

        await Assert.That(create.GetParameters().Select(parameter => parameter.Name))
            .DoesNotContain("id");
        await Assert.That(CreateMessage("{}"u8.ToArray()).Id.Version).IsEqualTo(7);
    }

    [Test]
    public async Task Create_CopiesExactBytesAndComputesSha256Authority()
    {
        var source = Encoding.UTF8.GetBytes("{\"type\":\"event.published\",\"value\":\"سلام\"}\n");
        var expected = source.ToArray();
        var message = CreateMessage(source);

        source[0] = (byte)'[';
        var firstRead = message.GetPayloadBytes();
        firstRead![1] = (byte)'x';
        var secondRead = message.GetPayloadBytes();

        await Assert.That(secondRead).IsEquivalentTo(expected);
        await Assert.That(message.PayloadHash).IsEqualTo(
            $"sha256:{Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant()}");
        await Assert.That(message.PayloadProvenanceId).IsEqualTo((int)WebhookPayloadProvenance.ExactBytes);

        var changed = expected.ToArray();
        changed[^1] = (byte)' ';
        var changedMessage = CreateMessage(changed);
        await Assert.That(changedMessage.PayloadHash).IsNotEqualTo(message.PayloadHash);
    }

    [Test]
    public async Task ClearPayload_RequiresRetentionExpiryAndPreservesHashEvidence()
    {
        var message = CreateMessage(Encoding.UTF8.GetBytes("{\"type\":\"event.published\"}"));
        var payloadHash = message.PayloadHash;

        await Assert.That(() => message.ClearPayload(CreatedAt.AddHours(1)))
            .Throws<InvalidOperationException>();

        var clearedAt = CreatedAt.AddDays(14);
        message.ClearPayload(clearedAt);

        await Assert.That(message.GetPayloadBytes()).IsNull();
        await Assert.That(message.PayloadHash).IsEqualTo(payloadHash);
        await Assert.That(message.PayloadClearedAt).IsEqualTo(clearedAt);
    }

    [Test]
    public async Task PayloadBuilder_HashesTheSameBytesItReturns()
    {
        var builder = new DefaultWebhookPayloadBuilder(new WebhookEventTypeRegistry());
        var result = await builder.BuildAsync(
            new WebhookEventBuildContext(
                MessageId,
                TenantId,
                WebhookEventNames.EventPublished,
                "domain-event-1",
                "Event",
                AggregateId,
                new DateTimeOffset(CreatedAt),
                new Dictionary<string, object?>
                {
                    ["eventId"] = AggregateId.ToString(),
                    ["status"] = "Published",
                    ["publicUrl"] = "https://example.org/events/community-iftar"
                }),
            CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.PayloadBytes).IsNotNull();
        await Assert.That(result.PayloadHash).IsEqualTo(
            $"sha256:{Convert.ToHexString(SHA256.HashData(result.PayloadBytes!.Value.Span)).ToLowerInvariant()}");
    }

    [Test]
    public async Task PayloadBuilder_WhenCancelled_DoesNotSerialize()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var builder = new DefaultWebhookPayloadBuilder(new WebhookEventTypeRegistry());

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await builder.BuildAsync(
                new WebhookEventBuildContext(
                    MessageId,
                    TenantId,
                    WebhookEventNames.EventPublished,
                    "domain-event-cancelled",
                    "Event",
                    AggregateId,
                    new DateTimeOffset(CreatedAt),
                    new Dictionary<string, object?>()),
                cancellation.Token));
    }

    private static WebhookMessage CreateMessage(byte[] payloadBytes) =>
        WebhookMessage.Create(
            TenantId,
            WebhookEventNames.EventPublished,
            "domain-event-1",
            "Event",
            AggregateId,
            consumerId: null,
            payloadBytes,
            "application/json",
            "utf-8",
            CreatedAt,
            CreatedAt.AddDays(14),
            CreatedAt);

}
