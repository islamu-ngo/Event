// ABOUTME: Defines custom OpenTelemetry business metrics for the platform.
// ABOUTME: Tracks domain activity plus moderation, external API-key, email-dispatch, storage, notification fanout, and governance signals.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Features.SupportAccess;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Telemetry;

/// <summary>
/// Custom business metrics exposed via OpenTelemetry.
/// Metrics use metric-specific bounded tags; not every metric includes tenant_id.
/// Meter name: "Explore.Business"
/// </summary>
public sealed class BusinessMetrics : ISchedulerJobTelemetry, IDisposable
{
    public const string MeterName = "Explore.Business";

    private readonly Meter _meter;
    private readonly Counter<long> _eventsCreated;
    private readonly Counter<long> _eventsPublished;
    private readonly Counter<long> _eventModerationActions;
    private readonly Counter<long> _schedulerAdminActions;
    private readonly Counter<long> _schedulerJobExecutions;
    private readonly Histogram<double> _schedulerJobDuration;
    private readonly Counter<long> _eventReportSubmissions;
    private readonly Counter<long> _eventReportWorkflowActions;
    private readonly Counter<long> _eventReportProviderSyncs;
    private readonly Counter<long> _eventReportProviderCallbacks;
    private readonly Counter<long> _eventPublicActionEngagements;
    private readonly Counter<long> _registrationsCreated;
    private readonly Counter<long> _registrationProviderManagementActions;
    private readonly Counter<long> _registrationProviderSubscriptionOperations;
    private readonly Histogram<long> _registrationProviderSubscriptionWatches;
    private readonly Counter<long> _refundOperations;
    private readonly Counter<long> _refundCampaignOperations;
    private readonly Counter<long> _organizationsCreated;
    private readonly Counter<long> _authorizationDecisions;
    private readonly Histogram<double> _authorizationDecisionDuration;
    private readonly Counter<long> _supportAccessLifecycleEvents;
    private readonly Counter<long> _supportAccessRequestAudits;
    private readonly Counter<long> _supportAccessSessionValidationDenials;
    private readonly Counter<long> _supportAccessBoundaryDenials;
    private readonly Counter<long> _eventRoleAssignmentChanged;
    private readonly Counter<long> _externalApiKeysCreated;
    private readonly Counter<long> _externalApiKeysRevoked;
    private readonly Counter<long> _externalApiKeyAuthenticationAttempts;
    private readonly Counter<long> _externalApiKeyThrottleEvents;
    private readonly Counter<long> _externalApiKeyPolicyUpdated;
    private readonly Counter<long> _externalApiKeyRotated;
    private readonly Counter<long> _emailDispatchAttempts;
    private readonly Counter<long> _emailDispatchOperationalOutcomes;
    private readonly Counter<long> _emailDispatchRabbitMqPublishes;
    private readonly Counter<long> _emailDispatchRabbitMqConsumes;
    private readonly Histogram<long> _emailDispatchTenantBacklog;
    private readonly Histogram<double> _emailDispatchOldestPendingAge;
    private long _emailDispatchOptionalReminderDeferralState;
    private readonly Counter<long> _notificationFanoutRuns;
    private readonly Counter<long> _notificationFanoutSubscribers;
    private readonly Counter<long> _notificationFanoutProcessorClaims;
    private readonly Counter<long> _notificationFanoutProcessorRecipients;
    private long _notificationFanoutDueOccurrenceCount;
    private long _notificationFanoutDueCoreOccurrenceCount;
    private long _notificationFanoutDueOptionalReminderCount;
    private long _notificationFanoutActiveClaimCount;
    private long _notificationFanoutExpiredClaimCount;
    private long _notificationFanoutSupersededOccurrenceCount;
    private long _notificationFanoutProcessedRecipientCount;
    private long _notificationFanoutRemainingOccurrenceCount;
    private long _notificationFanoutOldestDueAgeSeconds;
    private long _notificationFanoutOptionalReminderDeferralState;
    private readonly Counter<long> _webhookMessagesCreated;
    private readonly Counter<long> _webhookDeliveryAttempts;
    private readonly Counter<long> _webhookDeliverySuccess;
    private readonly Counter<long> _webhookDeliveryFailure;
    private readonly Counter<long> _webhookEndpointDisabled;
    private readonly Counter<long> _webhookManualRetries;
    private readonly Counter<long> _webhookProviderPublishFailures;
    private readonly Counter<long> _webhookRetentionCleanupRuns;
    private readonly Counter<long> _webhookRetentionCleanupItems;
    private readonly Histogram<double> _webhookClaimLag;
    private readonly Counter<long> _webhookProcessingOutcomes;
    private readonly Counter<long> _webhookRetriesScheduled;
    private readonly Counter<long> _webhookDeadLetters;
    private readonly Counter<long> _webhookManualReconciliations;
    private readonly Counter<long> _webhookEndpointAutoPauses;
    private readonly Counter<long> _webhookProviderHealthChecks;
    private readonly Histogram<double> _webhookPublicationUnknownAge;
    private readonly Counter<long> _queueDrainHealthChecks;
    private readonly Histogram<long> _queueDrainBacklog;
    private readonly Histogram<long> _queueDrainStaleWork;
    private readonly Counter<long> _customPropertyPurgeDecisions;
    private readonly Counter<long> _idempotencyCleanupRuns;
    private readonly Counter<long> _idempotencyCleanupRows;
    private readonly Counter<long> _atprotoTransientCleanupRuns;
    private readonly Counter<long> _atprotoTransientCleanupRows;
    private readonly Counter<long> _aiRetentionCleanupRuns;
    private readonly Counter<long> _aiRetentionCleanupRows;
    private readonly Counter<long> _aiProviderHealthChecks;
    private readonly Counter<long> _aiProviderRequests;
    private readonly Histogram<double> _aiProviderRequestDuration;
    private readonly Histogram<long> _aiProviderTokenUsage;
    private readonly Counter<long> _aiProviderProposedActions;
    private readonly Counter<long> _storageUploadSessions;
    private readonly Histogram<long> _storageUploadBytes;
    private readonly Counter<long> _storageReads;
    private readonly Histogram<long> _storageReadBytes;
    private readonly Counter<long> _storageDeletes;
    private readonly Counter<long> _storageQuotaReservations;
    private readonly Histogram<long> _storageQuotaBytes;
    private readonly Counter<long> _storageProviderTests;
    private readonly Counter<long> _storageReconciliationRuns;
    private readonly Counter<long> _storageReconciliationObjects;

    public BusinessMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        var meter = _meter;

        _eventsCreated = meter.CreateCounter<long>(
            "explore.events.created",
            unit: "{event}",
            description: "Total number of events created");

        _eventsPublished = meter.CreateCounter<long>(
            "explore.events.published",
            unit: "{event}",
            description: "Total number of events published");

        _schedulerAdminActions = meter.CreateCounter<long>(
            "explore.scheduler.admin_actions",
            unit: "{action}",
            description: "Operator-initiated scheduler control actions by bounded action kind and outcome");

        _schedulerJobExecutions = meter.CreateCounter<long>(
            "explore.scheduler.job_executions",
            unit: "{execution}",
            description: "Scheduled job executions by catalog job name, scheduler group, and outcome");

        _schedulerJobDuration = meter.CreateHistogram<double>(
            "explore.scheduler.job_duration",
            unit: "s",
            description: "Scheduled job execution duration in seconds by catalog job name and scheduler group");

        _eventModerationActions = meter.CreateCounter<long>(
            "explore.events.moderation_actions",
            unit: "{action}",
            description: "Total event moderation lifecycle decisions by bounded action kind, outcome, and failure category");

        _eventReportSubmissions = meter.CreateCounter<long>(
            "explore.event_reports.submissions",
            unit: "{submission}",
            description: "Total event-report submission outcomes by bounded failure category");

        _eventReportWorkflowActions = meter.CreateCounter<long>(
            "explore.event_reports.workflow_actions",
            unit: "{action}",
            description: "Total event-report moderation workflow actions by bounded action, outcome, and failure category");

        _eventReportProviderSyncs = meter.CreateCounter<long>(
            "explore.event_reports.provider_syncs",
            unit: "{sync}",
            description: "Total event-report provider sync outcomes by bounded provider, outcome, and failure category");

        _eventReportProviderCallbacks = meter.CreateCounter<long>(
            "explore.event_reports.provider_callbacks",
            unit: "{callback}",
            description: "Total moderation provider callback outcomes by bounded provider, outcome, and failure category");

        _eventPublicActionEngagements = meter.CreateCounter<long>(
            "explore.event_public_actions.engagements",
            unit: "{engagement}",
            description: "Total public-action redirects issued by bounded action kind, surface, and outcome");

        _registrationsCreated = meter.CreateCounter<long>(
            "explore.registrations.created",
            unit: "{registration}",
            description: "Total number of event registrations created");

        _registrationProviderManagementActions = meter.CreateCounter<long>(
            "explore.registration_providers.management_actions",
            unit: "{action}",
            description: "Total registration-provider management actions by bounded action and outcome");

        _registrationProviderSubscriptionOperations = meter.CreateCounter<long>(
            "explore.registration_providers.subscription_operations",
            unit: "{operation}",
            description: "Total registration-provider subscription renewal and sweep outcomes by bounded operation/outcome");

        _registrationProviderSubscriptionWatches = meter.CreateHistogram<long>(
            "explore.registration_providers.subscription_watches",
            unit: "{watch}",
            description: "Registration-provider subscription watch counts by bounded watch status");

        _refundOperations = meter.CreateCounter<long>(
            "explore.refunds.operations",
            unit: "{operation}",
            description: "Refund dispatch and reconciliation operations by bounded operation, status, and outcome");

        _refundCampaignOperations = meter.CreateCounter<long>(
            "explore.refunds.campaign_operations",
            unit: "{operation}",
            description: "Refund campaign processing operations by bounded kind, status, and outcome");

        _organizationsCreated = meter.CreateCounter<long>(
            "explore.organizations.created",
            unit: "{organization}",
            description: "Total number of organizations created");

        _authorizationDecisions = meter.CreateCounter<long>(
            "explore.authorization.decisions",
            unit: "{decision}",
            description: "Total authorization decisions by capability, outcome, reason code, and deciding provider");

        _authorizationDecisionDuration = meter.CreateHistogram<double>(
            "explore.authorization.decision.duration",
            unit: "ms",
            description: "Time to reach an authorization decision, by capability and deciding provider");

        _supportAccessLifecycleEvents = meter.CreateCounter<long>(
            "explore.support_access.lifecycle_events",
            unit: "{event}",
            description: "Total support-access lifecycle decisions by bounded event type, mode, outcome, and failure category");

        _supportAccessRequestAudits = meter.CreateCounter<long>(
            "explore.support_access.request_audits",
            unit: "{audit}",
            description: "Total support-access request audit persistence decisions by bounded event type and outcome");

        _supportAccessSessionValidationDenials = meter.CreateCounter<long>(
            "explore.support_access.session_validation_denials",
            unit: "{denial}",
            description: "Total forwarded support-access session validation denials by bounded reason and mode");

        _supportAccessBoundaryDenials = meter.CreateCounter<long>(
            "explore.support_access.authorization_boundary_denials",
            unit: "{denial}",
            description: "Total support-access authorization boundary denials by bounded reason, mode, and action class");

        _eventRoleAssignmentChanged = meter.CreateCounter<long>(
            "event_role_assignment.changed",
            unit: "{change}",
            description: "Total event-role assignment lifecycle changes by operation and outcome");

        _externalApiKeysCreated = meter.CreateCounter<long>(
            "explore.external_api_keys.created",
            unit: "{key}",
            description: "Total number of external API keys created");

        _externalApiKeysRevoked = meter.CreateCounter<long>(
            "explore.external_api_keys.revoked",
            unit: "{key}",
            description: "Total number of external API keys revoked");

        _externalApiKeyAuthenticationAttempts = meter.CreateCounter<long>(
            "explore.external_api_keys.authentication_attempts",
            unit: "{attempt}",
            description: "Total external API key authentication attempts by outcome");

