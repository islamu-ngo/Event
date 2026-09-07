using Explore.Domain;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Domain.UnitTests.Entities;

public sealed class PaidCheckoutSaleControlTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid EventId = Guid.CreateVersion7();
    private static readonly Guid Stopper = Guid.CreateVersion7();
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task StopIsImmediateAndEveryTransitionAppendsAudit()
    {
        PaidCheckoutSaleControl control = PaidCheckoutSaleControl.CreateActive(TenantId, EventId, Stopper, Now);

        bool changed = control.Stop(Stopper, "provider incident", Now.AddMinutes(1));

        await Assert.That(changed).IsTrue();
        await Assert.That(control.IsStopped).IsTrue();
        await Assert.That(control.AuditTrail.Select(entry => entry.ActionCode)).IsEquivalentTo(["activated", "stopped"]);
        await Assert.That(control.AuditTrail.Last().ReasonCode).IsEqualTo("provider_incident");
    }

    [Test]
    public async Task ResumeRequiresDifferentReviewerAndRejectedReviewKeepsStop()
    {
        Guid requester = Guid.CreateVersion7();
        Guid reviewer = Guid.CreateVersion7();
        PaidCheckoutSaleControl control = PaidCheckoutSaleControl.CreateStopped(TenantId, EventId, Stopper, "operator_hold", Now);
        control.RequestResume(requester, "incident_resolved", Now.AddMinutes(1));

        await Assert.That(() => control.ReviewResume(requester, true, "reviewed", Now.AddMinutes(2))).Throws<InvalidOperationException>();
        control.ReviewResume(reviewer, false, "evidence_incomplete", Now.AddMinutes(2));
        await Assert.That(control.IsStopped).IsTrue();
        control.RequestResume(requester, "evidence_added", Now.AddMinutes(3));
        control.ReviewResume(reviewer, true, "independent_approval", Now.AddMinutes(4));

        await Assert.That(control.IsStopped).IsFalse();
        await Assert.That(control.ResumeReviewedBy).IsEqualTo(reviewer);
        await Assert.That(control.AuditTrail.Count).IsEqualTo(5);
    }

    [Test]
    public async Task TenantAndEventLineageAreRequired()
    {
        await Assert.That(() => PaidCheckoutSaleControl.CreateActive(Guid.Empty, EventId, Stopper, Now)).Throws<ArgumentException>();
        await Assert.That(() => PaidCheckoutSaleControl.CreateActive(TenantId, null, Guid.Empty, Now)).Throws<ArgumentException>();
    }
}
