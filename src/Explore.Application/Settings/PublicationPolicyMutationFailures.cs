namespace Explore.Application.Settings;

public static class PublicationPolicyMutationFailureCodes
{
    public const string InvalidPolicy = "event_reporting_intake_policy_invalid";
    public const string LockedPolicy = "event_reporting_policy_locked";
}

public static class PublicationPolicyMutationMessages
{
    public const string InvalidPolicy = "The proposed publication policy is invalid.";
    public const string LockedPolicy = "A locked publication policy setting cannot be overridden.";
}
