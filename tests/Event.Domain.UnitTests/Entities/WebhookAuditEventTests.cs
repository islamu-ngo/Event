using Explore.Domain;
using TUnit.Core;

namespace Event.Domain.UnitTests.Entities;

public sealed class WebhookAuditEventTests
{
    [Test]
    public async Task Create_NormalizesClassificationsAndAllowsOnlySafeOperationalMetadata()
    {
        var tenantId = Guid.CreateVersion7();
        var targetId = Guid.CreateVersion7();

        var auditEvent = WebhookAuditEvent.Create(
            tenantId,
            WebhookAuditPrincipalKind.User,
            " user:018f0000-0000-7000-8000-000000000001 ",
            WebhookAuditScopeKind.Tenant,
            tenantId,
            WebhookAuditAction.EndpointUpdated,
            WebhookAuditTargetKind.Endpoint,
            targetId,
            null,
            """{"destinationHost":"integrator.example","payloadHash":"sha256:abc","credentialVersion":2}""",
            " endpoint-v2 ",
            " trace-123 ",
            " Pending_Work_Migrate ",
            WebhookAuditOutcome.Succeeded,
            "retention-v1",
            DomainTestClock.UtcNow.AddDays(365));

        await Assert.That(auditEvent.TenantId).IsEqualTo(tenantId);
        await Assert.That(auditEvent.PrincipalReference)
            .IsEqualTo("user:018f0000-0000-7000-8000-000000000001");
        await Assert.That(auditEvent.Action).IsEqualTo(WebhookAuditAction.EndpointUpdated);
        await Assert.That(auditEvent.TargetKind).IsEqualTo(WebhookAuditTargetKind.Endpoint);
        await Assert.That(auditEvent.TargetId).IsEqualTo(targetId);
        await Assert.That(auditEvent.ConfigurationVersion).IsEqualTo("endpoint-v2");
        await Assert.That(auditEvent.CorrelationId).IsEqualTo("trace-123");
        await Assert.That(auditEvent.ReasonCode).IsEqualTo("pending_work_migrate");
        await Assert.That(auditEvent.SafeAfterJson).Contains("integrator.example", StringComparison.Ordinal);
        await Assert.That(auditEvent.OccurredAt).IsEqualTo(default);
    }

    [Test]
    [Arguments("{\"payload\":{}}")]
    [Arguments("{\"secretRef\":\"binding\"}")]
    [Arguments("{\"svixSignature\":\"v1,value\"}")]
    [Arguments("{\"portalUrl\":\"redacted\"}")]
    [Arguments("{\"rawProviderError\":\"redacted\"}")]
    [Arguments("{\"destination\":\"https://integrator.example/hook\"}")]
    [Arguments("{\"authorization\":\"Bearer credential\"}")]
    [Arguments("{\"signingMaterial\":\"whsec_Y3JlZGVudGlhbA==\"}")]
    public async Task Create_RejectsUnsafeMetadataRecursively(string unsafeJson)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Task.Run(() => Create(unsafeJson)));
    }

    [Test]
    public async Task Create_RejectsCrossTenantEffectiveScope()
    {
        var tenantId = Guid.CreateVersion7();

        await Assert.ThrowsAsync<ArgumentException>(() => Task.Run(() => WebhookAuditEvent.Create(
            tenantId,
            WebhookAuditPrincipalKind.System,
            "system:test",
            WebhookAuditScopeKind.Tenant,
            Guid.CreateVersion7(),
            WebhookAuditAction.RetentionCleanupCompleted,
            WebhookAuditTargetKind.CleanupRun,
            Guid.CreateVersion7(),
            null,
            null,
            "retention-v1",
            null,
            "retention_cleanup",
            WebhookAuditOutcome.Succeeded,
            "retention-v1",
            DomainTestClock.UtcNow.AddDays(365))));
    }

    private static WebhookAuditEvent Create(string safeAfterJson)
    {
        var tenantId = Guid.CreateVersion7();
        return WebhookAuditEvent.Create(
            tenantId,
            WebhookAuditPrincipalKind.System,
            "system:test",
            WebhookAuditScopeKind.Tenant,
            tenantId,
            WebhookAuditAction.EndpointAutoPaused,
            WebhookAuditTargetKind.Endpoint,
            Guid.CreateVersion7(),
            null,
            safeAfterJson,
            "policy-v1",
            null,
            "automatic_circuit_opened",
            WebhookAuditOutcome.Succeeded,
            "retention-v1",
            DomainTestClock.UtcNow.AddDays(365));
    }
}
