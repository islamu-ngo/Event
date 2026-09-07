namespace Explore.Application.Notifications;

public sealed record NotificationIntentDraft(
    NotificationCategory Category,
    Guid? TenantId = null,
    string? RecipientKind = null,
    string? TemplateKey = null,
    string? SafePayloadReference = null,
    string? SafePayloadHash = null,
    bool IsUserFacing = true,
    bool IsIslamuInitiated = true,
    string? DeduplicationKey = null,
    string? CorrelationId = null,
    Guid? UserId = null,
    Guid? EventId = null,
    Guid? ReportId = null,
    Guid? ReportDecisionId = null,
    string? ExternalProviderId = null,
    string? ExternalCorrelationId = null,
    Guid? FanoutOccurrenceId = null);
