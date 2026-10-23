using Explore.Domain.Enums;

namespace Event.Domain.UnitTests;

public sealed class LocationPrivacyCorrectionReceiptTests
{
    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task GovernanceCorrectionRetainsItsAuditAndIdempotentOriginalMessage()
    {
        var audit = Audit(EventLocationDisclosureAuditReasonEnum.GovernanceTightening);
        Guid message = Guid.CreateVersion7(), eventId = Guid.CreateVersion7();
        audit.BindCorrection(message);
        audit.BindCorrection(message);
        var receipt = LocationPrivacyCorrectionReceipt.ForGovernance(message, eventId, audit, Now);
        await Assert.That(receipt.Id).IsEqualTo(message);
        await Assert.That(receipt.TenantId).IsEqualTo(audit.TenantId);
        await Assert.That(receipt.EventId).IsEqualTo(eventId);
        await Assert.That(receipt.EventLocationId).IsEqualTo(audit.EventLocationId);
        await Assert.That(receipt.DisclosureAuditId).IsEqualTo(audit.Id);
        await Assert.That(receipt.PolicyVersion).IsEqualTo(audit.NewPolicyVersion);
        await Assert.That(receipt.CreatedAtUtc).IsEqualTo(Now);
        await Assert.That(receipt.ErasureIntentId).IsNull();
        await Assert.That(() => audit.BindCorrection(Guid.CreateVersion7())).Throws<InvalidOperationException>();
        await Assert.That(audit.CorrectionMessageId).IsEqualTo(message);
        await Assert.That(() => ((ITenantEntity)receipt).TenantId = Guid.CreateVersion7())
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("unbound")]
    [Arguments("message")]
    [Arguments("time")]
    public async Task GovernanceReceiptRequiresTheOriginalAuditPair(string mismatch)
    {
        var audit = Audit(EventLocationDisclosureAuditReasonEnum.GovernanceTightening);
        Guid message = Guid.CreateVersion7();
        if (mismatch != "unbound")
            audit.BindCorrection(message);
        await Assert.That(() => LocationPrivacyCorrectionReceipt.ForGovernance(
            mismatch == "message" ? Guid.CreateVersion7() : message, Guid.CreateVersion7(), audit,
            mismatch == "time" ? Now.AddTicks(1) : Now)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("empty")]
    [Arguments("version")]
    [Arguments("reason")]
    public async Task InvalidCorrectionBindingDoesNotChangeTheAudit(string invalid)
    {
        var audit = Audit(invalid == "reason"
            ? EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange
            : EventLocationDisclosureAuditReasonEnum.GovernanceTightening);
        Guid message = invalid == "empty" ? Guid.Empty : invalid == "version" ? Guid.NewGuid() : Guid.CreateVersion7();
        await Assert.That(() => audit.BindCorrection(message)).Throws<InvalidOperationException>();
        await Assert.That(audit.CorrectionMessageId).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ErasureReceiptRetainsCommittedAuthorityAndRequiresItsLocation(bool missingLocation)
    {
        var intent = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 7, PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(), PrivacyErasureReasonCode.SubjectErasureRequest, 1, Now, Now);
        Guid message = Guid.CreateVersion7(), tenant = Guid.CreateVersion7(), eventId = Guid.CreateVersion7();
        Guid placement = Guid.CreateVersion7(), location = Guid.CreateVersion7();
        if (missingLocation)
        {
            await Assert.That(() => LocationPrivacyCorrectionReceipt.ForErasure(
                message, intent, tenant, eventId, placement, null, 2, Now)).Throws<InvalidOperationException>();
            return;
        }
        var receipt = LocationPrivacyCorrectionReceipt.ForErasure(
            message, intent, tenant, eventId, placement, location, 2, Now);
        await Assert.That(receipt.ErasureIntentId).IsEqualTo(intent.IntentId);
        await Assert.That(receipt.AuthoritySequence).IsEqualTo(7);
        await Assert.That(receipt.LocationId).IsEqualTo(location);
        await Assert.That(receipt.DisclosureAuditId).IsNull();
        await Assert.That(() => LocationPrivacyCorrectionReceipt.ForErasure(
            message, intent, tenant, eventId, placement, location, 2, Now.AddTicks(-1)))
            .Throws<InvalidOperationException>();
    }

    private static EventLocationDisclosureAudit Audit(EventLocationDisclosureAuditReasonEnum reason) =>
        EventLocationDisclosureAudit.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            EventLocationDisclosureFields.Country, EventLocationDisclosureFields.City,
            LocationDisclosureAudienceEnum.Never, LocationDisclosureAudienceEnum.Never,
            null, null, 1, 2, reason, Now);
}
