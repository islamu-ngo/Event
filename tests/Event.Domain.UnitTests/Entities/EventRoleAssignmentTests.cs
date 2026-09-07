using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public class EventRoleAssignmentTests
{
    [Test]
    public async Task EventRoleAssignment_ImplementsTenantAndAuditInterfacesOnly()
    {
        var interfaces = typeof(EventRoleAssignment).GetInterfaces();

        await Assert.That(interfaces.Contains(typeof(ITenantEntity))).IsTrue();
        await Assert.That(interfaces.Contains(typeof(IAuditableEntity))).IsTrue();
        await Assert.That(interfaces.Contains(typeof(ISoftDeletable))).IsFalse();
        await Assert.That(interfaces.Contains(typeof(IConcurrencyAware))).IsFalse();
    }

    [Test]
    public async Task Create_WithActiveStatus_InitializesVersionAndEffectiveWindow()
    {
        var now = DomainTestClock.UtcNow;
        var assignment = CreateAssignment(EventRoleAssignmentStatus.Active, now.AddMinutes(-1), now.AddMinutes(30));

        await Assert.That(assignment.Version).IsEqualTo(1);
        await Assert.That(assignment.IsEffectiveAt(now)).IsTrue();
    }

    [Test]
    public async Task IsEffectiveAt_WhenExpiredByTimeButStatusActive_ReturnsFalse()
    {
        var now = DomainTestClock.UtcNow;
        var assignment = CreateAssignment(EventRoleAssignmentStatus.Active, now.AddHours(-2), now.AddHours(-1));

        await Assert.That(assignment.Status).IsEqualTo(EventRoleAssignmentStatus.Active);
        await Assert.That(assignment.IsEffectiveAt(now)).IsFalse();
    }

    [Test]
    public async Task Revoke_TransitionsToRevokedAndIncrementsVersion()
    {
        var now = DomainTestClock.UtcNow;
        var actorUserId = Guid.NewGuid();
        var assignment = CreateAssignment(EventRoleAssignmentStatus.Active, now.AddMinutes(-1), null);

        assignment.Revoke(actorUserId, now);

        await Assert.That(assignment.Status).IsEqualTo(EventRoleAssignmentStatus.Revoked);
        await Assert.That(assignment.RevokedAtUtc).IsEqualTo(now);
        await Assert.That(assignment.RevokedByUserId).IsEqualTo(actorUserId);
        await Assert.That(assignment.Version).IsEqualTo(2);
        await Assert.That(assignment.IsEffectiveAt(now.AddSeconds(1))).IsFalse();
    }

    [Test]
    public async Task MarkExpired_WhenExpirationReached_TransitionsToExpiredAndIncrementsVersion()
    {
        var now = DomainTestClock.UtcNow;
        var assignment = CreateAssignment(EventRoleAssignmentStatus.Active, now.AddHours(-2), now.AddHours(-1));

        assignment.MarkExpired(now);

        await Assert.That(assignment.Status).IsEqualTo(EventRoleAssignmentStatus.Expired);
        await Assert.That(assignment.Version).IsEqualTo(2);
        await Assert.That(assignment.IsEffectiveAt(now)).IsFalse();
    }

    [Test]
    public async Task UpdateValidityWindow_WhenTerminal_Throws()
    {
        var now = DomainTestClock.UtcNow;
        var assignment = CreateAssignment(EventRoleAssignmentStatus.Active, now.AddMinutes(-1), null);
        assignment.Revoke(Guid.NewGuid(), now);

        await Assert.That(() => assignment.UpdateValidityWindow(now, now.AddHours(1), now))
            .Throws<InvalidOperationException>();
    }

    private static EventRoleAssignment CreateAssignment(
        EventRoleAssignmentStatus status,
        DateTime startsAtUtc,
        DateTime? expiresAtUtc)
    {
        return EventRoleAssignment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            (int)RoleEnum.EventOwner,
            status,
            startsAtUtc,
            expiresAtUtc,
            Guid.NewGuid());
    }
}
