namespace Explore.Application.Contracts.Webhooks;

public static class WebhookEventNames
{
    public const string EventCreated = "event.created";
    public const string EventPublished = "event.published";
    public const string EventUpdated = "event.updated";
    public const string EventCancelled = "event.cancelled";
    public const string EventLightModerated = "event.light_moderated";
    public const string EventHeavyRedacted = "event.heavy_redacted";
    public const string RegistrationCreated = "registration.created";
    public const string RegistrationApproved = "registration.approved";
    public const string RegistrationCancelled = "registration.cancelled";
    public const string ReportCreated = "report.created";
    public const string ReportDecisionCreated = "report.decision_created";
    public const string OrganizationVerified = "organization.verified";
    public const string WebhookTest = "webhook.test";
}
