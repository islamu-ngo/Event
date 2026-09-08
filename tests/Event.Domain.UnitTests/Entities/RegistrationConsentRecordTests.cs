using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationConsentRecordTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task GrantSnapshotsPinnedEvidenceAndWithdrawalIsOneWay()
    {
        ConsentScope scope = CreateScope();

        RegistrationConsentRecord record = RegistrationConsentRecord.Grant(scope.Submission, scope.Requirement,
            scope.Version, scope.ConsentField, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, scope.OrderId,
            null, UtcNow.AddMinutes(2));
        record.Withdraw(UtcNow.AddMinutes(3));

        await Assert.That(record.PurposeCode).IsEqualTo("EVENT_UPDATES");
        await Assert.That(record.ConsentTextSnapshot).IsEqualTo("I agree to receive event updates by email.");
        await Assert.That(record.ConsentTextVersion).IsEqualTo("2026-08");
        await Assert.That(record.RegistrationFormVersion).IsEqualTo(1);
        await Assert.That(record.LanguageTag).IsEqualTo("en");
        await Assert.That(record.OrderSubjectId).IsEqualTo(scope.OrderId);
        await Assert.That(record.WithdrawnAt).IsEqualTo(UtcNow.AddMinutes(3));
        await Assert.That(() => record.Withdraw(UtcNow.AddMinutes(4))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task GrantRejectsNonConsentField()
    {
        ConsentScope scope = CreateScope();

        await Assert.That(() => RegistrationConsentRecord.Grant(scope.Submission, scope.Requirement, scope.Version,
            scope.NonConsentField, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, scope.OrderId, null,
            UtcNow.AddMinutes(2))).Throws<ArgumentException>();
    }

    private static ConsentScope CreateScope()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenantId, eventId, "ATTENDEE_REGISTRATION", UtcNow);
        RegistrationRequirement requirement = RegistrationRequirement.Create(workflow, 1,
            RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration, RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, UtcNow);
        RegistrationChannel channel = RegistrationChannel.Create(requirement, 1, true, null, UtcNow);
        requirement.AddChannel(channel);
        workflow.AddRequirement(requirement);
        RegistrationForm form = RegistrationForm.Create(tenantId, eventId, "platform.registration", "native", "Native", UtcNow);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, UtcNow);
        RegistrationFormSection section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", UtcNow);
        RegistrationFormField consent = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1, "registration",
            "marketing_consent", "Send me event updates", RegistrationFieldTypeEnum.Consent, 1,
            RegistrationOrganizerVisibilityEnum.Hidden, true, false, UtcNow, "EVENT_UPDATES", "2026-08",
            "I agree to receive event updates by email.");
        RegistrationFormField nonConsent = RegistrationFormField.Create(Guid.CreateVersion7(), section, 2, "registration",
            "name", "Name", RegistrationFieldTypeEnum.ShortText, 1,
            RegistrationOrganizerVisibilityEnum.Hidden, false, false, UtcNow);
        version.AddSection(section);
        version.AddField(section, consent);
        version.AddField(section, nonConsent);
        form.AddVersion(version);
        RegistrationAttempt attempt = RegistrationAttempt.Create(tenantId, eventId, orderId, workflow.Id, requirement.Id,
            channel.Id, form.Id, version.Id, CapabilityTokenHash.Create(Hash("capability")), null, null, UtcNow,
            UtcNow.AddMinutes(10));
        RegistrationSubmission submission = RegistrationSubmission.CreateNativeEvidenceOnly(attempt,
            RegistrationEvidenceHash.Create(Hash("evidence")), UtcNow.AddMinutes(1), null);
        return new(orderId, requirement, version, consent, nonConsent, submission);
    }

    private static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record ConsentScope(Guid OrderId, RegistrationRequirement Requirement, RegistrationFormVersion Version,
        RegistrationFormField ConsentField, RegistrationFormField NonConsentField, RegistrationSubmission Submission);
}
