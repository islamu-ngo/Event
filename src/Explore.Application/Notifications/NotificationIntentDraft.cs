// ABOUTME: Captures the safe metadata needed to resolve notification ownership.
// ABOUTME: Excludes provider clients, raw evidence, and delivery transport details by design.

using Explore.Application.Contracts.Identity;

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
    Guid? FanoutOccurrenceId = null,
    ResolvedAccountAuthority? AccountAuthority = null);
