using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class GetControlPlaneOperationsQueryHandler(
    IOutboxRepository outboxRepository,
    IEmailDispatchOutboxRepository emailDispatchOutboxRepository,
    IEventReportRepository eventReportRepository,
    ITenantRepository tenantRepository,
    IHierarchicalSettingsResolver settingsResolver,
    IInstanceStorageSettingService storageSettingService,
    IInstanceSmtpSettingService smtpSettingService,
    IConfiguration configuration)
    : IRequestHandler<GetControlPlaneOperationsQuery, ControlPlaneOperationsDto>
{
    private const int GeneralOutboxSampleLimit = 100;
    private const int DefaultProcessingLeaseTimeoutSeconds = 900;
    private const int DefaultDueDispatchWarningThreshold = 1000;
    private const int DefaultStaleProcessingWarningThreshold = 1;
    private const int DefaultDeadLetterWarningThreshold = 1;
    private const int DefaultModerationReportingStuckSyncMinutes = 120;
    private const int DefaultModerationReportingFailedWarningThreshold = 1;

    public async Task<ControlPlaneOperationsDto> Handle(
        GetControlPlaneOperationsQuery request,
        CancellationToken cancellationToken)
    {
        _ = request;

        var now = DateTime.UtcNow;
        var processingStartedBefore = now.AddSeconds(-ReadInt(
            "EmailDispatchProcessor:ProcessingLeaseTimeoutSeconds",
            DefaultProcessingLeaseTimeoutSeconds));
        var dueDispatchWarningThreshold = ReadInt(
            "EmailDispatchProcessor:HealthDueDispatchWarningThreshold",
            DefaultDueDispatchWarningThreshold);
        var staleProcessingWarningThreshold = ReadInt(
            "EmailDispatchProcessor:HealthStaleProcessingWarningThreshold",
            DefaultStaleProcessingWarningThreshold);
        var deadLetterWarningThreshold = ReadInt(
            "EmailDispatchProcessor:HealthDeadLetterWarningThreshold",
            DefaultDeadLetterWarningThreshold);

        var dueOutboxMessages = await outboxRepository.GetPendingBatch(GeneralOutboxSampleLimit, cancellationToken);
        var failedOutboxMessages = await outboxRepository.GetFailedEntries(GeneralOutboxSampleLimit, cancellationToken);
        var dueEmailDispatchCount = await emailDispatchOutboxRepository.CountDueDispatchAsync(now, cancellationToken);
        var retryScheduledEmailDispatchCount = await emailDispatchOutboxRepository.CountRetryScheduledAsync(cancellationToken);
        var staleProcessingEmailDispatchCount = await emailDispatchOutboxRepository.CountStaleProcessingAsync(
            processingStartedBefore,
            cancellationToken);
        var deadLetteredEmailDispatchCount = await emailDispatchOutboxRepository.CountDeadLetteredAsync(cancellationToken);
        var moderationPendingCount = await eventReportRepository.CountExternalLinksBySyncStateAsync(
            EventReportSyncState.Pending,
            cancellationToken);
        var moderationFailedCount = await eventReportRepository.CountExternalLinksBySyncStateAsync(
            EventReportSyncState.Failed,
            cancellationToken);
        var moderationDisabledCount = await eventReportRepository.CountExternalLinksBySyncStateAsync(
            EventReportSyncState.Disabled,
            cancellationToken);
        var moderationIgnoredCount = await eventReportRepository.CountExternalLinksBySyncStateAsync(
            EventReportSyncState.Ignored,
            cancellationToken);
        var moderationStuckPendingCount = await eventReportRepository.CountExternalLinksBySyncStateBeforeAsync(
            EventReportSyncState.Pending,
            now.AddMinutes(-ReadInt(
                "Reporting:Health:StuckProviderSyncMinutes",
                DefaultModerationReportingStuckSyncMinutes)),
            cancellationToken);
        var activeTenantCount = await tenantRepository.GetActiveTenantCountAsync();
        var tenantDelegation = await settingsResolver.ResolveGroupAsync<TenantDelegationSettingGroup>(
            new SettingContext(),
            cancellationToken);
        var storageSettings = await storageSettingService.ReadSettingsAsync(cancellationToken);
        var smtpSettings = await smtpSettingService.ReadSettingsAsync();

        var warnings = new List<ControlPlaneWarningDto>();
        var generalOutbox = BuildGeneralOutboxStatus(dueOutboxMessages, failedOutboxMessages, warnings);
        var emailDispatch = BuildEmailDispatchStatus(
            dueEmailDispatchCount,
            retryScheduledEmailDispatchCount,
            staleProcessingEmailDispatchCount,
            deadLetteredEmailDispatchCount,
            dueDispatchWarningThreshold,
            staleProcessingWarningThreshold,
            deadLetterWarningThreshold,
            smtpSettings,
            warnings);
        var moderationReporting = BuildModerationReportingStatus(
            moderationPendingCount,
            moderationFailedCount,
            moderationDisabledCount,
            moderationIgnoredCount,
            moderationStuckPendingCount,
            activeTenantCount,
            tenantDelegation.LockReportingProviders,
            tenantDelegation.LockTenantOspreyProvider,
            tenantDelegation.LockTenantCoopProvider,
            ReadInt(
                "Reporting:Health:FailedProviderSyncWarningThreshold",
                DefaultModerationReportingFailedWarningThreshold),
            warnings);
        var storage = BuildStorageStatus(storageSettings, warnings);

        return new ControlPlaneOperationsDto
        {
            GeneratedAtUtc = now,
            Statuses = [generalOutbox, emailDispatch, moderationReporting, storage],
            Warnings = warnings
        };
    }

    private ControlPlaneOperationStatusDto BuildGeneralOutboxStatus(
        IReadOnlyCollection<OutboxMessage> dueMessages,
        IReadOnlyCollection<OutboxMessage> failedMessages,
        ICollection<ControlPlaneWarningDto> warnings)
    {
        var failedCount = failedMessages.Count(message => message.Status == OutboxMessageStatus.Failed);
        var deadLetteredCount = failedMessages.Count(message => message.Status == OutboxMessageStatus.DeadLettered);
        var dueIsCapped = dueMessages.Count >= GeneralOutboxSampleLimit;
        var failedIsCapped = failedMessages.Count >= GeneralOutboxSampleLimit;

        if (dueIsCapped)
        {
            warnings.Add(Warning(
                "general_outbox_due_backlog_capped",
                "warning",
                "The general outbox due backlog reached the reporting cap.",
                "Open the operations panel, verify the outbox worker is running, and inspect due rows before raising the reporting cap."));
        }

        if (failedCount > 0 || deadLetteredCount > 0)
        {
            warnings.Add(Warning(
                "general_outbox_failures_present",
                deadLetteredCount > 0 ? "critical" : "warning",
                "The general outbox has failed or dead-lettered messages.",
                "Inspect failed or dead-lettered outbox rows, fix the downstream handler or payload issue, then replay from an operator-approved path."));
        }

        return new ControlPlaneOperationStatusDto
        {
            Key = "general-outbox",
            DisplayName = "General outbox",
            Status = failedCount > 0 || deadLetteredCount > 0 ? "attention" : "healthy",
            Severity = deadLetteredCount > 0 ? "critical" : failedCount > 0 || dueIsCapped ? "warning" : "normal",
            Message = dueIsCapped
                ? "Due backlog is at or above the bounded reporting cap."
                : "General outbox status is within the bounded reporting window.",
            Metrics =
            [
                Metric("due", "Due now", dueMessages.Count, dueIsCapped),
                Metric("failed", "Failed", failedCount, failedIsCapped),
                Metric("dead-lettered", "Dead-lettered", deadLetteredCount, failedIsCapped)
            ]
        };
    }

    private static ControlPlaneOperationStatusDto BuildEmailDispatchStatus(
        int dueDispatchCount,
        int retryScheduledCount,
        int staleProcessingCount,
        int deadLetteredCount,
        int dueDispatchWarningThreshold,
        int staleProcessingWarningThreshold,
        int deadLetterWarningThreshold,
        InstanceSmtpSettingsDto smtpSettings,
        ICollection<ControlPlaneWarningDto> warnings)
    {
        var smtpConfigured = IsSmtpConfigured(smtpSettings);
        var hasDeadLetters = deadLetteredCount >= deadLetterWarningThreshold;
        var hasStaleProcessing = staleProcessingCount >= staleProcessingWarningThreshold;
        var hasDueBacklog = dueDispatchCount >= dueDispatchWarningThreshold;

        if (!smtpConfigured)
        {
            warnings.Add(Warning(
                "email_provider_missing",
                "warning",
                "SMTP is not configured for platform email delivery.",
                "Configure SMTP in instance settings before relying on platform email delivery."));
        }

        if (hasDeadLetters)
        {
            warnings.Add(Warning(
                "email_dispatch_dead_letters",
                "critical",
                "Email dispatch has dead-lettered rows.",
                "Review dead-lettered email dispatch rows, fix provider or configuration failures, and replay only after confirming recipients and payloads."));
        }

        if (hasStaleProcessing)
        {
            warnings.Add(Warning(
                "email_dispatch_stale_processing",
                "warning",
                "Email dispatch has stale processing rows.",
                "Check the email dispatch worker lease or heartbeat and restart the worker before replaying stuck rows."));
        }

        if (hasDueBacklog)
        {
            warnings.Add(Warning(
                "email_dispatch_due_backlog",
                "warning",
                "Email dispatch due backlog is above the configured threshold.",
                "Scale or restart email dispatch processing and check SMTP provider throttling before increasing thresholds."));
        }

        return new ControlPlaneOperationStatusDto
        {
            Key = "email-dispatch",
            DisplayName = "Email dispatch",
            Status = hasDeadLetters || hasStaleProcessing || hasDueBacklog || !smtpConfigured ? "attention" : "healthy",
            Severity = hasDeadLetters ? "critical" : hasStaleProcessing || hasDueBacklog || !smtpConfigured ? "warning" : "normal",
            Message = smtpConfigured
                ? "Email dispatch operational counts are available."
                : "SMTP must be configured before platform email can be delivered.",
            Metrics =
            [
                Metric("due", "Due now", dueDispatchCount),
                Metric("retry-scheduled", "Retry scheduled", retryScheduledCount),
                Metric("stale-processing", "Stale processing", staleProcessingCount),
                Metric("dead-lettered", "Dead-lettered", deadLetteredCount)
            ]
        };
    }

    private static ControlPlaneOperationStatusDto BuildModerationReportingStatus(
        int pendingCount,
        int failedCount,
        int disabledCount,
        int ignoredCount,
        int stuckPendingCount,
        int activeTenantCount,
        bool reportingProvidersLocked,
        bool tenantOspreyLocked,
        bool tenantCoopLocked,
        int failedWarningThreshold,
        ICollection<ControlPlaneWarningDto> warnings)
    {
        var hasFailures = failedCount >= failedWarningThreshold;
        var hasStuckPending = stuckPendingCount > 0;

        if (hasFailures)
        {
            warnings.Add(Warning(
                "moderation_reporting_provider_sync_failures",
                "warning",
                "Moderation reporting provider sync has failed rows.",
                "Review failed reporting provider sync records and provider configuration from an operator-approved path without exposing provider payloads."));
        }

        if (hasStuckPending)
        {
            warnings.Add(Warning(
                "moderation_reporting_provider_sync_stuck",
                "warning",
                "Moderation reporting provider sync has pending rows older than the configured health window.",
                "Verify the report provider sync dispatcher is running and review provider connectivity without exposing report payloads or provider identifiers."));
        }

        return new ControlPlaneOperationStatusDto
        {
            Key = "moderation-reporting",
            DisplayName = "Moderation reporting",
            Status = hasFailures || hasStuckPending ? "attention" : "healthy",
            Severity = hasFailures || hasStuckPending ? "warning" : "normal",
            Message = hasFailures || hasStuckPending
                ? "Moderation reporting provider sync requires operator attention."
                : "Moderation reporting provider sync counts are within the configured health window.",
            Metrics =
            [
                Metric("pending-sync", "Pending sync", pendingCount),
                Metric("stuck-pending-sync", "Stuck pending sync", stuckPendingCount),
                Metric("failed-sync", "Failed sync", failedCount),
                Metric("disabled-sync", "Disabled sync", disabledCount),
                Metric("ignored-sync", "Ignored sync", ignoredCount),
                Metric("reporting-locked-tenants", "Reporting locked tenants", reportingProvidersLocked ? activeTenantCount : 0),
                Metric("reporting-unlocked-tenants", "Reporting unlocked tenants", reportingProvidersLocked ? 0 : activeTenantCount),
                Metric("osprey-locked-tenants", "Osprey locked tenants", tenantOspreyLocked ? activeTenantCount : 0),
                Metric("coop-locked-tenants", "Coop locked tenants", tenantCoopLocked ? activeTenantCount : 0)
            ]
        };
    }

    private static ControlPlaneOperationStatusDto BuildStorageStatus(
        InstanceStorageSettingsDto storageSettings,
        ICollection<ControlPlaneWarningDto> warnings)
    {
        var providerStatus = storageSettings.ProviderStatus;
        var isUnavailable = !providerStatus.IsAvailable && !string.IsNullOrWhiteSpace(providerStatus.FailureCode);

        if (isUnavailable)
        {
            warnings.Add(Warning(
                "storage_provider_unavailable",
                "critical",
                "The configured storage provider is reporting an unavailable state.",
                "Verify storage provider settings, credentials, bucket or root reachability, and health checks before allowing upload-heavy operations."));
        }

        return new ControlPlaneOperationStatusDto
        {
            Key = "storage",
            DisplayName = "Storage",
            Status = providerStatus.IsAvailable ? "healthy" : isUnavailable ? "attention" : "unknown",
            Severity = isUnavailable ? "critical" : providerStatus.IsAvailable ? "normal" : "warning",
            Message = providerStatus.IsAvailable
                ? $"{providerStatus.Provider} storage is available."
                : string.IsNullOrWhiteSpace(providerStatus.Message)
                    ? $"{storageSettings.Provider} storage has not been verified."
                    : providerStatus.Message,
            Metrics =
            [
                Metric("used-bytes", "Used bytes", storageSettings.Usage.UsedBytes),
                Metric("reserved-bytes", "Reserved bytes", storageSettings.Usage.ReservedBytes),
                Metric("quarantined-bytes", "Quarantined bytes", storageSettings.Usage.QuarantinedBytes),
                Metric("object-count", "Object count", storageSettings.Usage.ObjectCount)
            ]
        };
    }

    private int ReadInt(string key, int fallback)
    {
        var configured = configuration[key];
        return int.TryParse(configured, out var value) && value > 0 ? value : fallback;
    }

    private static bool IsSmtpConfigured(InstanceSmtpSettingsDto settings) =>
        !string.IsNullOrWhiteSpace(settings.Host)
        && !string.IsNullOrWhiteSpace(settings.FromAddress);

    private static ControlPlaneOperationMetricDto Metric(
        string key,
        string displayName,
        long value,
        bool isCapped = false) => new()
        {
            Key = key,
            DisplayName = displayName,
            Value = value,
            IsCapped = isCapped
        };

    private static ControlPlaneWarningDto Warning(
        string code,
        string severity,
        string message,
        string remediation) => new()
        {
            Code = code,
            Severity = severity,
            Message = message,
            Remediation = remediation
        };
}
