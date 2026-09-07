using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Domain.UnitTests.Entities;

public class EventContactShareConsentTests
{
    [Test]
    public async Task EventContactShareConsent_ImplementsTenantEntityInterface_ExpectedBehavior()
    {
        await Assert.That(typeof(EventContactShareConsent).GetInterfaces().Contains(typeof(ITenantEntity))).IsTrue();
    }

    [Test]
    public async Task EventContactShareConsent_ImplementsAuditableEntityInterface_ExpectedBehavior()
    {
        await Assert.That(typeof(EventContactShareConsent).GetInterfaces().Contains(typeof(IAuditableEntity))).IsTrue();
    }

    [Test]
    public async Task EventContactShareConsent_DoesNotImplementSoftDeletable()
    {
        // Consent records must not be soft-deleted — withdrawal is tracked via ConsentStatus
        await Assert.That(typeof(EventContactShareConsent).GetInterfaces().Contains(typeof(ISoftDeletable))).IsFalse();
    }

    [Test]
    public async Task Grant_CreatesTypedUserSubjectAndNormalizedScope()
    {
        Guid userId = Guid.CreateVersion7();
        EventContactShareConsent consent = CreateConsent(ContactShareConsentSubjectTypeEnum.User, userId);

        await Assert.That(consent.SubjectTypeId).IsEqualTo((int)ContactShareConsentSubjectTypeEnum.User);
        await Assert.That(consent.SubjectId).IsEqualTo(userId);
        await Assert.That(consent.UserSubjectId).IsEqualTo(userId);
        await Assert.That(consent.RegistrationPurchaserOrderId).IsNull();
        await Assert.That(consent.PurposeCode).IsEqualTo("TEST");
        await Assert.That(consent.EmailNormalizedSnapshot).IsEqualTo("test@example.com");
    }

    [Test]
    public async Task Grant_RepresentsAllFourSubjectKinds()
    {
        foreach (ContactShareConsentSubjectTypeEnum subjectType in Enum.GetValues<ContactShareConsentSubjectTypeEnum>())
        {
            EventContactShareConsent consent = CreateConsent(subjectType, Guid.CreateVersion7());
            await Assert.That(consent.SubjectTypeId).IsEqualTo((int)subjectType);
        }
    }

    [Test]
    public async Task WithdrawAndRegrant_TransitionCurrentState()
    {
        EventContactShareConsent consent = CreateConsent(ContactShareConsentSubjectTypeEnum.User, Guid.CreateVersion7());
        DateTime withdrawnAt = DomainTestClock.UtcNow;

        EventContactShareConsentHistory withdrawal = consent.Withdraw(null, consent.SubjectId, withdrawnAt);
        await Assert.That(consent.Status).IsEqualTo(ConsentStatus.Withdrawn);
        await Assert.That(withdrawal.StatusSnapshot).IsEqualTo(ConsentStatus.Withdrawn);

        EventContactShareConsentHistory regrant = consent.Regrant(
            "new@example.com", "Updated consent", "v2", null, null, null, consent.SubjectId,
            withdrawnAt.AddMinutes(1));
        await Assert.That(consent.Status).IsEqualTo(ConsentStatus.Granted);
        await Assert.That(consent.WithdrawnAt).IsNull();
        await Assert.That(regrant.StatusSnapshot).IsEqualTo(ConsentStatus.Granted);
    }

    private static EventContactShareConsent CreateConsent(ContactShareConsentSubjectTypeEnum subjectType, Guid subjectId) =>
        EventContactShareConsent.Grant(Guid.CreateVersion7(), subjectType, subjectId, Guid.CreateVersion7(),
            " test ", " Test@Example.com ", "Test consent", "v1", DomainTestClock.UtcNow);
}
