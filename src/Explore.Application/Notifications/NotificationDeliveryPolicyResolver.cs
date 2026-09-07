using Explore.Domain.Enums;

namespace Explore.Application.Notifications;

public enum EmailDispatchConsentRequirement
{
    None = 0,
    ReportCaseUpdates = 1,
    ReportFollowUpContact = 2
}

public sealed record NotificationDeliveryPolicyResolution(
    bool IsSupported,
    bool HonorsPreference,
    EmailDispatchConsentRequirement ConsentRequirement,
    bool UsesInvitationDestination,
    string? SkipReason);

public sealed class NotificationDeliveryPolicyResolver
{
    private const int CurrentPolicyVersion = 1;

    public NotificationDeliveryPolicyResolution Resolve(int policyId, string policyCode, int policyVersion)
    {
        if (policyVersion != CurrentPolicyVersion)
        {
            return Unsupported("delivery_policy_version_unsupported");
        }

        if (!Policies.TryGetValue(policyId, out var policy)
            || !string.Equals(policy.Code, policyCode, StringComparison.Ordinal))
        {
            return Unsupported("delivery_policy_mismatch");
        }

        return new NotificationDeliveryPolicyResolution(
            true,
            policy.HonorsPreference,
            policy.ConsentRequirement,
            policy.UsesInvitationDestination,
            null);
    }

    private static NotificationDeliveryPolicyResolution Unsupported(string reason) =>
        new(false, false, EmailDispatchConsentRequirement.None, false, reason);

    private static readonly IReadOnlyDictionary<int, Policy> Policies = new Dictionary<int, Policy>
    {
        [(int)NotificationDeliveryPolicyEnum.RegistrationStatusOptional] = new(
            NotificationDeliveryPolicyCodes.RegistrationStatusOptional, true, EmailDispatchConsentRequirement.None, false),
        [(int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional] = new(
            NotificationDeliveryPolicyCodes.CriticalEventUpdateOptional, true, EmailDispatchConsentRequirement.None, false),
        [(int)NotificationDeliveryPolicyEnum.ReportCaseUpdate] = new(
            NotificationDeliveryPolicyCodes.ReportCaseUpdate, true, EmailDispatchConsentRequirement.ReportCaseUpdates, false),
        [(int)NotificationDeliveryPolicyEnum.ReportFollowUpContact] = new(
            NotificationDeliveryPolicyCodes.ReportFollowUpContact, true, EmailDispatchConsentRequirement.ReportFollowUpContact, false),
        [(int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired] = new(
            NotificationDeliveryPolicyCodes.ModerationAvailabilityRequired, false, EmailDispatchConsentRequirement.None, false),
        [(int)NotificationDeliveryPolicyEnum.ModerationContextOptional] = new(
            NotificationDeliveryPolicyCodes.ModerationContextOptional, true, EmailDispatchConsentRequirement.None, false),
        [(int)NotificationDeliveryPolicyEnum.ReminderOptional] = new(
            NotificationDeliveryPolicyCodes.ReminderOptional, true, EmailDispatchConsentRequirement.None, false),
        [(int)NotificationDeliveryPolicyEnum.TenantAdministrationRequired] = new(
            NotificationDeliveryPolicyCodes.TenantAdministrationRequired, false, EmailDispatchConsentRequirement.None, true)
    };

    private sealed record Policy(
        string Code,
        bool HonorsPreference,
        EmailDispatchConsentRequirement ConsentRequirement,
        bool UsesInvitationDestination);
}

public static class ReportEmailConsentPurposeCodes
{
    public const string CaseUpdates = "report-case-updates";
    public const string FollowUpContact = "report-follow-up-contact";
}