        _externalApiKeyThrottleEvents = meter.CreateCounter<long>(
            "explore.external_api_keys.throttled",
            unit: "{throttle}",
            description: "Total external API key throttle events by policy");

        _externalApiKeyPolicyUpdated = meter.CreateCounter<long>(
            "explore.external_api_keys.policy_updated",
            unit: "{update}",
            description: "Total external API key policy updates");

        _externalApiKeyRotated = meter.CreateCounter<long>(
            "explore.external_api_keys.rotated",
            unit: "{rotation}",
            description: "Total external API key rotations");

        _emailDispatchAttempts = meter.CreateCounter<long>(
            "explore.email_dispatch.attempts",
            unit: "{attempt}",
            description: "Total SMTP provider-handoff attempts by bounded outcome and failure category");

        _emailDispatchOperationalOutcomes = meter.CreateCounter<long>(
            "explore.email_dispatch.operational_outcomes",
            unit: "{outcome}",
            description: "Total pre-handoff email dispatch decisions by bounded outcome and reason");

        _emailDispatchRabbitMqPublishes = meter.CreateCounter<long>(
            "explore.email_dispatch.rabbitmq.publishes",
            unit: "{publish}",
            description: "Total RabbitMQ Dispatch Mode pointer publishes by outcome");

        _emailDispatchRabbitMqConsumes = meter.CreateCounter<long>(
            "explore.email_dispatch.rabbitmq.consumes",
            unit: "{delivery}",
            description: "Total RabbitMQ Dispatch Mode pointer deliveries by durable consumer outcome");

        _emailDispatchTenantBacklog = meter.CreateHistogram<long>(
            "explore.email_dispatch.tenant_backlog",
            unit: "{dispatch}",
            description: "Bounded observations of due email dispatch rows by tenant");

        _emailDispatchOldestPendingAge = meter.CreateHistogram<double>(
            "explore.email_dispatch.oldest_pending_age",
            unit: "s",
            description: "Observed age in seconds of the oldest due email dispatch row");

        meter.CreateObservableGauge(
            "explore.email_dispatch.optional_reminder_deferral",
            () => Volatile.Read(ref _emailDispatchOptionalReminderDeferralState),
            unit: "{state}",
            description: "Durable optional-reminder backpressure state where 1 is active and 0 is inactive");

        _notificationFanoutRuns = meter.CreateCounter<long>(
            "explore.notifications.fanout_runs",
            unit: "{run}",
            description: "Total notification fanout runs by kind and bounded outcome");

        _notificationFanoutSubscribers = meter.CreateCounter<long>(
            "explore.notifications.fanout_subscribers",
            unit: "{subscriber}",
            description: "Total notification fanout subscriber decisions by kind and bounded outcome");

        _notificationFanoutProcessorClaims = meter.CreateCounter<long>(
            "explore.notifications.fanout_processor.claims",
            unit: "{claim}",
            description: "Fanout processor claim decisions by bounded outcome");

        _notificationFanoutProcessorRecipients = meter.CreateCounter<long>(
            "explore.notifications.fanout_processor.recipients",
            unit: "{recipient}",
            description: "Fanout processor recipient outcomes without tenant or recipient labels");

        meter.CreateObservableGauge(
            "explore.notifications.fanout_processor.due_occurrences",
            () => Volatile.Read(ref _notificationFanoutDueOccurrenceCount),
            unit: "{occurrence}",
            description: "Current due fanout occurrence count");
        meter.CreateObservableGauge(
            "explore.notifications.fanout_processor.due_core_occurrences",
            () => Volatile.Read(ref _notificationFanoutDueCoreOccurrenceCount),
            unit: "{occurrence}",
            description: "Current due non-reminder fanout occurrence count");
        meter.CreateObservableGauge(
            "explore.notifications.fanout_processor.due_optional_reminders",
            () => Volatile.Read(ref _notificationFanoutDueOptionalReminderCount),
            unit: "{occurrence}",
            description: "Current due optional-reminder fanout occurrence count");
        meter.CreateObservableGauge(
            "explore.notifications.fanout_processor.active_claims",
            () => Volatile.Read(ref _notificationFanoutActiveClaimCount),
            unit: "{claim}",
            description: "Current active fanout claim count");
        meter.CreateObservableGauge(
            "explore.notifications.fanout_processor.expired_claims",
            () => Volatile.Read(ref _notificationFanoutExpiredClaimCount),
            unit: "{claim}",
            description: "Current expired fanout claim count");
        meter.CreateObservableGauge(
            "explore.notifications.fanout_processor.superseded_occurrences",
            () => Volatile.Read(ref _notificationFanoutSupersededOccurrenceCount),
            unit: "{occurrence}",
            description: "Current durable superseded fanout occurrence count");
        meter.CreateObservableGauge(
            "explore.notifications.fanout_processor.processed_recipients",
            () => Volatile.Read(ref _notificationFanoutProcessedRecipientCount),
            unit: "{recipient}",
            description: "Current durable processed-recipient total across fanout runs");
        meter.CreateObservableGauge(
            "explore.notifications.fanout_processor.remaining_occurrences",
            () => Volatile.Read(ref _notificationFanoutRemainingOccurrenceCount),
            unit: "{occurrence}",
            description: "Current due plus actively processing fanout occurrence count");
        meter.CreateObservableGauge(
            "explore.notifications.fanout_processor.oldest_due_age",
            () => Volatile.Read(ref _notificationFanoutOldestDueAgeSeconds),
            unit: "s",
            description: "Current age in seconds of the oldest due fanout occurrence");
        meter.CreateObservableGauge(
            "explore.notifications.fanout_processor.optional_reminder_deferral",
            () => Volatile.Read(ref _notificationFanoutOptionalReminderDeferralState),
            unit: "{state}",
            description: "Optional-reminder fanout backpressure state where 1 is active and 0 is inactive");

        _webhookMessagesCreated = meter.CreateCounter<long>(
            "explore.webhooks.messages_created",
            unit: "{message}",
            description: "Total canonical webhook messages created by event type, provider, and bounded outcome");

        _webhookDeliveryAttempts = meter.CreateCounter<long>(
            "explore.webhooks.delivery_attempts",
            unit: "{attempt}",
            description: "Total LocalProvider webhook delivery attempt settlements by event type and bounded outcome");

        _webhookDeliverySuccess = meter.CreateCounter<long>(
            "explore.webhooks.delivery_success",
            unit: "{delivery}",
            description: "Total LocalProvider webhook delivery successes by event type");

        _webhookDeliveryFailure = meter.CreateCounter<long>(
            "explore.webhooks.delivery_failure",
            unit: "{delivery}",
            description: "Total LocalProvider webhook delivery failures by event type, bounded outcome, and failure category");

        _webhookEndpointDisabled = meter.CreateCounter<long>(
            "explore.webhooks.endpoint_disabled",
            unit: "{endpoint}",
            description: "Total LocalProvider webhook endpoint disable decisions by bounded failure category");

        _webhookManualRetries = meter.CreateCounter<long>(
            "explore.webhooks.manual_retries",
            unit: "{retry}",
            description: "Total LocalProvider manual retry scheduling decisions by bounded outcome and failure category");

        _webhookProviderPublishFailures = meter.CreateCounter<long>(
            "explore.webhooks.provider_publish_failure",
            unit: "{failure}",
            description: "Total outgoing webhook provider publish failures by provider and bounded failure category");

        _webhookRetentionCleanupRuns = meter.CreateCounter<long>(
            "explore.webhooks.retention.cleanup_runs",
            unit: "{run}",
            description: "Total webhook retention cleanup passes by mode and bounded outcome");

        _webhookRetentionCleanupItems = meter.CreateCounter<long>(
            "explore.webhooks.retention.cleanup_items",
            unit: "{item}",
            description: "Total webhook retention items selected or changed by mode and bounded data kind");

        _webhookClaimLag = meter.CreateHistogram<double>(
            "explore.webhooks.claim_lag",
            unit: "s",
            description: "Webhook work-item claim lag in seconds by provider and bounded operation");

        _webhookProcessingOutcomes = meter.CreateCounter<long>(
            "explore.webhooks.processing_outcomes",
            unit: "{item}",
            description: "Webhook processing outcomes by provider, operation, and bounded outcome");

        _webhookRetriesScheduled = meter.CreateCounter<long>(
            "explore.webhooks.retries_scheduled",
            unit: "{retry}",
            description: "Webhook automatic retries scheduled by provider and operation");

        _webhookDeadLetters = meter.CreateCounter<long>(
            "explore.webhooks.dead_letters",
            unit: "{item}",
            description: "Webhook work items moved to terminal dead-letter state by provider and operation");

        _webhookManualReconciliations = meter.CreateCounter<long>(
            "explore.webhooks.manual_reconciliations",
            unit: "{item}",
            description: "Webhook publications requiring manual reconciliation by provider");

        _webhookEndpointAutoPauses = meter.CreateCounter<long>(
            "explore.webhooks.endpoint_auto_pauses",
            unit: "{endpoint}",
            description: "Webhook endpoint transitions into automatic pause by provider");

        _webhookProviderHealthChecks = meter.CreateCounter<long>(
            "explore.webhooks.provider_health_checks",
            unit: "{check}",
            description: "Webhook provider readiness outcomes by provider and bounded outcome");

        _webhookPublicationUnknownAge = meter.CreateHistogram<double>(
            "explore.webhooks.publication_unknown_age",
            unit: "s",
            description: "Age in seconds of provider publications observed in an uncertain state");

        _queueDrainHealthChecks = meter.CreateCounter<long>(
            "explore.queue_drains.health_checks",
            unit: "{check}",
            description: "Readiness outcomes for scheduler-owned durable queue drains");
        _queueDrainBacklog = meter.CreateHistogram<long>(
            "explore.queue_drains.backlog",
            unit: "{item}",
            description: "Tenant-free due backlog observed for scheduler-owned durable queue drains");
        _queueDrainStaleWork = meter.CreateHistogram<long>(
            "explore.queue_drains.stale_work",
            unit: "{item}",
            description: "Tenant-free stale or ambiguous work observed for scheduler-owned durable queue drains");

        _customPropertyPurgeDecisions = meter.CreateCounter<long>(
            "explore.custom_properties.purge_decisions",
            unit: "{decision}",
            description: "Total custom-property hard-purge decisions by scope, outcome, and bounded blocker category");

        _idempotencyCleanupRuns = meter.CreateCounter<long>(
            "explore.idempotency.cleanup_runs",
            unit: "{run}",
            description: "Total idempotency cleanup passes by mode and outcome");

        _idempotencyCleanupRows = meter.CreateCounter<long>(
            "explore.idempotency.cleanup_rows",
            unit: "{row}",
            description: "Total idempotency rows selected or deleted by cleanup mode and outcome");

        _atprotoTransientCleanupRuns = meter.CreateCounter<long>(
            "explore.atproto.transient.cleanup_runs", "{run}", "Transient cleanup passes by bounded outcome");
        _atprotoTransientCleanupRows = meter.CreateCounter<long>(
            "explore.atproto.transient.cleanup_rows", "{row}", "Rows deleted by completed transient cleanup passes");

        _aiRetentionCleanupRuns = meter.CreateCounter<long>(
            "explore.ai.retention.cleanup_runs",
            unit: "{run}",
            description: "Total AI retention cleanup passes by mode and bounded outcome");

        _aiRetentionCleanupRows = meter.CreateCounter<long>(
            "explore.ai.retention.cleanup_rows",
            unit: "{row}",
            description: "Total AI retention rows selected or redacted by cleanup mode and bounded category");

        _aiProviderHealthChecks = meter.CreateCounter<long>(
            "explore.ai.provider.health_checks",
            unit: "{check}",
            description: "Total AI provider health checks by provider, status, and bounded reason");

        _aiProviderRequests = meter.CreateCounter<long>(
            "explore.ai.provider.requests",
            unit: "{request}",
            description: "Total AI provider requests by provider, outcome, and bounded failure category");

