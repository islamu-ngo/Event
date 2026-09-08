using Explore.Application.Contracts.Webhooks;

namespace Explore.Application.Webhooks;

public sealed class WebhookEventTypeRegistry : IWebhookEventTypeRegistry
{
    private static readonly WebhookEventTypeDescriptor[] Descriptors =
    [
        CreateEventDescriptor(
            WebhookEventNames.EventCreated,
            "event",
            "Raised when an event draft is created.",
            [
                Field("eventId", "Created event identifier.", "018f0000-0000-7000-8000-000000000001"),
                Field("status", "Current event lifecycle status.", "Draft")
            ]),
        CreateEventDescriptor(
            WebhookEventNames.EventPublished,
            "event",
            "Raised when an event becomes publicly published.",
            [
                Field("eventId", "Published event identifier.", "018f0000-0000-7000-8000-000000000001"),
                Field("status", "Current event lifecycle status.", "Published"),
                Field("publicUrl", "Public event URL when available.", "https://example.org/events/example-event", required: false)
            ]),
        CreateEventDescriptor(
            WebhookEventNames.EventUpdated,
            "event",
            "Raised when published or managed event metadata changes.",
            [
                Field("eventId", "Updated event identifier.", "018f0000-0000-7000-8000-000000000001"),
                Field("status", "Current event lifecycle status.", "Published")
            ]),
        CreateEventDescriptor(
            WebhookEventNames.EventCancelled,
            "event",
            "Raised when an event is cancelled.",
            [
                Field("eventId", "Cancelled event identifier.", "018f0000-0000-7000-8000-000000000001"),
                Field("status", "Current event lifecycle status.", "Cancelled")
            ]),
        CreateEventDescriptor(
            WebhookEventNames.EventLightModerated,
            "event",
            "Raised when an event receives reversible light moderation.",
            [
                Field("eventId", "Moderated event identifier.", "018f0000-0000-7000-8000-000000000001"),
                Field("moderationRecordId", "Moderation record identifier.", "018f0000-0000-7000-8000-000000000010"),
                Field("status", "Current event lifecycle status.", "LightModerated")
            ],
            payloadRetentionDays: 7),
        CreateEventDescriptor(
            WebhookEventNames.EventHeavyRedacted,
            "event",
            "Raised when unsafe event content is irreversibly redacted. Payload is deliberately generic.",
            [
                Field("moderationRecordId", "Heavy moderation record identifier.", "018f0000-0000-7000-8000-000000000010"),
                Field("reportId", "Related report identifier when available.", "018f0000-0000-7000-8000-000000000020", required: false),
                Field("caseId", "Related moderation case identifier when available.", "018f0000-0000-7000-8000-000000000030", required: false),
                Field("status", "Generic moderation status.", "HeavyRedacted")
            ],
            payloadRetentionDays: 1),
        CreateEventDescriptor(
            WebhookEventNames.RegistrationCreated,
            "registration",
            "Raised when an event registration is created.",
            [
                Field("registrationId", "Registration identifier.", "018f0000-0000-7000-8000-000000000101"),
                Field("eventId", "Registered event identifier.", "018f0000-0000-7000-8000-000000000001"),
                Field("status", "Registration status.", "Pending"),
                Field(
                    "consentToEmailShare",
                    "Whether the attendee explicitly consented to share contact details with the organizer.",
                    false,
                    WebhookJsonSchemaTypes.Flag),
                Field("attendeeEmail", "Attendee email address when contact sharing consent was granted.", "attendee@example.test", required: false),
                Field("attendeeFirstName", "Attendee first name when contact sharing consent was granted.", "Amina", required: false),
                Field("attendeeLastName", "Attendee last name when contact sharing consent was granted.", "Rahman", required: false)
            ],
            schemaVersion: 2),
        CreateEventDescriptor(
            WebhookEventNames.RegistrationApproved,
            "registration",
            "Raised when an event registration is approved.",
            [
                Field("registrationId", "Registration identifier.", "018f0000-0000-7000-8000-000000000101"),
                Field("eventId", "Registered event identifier.", "018f0000-0000-7000-8000-000000000001"),
                Field("status", "Registration status.", "Approved")
            ]),
        CreateEventDescriptor(
            WebhookEventNames.RegistrationCancelled,
            "registration",
            "Raised when an event registration is cancelled.",
            [
                Field("registrationId", "Registration identifier.", "018f0000-0000-7000-8000-000000000101"),
                Field("eventId", "Registered event identifier.", "018f0000-0000-7000-8000-000000000001"),
                Field("status", "Registration status.", "Cancelled")
            ]),
        CreateEventDescriptor(
            WebhookEventNames.ReportCreated,
            "report",
            "Raised when a moderation report is created.",
            [
                Field("reportId", "Report identifier.", "018f0000-0000-7000-8000-000000000020"),
                Field("caseId", "Moderation case identifier.", "018f0000-0000-7000-8000-000000000030"),
                Field("targetKind", "Reported target kind.", "event"),
                Field("reasonCode", "Normalized report reason code.", "safety")
            ],
            payloadRetentionDays: 7),
        CreateEventDescriptor(
            WebhookEventNames.ReportDecisionCreated,
            "report",
            "Raised when a moderation report decision is recorded.",
            [
                Field("reportId", "Report identifier.", "018f0000-0000-7000-8000-000000000020"),
                Field("caseId", "Moderation case identifier.", "018f0000-0000-7000-8000-000000000030"),
                Field("decisionId", "Decision identifier.", "018f0000-0000-7000-8000-000000000040"),
                Field("decisionKind", "Normalized decision kind.", "light_moderate")
            ],
            payloadRetentionDays: 7),
        CreateEventDescriptor(
            WebhookEventNames.OrganizationVerified,
            "organization",
            "Raised when an organization is verified.",
            [
                Field("organizationId", "Organization identifier.", "018f0000-0000-7000-8000-000000000201"),
                Field("status", "Organization verification status.", "Verified")
            ]),
        CreateEventDescriptor(
            WebhookEventNames.WebhookTest,
            "webhook",
            "Raised when an administrator schedules a LocalProvider test delivery for one endpoint.",
            [
                Field("endpointId", "Webhook endpoint identifier receiving the test delivery.", "018f0000-0000-7000-8000-000000000301"),
                Field("consumerId", "Webhook consumer that owns the endpoint.", "018f0000-0000-7000-8000-000000000302"),
                Field("providerMode", "Consumer provider mode at the time the test was scheduled.", "Local"),
                Field("requestedAt", "UTC timestamp when the test delivery was requested.", "2026-07-02T10:00:00.0000000+00:00")
            ],
            payloadRetentionDays: 1)
    ];

    private static readonly Dictionary<string, WebhookEventTypeDescriptor> ByName =
        Descriptors.ToDictionary(descriptor => descriptor.Name, StringComparer.Ordinal);

    public IReadOnlyCollection<WebhookEventTypeDescriptor> GetAll() => Descriptors;

    public WebhookEventTypeDescriptor? FindByName(string name) =>
        ByName.GetValueOrDefault(name);

    public bool IsKnownEventType(string name) =>
        ByName.ContainsKey(name);

    public static bool IsValidEventTypeName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.');

    private static WebhookEventTypeDescriptor CreateEventDescriptor(
        string name,
        string groupName,
        string description,
        IReadOnlyList<WebhookEventDataFieldDescriptor> dataFields,
        int payloadRetentionDays = 14,
        int schemaVersion = 1) =>
        new(
            name,
            groupName,
            description,
            schemaVersion,
            IsPublic: true,
            IsEnabled: true,
            payloadRetentionDays,
            dataFields);

    private static WebhookEventDataFieldDescriptor Field(
        string name,
        string description,
        object? example,
        string jsonType = WebhookJsonSchemaTypes.Text,
        bool required = true) =>
        new(name, jsonType, description, example, required);
}
