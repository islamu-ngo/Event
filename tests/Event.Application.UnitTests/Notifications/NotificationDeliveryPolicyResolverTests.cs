using Explore.Application.Notifications;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Notifications;

public sealed class NotificationDeliveryPolicyResolverTests
{
    [Test]
    public async Task ResolveRecognizesCanonicalVersionOnePolicies()
    {
        var resolver = new NotificationDeliveryPolicyResolver();

        var cases = new[]
        {
            (NotificationDeliveryPolicyEnum.RegistrationStatusOptional, NotificationDeliveryPolicyCodes.RegistrationStatusOptional, true, EmailDispatchConsentRequirement.None, false),
            (NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional, NotificationDeliveryPolicyCodes.CriticalEventUpdateOptional, true, EmailDispatchConsentRequirement.None, false),
            (NotificationDeliveryPolicyEnum.ReportCaseUpdate, NotificationDeliveryPolicyCodes.ReportCaseUpdate, true, EmailDispatchConsentRequirement.ReportCaseUpdates, false),
            (NotificationDeliveryPolicyEnum.ReportFollowUpContact, NotificationDeliveryPolicyCodes.ReportFollowUpContact, true, EmailDispatchConsentRequirement.ReportFollowUpContact, false),
            (NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired, NotificationDeliveryPolicyCodes.ModerationAvailabilityRequired, false, EmailDispatchConsentRequirement.None, false),
            (NotificationDeliveryPolicyEnum.ModerationContextOptional, NotificationDeliveryPolicyCodes.ModerationContextOptional, true, EmailDispatchConsentRequirement.None, false),
            (NotificationDeliveryPolicyEnum.ReminderOptional, NotificationDeliveryPolicyCodes.ReminderOptional, true, EmailDispatchConsentRequirement.None, false),
            (NotificationDeliveryPolicyEnum.TenantAdministrationRequired, NotificationDeliveryPolicyCodes.TenantAdministrationRequired, false, EmailDispatchConsentRequirement.None, true)
        };

        foreach (var (id, code, honorsPreference, consent, invitationDestination) in cases)
        {
            var result = resolver.Resolve((int)id, code, 1);

            await Assert.That(result.IsSupported).IsTrue();
            await Assert.That(result.HonorsPreference).IsEqualTo(honorsPreference);
            await Assert.That(result.ConsentRequirement).IsEqualTo(consent);
            await Assert.That(result.UsesInvitationDestination).IsEqualTo(invitationDestination);
        }
    }

    [Test]
    public async Task ResolveFailsClosedForMismatchedCodeOrVersion()
    {
        var resolver = new NotificationDeliveryPolicyResolver();

        var wrongCode = resolver.Resolve(
            (int)NotificationDeliveryPolicyEnum.RegistrationStatusOptional,
            NotificationDeliveryPolicyCodes.ReminderOptional,
            1);
        var futureVersion = resolver.Resolve(
            (int)NotificationDeliveryPolicyEnum.RegistrationStatusOptional,
            NotificationDeliveryPolicyCodes.RegistrationStatusOptional,
            2);

        await Assert.That(wrongCode.IsSupported).IsFalse();
        await Assert.That(wrongCode.SkipReason).IsEqualTo("delivery_policy_mismatch");
        await Assert.That(futureVersion.IsSupported).IsFalse();
        await Assert.That(futureVersion.SkipReason).IsEqualTo("delivery_policy_version_unsupported");
    }
}