        _aiProviderRequestDuration = meter.CreateHistogram<double>(
            "explore.ai.provider.request_duration",
            unit: "s",
            description: "AI provider request duration by provider, outcome, and bounded failure category");

        _aiProviderTokenUsage = meter.CreateHistogram<long>(
            "explore.ai.provider.token_usage",
            unit: "{token}",
            description: "AI provider token usage by provider and bounded token type");

        _aiProviderProposedActions = meter.CreateCounter<long>(
            "explore.ai.provider.proposed_actions",
            unit: "{action}",
            description: "Total AI proposed actions returned by provider and bounded action kind");

        _storageUploadSessions = meter.CreateCounter<long>(
            "explore.storage.upload_sessions",
            unit: "{session}",
            description: "Total storage upload session lifecycle outcomes by provider and bounded category");

        _storageUploadBytes = meter.CreateHistogram<long>(
            "explore.storage.upload_bytes",
            unit: "By",
            description: "Storage upload byte counts by provider and bounded outcome");

        _storageReads = meter.CreateCounter<long>(
            "explore.storage.reads",
            unit: "{read}",
            description: "Total storage read outcomes by provider, visibility, and bounded failure category");

        _storageReadBytes = meter.CreateHistogram<long>(
            "explore.storage.read_bytes",
            unit: "By",
            description: "Storage read byte counts by provider, visibility, and bounded outcome");

        _storageDeletes = meter.CreateCounter<long>(
            "explore.storage.deletes",
            unit: "{delete}",
            description: "Total storage delete outcomes by provider and bounded failure category");

        _storageQuotaReservations = meter.CreateCounter<long>(
            "explore.storage.quota_reservations",
            unit: "{reservation}",
            description: "Total storage quota reservation outcomes by provider and bounded operation");

        _storageQuotaBytes = meter.CreateHistogram<long>(
            "explore.storage.quota_bytes",
            unit: "By",
            description: "Storage quota reservation byte counts by provider, operation, and bounded outcome");

        _storageProviderTests = meter.CreateCounter<long>(
            "explore.storage.provider_tests",
            unit: "{test}",
            description: "Total storage provider connection test outcomes by provider and bounded failure category");

        _storageReconciliationRuns = meter.CreateCounter<long>(
            "explore.storage.reconciliation_runs",
            unit: "{run}",
            description: "Total storage reconciliation passes by mode and bounded outcome");

        _storageReconciliationObjects = meter.CreateCounter<long>(
            "explore.storage.reconciliation_objects",
            unit: "{object}",
            description: "Total storage reconciliation object decisions by provider, category, action, and bounded outcome");
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    public void RecordEventCreated()
    {
        _eventsCreated.Add(1);
    }

    public void RecordEventPublished(string? tenantId = null)
    {
        _eventsPublished.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"));
    }

    public void RecordEventPublicActionEngagement(EventPublicActionKindEnum actionKind, string? surface)
    {
        _eventPublicActionEngagements.Add(1,
            new KeyValuePair<string, object?>("action_kind", NormalizeEventPublicActionKind(actionKind)),
            new KeyValuePair<string, object?>("surface", NormalizeEventPublicActionSurface(surface)),
            new KeyValuePair<string, object?>("outcome", "redirect_issued"));
    }

    /// <summary>
    /// Records one operator-initiated scheduler control action. The scheduler is instance-level infrastructure, so
    /// this metric deliberately carries no tenant or principal identity — accountability for <em>who</em> acted
    /// belongs in the audit record, not in a metric dimension that would explode cardinality.
    /// </summary>
    /// <summary>
    /// Records one scheduled job execution. Labels are restricted to the job's scheduler identity and a
    /// bounded outcome: a job's payload identifies a tenant and an aggregate, and metric labels are a
    /// high-cardinality, long-retention, widely-exported surface that must never carry either.
    /// </summary>
    public void RecordSchedulerJobExecution(string? jobName, string? jobGroup, string? outcome, double durationSeconds)
    {
        var normalizedJobName = NormalizeSchedulerJobName(jobName);
        var normalizedGroup = NormalizeTag(jobGroup);

        _schedulerJobExecutions.Add(1,
            new KeyValuePair<string, object?>("job_name", normalizedJobName),
            new KeyValuePair<string, object?>("job_group", normalizedGroup),
            new KeyValuePair<string, object?>("outcome", NormalizeSchedulerJobOutcome(outcome)));

        _schedulerJobDuration.Record(Math.Max(0, durationSeconds),
            new KeyValuePair<string, object?>("job_name", normalizedJobName),
            new KeyValuePair<string, object?>("job_group", normalizedGroup));
    }

    /// <summary>
    /// Job names are collapsed to the Application-owned catalog. An unrecognized name becomes
    /// <c>other</c> rather than becoming its own time series, so an ad-hoc or test job — or a future rename
    /// that misses this catalog — cannot grow the metric's cardinality without bound.
    /// </summary>
    private static string NormalizeSchedulerJobName(string? jobName)
        => jobName is not null && ScheduledJobNames.All.Contains(jobName) ? jobName : "other";

    private static string NormalizeSchedulerJobOutcome(string? outcome) => NormalizeTag(outcome) switch
    {
        SchedulerJobOutcomes.Succeeded => SchedulerJobOutcomes.Succeeded,
        SchedulerJobOutcomes.Failed => SchedulerJobOutcomes.Failed,
        SchedulerJobOutcomes.Vetoed => SchedulerJobOutcomes.Vetoed,
        _ => "unknown"
    };

    public void RecordSchedulerAdminAction(string? action, string? outcome)
    {
        _schedulerAdminActions.Add(1,
            new KeyValuePair<string, object?>("action", NormalizeSchedulerAdminAction(action)),
            new KeyValuePair<string, object?>("outcome", NormalizeSchedulerAdminOutcome(outcome)));
    }

    private static string NormalizeSchedulerAdminAction(string? action) => action switch
    {
        SchedulerAdminAuditActions.PauseScheduler => SchedulerAdminAuditActions.PauseScheduler,
        SchedulerAdminAuditActions.ResumeScheduler => SchedulerAdminAuditActions.ResumeScheduler,
        SchedulerAdminAuditActions.PauseJob => SchedulerAdminAuditActions.PauseJob,
        SchedulerAdminAuditActions.ResumeJob => SchedulerAdminAuditActions.ResumeJob,
        SchedulerAdminAuditActions.TriggerJob => SchedulerAdminAuditActions.TriggerJob,
        SchedulerAdminAuditActions.ResetJobErrorState => SchedulerAdminAuditActions.ResetJobErrorState,
        SchedulerAdminAuditActions.InterruptJob => SchedulerAdminAuditActions.InterruptJob,
        _ => "unknown"
    };

    private static string NormalizeSchedulerAdminOutcome(string? outcome) => outcome switch
    {
        "succeeded" => "succeeded",
        FailureCodes.NotFound => FailureCodes.NotFound,
        FailureCodes.SchedulerReadOnly => FailureCodes.SchedulerReadOnly,
        FailureCodes.SchedulerUnavailable => FailureCodes.SchedulerUnavailable,
        FailureCodes.SchedulerActionNotApplicable => FailureCodes.SchedulerActionNotApplicable,
        _ => "refused"
    };

