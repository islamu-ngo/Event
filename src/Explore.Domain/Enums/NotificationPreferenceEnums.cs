namespace Explore.Domain.Enums;

public enum NotificationPreferenceCategoryEnum
{
    AccountSecurity = 1,
    BillingLegal = 2,
    RegistrationStatus = 3,
    EventUpdates = 4,
    OrganizationUpdates = 5,
    GroupUpdates = 6,
    TrustSafety = 7,
    ProductAnnouncements = 8,
    Marketing = 9
}

public enum NotificationPreferenceChannelEnum
{
    Email = 1,
    InApp = 2,
    Push = 3
}

public static class NotificationPreferenceCategoryCodes
{
    public const string AccountSecurity = "account-security";
    public const string BillingLegal = "billing-legal";
    public const string RegistrationStatus = "registration-status";
    public const string EventUpdates = "event-updates";
    public const string OrganizationUpdates = "organization-updates";
    public const string GroupUpdates = "group-updates";
    public const string TrustSafety = "trust-safety";
    public const string ProductAnnouncements = "product-announcements";
    public const string Marketing = "marketing";
}

public static class NotificationPreferenceChannelCodes
{
    public const string Email = "email";
    public const string InApp = "in_app";
    public const string Push = "push";
}
