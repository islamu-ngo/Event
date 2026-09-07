using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class WebhookRetentionCleanupRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CleanupTenantAsync_DryRunReportsEligibleRowsWithoutMutationAndHonorsHolds()
    {
        var utcNow = new DateTime(2026, 7, 14, 13, 0, 0, DateTimeKind.Utc);
        var seeded = await ResetAndSeedAsync(utcNow, includeSecondTenant: false);

        await using (var cleanupContext = fixture.CreateDbContext())
        {
            var result = await new WebhookRetentionCleanupRepository(cleanupContext).CleanupTenantAsync(
                seeded.TenantAId,
                utcNow,
                batchSize: 100,
                dryRun: true,
                CancellationToken.None);

            await Assert.That(result.DryRun).IsTrue();
            await Assert.That(result.OutboundPayloadsCleared).IsEqualTo(1);
            await Assert.That(result.InboundPayloadsCleared).IsEqualTo(1);
            await Assert.That(result.AdministrativeAuditsDeleted).IsEqualTo(1);
            await Assert.That(result.DeliveryAttemptsDeleted).IsEqualTo(0);
            await Assert.That(result.IncomingAttemptsDeleted).IsEqualTo(0);
        }

        await using var verificationContext = fixture.CreateDbContext();
        var outgoing = await LoadOutgoingAsync(verificationContext, seeded.EligibleOutgoingId);
        var incoming = await LoadIncomingAsync(verificationContext, seeded.EligibleIncomingId);
        await Assert.That(outgoing.GetPayloadBytes()).IsNotNull();
        await Assert.That(incoming.PayloadBytes.IsEmpty).IsFalse();
        await Assert.That(await CountAuditAsync(verificationContext, seeded.ExpiredAuditId)).IsEqualTo(1);
    }

    [Test]
    public async Task CleanupTenantAsync_ClearsOnlyEligibleTenantPayloadsAndPreservesMinimumEvidence()
    {
        var utcNow = new DateTime(2026, 7, 14, 13, 0, 0, DateTimeKind.Utc);
        var seeded = await ResetAndSeedAsync(utcNow, includeSecondTenant: true);

        await using (var cleanupContext = fixture.CreateDbContext())
        {
            var result = await new WebhookRetentionCleanupRepository(cleanupContext).CleanupTenantAsync(
                seeded.TenantAId,
                utcNow,
                batchSize: 100,
                dryRun: false,
                CancellationToken.None);

            await Assert.That(result.DryRun).IsFalse();
            await Assert.That(result.OutboundPayloadsCleared).IsEqualTo(1);
            await Assert.That(result.InboundPayloadsCleared).IsEqualTo(1);
            await Assert.That(result.AdministrativeAuditsDeleted).IsEqualTo(1);
        }

        await using var verificationContext = fixture.CreateDbContext();
        var outgoing = await LoadOutgoingAsync(verificationContext, seeded.EligibleOutgoingId);
        var heldOutgoing = await LoadOutgoingAsync(verificationContext, seeded.HeldOutgoingId);
        var otherTenantOutgoing = await LoadOutgoingAsync(verificationContext, seeded.OtherTenantOutgoingId!.Value);
        var incoming = await LoadIncomingAsync(verificationContext, seeded.EligibleIncomingId);
        var heldIncoming = await LoadIncomingAsync(verificationContext, seeded.HeldIncomingId);

        await Assert.That(outgoing.GetPayloadBytes()).IsNull();
        await Assert.That(outgoing.PayloadClearedAt).IsEqualTo(utcNow);
        await Assert.That(outgoing.PayloadHash).IsEqualTo(seeded.EligibleOutgoingHash);
        await Assert.That(outgoing.PayloadByteLength).IsGreaterThan(0);
        await Assert.That(heldOutgoing.GetPayloadBytes()).IsNotNull();
        await Assert.That(otherTenantOutgoing.GetPayloadBytes()).IsNotNull();

        await Assert.That(incoming.PayloadBytes.IsEmpty).IsTrue();
        await Assert.That(incoming.PayloadClearedAt).IsEqualTo(utcNow);
        await Assert.That(incoming.PayloadHash).IsEqualTo(seeded.EligibleIncomingHash);
        await Assert.That(incoming.Status).IsEqualTo(IncomingWebhookMessageStatus.Ignored);
        await Assert.That(heldIncoming.PayloadBytes.IsEmpty).IsFalse();
        await Assert.That(await CountAuditAsync(verificationContext, seeded.ExpiredAuditId)).IsEqualTo(0);
        await Assert.That(await CountAuditAsync(verificationContext, seeded.HeldAuditId)).IsEqualTo(1);
    }

    private async Task<SeededRetentionEvidence> ResetAndSeedAsync(DateTime utcNow, bool includeSecondTenant)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        await LookupTableSeeder.SeedAsync(context);
        var tenantA = CreateTenant("retention-a");
        var tenantB = CreateTenant("retention-b");
        context.Tenants.Add(tenantA);
        if (includeSecondTenant)
        {
            context.Tenants.Add(tenantB);
        }

        await context.SaveChangesAsync();

        var eligibleOutgoing = CreateOutgoing(tenantA.Id, "eligible-outgoing", utcNow.AddDays(-20));
        var heldOutgoing = CreateOutgoing(tenantA.Id, "held-outgoing", utcNow.AddDays(-20));
        var eligibleIncoming = CreateIgnoredIncoming(tenantA.Id, "eligible-incoming", utcNow.AddDays(-20));
        var heldIncoming = CreateIgnoredIncoming(tenantA.Id, "held-incoming", utcNow.AddDays(-20));
        var activeIncoming = CreateIncoming(tenantA.Id, "active-incoming", utcNow.AddDays(-20));
        var expiredAudit = CreateAudit(tenantA.Id, utcNow.AddDays(-1));
        var heldAudit = CreateAudit(tenantA.Id, utcNow.AddDays(-1));
        var otherTenantOutgoing = includeSecondTenant
            ? CreateOutgoing(tenantB.Id, "other-tenant-outgoing", utcNow.AddDays(-20))
            : null;

        context.AddRange(eligibleOutgoing, heldOutgoing, eligibleIncoming, heldIncoming, activeIncoming, expiredAudit, heldAudit);
        if (otherTenantOutgoing is not null)
        {
            context.Add(otherTenantOutgoing);
        }

        context.WebhookRetentionHolds.AddRange(
            WebhookRetentionHold.Create(
                tenantA.Id,
                WebhookRetentionSubjectKind.OutgoingMessage,
                heldOutgoing.Id,
                "legal_hold",
                utcNow.AddDays(-1)),
            WebhookRetentionHold.Create(
                tenantA.Id,
                WebhookRetentionSubjectKind.IncomingMessage,
                heldIncoming.Id,
                "legal_hold",
                utcNow.AddDays(-1)),
            WebhookRetentionHold.Create(
                tenantA.Id,
                WebhookRetentionSubjectKind.AdministrativeAudit,
                heldAudit.Id,
                "legal_hold",
                utcNow.AddDays(-1)));
        await context.SaveChangesAsync();

        return new SeededRetentionEvidence(
            tenantA.Id,
            eligibleOutgoing.Id,
            eligibleOutgoing.PayloadHash,
            heldOutgoing.Id,
            eligibleIncoming.Id,
            eligibleIncoming.PayloadHash,
            heldIncoming.Id,
            expiredAudit.Id,
            heldAudit.Id,
            otherTenantOutgoing?.Id);
    }

    private static Tenant CreateTenant(string slugPrefix) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Webhook Retention {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };

    private static WebhookMessage CreateOutgoing(Guid tenantId, string identity, DateTime materializedAt) =>
        WebhookMessage.Create(
            tenantId,
            "retention.test",
            identity,
            "retention-test",
            Guid.CreateVersion7(),
            null,
            System.Text.Encoding.UTF8.GetBytes($"{{\"identity\":\"{identity}\"}}"),
            "application/json",
            "utf-8",
            materializedAt,
            materializedAt.AddDays(14),
            materializedAt);

    private static IncomingWebhookMessage CreateIgnoredIncoming(
        Guid tenantId,
        string identity,
        DateTime verifiedAt)
    {
        var message = CreateIncoming(tenantId, identity, verifiedAt);
        var leaseToken = Guid.CreateVersion7();
        message.Claim("retention-test-worker", leaseToken, verifiedAt.AddMinutes(5), verifiedAt.AddSeconds(1));
        message.Ignore(
            leaseToken,
            message.ProcessingFence,
            message.ProcessingGeneration,
            "retention_test_ignored",
            null,
            verifiedAt.AddSeconds(2));
        return message;
    }

    private static IncomingWebhookMessage CreateIncoming(Guid tenantId, string identity, DateTime verifiedAt)
    {
        var payload = System.Text.Encoding.UTF8.GetBytes($"{{\"identity\":\"{identity}\"}}");
        var payloadHash = "sha256:" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant();
        return IncomingWebhookMessage.CreateVerified(
            tenantId,
            "retention-test",
            identity,
            identity,
            "retention.test",
            payload,
            payloadHash,
            "application/json",
            "utf-8",
            null,
            verifiedAt,
            verifiedAt,
            verifiedAt.AddDays(14),
            "webhook-retention-test-v1",
            verifiedAt.AddDays(30),
            verifiedAt.AddDays(90),
            verifiedAt.AddDays(14),
            verifiedAt.AddDays(30));
    }

    private static WebhookAuditEvent CreateAudit(Guid tenantId, DateTime retentionUntil) =>
        WebhookAuditEvent.Create(
            tenantId,
            WebhookAuditPrincipalKind.System,
            "system:retention-test",
            WebhookAuditScopeKind.Tenant,
            tenantId,
            WebhookAuditAction.EndpointUpdated,
            WebhookAuditTargetKind.Endpoint,
            Guid.CreateVersion7(),
            null,
            null,
            "retention-test-v1",
            null,
            "retention_test",
            WebhookAuditOutcome.Succeeded,
            "webhook-retention-test-v1",
            retentionUntil);

    private static Task<WebhookMessage> LoadOutgoingAsync(Explore.Persistence.ExploreDbContext context, Guid id) =>
        context.WebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .SingleAsync(message => message.Id == id);

    private static Task<IncomingWebhookMessage> LoadIncomingAsync(
        Explore.Persistence.ExploreDbContext context,
        Guid id) =>
        context.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .SingleAsync(message => message.Id == id);

    private static Task<int> CountAuditAsync(Explore.Persistence.ExploreDbContext context, Guid id) =>
        context.WebhookAuditEvents
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .CountAsync(audit => audit.Id == id);

    private sealed record SeededRetentionEvidence(
        Guid TenantAId,
        Guid EligibleOutgoingId,
        string EligibleOutgoingHash,
        Guid HeldOutgoingId,
        Guid EligibleIncomingId,
        string EligibleIncomingHash,
        Guid HeldIncomingId,
        Guid ExpiredAuditId,
        Guid HeldAuditId,
        Guid? OtherTenantOutgoingId);
}