    public void RecordEventModerationAction(
        string? tenantId,
        string? actionKind,
        string? outcome,
        string? failureCategory = null,
        bool? irreversible = null)
    {
        _eventModerationActions.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("action_kind", NormalizeModerationActionKind(actionKind)),
            new KeyValuePair<string, object?>("outcome", NormalizeModerationOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeModerationFailureCategory(failureCategory)),
            new KeyValuePair<string, object?>("irreversible", irreversible?.ToString().ToLowerInvariant() ?? "unknown"));
    }

    public void RecordEventReportSubmission(
        string? tenantId,
        string? outcome,
        string? failureCategory = null)
    {
        _eventReportSubmissions.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("outcome", NormalizeEventReportSubmissionOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeEventReportSubmissionFailureCategory(failureCategory)));
    }

    public void RecordEventReportWorkflowAction(
        string? tenantId,
        string? action,
        string? outcome,
        string? failureCategory = null)
    {
        _eventReportWorkflowActions.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("action", NormalizeEventReportWorkflowAction(action)),
            new KeyValuePair<string, object?>("outcome", NormalizeEventReportOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeEventReportFailureCategory(failureCategory)));
    }

    public void RecordEventReportProviderSync(
        string? tenantId,
        string? provider,
        string? outcome,
        string? failureCategory = null)
    {
        _eventReportProviderSyncs.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("provider", NormalizeEventReportProvider(provider)),
            new KeyValuePair<string, object?>("outcome", NormalizeEventReportOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeEventReportFailureCategory(failureCategory)));
    }

    public void RecordEventReportProviderCallback(
        string? provider,
        string? outcome,
        string? failureCategory = null)
    {
        _eventReportProviderCallbacks.Add(1,
            new KeyValuePair<string, object?>("provider", NormalizeEventReportProvider(provider)),
            new KeyValuePair<string, object?>("outcome", NormalizeEventReportOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeEventReportFailureCategory(failureCategory)));
    }

    public void RecordRegistrationCreated(string? tenantId = null)
    {
        _registrationsCreated.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"));
    }

    public void RecordRegistrationProviderManagementAction(string action, string outcome)
    {
        _registrationProviderManagementActions.Add(1,
            new KeyValuePair<string, object?>("action", NormalizeTag(action)),
            new KeyValuePair<string, object?>("outcome", NormalizeTag(outcome)));
    }

    public void RecordRegistrationProviderSubscriptionOperation(string? operation, string? outcome)
    {
        _registrationProviderSubscriptionOperations.Add(1,
            new KeyValuePair<string, object?>("operation", NormalizeRegistrationProviderSubscriptionOperation(operation)),
            new KeyValuePair<string, object?>("outcome", NormalizeRegistrationProviderSubscriptionOutcome(outcome)));
    }

    public void RecordRegistrationProviderSubscriptionWatchCount(string? status, long count)
    {
        _registrationProviderSubscriptionWatches.Record(Math.Max(0, count),
            new KeyValuePair<string, object?>("status", NormalizeRegistrationProviderWatchStatus(status)));
    }

    public void RecordRefundOperation(string? operation, string? status, string? outcome) =>
        _refundOperations.Add(
            1,
            new KeyValuePair<string, object?>("operation", NormalizeRefundOperation(operation)),
            new KeyValuePair<string, object?>("status", NormalizeRefundStatus(status)),
            new KeyValuePair<string, object?>("outcome", NormalizeRefundOutcome(outcome)));

    public void RecordRefundCampaignOperation(string? kind, string? status, string? outcome) =>
        _refundCampaignOperations.Add(
            1,
            new KeyValuePair<string, object?>("kind", NormalizeRefundCampaignKind(kind)),
            new KeyValuePair<string, object?>("status", NormalizeRefundCampaignStatus(status)),
            new KeyValuePair<string, object?>("outcome", NormalizeRefundOutcome(outcome)));

    public void RecordOrganizationCreated(string? tenantId = null)
    {
        _organizationsCreated.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"));
    }

    /// <summary>
    /// Records one authorization decision.
    /// <para>
    /// Every dimension here is drawn from a closed set: resource kinds and actions come from the
    /// capability catalog, outcomes and reason codes from fixed constants, providers from the three
    /// provider identifiers. Nothing derived from a request — no tenant, user, or resource identifier,
    /// no token, no fact value — is admitted, because a metric dimension that grows with traffic turns
    /// the metrics backend into a per-request store.
    /// </para>
    /// <para>
    /// The observed policy revision is deliberately <em>not</em> a dimension. It is bounded at any instant
    /// but changes on every policy publish, so over time it is unbounded. It belongs on the span and in
    /// the log line, where a single decision can be attributed to a single policy set, and is passed here
    /// only so the caller has one place to send everything.
    /// </para>
    /// </summary>
    /// <param name="resourceKind">Catalogued resource kind. Never a resource identifier.</param>
    /// <param name="action">Catalogued action verb.</param>
    /// <param name="outcome">Decision outcome, allowed or denied.</param>
    /// <param name="reasonCode">Closed-set reason code explaining the outcome.</param>
    /// <param name="providerId">Which provider decided: local, cerbos, or runtime.</param>
    /// <param name="durationMs">Wall time spent reaching the decision.</param>
    public void RecordAuthorizationDecision(
        string resourceKind,
        string action,
        string outcome,
        string reasonCode,
        string providerId,
        double durationMs)
    {
        var dimensions = new TagList
        {
            { "resource_kind", resourceKind },
            { "action", action },
            { "outcome", outcome },
            { "reason_code", reasonCode },
            { "provider", providerId }
        };

        _authorizationDecisions.Add(1, dimensions);
        _authorizationDecisionDuration.Record(Math.Max(0, durationMs), dimensions);
    }

    public void RecordSupportAccessLifecycleEvent(
        string? eventType,
        string? mode,
        string? outcome,
        string? failureCategory = null)
    {
        _supportAccessLifecycleEvents.Add(1,
            new KeyValuePair<string, object?>("event_type", NormalizeSupportAccessLifecycleEvent(eventType)),
            new KeyValuePair<string, object?>("mode", NormalizeSupportAccessMode(mode)),
            new KeyValuePair<string, object?>("outcome", NormalizeSupportAccessOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeSupportAccessFailureCategory(failureCategory)));
    }

    public void RecordSupportAccessRequestAudit(
        string? eventType,
        string? outcome,
        string? persistenceOutcome,
        string? failureCategory = null)
    {
        _supportAccessRequestAudits.Add(1,
            new KeyValuePair<string, object?>("event_type", NormalizeSupportAccessAuditEvent(eventType)),
            new KeyValuePair<string, object?>("outcome", NormalizeSupportAccessOutcome(outcome)),
            new KeyValuePair<string, object?>("persistence_outcome", NormalizeSupportAccessPersistenceOutcome(persistenceOutcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeSupportAccessFailureCategory(failureCategory)));
    }

    public void RecordSupportAccessBoundaryDenial(string? reason, string? action, string? mode)
    {
        _supportAccessBoundaryDenials.Add(1,
            new KeyValuePair<string, object?>("reason", NormalizeSupportAccessFailureCategory(reason)),
            new KeyValuePair<string, object?>("mode", NormalizeSupportAccessMode(mode)),
            new KeyValuePair<string, object?>("action_class", NormalizeSupportAccessActionClass(action)));
    }

    public void RecordSupportAccessSessionValidationDenial(string? reason, string? mode)
    {
        _supportAccessSessionValidationDenials.Add(1,
            new KeyValuePair<string, object?>("reason", NormalizeSupportAccessFailureCategory(reason)),
            new KeyValuePair<string, object?>("mode", NormalizeSupportAccessMode(mode)));
    }

    public void RecordEventRoleAssignmentChanged(string operation, string outcome, string? roleCode = null)
    {
        _eventRoleAssignmentChanged.Add(1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("role", roleCode ?? "unknown"));
    }

    public void RecordExternalApiKeyCreated(string? tenantId = null, string? ownerType = null)
    {
        _externalApiKeysCreated.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("owner_type", ownerType ?? "unknown"));
    }

    public void RecordExternalApiKeyRevoked(string? tenantId = null, string? ownerType = null)
    {
        _externalApiKeysRevoked.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("owner_type", ownerType ?? "unknown"));
    }

    public void RecordExternalApiKeyAuthentication(string outcome, string? tenantId = null, string? ownerType = null)
    {
        _externalApiKeyAuthenticationAttempts.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("owner_type", ownerType ?? "unknown"),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordExternalApiKeyThrottle(string policy, string? tenantId = null, string? ownerType = null)
    {
        _externalApiKeyThrottleEvents.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("owner_type", ownerType ?? "unknown"),
            new KeyValuePair<string, object?>("policy", policy));
    }

    public void RecordExternalApiKeyPolicyUpdated(string? tenantId = null, string? ownerType = null)
    {
        _externalApiKeyPolicyUpdated.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("owner_type", ownerType ?? "unknown"));
    }

    public void RecordExternalApiKeyRotated(string? tenantId = null, string? ownerType = null)
    {
        _externalApiKeyRotated.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("owner_type", ownerType ?? "unknown"));
    }

    public void RecordEmailDispatchAttempt(string? outcome = null, string? failureCategory = null)
    {
        _emailDispatchAttempts.Add(1,
            new KeyValuePair<string, object?>("outcome", NormalizeEmailDispatchAttemptOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeEmailDispatchFailureCategory(failureCategory)));
    }

    public void RecordEmailDispatchOperationalOutcome(string? outcome, string? reason)
    {
        _emailDispatchOperationalOutcomes.Add(1,
            new KeyValuePair<string, object?>("outcome", NormalizeEmailDispatchOperationalOutcome(outcome)),
            new KeyValuePair<string, object?>("reason", NormalizeEmailDispatchOperationalReason(reason)));
    }

    public void RecordEmailDispatchRabbitMqPublish(string? outcome = null, string? failureCategory = null)
    {
        _emailDispatchRabbitMqPublishes.Add(1,
            new KeyValuePair<string, object?>("outcome", NormalizeEmailDispatchRabbitMqPublishOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeEmailDispatchRabbitMqPublishFailureCategory(failureCategory)));
    }

    public void RecordEmailDispatchRabbitMqConsume(string? outcome = null, string? failureCategory = null)
    {
        _emailDispatchRabbitMqConsumes.Add(1,
            new KeyValuePair<string, object?>("outcome", NormalizeEmailDispatchRabbitMqConsumeOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeEmailDispatchRabbitMqConsumeFailureCategory(failureCategory)));
    }

    public void RecordEmailDispatchTenantBacklog(int sampleRank, long count)
    {
        _emailDispatchTenantBacklog.Record(Math.Max(0, count),
            new KeyValuePair<string, object?>("sample_rank", Math.Clamp(sampleRank, 1, 100)));
    }

    public void RecordEmailDispatchOldestPendingAge(double seconds)
    {
        _emailDispatchOldestPendingAge.Record(Math.Max(0, seconds));
    }

    public void RecordEmailDispatchOptionalReminderDeferral(bool active)
    {
        Volatile.Write(ref _emailDispatchOptionalReminderDeferralState, active ? 1 : 0);
    }

    public void RecordNotificationFanoutRun(string? fanoutKind, string? outcome)
    {
        _notificationFanoutRuns.Add(1,
            new KeyValuePair<string, object?>("fanout_kind", NormalizeNotificationFanoutKind(fanoutKind)),
            new KeyValuePair<string, object?>("outcome", NormalizeNotificationFanoutOutcome(outcome)));
    }

    public void RecordNotificationFanoutSubscribers(long subscriberCount, string? fanoutKind, string? outcome)
    {
        if (subscriberCount <= 0)
        {
            return;
        }

        _notificationFanoutSubscribers.Add(subscriberCount,
            new KeyValuePair<string, object?>("fanout_kind", NormalizeNotificationFanoutKind(fanoutKind)),
            new KeyValuePair<string, object?>("outcome", NormalizeNotificationFanoutOutcome(outcome)));
    }

    public void RecordNotificationFanoutProcessorClaims(long count, string? outcome)
    {
        if (count <= 0)
        {
            return;
        }

        _notificationFanoutProcessorClaims.Add(count,
            new KeyValuePair<string, object?>("outcome", NormalizeNotificationFanoutProcessorOutcome(outcome)));
    }

    public void RecordNotificationFanoutProcessorRecipients(long count, string? outcome)
    {
        if (count <= 0)
        {
            return;
        }

        _notificationFanoutProcessorRecipients.Add(count,
            new KeyValuePair<string, object?>("outcome", NormalizeNotificationFanoutRecipientOutcome(outcome)));
    }

    public void RecordNotificationFanoutProcessorSnapshot(
        long dueOccurrenceCount,
        long dueCoreOccurrenceCount,
        long dueOptionalReminderCount,
        long activeClaimCount,
        long expiredClaimCount,
        long supersededOccurrenceCount,
        long processedRecipientCount,
        long oldestDueAgeSeconds,
        bool optionalReminderDeferralActive)
    {
        Interlocked.Exchange(ref _notificationFanoutDueOccurrenceCount, Math.Max(0, dueOccurrenceCount));
        Interlocked.Exchange(ref _notificationFanoutDueCoreOccurrenceCount, Math.Max(0, dueCoreOccurrenceCount));
        Interlocked.Exchange(ref _notificationFanoutDueOptionalReminderCount, Math.Max(0, dueOptionalReminderCount));
        Interlocked.Exchange(ref _notificationFanoutActiveClaimCount, Math.Max(0, activeClaimCount));
        Interlocked.Exchange(ref _notificationFanoutExpiredClaimCount, Math.Max(0, expiredClaimCount));
        Interlocked.Exchange(ref _notificationFanoutSupersededOccurrenceCount, Math.Max(0, supersededOccurrenceCount));
        Interlocked.Exchange(ref _notificationFanoutProcessedRecipientCount, Math.Max(0, processedRecipientCount));
        Interlocked.Exchange(
            ref _notificationFanoutRemainingOccurrenceCount,
            Math.Max(0, dueOccurrenceCount + activeClaimCount));
        Interlocked.Exchange(ref _notificationFanoutOldestDueAgeSeconds, Math.Max(0, oldestDueAgeSeconds));
        Volatile.Write(ref _notificationFanoutOptionalReminderDeferralState,
            optionalReminderDeferralActive ? 1 : 0);
    }

    public void RecordWebhookMessageCreated(
        string? eventType,
        string? provider,
        string? outcome)
    {
        _webhookMessagesCreated.Add(1,
            new KeyValuePair<string, object?>("event_type", NormalizeWebhookEventType(eventType)),
            new KeyValuePair<string, object?>("provider", NormalizeWebhookProvider(provider)),
            new KeyValuePair<string, object?>("outcome", NormalizeWebhookOutcome(outcome)));
    }

    public void RecordWebhookDeliveryAttempt(
        string? eventType,
        string? outcome,
        string? failureCategory = null)
    {
        _webhookDeliveryAttempts.Add(1,
            new KeyValuePair<string, object?>("event_type", NormalizeWebhookEventType(eventType)),
            new KeyValuePair<string, object?>("outcome", NormalizeWebhookOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeWebhookFailureCategory(failureCategory)));
    }

    public void RecordWebhookDeliverySuccess(string? eventType)
    {
        _webhookDeliverySuccess.Add(1,
            new KeyValuePair<string, object?>("event_type", NormalizeWebhookEventType(eventType)));
    }

    public void RecordWebhookDeliveryFailure(
        string? eventType,
        string? outcome,
        string? failureCategory)
    {
        _webhookDeliveryFailure.Add(1,
            new KeyValuePair<string, object?>("event_type", NormalizeWebhookEventType(eventType)),
            new KeyValuePair<string, object?>("outcome", NormalizeWebhookOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeWebhookFailureCategory(failureCategory)));
    }

    public void RecordWebhookEndpointDisabled(string? failureCategory)
    {
        _webhookEndpointDisabled.Add(1,
            new KeyValuePair<string, object?>("failure_category", NormalizeWebhookFailureCategory(failureCategory)));
    }

    public void RecordWebhookManualRetry(
        string? eventType,
        string? outcome,
        string? failureCategory = null)
    {
        _webhookManualRetries.Add(1,
            new KeyValuePair<string, object?>("event_type", NormalizeWebhookEventType(eventType)),
            new KeyValuePair<string, object?>("outcome", NormalizeWebhookOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeWebhookFailureCategory(failureCategory)));
    }

    public void RecordWebhookProviderPublishFailure(
        string? eventType,
        string? provider,
        string? failureCategory)
    {
        _webhookProviderPublishFailures.Add(1,
            new KeyValuePair<string, object?>("event_type", NormalizeWebhookEventType(eventType)),
            new KeyValuePair<string, object?>("provider", NormalizeWebhookProvider(provider)),
            new KeyValuePair<string, object?>("failure_category", NormalizeWebhookFailureCategory(failureCategory)));
    }

    public void RecordWebhookRetentionCleanupRun(string? mode, string? outcome)
    {
        _webhookRetentionCleanupRuns.Add(1,
            new KeyValuePair<string, object?>("mode", NormalizeWebhookCleanupMode(mode)),
            new KeyValuePair<string, object?>("outcome", NormalizeWebhookOutcome(outcome)));
    }

    public void RecordWebhookRetentionCleanupItems(long itemCount, string? mode, string? dataKind)
    {
        if (itemCount <= 0)
        {
            return;
        }

        _webhookRetentionCleanupItems.Add(itemCount,
            new KeyValuePair<string, object?>("mode", NormalizeWebhookCleanupMode(mode)),
            new KeyValuePair<string, object?>("data_kind", NormalizeWebhookCleanupDataKind(dataKind)));
    }

    public void RecordWebhookClaimLag(
        WebhookTelemetryProvider provider,
        WebhookTelemetryOperation operation,
        TimeSpan claimLag)
    {
        _webhookClaimLag.Record(
            Math.Max(0, claimLag.TotalSeconds),
            new KeyValuePair<string, object?>("provider", WebhookTelemetryDimensionCodes.Provider(provider)),
            new KeyValuePair<string, object?>("operation", WebhookTelemetryDimensionCodes.Operation(operation)));
    }

    public void RecordWebhookProcessingOutcome(
        WebhookTelemetryProvider provider,
        WebhookTelemetryOperation operation,
        WebhookTelemetryOutcome outcome,
        long count = 1)
    {
        if (count <= 0)
        {
            return;
        }

        _webhookProcessingOutcomes.Add(
            count,
            new KeyValuePair<string, object?>("provider", WebhookTelemetryDimensionCodes.Provider(provider)),
            new KeyValuePair<string, object?>("operation", WebhookTelemetryDimensionCodes.Operation(operation)),
            new KeyValuePair<string, object?>("outcome", WebhookTelemetryDimensionCodes.Outcome(outcome)));
    }

    public void RecordWebhookRetryScheduled(
        WebhookTelemetryProvider provider,
        WebhookTelemetryOperation operation,
        long count = 1)
    {
        RecordWebhookCount(_webhookRetriesScheduled, provider, operation, count);
    }

    public void RecordWebhookDeadLetter(
        WebhookTelemetryProvider provider,
        WebhookTelemetryOperation operation,
        long count = 1)
    {
        RecordWebhookCount(_webhookDeadLetters, provider, operation, count);
    }

    public void RecordWebhookManualReconciliation(
        WebhookTelemetryProvider provider,
        long count = 1)
    {
        if (count <= 0)
        {
            return;
        }

        _webhookManualReconciliations.Add(
            count,
            new KeyValuePair<string, object?>("provider", WebhookTelemetryDimensionCodes.Provider(provider)));
    }

    public void RecordWebhookEndpointAutoPause(
        WebhookTelemetryProvider provider,
        long count = 1)
    {
        if (count <= 0)
        {
            return;
        }

        _webhookEndpointAutoPauses.Add(
            count,
            new KeyValuePair<string, object?>("provider", WebhookTelemetryDimensionCodes.Provider(provider)));
    }

    public void RecordWebhookProviderHealthCheck(
        WebhookTelemetryProvider provider,
        WebhookTelemetryOutcome outcome)
    {
        _webhookProviderHealthChecks.Add(
            1,
            new KeyValuePair<string, object?>("provider", WebhookTelemetryDimensionCodes.Provider(provider)),
            new KeyValuePair<string, object?>("outcome", WebhookTelemetryDimensionCodes.Outcome(outcome)));
    }

    public void RecordWebhookPublicationUnknownAge(
        WebhookTelemetryProvider provider,
        TimeSpan unknownAge)
    {
        _webhookPublicationUnknownAge.Record(
            Math.Max(0, unknownAge.TotalSeconds),
            new KeyValuePair<string, object?>("provider", WebhookTelemetryDimensionCodes.Provider(provider)));
    }

    public void RecordQueueDrainHealth(string jobName, string outcome, int backlog, int staleWork)
    {
        if (!ScheduledJobNames.All.Contains(jobName))
        {
            return;
        }

        string boundedOutcome = outcome switch
        {
            "healthy" => "healthy",
            "degraded" => "degraded",
            "disabled" => "disabled",
            _ => "unhealthy"
        };
        KeyValuePair<string, object?> jobTag = new("job_name", jobName);
        _queueDrainHealthChecks.Add(
            1,
            jobTag,
            new KeyValuePair<string, object?>("outcome", boundedOutcome));
        _queueDrainBacklog.Record(Math.Max(0, backlog), jobTag);
        _queueDrainStaleWork.Record(Math.Max(0, staleWork), jobTag);
    }

    public void RecordCustomPropertyPurgeDecision(string? tenantId, string scope, string outcome, string blockerCategory)
    {
        _customPropertyPurgeDecisions.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId ?? "default"),
            new KeyValuePair<string, object?>("scope", NormalizeTag(scope)),
            new KeyValuePair<string, object?>("outcome", NormalizeTag(outcome)),
            new KeyValuePair<string, object?>("blocker_category", NormalizeTag(blockerCategory)));
    }

    public void RecordIdempotencyCleanupRun(string mode, string outcome)
    {
        _idempotencyCleanupRuns.Add(1,
            new KeyValuePair<string, object?>("mode", NormalizeTag(mode)),
            new KeyValuePair<string, object?>("outcome", NormalizeTag(outcome)));
    }

    public void RecordIdempotencyCleanupRows(long rowCount, string mode, string outcome)
    {
        _idempotencyCleanupRows.Add(rowCount,
            new KeyValuePair<string, object?>("mode", NormalizeTag(mode)),
            new KeyValuePair<string, object?>("outcome", NormalizeTag(outcome)));
    }

    public void RecordAtprotoTransientCleanup(bool succeeded, int transientRows = 0, int replayRows = 0)
    {
        _atprotoTransientCleanupRuns.Add(1, new KeyValuePair<string, object?>("outcome", succeeded ? "succeeded" : "failed"));
        if (!succeeded) return;
        _atprotoTransientCleanupRows.Add(Math.Max(0, transientRows), new KeyValuePair<string, object?>("store", "transients"));
        _atprotoTransientCleanupRows.Add(Math.Max(0, replayRows), new KeyValuePair<string, object?>("store", "assertions"));
    }

    public void RecordAiRetentionCleanupRun(string mode, string outcome)
    {
        _aiRetentionCleanupRuns.Add(1,
            new KeyValuePair<string, object?>("mode", NormalizeTag(mode)),
            new KeyValuePair<string, object?>("outcome", NormalizeTag(outcome)));
    }

    public void RecordAiRetentionCleanupRows(long rowCount, string mode, string category)
    {
        if (rowCount <= 0)
        {
            return;
        }

        _aiRetentionCleanupRows.Add(rowCount,
            new KeyValuePair<string, object?>("mode", NormalizeTag(mode)),
            new KeyValuePair<string, object?>("category", NormalizeAiRetentionCleanupCategory(category)));
    }

    public void RecordAiProviderHealthCheck(string? provider, string? status, string? reason)
    {
        _aiProviderHealthChecks.Add(1,
            new KeyValuePair<string, object?>("provider", NormalizeAiProvider(provider)),
            new KeyValuePair<string, object?>("status", NormalizeTag(status)),
            new KeyValuePair<string, object?>("reason", NormalizeTag(reason)));
    }

    public void RecordAiProviderRequest(string? provider, string? outcome, string? failureCategory = null)
    {
        _aiProviderRequests.Add(1,
            new KeyValuePair<string, object?>("provider", NormalizeAiProvider(provider)),
            new KeyValuePair<string, object?>("outcome", NormalizeAiProviderOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeAiProviderFailureCategory(failureCategory)));
    }

    public void RecordAiProviderRequestDuration(
        TimeSpan duration,
        string? provider,
        string? outcome,
        string? failureCategory = null)
    {
        if (duration < TimeSpan.Zero)
        {
            return;
        }

        _aiProviderRequestDuration.Record(duration.TotalSeconds,
            new KeyValuePair<string, object?>("provider", NormalizeAiProvider(provider)),
            new KeyValuePair<string, object?>("outcome", NormalizeAiProviderOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeAiProviderFailureCategory(failureCategory)));
    }

    public void RecordAiProviderTokenUsage(
        string? provider,
        int? inputTokens,
        int? outputTokens,
        int? totalTokens)
    {
        RecordAiProviderTokenUsage(provider, "input", inputTokens);
        RecordAiProviderTokenUsage(provider, "output", outputTokens);
        RecordAiProviderTokenUsage(provider, "total", totalTokens);
    }

    public void RecordAiProviderProposedActions(string? provider, int count, string? actionKind)
    {
        if (count <= 0)
        {
            return;
        }

        _aiProviderProposedActions.Add(count,
            new KeyValuePair<string, object?>("provider", NormalizeAiProvider(provider)),
            new KeyValuePair<string, object?>("action_kind", NormalizeAiProposedActionKind(actionKind)));
    }

    public void RecordStorageUploadSession(
        string? provider,
        string? operation,
        string? outcome,
        string? failureCategory = null)
    {
        _storageUploadSessions.Add(1,
            new KeyValuePair<string, object?>("provider", NormalizeStorageProvider(provider)),
            new KeyValuePair<string, object?>("operation", NormalizeStorageOperation(operation)),
            new KeyValuePair<string, object?>("outcome", NormalizeStorageOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeStorageFailureCategory(failureCategory)));
    }

    public void RecordStorageUploadBytes(
        long byteCount,
        string? provider,
        string? outcome,
        string? failureCategory = null)
    {
        if (byteCount < 0)
        {
            return;
        }

        _storageUploadBytes.Record(byteCount,
            new KeyValuePair<string, object?>("provider", NormalizeStorageProvider(provider)),
            new KeyValuePair<string, object?>("outcome", NormalizeStorageOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeStorageFailureCategory(failureCategory)));
    }

    public void RecordStorageRead(
        string? provider,
        string? outcome,
        string? failureCategory,
        string? visibility)
    {
        _storageReads.Add(1,
            new KeyValuePair<string, object?>("provider", NormalizeStorageProvider(provider)),
            new KeyValuePair<string, object?>("outcome", NormalizeStorageOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeStorageFailureCategory(failureCategory)),
            new KeyValuePair<string, object?>("visibility", NormalizeStorageVisibility(visibility)));
    }

    public void RecordStorageReadBytes(
        long byteCount,
        string? provider,
        string? outcome,
        string? visibility)
    {
        if (byteCount < 0)
        {
            return;
        }

        _storageReadBytes.Record(byteCount,
            new KeyValuePair<string, object?>("provider", NormalizeStorageProvider(provider)),
            new KeyValuePair<string, object?>("outcome", NormalizeStorageOutcome(outcome)),
            new KeyValuePair<string, object?>("visibility", NormalizeStorageVisibility(visibility)));
    }

    public void RecordStorageDelete(string? provider, string? outcome, string? failureCategory = null)
    {
        _storageDeletes.Add(1,
            new KeyValuePair<string, object?>("provider", NormalizeStorageProvider(provider)),
            new KeyValuePair<string, object?>("outcome", NormalizeStorageOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeStorageFailureCategory(failureCategory)));
    }

    public void RecordStorageQuotaReservation(
        string? provider,
        string? operation,
        string? outcome,
        string? failureCategory = null)
    {
        _storageQuotaReservations.Add(1,
            new KeyValuePair<string, object?>("provider", NormalizeStorageProvider(provider)),
            new KeyValuePair<string, object?>("operation", NormalizeStorageOperation(operation)),
            new KeyValuePair<string, object?>("outcome", NormalizeStorageOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeStorageFailureCategory(failureCategory)));
    }

    public void RecordStorageQuotaBytes(
        long byteCount,
        string? provider,
        string? operation,
        string? outcome)
    {
        if (byteCount < 0)
        {
            return;
        }

        _storageQuotaBytes.Record(byteCount,
            new KeyValuePair<string, object?>("provider", NormalizeStorageProvider(provider)),
            new KeyValuePair<string, object?>("operation", NormalizeStorageOperation(operation)),
            new KeyValuePair<string, object?>("outcome", NormalizeStorageOutcome(outcome)));
    }

    public void RecordStorageProviderTest(string? provider, string? outcome, string? failureCategory = null)
    {
        _storageProviderTests.Add(1,
            new KeyValuePair<string, object?>("provider", NormalizeStorageProvider(provider)),
            new KeyValuePair<string, object?>("outcome", NormalizeStorageOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeStorageFailureCategory(failureCategory)));
    }

    public void RecordStorageReconciliationRun(string? mode, string? outcome, string? failureCategory = null)
    {
        _storageReconciliationRuns.Add(1,
            new KeyValuePair<string, object?>("mode", NormalizeStorageReconciliationMode(mode)),
            new KeyValuePair<string, object?>("outcome", NormalizeStorageOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeStorageFailureCategory(failureCategory)));
    }

    public void RecordStorageReconciliationObjects(
        long count,
        string? provider,
        string? category,
        string? action,
        string? outcome,
        string? failureCategory = null)
    {
        if (count <= 0)
        {
            return;
        }

        _storageReconciliationObjects.Add(count,
            new KeyValuePair<string, object?>("provider", NormalizeStorageProvider(provider)),
            new KeyValuePair<string, object?>("category", NormalizeStorageReconciliationCategory(category)),
            new KeyValuePair<string, object?>("action", NormalizeStorageReconciliationAction(action)),
            new KeyValuePair<string, object?>("outcome", NormalizeStorageOutcome(outcome)),
            new KeyValuePair<string, object?>("failure_category", NormalizeStorageFailureCategory(failureCategory)));
    }

    private static string NormalizeTag(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeRegistrationProviderSubscriptionOperation(string? operation) => NormalizeTag(operation) switch
    {
        "renewal" => "renewal",
        "sweep" => "sweep",
        _ => "unknown"
    };

    private static string NormalizeRegistrationProviderSubscriptionOutcome(string? outcome) => NormalizeTag(outcome) switch
    {
        "success" => "success",
        "failure" => "failure",
        "unsupported" => "unsupported",
        "missing_binding" => "missing_binding",
        _ => "unknown"
    };

    private static string NormalizeRegistrationProviderWatchStatus(string? status) => NormalizeTag(status) switch
    {
        "expiring" => "expiring",
        "expired" => "expired",
        _ => "unknown"
    };

    private static string NormalizeRefundOperation(string? operation) => NormalizeTag(operation) switch
    {
        "dispatch" => "dispatch",
        "reconcile" => "reconcile",
        _ => "unknown"
    };

    private static string NormalizeRefundStatus(string? status) => NormalizeTag(status) switch
    {
        "requested" => "requested",
        "dispatchpending" => "dispatch_pending",
        "pending" => "pending",
        "requiresaction" => "requires_action",
        "succeeded" => "succeeded",
        "failed" => "failed",
        "unknown" => "unknown",
        _ => "unknown"
    };

    private static string NormalizeRefundOutcome(string? outcome) => NormalizeTag(outcome) switch
    {
        "completed" => "completed",
        "retry" => "retry",
        "failed" => "failed",
        _ => "unknown"
    };

    private static string NormalizeRefundCampaignKind(string? kind) => NormalizeTag(kind) switch
    {
        "eventcancellation" => "event_cancellation",
        "materialchange" => "material_change",
        _ => "unknown"
    };

    private static string NormalizeRefundCampaignStatus(string? status) => NormalizeTag(status) switch
    {
        "pending" => "pending",
        "processing" => "processing",
        "completed" => "completed",
        "requiresoperator" => "requires_operator",
        _ => "unknown"
    };

    private static string NormalizeEventPublicActionKind(EventPublicActionKindEnum actionKind)
    {
        return actionKind switch
        {
            EventPublicActionKindEnum.OriginalSource => "original_source",
            EventPublicActionKindEnum.ExternalEventPage => "external_event_page",
            EventPublicActionKindEnum.ExternalRegistration => "external_registration",
            EventPublicActionKindEnum.OptionalQuestionnaire => "optional_questionnaire",
            EventPublicActionKindEnum.Livestream => "livestream",
            EventPublicActionKindEnum.OrganizerContact => "organizer_contact",
            _ => "unknown"
        };
    }

    private static string NormalizeEventPublicActionSurface(string? surface)
    {
        return NormalizeTag(surface) switch
        {
            "event_detail" => "event_detail",
            "event_list" => "event_list",
            "event_preview" => "event_preview",
            _ => "other"
        };
    }

    private static string NormalizeNotificationFanoutKind(string? fanoutKind) => NormalizeTag(fanoutKind) switch
    {
        "event-published" => "event-published",
        "event-moderated-light" => "event-moderated-light",
        "event-moderated-heavy" => "event-moderated-heavy",
        "recipient_occurrence" => "recipient_occurrence",
        _ => "unknown"
    };

    private static string NormalizeNotificationFanoutOutcome(string? outcome) => NormalizeTag(outcome) switch
    {
        "processing" => "processing",
        "processed" => "processed",
        "notification_created" => "notification_created",
        "duplicate_skipped" => "duplicate_skipped",
        "skipped_completed" => "skipped_completed",
        "completed" => "completed",
        "failed" => "failed",
        _ => "unknown"
    };

    private static string NormalizeNotificationFanoutProcessorOutcome(string? outcome) => NormalizeTag(outcome) switch
    {
        "claimed" => "claimed",
        "lease_contention" => "lease_contention",
        "capacity_deferred" => "capacity_deferred",
        "unavailable" => "unavailable",
        "completed" => "completed",
        "stale_claim" => "stale_claim",
        "failed" => "failed",
        _ => "unknown"
    };

    private static string NormalizeNotificationFanoutRecipientOutcome(string? outcome) => NormalizeTag(outcome) switch
    {
        "processed" => "processed",
        "notification_created" => "notification_created",
        _ => "unknown"
    };

    private static string NormalizeWebhookEventType(string? eventType)
    {
        var normalized = NormalizeTag(eventType);
        if (normalized is "unknown" || normalized.Length > 100)
        {
            return "unknown";
        }

        foreach (var current in normalized)
        {
            if (!char.IsAsciiLetterOrDigit(current) && current is not '.' and not '_' and not '-')
            {
                return "unknown";
            }
        }

        return normalized;
    }

    private static string NormalizeSupportAccessLifecycleEvent(string? eventType)
    {
        return NormalizeTag(eventType) switch
        {
            "start" or "started" => "started",
            "stop" or "stopped" => "stopped",
            "expire" or "expired" => "expired",
            "revoke" or "revoked" => "revoked",
            "force_stop" or "force_stopped" or "force-stopped" => "force_stopped",
            _ => "unknown"
        };
    }

    private static string NormalizeSupportAccessAuditEvent(string? eventType)
    {
        return NormalizeTag(eventType) switch
        {
            "requestobserved" or "request_observed" => "request_observed",
            "commandcommitted" or "command_committed" => "command_committed",
            _ => "unknown"
        };
    }

    private static string NormalizeSupportAccessMode(string? mode)
    {
        return NormalizeTag(mode) switch
        {
            "readonly" or "read_only" or "read-only" => "read_only",
            "write" => "write",
            "inactive" => "inactive",
            _ => "unknown"
        };
    }

    private static string NormalizeSupportAccessOutcome(string? outcome)
    {
        return NormalizeTag(outcome) switch
        {
            "success" or "succeeded" => "succeeded",
            "failure" or "failed" => "failed",
            "denied" => "denied",
            "client_error" => "client_error",
            "server_error" => "server_error",
            "observed" => "observed",
            "skipped" => "skipped",
            _ => "unknown"
        };
    }

    private static string NormalizeSupportAccessPersistenceOutcome(string? outcome)
    {
        return NormalizeTag(outcome) switch
        {
            "persisted" => "persisted",
            "failed" or "failure" => "failed",
            "skipped" => "skipped",
            _ => "unknown"
        };
    }

    private static string NormalizeSupportAccessActionClass(string? action)
    {
        return NormalizeTag(action) switch
        {
            "view" or "list" or "view_audit" or "viewaudit" or "download" or "presigned_download" or "presigneddownload" => "read",
            "create" or "update" or "delete" or "stop" or "force_stop" or "forcestop" or "force-stop" => "write",
            var value when value.EndsWith(":view", StringComparison.Ordinal) => "read",
            var value when value.EndsWith(":view-delivery", StringComparison.Ordinal) => "read",
            _ => "unknown"
        };
    }

    private static string NormalizeSupportAccessFailureCategory(string? failureCategory)
    {
        return NormalizeTag(failureCategory ?? "none") switch
        {
            "none" => "none",
            SupportAccessFailureCodes.ValidationFailed => SupportAccessFailureCodes.ValidationFailed,
            SupportAccessFailureCodes.Disabled => SupportAccessFailureCodes.Disabled,
            SupportAccessFailureCodes.WriteModeDisabled => SupportAccessFailureCodes.WriteModeDisabled,
            SupportAccessFailureCodes.DurationExceedsPolicy => SupportAccessFailureCodes.DurationExceedsPolicy,
            SupportAccessFailureCodes.TicketReferenceRequired => SupportAccessFailureCodes.TicketReferenceRequired,
            SupportAccessFailureCodes.ActorNotResolved => SupportAccessFailureCodes.ActorNotResolved,
            SupportAccessFailureCodes.TargetTenantNotFound => SupportAccessFailureCodes.TargetTenantNotFound,
            SupportAccessFailureCodes.TargetTenantUserMismatch => SupportAccessFailureCodes.TargetTenantUserMismatch,
            SupportAccessFailureCodes.ActiveSessionExists => SupportAccessFailureCodes.ActiveSessionExists,
            SupportAccessFailureCodes.SessionNotFound => SupportAccessFailureCodes.SessionNotFound,
            SupportAccessFailureCodes.SessionNotActive => SupportAccessFailureCodes.SessionNotActive,
            SupportAccessFailureCodes.ConcurrencyConflict => SupportAccessFailureCodes.ConcurrencyConflict,
            "support_access_inactive" => "support_access_inactive",
            "support_access_read_only" => "support_access_read_only",
            "support_access_missing_target_tenant" => "support_access_missing_target_tenant",
            "support_access_missing_tenant_context" => "support_access_missing_tenant_context",
            "support_access_target_tenant_mismatch" => "support_access_target_tenant_mismatch",
            "support_access_audit_persistence_failed" => "support_access_audit_persistence_failed",
            _ => "unknown"
        };
    }

    private static string NormalizeWebhookProvider(string? provider)
    {
        return NormalizeTag(provider) switch
        {
            "disabled" => "disabled",
            "local" => "local",
            "svix" => "svix",
            "composite" => "composite",
            "dryrun" or "dry_run" => "dry_run",
            _ => "unknown"
        };
    }

    private static string NormalizeWebhookOutcome(string? outcome)
    {
        return NormalizeTag(outcome) switch
        {
            "created" => "created",
            "queued" => "queued",
            "succeeded" or "success" => "succeeded",
            "retry_scheduled" or "retry" => "retry_scheduled",
            "abandoned" => "abandoned",
            "failed" or "failure" => "failed",
            "partial_failure" => "partial_failure",
            "skipped" => "skipped",
            "missing" => "missing",
            "already_claimed" => "already_claimed",
            "already_settled" => "already_settled",
            "deferred" => "deferred",
            "disabled" => "disabled",
            _ => "unknown"
        };
    }

    private static string NormalizeWebhookFailureCategory(string? failureCategory)
    {
        return NormalizeTag(failureCategory ?? "none") switch
        {
            "none" => "none",
            "provider_disabled" => "provider_disabled",
            "processing_lease_expired" => "processing_lease_expired",
            "missing_endpoint" => "missing_endpoint",
            "missing_message" => "missing_message",
            "endpoint_not_active" => "endpoint_not_active",
            "payload_unavailable" => "payload_unavailable",
            "payload_too_large" => "payload_too_large",
            "missing_secret" => "missing_secret",
            "invalid_secret" => "invalid_secret",
            "invalid_url" => "invalid_url",
            "redirect_response" => "redirect_response",
            "http_non_success" => "http_non_success",
            "timeout" => "timeout",
            "network_error" => "network_error",
            "private_network_blocked" => "private_network_blocked",
            "localhost_blocked" => "localhost_blocked",
            "metadata_address_blocked" => "metadata_address_blocked",
            "dns_resolution_failed" => "dns_resolution_failed",
            "endpoint_status_not_retryable" => "endpoint_status_not_retryable",
            "attempt_status_not_retryable" => "attempt_status_not_retryable",
            "message_payload_cleared" => "message_payload_cleared",
            "webhook_provider_failed" => "webhook_provider_failed",
            "unsupported_webhook_provider" => "unsupported_webhook_provider",
            "webhooks_disabled" => "webhooks_disabled",
            "svix_auth_failed" => "svix_auth_failed",
            "svix_request_rejected" => "svix_request_rejected",
            "svix_provider_unavailable" => "svix_provider_unavailable",
            "svix_provider_failed" => "svix_provider_failed",
            "svix_auth_token_secret_missing" => "svix_auth_token_secret_missing",
            "svix_auth_token_unresolved" => "svix_auth_token_unresolved",
            _ => "unknown"
        };
    }

    private static string NormalizeWebhookCleanupMode(string? mode)
    {
        return NormalizeTag(mode) switch
        {
            "cleanup" => "cleanup",
            "dry_run" or "dryrun" => "dry_run",
            _ => "unknown"
        };
    }

    private static string NormalizeWebhookCleanupDataKind(string? dataKind)
    {
        return NormalizeTag(dataKind) switch
        {
            "outbound_payload" => "outbound_payload",
            "inbound_payload" => "inbound_payload",
            "delivery_attempt" => "delivery_attempt",
            "incoming_attempt" => "incoming_attempt",
            "incoming_redrive" => "incoming_redrive",
            "provider_attempt" => "provider_attempt",
            "provider_publication" => "provider_publication",
            "administrative_audit" => "administrative_audit",
            _ => "unknown"
        };
    }

    private static void RecordWebhookCount(
        Counter<long> counter,
        WebhookTelemetryProvider provider,
        WebhookTelemetryOperation operation,
        long count)
    {
        if (count <= 0)
        {
            return;
        }

        counter.Add(
            count,
            new KeyValuePair<string, object?>("provider", WebhookTelemetryDimensionCodes.Provider(provider)),
            new KeyValuePair<string, object?>("operation", WebhookTelemetryDimensionCodes.Operation(operation)));
    }

    private static string NormalizeStorageProvider(string? provider)
    {
        return NormalizeTag(provider) switch
        {
            StorageProviders.Local => StorageProviders.Local,
            StorageProviders.S3Compatible => StorageProviders.S3Compatible,
            StorageProviders.LegacyExternal => StorageProviders.LegacyExternal,
            _ => "unknown"
        };
    }

    private static string NormalizeAiRetentionCleanupCategory(string? category)
    {
        return NormalizeTag(category) switch
        {
            "eligible_conversations" => "eligible_conversations",
            "redacted_conversations" => "redacted_conversations",
            "redacted_messages" => "redacted_messages",
            "redacted_runs" => "redacted_runs",
            "redacted_references" => "redacted_references",
            "redacted_proposed_actions" => "redacted_proposed_actions",
            "redacted_tool_executions" => "redacted_tool_executions",
            _ => "unknown"
        };
    }

    private static string NormalizeAiProvider(string? provider)
    {
        return NormalizeTag(provider) switch
        {
            "none" => "none",
            "fake" => "fake",
            "openai" => "openai",
            "openai-compatible" => "openai-compatible",
            "azure-openai" => "azure-openai",
            "microsoft-extensions-ai" => "microsoft-extensions-ai",
            _ => "unknown"
        };
    }

    private static string NormalizeAiProviderOutcome(string? outcome)
    {
        return NormalizeTag(outcome) switch
        {
            "succeeded" => "succeeded",
            "failed" => "failed",
            "cancelled" => "cancelled",
            _ => "unknown"
        };
    }

    private static string NormalizeAiProviderFailureCategory(string? failureCategory)
    {
        var normalized = NormalizeTag(failureCategory ?? "none");
        if (normalized == "none")
        {
            return normalized;
        }

        if (normalized.StartsWith("http_", StringComparison.Ordinal)
            && int.TryParse(normalized.AsSpan(5), out var statusCode)
            && statusCode is >= 100 and <= 599)
        {
            return normalized;
        }

        return normalized switch
        {
            "provider_disabled" => "provider_disabled",
            "provider_not_configured" => "provider_not_configured",
            "invalid_settings" => "invalid_settings",
            "streaming_not_supported" => "streaming_not_supported",
            "empty_messages" => "empty_messages",
            "unsupported_message_role" => "unsupported_message_role",
            "invalid_action_schema" => "invalid_action_schema",
            "provider_timeout" => "provider_timeout",
            "provider_unreachable" => "provider_unreachable",
            "invalid_response" => "invalid_response",
            "provider_failure" => "provider_failure",
            "content_filtered" => "content_filtered",
            "invalid_tool_arguments" => "invalid_tool_arguments",
            _ => "unknown"
        };
    }

    private void RecordAiProviderTokenUsage(string? provider, string tokenType, int? tokenCount)
    {
        if (tokenCount is null or <= 0)
        {
            return;
        }

        _aiProviderTokenUsage.Record(tokenCount.Value,
            new KeyValuePair<string, object?>("provider", NormalizeAiProvider(provider)),
            new KeyValuePair<string, object?>("token_type", NormalizeAiProviderTokenType(tokenType)));
    }

    private static string NormalizeAiProviderTokenType(string? tokenType)
    {
        return NormalizeTag(tokenType) switch
        {
            "input" => "input",
            "output" => "output",
            "total" => "total",
            _ => "unknown"
        };
    }

    private static string NormalizeAiProposedActionKind(string? actionKind)
    {
        return NormalizeTag(actionKind) switch
        {
            "create_event_draft" or "createeventdraft" => "create_event_draft",
            "update_event_draft" or "updateeventdraft" => "update_event_draft",
            "publish_event" or "publishevent" => "publish_event",
            "delete_event" or "deleteevent" => "delete_event",
            "upsert_event_islamic_aspect" or "upserteventislamicaspect" => "upsert_event_islamic_aspect",
            "delete_event_islamic_aspect" or "deleteeventislamicaspect" => "delete_event_islamic_aspect",
            "upsert_event_tech_aspect" or "upserteventtechaspect" => "upsert_event_tech_aspect",
            "delete_event_tech_aspect" or "deleteeventtechaspect" => "delete_event_tech_aspect",
            "create_event_session" or "createeventsession" => "create_event_session",
            "update_event_session" or "updateeventsession" => "update_event_session",
            "delete_event_session" or "deleteeventsession" => "delete_event_session",
            "create_event_session_group" or "createeventsessiongroup" => "create_event_session_group",
            "update_event_session_group" or "updateeventsessiongroup" => "update_event_session_group",
            "delete_event_session_group" or "deleteeventsessiongroup" => "delete_event_session_group",
            "assign_session_to_event_session_group" or "assignsessiontoeventsessiongroup" => "assign_session_to_event_session_group",
            "unassign_session_from_event_session_group" or "unassignsessionfromeventsessiongroup" => "unassign_session_from_event_session_group",
            "create_event_day" or "createeventday" => "create_event_day",
            "update_event_day" or "updateeventday" => "update_event_day",
            "delete_event_day" or "deleteeventday" => "delete_event_day",
            "create_event_agenda_item" or "createeventagendaitem" => "create_event_agenda_item",
            "update_event_agenda_item" or "updateeventagendaitem" => "update_event_agenda_item",
            "delete_event_agenda_item" or "deleteeventagendaitem" => "delete_event_agenda_item",
            "create_event_custom_property_definition" or "createeventcustompropertydefinition" => "create_event_custom_property_definition",
            "update_event_custom_property_definition" or "updateeventcustompropertydefinition" => "update_event_custom_property_definition",
            "delete_event_custom_property_definition" or "deleteeventcustompropertydefinition" => "delete_event_custom_property_definition",
            "purge_event_custom_property_definition" or "purgeeventcustompropertydefinition" => "purge_event_custom_property_definition",
            "set_event_custom_property_value" or "seteventcustompropertyvalue" => "set_event_custom_property_value",
            "set_event_custom_property_multi_values" or "seteventcustompropertymultivalues" => "set_event_custom_property_multi_values",
            "create_event_registration" or "createeventregistration" => "create_event_registration",
            "update_event_registration" or "updateeventregistration" => "update_event_registration",
            "delete_event_registration" or "deleteeventregistration" => "delete_event_registration",
            "assign_event_team_role" or "assigneventteamrole" => "assign_event_team_role",
            "revoke_event_team_role" or "revokeeventteamrole" => "revoke_event_team_role",
            "create_event_template" or "createeventtemplate" => "create_event_template",
            "update_event_template" or "updateeventtemplate" => "update_event_template",
            "delete_event_template" or "deleteeventtemplate" => "delete_event_template",
            "create_event_session_template" or "createeventsessiontemplate" => "create_event_session_template",
            "update_event_session_template" or "updateeventsessiontemplate" => "update_event_session_template",
            "delete_event_session_template" or "deleteeventsessiontemplate" => "delete_event_session_template",
            "apply_event_template_sync" or "applyeventtemplatesync" => "apply_event_template_sync",
            "apply_event_session_template_sync" or "applyeventsessiontemplatesync" => "apply_event_session_template_sync",
            _ => "unknown"
        };
    }

    private static string NormalizeModerationActionKind(string? actionKind)
    {
        return NormalizeTag(actionKind) switch
        {
            "light_moderated" or "lightmoderated" or "moderate-light" or "light" => "light_moderated",
            "heavy_redacted" or "heavyredacted" or "moderate-heavy" or "heavy" => "heavy_redacted",
            "unmoderated" or "unmoderate" => "unmoderated",
            _ => "unknown"
        };
    }

    private static string NormalizeModerationOutcome(string? outcome)
    {
        return NormalizeTag(outcome) switch
        {
            "succeeded" => "succeeded",
            "failed" => "failed",
            "idempotent" => "idempotent",
            "pending_storage_deletion" => "pending_storage_deletion",
            _ => "unknown"
        };
    }

    private static string NormalizeModerationFailureCategory(string? failureCategory)
    {
        return NormalizeTag(failureCategory ?? "none") switch
        {
            "none" => "none",
            "not_found" => "not_found",
            "invalid_status" => "invalid_status",
            "not_reversible" => "not_reversible",
            "user_unresolved" => "user_unresolved",
            "storage_deletion_pending" => "storage_deletion_pending",
            _ => "unknown"
        };
    }

    private static string NormalizeEventReportSubmissionOutcome(string? outcome)
    {
        return NormalizeEventReportOutcome(outcome) switch
        {
            "succeeded" => "succeeded",
            "failed" => "failed",
            _ => "unknown"
        };
    }

    private static string NormalizeEventReportSubmissionFailureCategory(string? failureCategory)
    {
        return NormalizeEventReportFailureCategory(failureCategory) switch
        {
            "none" => "none",
            "validation_failed" => "validation_failed",
            "tenant_unresolved" => "tenant_unresolved",
            "user_unresolved" => "user_unresolved",
            "actor_unresolved" => "actor_unresolved",
            "event_not_found" => "event_not_found",
            "invalid_status" => "invalid_status",
            "duplicate" => "duplicate",
            FailureCodes.QuotaExceeded => FailureCodes.QuotaExceeded,
            _ => "unknown"
        };
    }

    private static string NormalizeEventReportWorkflowAction(string? action)
    {
        return NormalizeTag(action) switch
        {
            "triage" or "triage_report" or "triagereport" => "triage",
            "assign" or "assign_report" or "assignreport" => "assign",
            "decide" or "decision" or "decide_report" or "decidereport" => "decide",
            "execute" or "execute_decision" or "execute_report_decision" or "executereportdecision" => "execute",
            _ => "unknown"
        };
    }

    private static string NormalizeEventReportProvider(string? provider)
    {
        return NormalizeTag(provider) switch
        {
            "local" or "localonly" or "local_only" => "local",
            "osprey" => "osprey",
            "coop" => "coop",
            "composite" => "composite",
            "none" or "disabled" => "none",
            _ => "unknown"
        };
    }

    private static string NormalizeEventReportOutcome(string? outcome)
    {
        return NormalizeTag(outcome) switch
        {
            "succeeded" or "success" => "succeeded",
            "failed" or "failure" => "failed",
            "retryable_failure" or "retryable" => "retryable_failure",
            "nonretryable_failure" or "non_retryable_failure" or "nonretryable" => "nonretryable_failure",
            "disabled" => "disabled",
            "skipped" => "skipped",
            "idempotent" => "idempotent",
            _ => "unknown"
        };
    }

    private static string NormalizeEventReportFailureCategory(string? failureCategory)
    {
        return NormalizeTag(failureCategory ?? "none") switch
        {
            "none" => "none",
            "validation_failed" => "validation_failed",
            "tenant_unresolved" => "tenant_unresolved",
            "user_unresolved" => "user_unresolved",
            "actor_unresolved" => "actor_unresolved",
            "event_not_found" => "event_not_found",
            "event_mismatch" => "event_mismatch",
            "report_not_found" => "report_not_found",
            "case_not_found" => "case_not_found",
            "case_concurrency_conflict" => "case_concurrency_conflict",
            "case_invalid_status" => "case_invalid_status",
            "report_invalid_status" => "report_invalid_status",
            "moderator_unavailable" => "moderator_unavailable",
            "assignee_unavailable" => "assignee_unavailable",
            "assignment_mismatch" => "assignment_mismatch",
            "duplicate_group_required" => "duplicate_group_required",
            "decision_not_found" => "decision_not_found",
            "decision_invalid" => "decision_invalid",
            "decision_execution_failed" => "decision_execution_failed",
            "duplicate" => "duplicate",
            FailureCodes.QuotaExceeded => FailureCodes.QuotaExceeded,
            "provider_disabled" => "provider_disabled",
            "provider_sync_failed" => "provider_sync_failed",
            "provider_timeout" => "provider_timeout",
            "provider_unreachable" => "provider_unreachable",
            "provider_auth_failed" => "provider_auth_failed",
            "provider_invalid_request" => "provider_invalid_request",
            "provider_conflict" => "provider_conflict",
            "provider_rate_limited" => "provider_rate_limited",
            "provider_transient_http" => "provider_transient_http",
            "provider_invalid_response" => "provider_invalid_response",
            "coop_timeout" => "provider_timeout",
            "coop_webhook_signature_invalid" => "webhook_signature_invalid",
            "coop_webhook_secret_missing" => "webhook_secret_missing",
            "coop_webhook_body_too_large" => "webhook_body_too_large",
            "coop_webhook_json_invalid" => "webhook_json_invalid",
            "coop_webhook_body_required" => "webhook_body_required",
            _ => "unknown"
        };
    }

    private static string NormalizeEmailDispatchAttemptOutcome(string? outcome)
    {
        return NormalizeTag(outcome) switch
        {
            "sent" => "sent",
            "retry_scheduled" => "retry_scheduled",
            "dead_lettered" => "dead_lettered",
            "unknown" => "unknown",
            _ => "other"
        };
    }

    private static string NormalizeEmailDispatchOperationalOutcome(string? outcome)
    {
        return NormalizeTag(outcome) switch
        {
            "skipped" => "skipped",
            "rate_deferred" => "rate_deferred",
            _ => "other"
        };
    }

    private static string NormalizeEmailDispatchFailureCategory(string? failureCategory)
    {
        return NormalizeTag(failureCategory ?? "none") switch
        {
            "none" => "none",
            "smtp_send_failed" => "smtp_send_failed",
            "smtp_outcome_unknown" => "smtp_outcome_unknown",
            "accepted_settlement_unknown" => "accepted_settlement_unknown",
            "processing_lease_expired" => "processing_lease_expired",
            _ => "other"
        };
    }

    private static string NormalizeEmailDispatchOperationalReason(string? reason)
    {
        return NormalizeTag(reason ?? "none") switch
        {
            "none" => "none",
            "smtp_rate_deferred" => "smtp_rate_deferred",
            "delivery_authority_missing" => "delivery_authority_missing",
            "delivery_superseded" => "delivery_superseded",
            "delivery_state_ineligible" => "delivery_state_ineligible",
            "delivery_policy_version_unsupported" => "delivery_policy_version_unsupported",
            "delivery_policy_mismatch" => "delivery_policy_mismatch",
            "tenant_inactive" => "tenant_inactive",
            "recipient_membership_inactive" => "recipient_membership_inactive",
            "recipient_deleted" => "recipient_deleted",
            "invitation_authority_missing" => "invitation_authority_missing",
            "invitation_authority_invalid" => "invitation_authority_invalid",
            "recipient_address_source_mismatch" => "recipient_address_source_mismatch",
            "recipient_email_unverified" => "recipient_email_unverified",
            "notification_preference_category_missing" => "notification_preference_category_missing",
            "recipient_notification_preference_disabled" => "recipient_notification_preference_disabled",
            "recipient_unsubscribed" => "recipient_unsubscribed",
            "report_consent_source_missing" => "report_consent_source_missing",
            "report_case_update_consent_unavailable" => "report_case_update_consent_unavailable",
            "report_consent_purpose_mismatch" => "report_consent_purpose_mismatch",
            "report_follow_up_consent_withdrawn" => "report_follow_up_consent_withdrawn",
            _ => "other"
        };
    }

    private static string NormalizeEmailDispatchRabbitMqPublishOutcome(string? outcome)
    {
        return NormalizeTag(outcome) switch
        {
            "disabled" => "disabled",
            "confirmed" => "confirmed",
            "returned" => "returned",
            "nacked" => "nacked",
            "failed" => "failed",
            "timeout" => "timeout",
            _ => "other"
        };
    }

    private static string NormalizeEmailDispatchRabbitMqConsumeOutcome(string? outcome)
    {
        return NormalizeTag(outcome) switch
        {
            "acked" => "acked",
            "rejected" => "rejected",
            "nacked" => "nacked",
            "replayed" => "replayed",
            "parked" => "parked",
            _ => "other"
        };
    }

    private static string NormalizeEmailDispatchRabbitMqPublishFailureCategory(string? failureCategory)
    {
        return NormalizeTag(failureCategory ?? "none") switch
        {
            "none" => "none",
            "mandatory_return" => "mandatory_return",
            "publisher_nack" => "publisher_nack",
            "publish_timeout" => "publish_timeout",
            "broker_publish_failed" => "broker_publish_failed",
            _ => "other"
        };
    }

    private static string NormalizeEmailDispatchRabbitMqConsumeFailureCategory(string? failureCategory)
    {
        return NormalizeTag(failureCategory ?? "none") switch
        {
            "none" => "none",
            "malformed_pointer" => "malformed_pointer",
            "invalid_pointer" => "invalid_pointer",
            "missing_outbox" => "missing_outbox",
            "consumer_exception" => "consumer_exception",
            "replay_state_changed" => "replay_state_changed",
            "dlq_replay_exception" => "dlq_replay_exception",
            "outbox_missing" => "outbox_missing",
            "tenant_mismatch" => "tenant_mismatch",
            "publish_event_mismatch" => "publish_event_mismatch",
            "event_mismatch" => "event_mismatch",
            "already_sent" => "already_sent",
            "already_skipped" => "already_skipped",
            "already_processing" => "already_processing",
            "already_settled" => "already_settled",
            "retry_deferred" => "retry_deferred",
            "invalid_status" => "invalid_status",
            _ => "other"
        };
    }

    private static string NormalizeStorageOperation(string? operation)
    {
        return NormalizeTag(operation) switch
        {
            "create" => "create",
            "finalize" => "finalize",
            "cancel" => "cancel",
            "read" => "read",
            "delete" => "delete",
            "reserve" => "reserve",
            "release" => "release",
            "commit" => "commit",
            "test" => "test",
            "scan" => "scan",
            "exists" => "exists",
            "quarantine" => "quarantine",
            _ => "unknown"
        };
    }

    private static string NormalizeStorageOutcome(string? outcome)
    {
        return NormalizeTag(outcome) switch
        {
            "succeeded" => "succeeded",
            "failed" => "failed",
            "idempotent" => "idempotent",
            "skipped" => "skipped",
            _ => "unknown"
        };
    }

    private static string NormalizeStorageVisibility(string? visibility)
    {
        return NormalizeTag(visibility) switch
        {
            StorageObjectVisibilities.PublicImage => StorageObjectVisibilities.PublicImage,
            StorageObjectVisibilities.AuthenticatedTenant => StorageObjectVisibilities.AuthenticatedTenant,
            StorageObjectVisibilities.PrivateOwner => StorageObjectVisibilities.PrivateOwner,
            _ => "unknown"
        };
    }

    private static string NormalizeStorageFailureCategory(string? failureCategory)
    {
        if (string.IsNullOrWhiteSpace(failureCategory))
        {
            return "none";
        }

        return NormalizeTag(failureCategory) switch
        {
            "none" => "none",
            "validation_failed" => "validation_failed",
            "metadata_not_found" => "metadata_not_found",
            "object_not_found" => "object_not_found",
            "missing_object_key" => "missing_object_key",
            "access_denied" => "access_denied",
            "provider_resolution_failed" => "provider_resolution_failed",
            "provider_unavailable" => "provider_unavailable",
            "delete_failed" => "delete_failed",
            "local_storage_unavailable" => "local_storage_unavailable",
            "s3_not_configured" => "s3_not_configured",
            "s3_unavailable" => "s3_unavailable",
            "reconciliation_failed" => "reconciliation_failed",
            "inventory_unavailable" => "inventory_unavailable",
            "quarantine_failed" => "quarantine_failed",
            FailureCodes.QuotaExceeded => FailureCodes.QuotaExceeded,
            FailureCodes.StorageUploadTooLarge => FailureCodes.StorageUploadTooLarge,
            FailureCodes.StorageUploadSessionNotFound => FailureCodes.StorageUploadSessionNotFound,
            FailureCodes.StorageUploadSessionFinalized => FailureCodes.StorageUploadSessionFinalized,
            FailureCodes.StorageUploadSessionExpired => FailureCodes.StorageUploadSessionExpired,
            FailureCodes.StorageUploadSessionInvalidState => FailureCodes.StorageUploadSessionInvalidState,
            FailureCodes.StorageUploadSizeMismatch => FailureCodes.StorageUploadSizeMismatch,
            FailureCodes.StorageUploadContentTypeMismatch => FailureCodes.StorageUploadContentTypeMismatch,
            FailureCodes.StorageUploadWriteFailed => FailureCodes.StorageUploadWriteFailed,
            _ => "unknown"
        };
    }

    private static string NormalizeStorageReconciliationMode(string? mode)
    {
        return NormalizeTag(mode) switch
        {
            "dry_run" => "dry_run",
            "report" => "report",
            "quarantine" => "quarantine",
            "delete" => "delete",
            "mixed" => "mixed",
            _ => "unknown"
        };
    }

    private static string NormalizeStorageReconciliationCategory(string? category)
    {
        return NormalizeTag(category) switch
        {
            "metadata" => "metadata",
            "backing_object" => "backing_object",
            _ => "unknown"
        };
    }

    private static string NormalizeStorageReconciliationAction(string? action)
    {
        return NormalizeTag(action) switch
        {
            "scan" => "scan",
            "missing" => "missing",
            "orphan" => "orphan",
            "quarantine" => "quarantine",
            "delete" => "delete",
            "skip" => "skip",
            _ => "unknown"
        };
    }
}
