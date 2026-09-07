using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public class SupportAccessSessionTests
{
    [Test]
    public async Task Start_CreatesActiveActorBoundTenantScopedSession()
    {
        var actorUserId = Guid.NewGuid();
        var targetTenantId = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero);
        var expiresAt = startedAt.AddMinutes(30);

        var session = SupportAccessSession.Start(
            actorUserId,
            targetTenantId,
            SupportAccessModeEnum.ReadOnly,
            " support_case ",
            " Investigating event visibility ",
            " TICKET-123 ",
            startedAt,
            expiresAt);

        await Assert.That(session.ActorUserId).IsEqualTo(actorUserId);
        await Assert.That(session.TargetTenantId).IsEqualTo(targetTenantId);
        await Assert.That(session.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
        await Assert.That(session.ModeId).IsEqualTo((int)SupportAccessModeEnum.ReadOnly);
        await Assert.That(session.ReasonCode).IsEqualTo("support_case");
        await Assert.That(session.ReasonText).IsEqualTo("Investigating event visibility");
        await Assert.That(session.TicketReference).IsEqualTo("TICKET-123");
        await Assert.That(session.IsActiveAt(startedAt.AddMinutes(1))).IsTrue();
        await Assert.That(session.AllowsWrites).IsFalse();
        await Assert.That(typeof(SupportAccessSession).GetInterfaces().Contains(typeof(ITenantEntity))).IsFalse();
    }

    [Test]
    public async Task Stop_WhenActive_MakesSessionTerminal()
    {
        var startedAt = DomainTestClock.UtcNowOffset;
        var session = CreateSession(startedAt);
        var stoppedAt = startedAt.AddMinutes(5);

        session.Stop(stoppedAt, "resolved");

        await Assert.That(session.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Stopped);
        await Assert.That(session.EndReasonId).IsEqualTo((int)SupportAccessEndReasonEnum.UserStopped);
        await Assert.That(session.EndedAtUtc).IsEqualTo(stoppedAt);
        await Assert.That(session.EndReasonText).IsEqualTo("resolved");
        await Assert.That(session.IsActiveAt(stoppedAt.AddSeconds(1))).IsFalse();
    }

    [Test]
    public async Task Expire_AfterStop_ThrowsInvalidOperationException()
    {
        var startedAt = DomainTestClock.UtcNowOffset;
        var session = CreateSession(startedAt);
        session.Stop(startedAt.AddMinutes(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            session.Expire(startedAt.AddMinutes(2));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task Revoke_WithPolicyReason_MakesSessionRevoked()
    {
        var startedAt = DomainTestClock.UtcNowOffset;
        var session = CreateSession(startedAt);

        session.Revoke(startedAt.AddMinutes(2), SupportAccessEndReasonEnum.RevokedByPolicy, "kill switch");

        await Assert.That(session.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Revoked);
        await Assert.That(session.EndReasonId).IsEqualTo((int)SupportAccessEndReasonEnum.RevokedByPolicy);
        await Assert.That(session.EndReasonText).IsEqualTo("kill switch");
    }

    [Test]
    public async Task Start_WhenExpiryIsNotAfterStart_ThrowsArgumentException()
    {
        var startedAt = DomainTestClock.UtcNowOffset;

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            _ = SupportAccessSession.Start(
                Guid.NewGuid(),
                Guid.NewGuid(),
                SupportAccessModeEnum.ReadOnly,
                "support_case",
                "reason",
                "ticket",
                startedAt,
                startedAt);

            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task CreateAuditEvent_StoresBoundedJsonMetadata()
    {
        var session = CreateSession(DomainTestClock.UtcNowOffset);

        var auditEvent = SupportAccessAuditEvent.Create(
            session.Id,
            SupportAccessAuditEventTypeEnum.RequestObserved,
            session.ActorUserId ?? throw new InvalidOperationException(),
            session.TargetTenantId,
            "allowed",
            DomainTestClock.UtcNowOffset,
            routeName: "TenantSettings_Get",
            action: "view",
            httpStatusCode: 200,
            sanitizedMetadataJson: "{\"resource\":\"tenant_settings\"}");

        await Assert.That(auditEvent.SupportAccessSessionId).IsEqualTo(session.Id);
        await Assert.That(auditEvent.ActorUserId).IsEqualTo(session.ActorUserId);
        await Assert.That(auditEvent.TargetTenantId).IsEqualTo(session.TargetTenantId);
        await Assert.That(auditEvent.Outcome).IsEqualTo("allowed");
        await Assert.That(auditEvent.SanitizedMetadataJson).IsEqualTo("{\"resource\":\"tenant_settings\"}");
    }

    [Test]
    public async Task CreateAuditEvent_WithInvalidJsonMetadata_ThrowsArgumentException()
    {
        var session = CreateSession(DomainTestClock.UtcNowOffset);

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            _ = SupportAccessAuditEvent.Create(
                session.Id,
                SupportAccessAuditEventTypeEnum.Denied,
                session.ActorUserId ?? throw new InvalidOperationException(),
                session.TargetTenantId,
                "denied",
                DomainTestClock.UtcNowOffset,
                sanitizedMetadataJson: "{not-json");

            return Task.CompletedTask;
        });
    }

    private static SupportAccessSession CreateSession(DateTimeOffset startedAt)
    {
        return SupportAccessSession.Start(
            Guid.NewGuid(),
            Guid.NewGuid(),
            SupportAccessModeEnum.Write,
            "support_case",
            "Investigating a tenant support ticket",
            "TICKET-123",
            startedAt,
            startedAt.AddMinutes(10));
    }
}
