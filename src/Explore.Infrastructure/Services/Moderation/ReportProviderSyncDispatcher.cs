using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services.Moderation;

public sealed class ReportProviderSyncDispatcher(
    IEventReportProvider provider,
    IEventReportRepository eventReportRepository,
    IOptionsMonitor<ModerationProviderOptions> options,
    BusinessMetrics metrics,
    ILogger<ReportProviderSyncDispatcher> logger) : IReportProviderSyncDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        if (message.EventType != EventReportOutboxMessageFactory.EventReportProviderSyncRequestedEventType)
        {
            throw new InvalidOperationException(
                $"Outbox message {message.Id} has unsupported event type '{message.EventType}' for report provider sync.");
        }

        var request = DeserializeRequest(message);
        var idempotencyKey = BuildIdempotencyKey(message);
        var currentOptions = options.CurrentValue;
        var configuredTargets = ResolveConfiguredTargets(currentOptions);
        var report = await LoadReportAsync(request, cancellationToken);
        var reportCase = ResolveCase(report, request.CaseId, message.Id);

        if (HasCompletedSyncMarker(report, configuredTargets, idempotencyKey))
        {
            RecordProviderSync(report.TenantId, configuredTargets, "skipped");
            logger.LogInformation(
                "Skipping already-recorded event report provider sync for report {ReportId} from outbox message {MessageId}",
                request.ReportId,
                message.Id);
            return;
        }

        var envelope = CreateEnvelope(request, report, reportCase, idempotencyKey, currentOptions);
        var result = await provider.SyncReportAsync(envelope, cancellationToken);

        if (result.ProviderDisabled)
        {
            await PersistDisabledAsync(report, reportCase.Id, configuredTargets, idempotencyKey, cancellationToken);
            RecordProviderSync(report.TenantId, configuredTargets, "disabled", "provider_disabled");
            logger.LogInformation(
                "Event report provider sync disabled for report {ReportId} from outbox message {MessageId}",
                request.ReportId,
                message.Id);
            return;
        }

        if (!result.Succeeded)
        {
            var category = NormalizeErrorCategory(result.Error?.Category);
            await PersistFailureAsync(report, reportCase.Id, configuredTargets, idempotencyKey, category, cancellationToken);
            RecordProviderSync(
                report.TenantId,
                configuredTargets,
                result.IsRetryable ? "retryable_failure" : "nonretryable_failure",
                category);
            logger.LogWarning(
                "Event report provider sync failed for report {ReportId} from outbox message {MessageId} with category {FailureCategory} retryable {IsRetryable}",
                request.ReportId,
                message.Id,
                category,
                result.IsRetryable);

            if (result.IsRetryable)
            {
                throw new InvalidOperationException($"event_report_provider_sync_failed:{category}");
            }

            return;
        }

        await PersistSuccessAsync(report, reportCase.Id, result, idempotencyKey, cancellationToken);
        RecordProviderSync(report.TenantId, configuredTargets, "succeeded");
        logger.LogInformation(
            "Event report provider sync succeeded for report {ReportId} from outbox message {MessageId}",
            request.ReportId,
            message.Id);
    }

    private static EventReportProviderSyncRequested DeserializeRequest(OutboxMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Payload))
        {
            throw new InvalidOperationException($"Outbox message {message.Id} has no payload for report provider sync.");
        }

        return JsonSerializer.Deserialize<EventReportProviderSyncRequested>(message.Payload, JsonOptions)
            ?? throw new JsonException($"Failed to deserialize report provider sync payload for message {message.Id}.");
    }

    private async Task<EventReport> LoadReportAsync(
        EventReportProviderSyncRequested request,
        CancellationToken cancellationToken)
    {
        var report = await eventReportRepository.GetByIdForUpdateAsync(
            request.TenantId,
            request.ReportId,
            cancellationToken);

        return report
            ?? throw new InvalidOperationException(
                $"Event report {request.ReportId} for tenant {request.TenantId} was not found for provider sync.");
    }

    private static EventReportCase ResolveCase(EventReport report, Guid caseId, Guid messageId)
    {
        return report.Cases.FirstOrDefault(reportCase => reportCase.Id == caseId)
            ?? throw new InvalidOperationException(
                $"Event report {report.Id} does not contain case {caseId} for provider sync message {messageId}.");
    }

    private static EventReportProviderEnvelope CreateEnvelope(
        EventReportProviderSyncRequested request,
        EventReport report,
        EventReportCase reportCase,
        string idempotencyKey,
        ModerationProviderOptions currentOptions) =>
        new(
            request.TenantId,
            request.ReportId,
            request.EventId,
            request.CaseId,
            request.CaseConcurrencyStamp,
            report.ReasonCode,
            reportCase.QueueCode,
            ToCode(report.Status),
            ToCode(reportCase.Status),
            ToCode(reportCase.Priority),
            request.SubmittedAtUtc,
            Max(report.UpdatedAt, reportCase.UpdatedAt),
            idempotencyKey,
            NormalizeOptional(request.CorrelationId),
            currentOptions.EvidenceMode);

    private async Task PersistSuccessAsync(
        EventReport report,
        Guid caseId,
        EventReportProviderSyncResult result,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var changed = false;

        foreach (var signal in result.Signals)
        {
            if (HasSignal(report, signal))
            {
                continue;
            }

            report.Signals.Add(EventReportSignal.Create(
                report.TenantId,
                report.Id,
                report.EventId,
                signal.Provider,
                signal.SignalType,
                signal.PolicyCode,
                signal.Score,
                signal.Verdict,
                signal.RecommendedAction,
                signal.SafeSummary,
                signal.ExternalSignalId,
                string.IsNullOrWhiteSpace(signal.CorrelationId) ? idempotencyKey : signal.CorrelationId,
                signal.CreatedAtUtc == default ? utcNow : signal.CreatedAtUtc,
                signal.ProviderTargetScope,
                signal.ProviderTargetId));
            changed = true;
        }

        foreach (EventReportProviderExternalLinkEnvelope linkEnvelope in ResolveSuccessfulExternalLinks(result))
        {
            var targetToMark = new ProviderTarget(
                linkEnvelope.Provider,
                linkEnvelope.ProviderTargetScope,
                linkEnvelope.ProviderTargetId);
            var link = GetOrCreateLink(report, caseId, targetToMark, idempotencyKey, utcNow);
            link.MarkSynced(
                linkEnvelope.ProviderCaseId,
                linkEnvelope.ProviderSignalId,
                linkEnvelope.ProviderUrl,
                utcNow);
            changed = true;
        }

        if (changed)
        {
            await eventReportRepository.Update(report);
        }
    }

    private async Task PersistDisabledAsync(
        EventReport report,
        Guid caseId,
        IReadOnlyList<ProviderTarget> targets,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var utcNow = DateTime.UtcNow;
        foreach (var configuredTarget in targets)
        {
            GetOrCreateLink(report, caseId, configuredTarget, idempotencyKey, utcNow).Disable(utcNow);
        }

        await eventReportRepository.Update(report);
    }

    private async Task PersistFailureAsync(
        EventReport report,
        Guid caseId,
        IReadOnlyList<ProviderTarget> targets,
        string idempotencyKey,
        string category,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var utcNow = DateTime.UtcNow;
        foreach (var configuredTarget in targets)
        {
            var link = GetOrCreateLink(report, caseId, configuredTarget, idempotencyKey, utcNow);
            if (link.SyncState == EventReportSyncState.Synced)
            {
                continue;
            }

            link.MarkFailed(category, utcNow);
        }

        await eventReportRepository.Update(report);
    }

    private static EventReportExternalLink GetOrCreateLink(
        EventReport report,
        Guid caseId,
        ProviderTarget target,
        string idempotencyKey,
        DateTime utcNow)
    {
        var existing = report.ExternalLinks.FirstOrDefault(link =>
            link.Provider == target.Provider &&
            link.ProviderTargetScope == target.Scope &&
            string.Equals(link.ProviderTargetId, target.TargetId, StringComparison.Ordinal) &&
            string.Equals(link.CorrelationId, idempotencyKey, StringComparison.Ordinal));

        if (existing is not null)
        {
            return existing;
        }

        var link = EventReportExternalLink.CreatePending(
            report.TenantId,
            report.Id,
            caseId,
            target.Provider,
            idempotencyKey,
            utcNow,
            target.Scope,
            target.TargetId);
        report.ExternalLinks.Add(link);
        return link;
    }

    private static bool HasCompletedSyncMarker(
        EventReport report,
        IReadOnlyList<ProviderTarget> targets,
        string idempotencyKey)
    {
        return targets.Count > 0 && targets.All(targetToCheck =>
            report.ExternalLinks.Any(link =>
                link.Provider == targetToCheck.Provider &&
                link.ProviderTargetScope == targetToCheck.Scope &&
                string.Equals(link.ProviderTargetId, targetToCheck.TargetId, StringComparison.Ordinal) &&
                string.Equals(link.CorrelationId, idempotencyKey, StringComparison.Ordinal) &&
                link.SyncState is EventReportSyncState.Synced or EventReportSyncState.Disabled or EventReportSyncState.Ignored));
    }

    private static bool HasSignal(EventReport report, EventSafetySignalEnvelope signal)
    {
        var externalSignalId = NormalizeOptional(signal.ExternalSignalId);
        if (!string.IsNullOrWhiteSpace(externalSignalId))
        {
            return report.Signals.Any(existing =>
                existing.Provider == signal.Provider &&
                existing.ProviderTargetScope == signal.ProviderTargetScope &&
                string.Equals(existing.ProviderTargetId, signal.ProviderTargetId, StringComparison.Ordinal) &&
                string.Equals(existing.ExternalSignalId, externalSignalId, StringComparison.Ordinal));
        }

        var signalType = NormalizeRequired(signal.SignalType);
        var policyCode = NormalizeRequired(signal.PolicyCode);
        var correlationId = NormalizeRequired(signal.CorrelationId);

        return report.Signals.Any(existing =>
            existing.Provider == signal.Provider &&
            existing.ProviderTargetScope == signal.ProviderTargetScope &&
            string.Equals(existing.ProviderTargetId, signal.ProviderTargetId, StringComparison.Ordinal) &&
            string.Equals(existing.SignalType, signalType, StringComparison.Ordinal) &&
            string.Equals(existing.PolicyCode, policyCode, StringComparison.Ordinal) &&
            string.Equals(existing.CorrelationId, correlationId, StringComparison.Ordinal));
    }

    private static IReadOnlyList<ProviderTarget> ResolveConfiguredTargets(ModerationProviderOptions currentOptions)
    {
        List<ProviderTarget> targets = [];

        if (currentOptions.UsesOsprey && currentOptions.EvaluateSignals)
        {
            targets.Add(InstanceProviderTarget(EventReportExternalProvider.Osprey));
        }

        if (currentOptions.UsesCoop && currentOptions.MirrorReviewQueue)
        {
            targets.Add(InstanceProviderTarget(EventReportExternalProvider.Coop));
        }

        return targets;
    }

    private static IReadOnlyList<EventReportProviderExternalLinkEnvelope> ResolveSuccessfulExternalLinks(EventReportProviderSyncResult result)
    {
        if (result.ExternalLinks.Count > 0)
        {
            return result.ExternalLinks;
        }

        List<EventReportProviderExternalLinkEnvelope> links = [];

        if (!string.IsNullOrWhiteSpace(result.ProviderCaseId) || !string.IsNullOrWhiteSpace(result.ProviderUrl))
        {
            links.Add(new EventReportProviderExternalLinkEnvelope(
                EventReportExternalProvider.Coop,
                EventReportProviderTargetScope.Instance,
                "instance",
                result.ProviderCaseId,
                ProviderUrl: result.ProviderUrl));
        }

        if (!string.IsNullOrWhiteSpace(result.ProviderSignalId) ||
            result.Signals.Any(signal => signal.Provider == EventReportSignalProvider.Osprey))
        {
            links.Add(new EventReportProviderExternalLinkEnvelope(
                EventReportExternalProvider.Osprey,
                EventReportProviderTargetScope.Instance,
                "instance",
                ProviderSignalId: result.ProviderSignalId ?? result.Signals.FirstOrDefault(signal => signal.Provider == EventReportSignalProvider.Osprey)?.ExternalSignalId));
        }

        return links;
    }

    private static string BuildIdempotencyKey(OutboxMessage message) =>
        $"event-report-provider-sync:{message.Id:N}";

    private void RecordProviderSync(
        Guid tenantId,
        IReadOnlyList<ProviderTarget> targets,
        string outcome,
        string? failureCategory = null)
    {
        if (targets.Count == 0)
        {
            metrics.RecordEventReportProviderSync(tenantId.ToString(), "local", outcome, failureCategory);
            return;
        }

        foreach (var targetToRecord in targets)
        {
            metrics.RecordEventReportProviderSync(
                tenantId.ToString(),
                ToProviderCode(targetToRecord.Provider),
                outcome,
                failureCategory);
        }
    }

    private static ProviderTarget InstanceProviderTarget(EventReportExternalProvider provider) =>
        new(provider, EventReportProviderTargetScope.Instance, "instance");

    private static ProviderSignalTarget ResolveSignalProviderTarget(EventReportSignalProvider provider) =>
        provider is EventReportSignalProvider.Osprey or EventReportSignalProvider.Coop
            ? new ProviderSignalTarget(EventReportProviderTargetScope.Instance, "instance")
            : new ProviderSignalTarget(EventReportProviderTargetScope.Local, "local");

    private static string NormalizeErrorCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? "provider_sync_failed" : category.Trim();

    private static string ToProviderCode(EventReportExternalProvider providerToRecord)
        => providerToRecord switch
        {
            EventReportExternalProvider.Osprey => "osprey",
            EventReportExternalProvider.Coop => "coop",
            _ => "unknown"
        };

    private static string NormalizeRequired(string value) => value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime? Max(DateTime? first, DateTime? second)
    {
        if (!first.HasValue)
        {
            return second;
        }

        if (!second.HasValue)
        {
            return first;
        }

        return first > second ? first : second;
    }

    private static string ToCode<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var character = name[i];
            if (i > 0 && char.IsUpper(character))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private readonly record struct ProviderTarget(
        EventReportExternalProvider Provider,
        EventReportProviderTargetScope Scope,
        string TargetId);

    private readonly record struct ProviderSignalTarget(EventReportProviderTargetScope Scope, string TargetId);
}
