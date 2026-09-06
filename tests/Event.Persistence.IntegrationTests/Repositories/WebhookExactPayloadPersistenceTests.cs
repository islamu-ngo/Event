// ABOUTME: Persistence-contract tests for authoritative inbound and outbound webhook payload bytes.
// ABOUTME: Verifies bytea mapping, byte/hash identity, immutable evidence metadata, and honest legacy provenance.

using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class WebhookExactPayloadPersistenceTests
{
    [Test]
    public async Task DomainPayloadEvidence_PreservesExactBytesAndDetectsOneByteMutation()
    {
        var exactBytes = Encoding.UTF8.GetBytes("{ \"b\":2, \"a\": 1 }\n");
        var mutatedBytes = exactBytes.ToArray();
        mutatedBytes[^2] = (byte)'2';
        var now = new DateTime(2026, 7, 13, 19, 0, 0, DateTimeKind.Utc);

        var outbound = WebhookMessage.Create(
            Guid.CreateVersion7(),
            "event.updated",
            "evt-exact",
            "event",
            Guid.CreateVersion7(),
            null,
            exactBytes,
            "application/json",
            "utf-8",
            now.AddMinutes(-1),
            now.AddDays(14),
            now);
        var inbound = IncomingWebhookMessage.CreateVerified(
            outbound.TenantId,
            "svix",
            "msg-exact",
            "idem-exact",
            "event.updated",
            exactBytes,
            Sha256(exactBytes),
            "application/json",
            "utf-8",
            null,
            now,
            now,
            now.AddDays(14),
            "webhook-retention-test-v1",
            now.AddDays(30),
            now.AddDays(90),
            now.AddDays(14),
            now.AddDays(30));

        await Assert.That(outbound.GetPayloadBytes()).IsEquivalentTo(exactBytes);
        await Assert.That(inbound.PayloadBytes.ToArray()).IsEquivalentTo(exactBytes);
        await Assert.That(outbound.PayloadByteLength).IsEqualTo(exactBytes.LongLength);
        await Assert.That(inbound.PayloadByteLength).IsEqualTo(exactBytes.LongLength);
        await Assert.That(outbound.PayloadHash).IsEqualTo(Sha256(exactBytes));
        await Assert.That(inbound.PayloadHash).IsEqualTo(Sha256(exactBytes));
        await Assert.That(Sha256(mutatedBytes)).IsNotEqualTo(outbound.PayloadHash);
        await Assert.That(outbound.PayloadProvenanceId).IsEqualTo((int)WebhookPayloadProvenance.ExactBytes);
        await Assert.That(inbound.PayloadProvenanceId).IsEqualTo((int)WebhookPayloadProvenance.ExactBytes);
    }

    [Test]
    public async Task EfModel_MapsBothPayloadAuthoritiesToPostgresByteaWithEvidenceFields()
    {
        await using var context = CreateModelContext();

        foreach (var entityType in new[] { typeof(WebhookMessage), typeof(IncomingWebhookMessage) })
        {
            var entity = context.Model.FindEntityType(entityType)!;
            await Assert.That(entity.FindProperty("_payloadBytes")!.GetColumnType()).IsEqualTo("bytea");
            await Assert.That(entity.FindProperty(nameof(WebhookMessage.PayloadByteLength))!.ClrType).IsEqualTo(typeof(long));
            await Assert.That(entity.FindProperty(nameof(WebhookMessage.PayloadProvenanceId))!.IsNullable).IsFalse();
            await Assert.That(entity.FindProperty(nameof(WebhookMessage.ContentType))!.GetMaxLength()).IsEqualTo(200);
            await Assert.That(entity.FindProperty(nameof(WebhookMessage.ContentEncoding))!.GetMaxLength()).IsEqualTo(50);
        }
    }

    [Test]
    public async Task CurrentBaseline_RequiresHonestPayloadEvidenceFields()
    {
        await using var context = CreateModelContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var outbound = model.FindEntityType(typeof(WebhookMessage))!;
        var inbound = model.FindEntityType(typeof(IncomingWebhookMessage))!;

        await Assert.That(outbound.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_webhook_messages_payload_provenance");
        await Assert.That(outbound.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_webhook_messages_payload_byte_length");
        await Assert.That(inbound.GetCheckConstraints().Select(constraint => constraint.Name))
            .Contains("ck_incoming_webhook_messages_payload_byte_length");
    }

    private static ExploreDbContext CreateModelContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=webhook_model;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
}
