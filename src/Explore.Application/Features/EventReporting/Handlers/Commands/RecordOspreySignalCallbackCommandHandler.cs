using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventReporting.Handlers.Commands;

public sealed class RecordOspreySignalCallbackCommandHandler(
    IEventReportRepository eventReportRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext) : IRequestHandler<RecordOspreySignalCallbackCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        RecordOspreySignalCallbackCommand request,
        CancellationToken cancellationToken)
    {
        var validationResult = await new RecordOspreySignalCallbackCommandValidator().ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                request.Request.ReportId,
                "Osprey signal callback is invalid.",
                validationResult.Errors.Select(error => error.ErrorMessage),
                EventReportFailureCodes.ValidationFailed);
        }

        if (tenantContext.TenantId == Guid.Empty)
        {
            return Failure(request.Request.ReportId, "Tenant context could not be resolved.", ["Tenant context is required."], EventReportFailureCodes.TenantUnresolved);
        }

        if (tenantContext.TenantId != request.Request.TenantId)
        {
            return Failure(
                request.Request.ReportId,
                "Osprey callback tenant does not match the request tenant.",
                ["Osprey callback tenant does not match the request tenant."],
                EventReportFailureCodes.TenantUnresolved);
        }

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var report = await eventReportRepository.GetByIdForUpdateAsync(
                request.Request.TenantId,
                request.Request.ReportId,
                token);

            if (report is null)
            {
                return Failure(
                    request.Request.ReportId,
                    "Event report was not found.",
                    ["Event report was not found."],
                    EventReportFailureCodes.ReportNotFound);
            }

            if (report.EventId != request.Request.EventId)
            {
                return Failure(
                    request.Request.ReportId,
                    "Event report does not belong to the requested event.",
                    ["Event report does not belong to the requested event."],
                    EventReportFailureCodes.EventMismatch);
            }

            var reportCase = ResolveCase(report, request.Request.CaseId);
            if (reportCase is null)
            {
                return Failure(
                    request.Request.ReportId,
                    "Event report case was not found.",
                    ["Event report case was not found."],
                    EventReportFailureCodes.CaseNotFound);
            }

            var now = DateTime.UtcNow;
            var providerTarget = ResolveProviderTarget(
                request.Request.TenantId,
                request.Request.ProviderTargetScope,
                request.Request.ProviderTargetId);
            var targetValidation = ValidateProviderTarget(report, request.Request, providerTarget.Scope, providerTarget.TargetId);
            if (!targetValidation.IsSuccess)
            {
                return targetValidation;
            }

            var changed = false;
            var urgentRecommendationReceived = false;

            for (var index = 0; index < request.Request.Signals.Count; index++)
            {
                var callbackSignal = request.Request.Signals[index];
                var normalized = NormalizeSignal(request.Request, callbackSignal, index, now, providerTarget.Scope, providerTarget.TargetId);
                urgentRecommendationReceived |= IsUrgentRecommendation(normalized.Verdict, normalized.RecommendedAction);

                if (HasSignal(report, normalized))
                {
                    continue;
                }

                report.Signals.Add(EventReportSignal.Create(
                    report.TenantId,
                    report.Id,
                    report.EventId,
                    EventReportSignalProvider.Osprey,
                    normalized.SignalType,
                    normalized.PolicyCode,
                    normalized.Score,
                    normalized.Verdict,
                    normalized.RecommendedAction,
                    normalized.SafeSummary,
                    normalized.ExternalSignalId,
                    normalized.CorrelationId,
                    normalized.CreatedAtUtc,
                    normalized.ProviderTargetScope,
                    normalized.ProviderTargetId));
                changed = true;
            }

            if (urgentRecommendationReceived && PromoteForHumanReview(report, reportCase, now))
            {
                changed = true;
            }

            var linkCorrelationId = ResolveCallbackCorrelationId(request.Request);
            var link = GetOrCreateLink(report, reportCase.Id, linkCorrelationId, now, providerTarget.Scope, providerTarget.TargetId);
            var providerSignalId = NormalizeOptional(request.Request.ProviderSignalId);
            if (link.SyncState != EventReportSyncState.Synced)
            {
                link.MarkSynced(
                    providerCaseId: null,
                    providerSignalId: providerSignalId,
                    providerUrl: null,
                    now);
                changed = true;
            }
            else if (!string.IsNullOrWhiteSpace(providerSignalId) && string.IsNullOrWhiteSpace(link.ProviderSignalId))
            {
                link.MarkSynced(link.ProviderCaseId, providerSignalId, link.ProviderUrl, now);
                changed = true;
            }

            if (changed)
            {
                await eventReportRepository.Update(report);
            }

            return Success(report.Id, changed
                ? "Osprey signal callback processed successfully."
                : "Osprey signal callback was already recorded.");
        }, cancellationToken);
    }

    private static EventReportCase? ResolveCase(EventReport report, Guid? caseId)
    {
        if (caseId.HasValue)
        {
            return report.Cases.FirstOrDefault(candidate => candidate.Id == caseId.Value);
        }

        return report.Cases
            .OrderBy(candidate => candidate.Status == EventReportCaseStatus.Closed)
            .ThenBy(candidate => candidate.CreatedAt)
            .FirstOrDefault();
    }

    private static NormalizedOspreySignal NormalizeSignal(
        OspreySignalCallbackRequestDto request,
        OspreySignalCallbackItemDto signal,
        int index,
        DateTime utcNow,
        EventReportProviderTargetScope providerTargetScope,
        string providerTargetId)
    {
        var externalSignalId = NormalizeOptional(signal.ExternalSignalId)
            ?? NormalizeOptional(request.ProviderSignalId);
        var correlationId = NormalizeOptional(signal.CorrelationId)
            ?? NormalizeOptional(request.CorrelationId)
            ?? externalSignalId
            ?? $"osprey-callback:{request.ReportId:N}:{index}";

        return new NormalizedOspreySignal(
            signal.SignalType.Trim(),
            signal.PolicyCode.Trim(),
            signal.Score,
            MapVerdict(signal.Verdict),
            MapRecommendedAction(signal.RecommendedAction),
            NormalizeOptional(signal.SafeSummary),
            externalSignalId,
            NormalizeCorrelationId(correlationId),
            providerTargetScope,
            providerTargetId,
            signal.CreatedAtUtc?.ToUniversalTime() ?? utcNow);
    }

    private static BaseCommandResponse<Guid> ValidateProviderTarget(
        EventReport report,
        OspreySignalCallbackRequestDto request,
        EventReportProviderTargetScope providerTargetScope,
        string providerTargetId)
    {
        var providerSignalId = NormalizeOptional(request.ProviderSignalId);
        var correlationId = ResolveCallbackCorrelationId(request);
        var conflictingLink = report.ExternalLinks.FirstOrDefault(link =>
            link.Provider == EventReportExternalProvider.Osprey &&
            (MatchesOptional(link.ProviderSignalId, providerSignalId) || MatchesOptional(link.CorrelationId, correlationId)) &&
            !IsSameTarget(link.ProviderTargetScope, link.ProviderTargetId, providerTargetScope, providerTargetId));
        if (conflictingLink is not null)
        {
            return Failure(
                request.ReportId,
                "Osprey callback target does not match the existing provider link.",
                ["Osprey callback target does not match the existing provider link."],
                EventReportFailureCodes.ValidationFailed);
        }

        return Success(request.ReportId, "Osprey callback target is valid.");
    }

    private static bool HasSignal(EventReport report, NormalizedOspreySignal signal)
    {
        if (!string.IsNullOrWhiteSpace(signal.ExternalSignalId))
        {
            return report.Signals.Any(existing =>
                existing.Provider == EventReportSignalProvider.Osprey &&
                IsSameTarget(existing.ProviderTargetScope, existing.ProviderTargetId, signal.ProviderTargetScope, signal.ProviderTargetId) &&
                string.Equals(existing.ExternalSignalId, signal.ExternalSignalId, StringComparison.Ordinal));
        }

        return report.Signals.Any(existing =>
            existing.Provider == EventReportSignalProvider.Osprey &&
            IsSameTarget(existing.ProviderTargetScope, existing.ProviderTargetId, signal.ProviderTargetScope, signal.ProviderTargetId) &&
            string.Equals(existing.SignalType, signal.SignalType, StringComparison.Ordinal) &&
            string.Equals(existing.PolicyCode, signal.PolicyCode, StringComparison.Ordinal) &&
            string.Equals(existing.CorrelationId, signal.CorrelationId, StringComparison.Ordinal));
    }

    private static EventReportExternalLink GetOrCreateLink(
        EventReport report,
        Guid caseId,
        string correlationId,
        DateTime utcNow,
        EventReportProviderTargetScope providerTargetScope,
        string providerTargetId)
    {
        var existing = report.ExternalLinks.FirstOrDefault(link =>
            link.Provider == EventReportExternalProvider.Osprey &&
            IsSameTarget(link.ProviderTargetScope, link.ProviderTargetId, providerTargetScope, providerTargetId) &&
            string.Equals(link.CorrelationId, correlationId, StringComparison.Ordinal));

        if (existing is not null)
        {
            return existing;
        }

        var link = EventReportExternalLink.CreatePending(
            report.TenantId,
            report.Id,
            caseId,
            EventReportExternalProvider.Osprey,
            correlationId,
            utcNow,
            providerTargetScope,
            providerTargetId);
        report.ExternalLinks.Add(link);
        return link;
    }

    private static bool PromoteForHumanReview(EventReport report, EventReportCase reportCase, DateTime utcNow)
    {
        var changed = false;

        if (!report.IsTerminal && report.Priority < EventReportPriority.Urgent)
        {
            report.ChangePriority(EventReportPriority.Urgent, utcNow);
            changed = true;
        }

        if (reportCase.Status != EventReportCaseStatus.Closed && reportCase.Priority < EventReportPriority.Urgent)
        {
            reportCase.ChangePriority(EventReportPriority.Urgent, utcNow);
            changed = true;
        }

        return changed;
    }

    private static bool IsUrgentRecommendation(
        EventReportSignalVerdict verdict,
        EventReportRecommendedAction? recommendedAction)
    {
        return verdict is EventReportSignalVerdict.Urgent or EventReportSignalVerdict.AutoActionRecommended
            || recommendedAction is EventReportRecommendedAction.HeavyRedact or EventReportRecommendedAction.Escalate;
    }

    private static EventReportSignalVerdict MapVerdict(string? value)
    {
        return NormalizeCode(value) switch
        {
            "no_signal" or "none" or "allow" or "ok" => EventReportSignalVerdict.NoSignal,
            "likely_violation" or "violation" or "match" or "matched" => EventReportSignalVerdict.LikelyViolation,
            "urgent" or "critical" or "high_risk" => EventReportSignalVerdict.Urgent,
            "auto_action_recommended" or "auto_action" or "action" => EventReportSignalVerdict.AutoActionRecommended,
            _ => EventReportSignalVerdict.NeedsReview
        };
    }

    private static EventReportRecommendedAction? MapRecommendedAction(string? value)
    {
        return NormalizeCode(value) switch
        {
            "" => null,
            "none" or "no_action" => EventReportRecommendedAction.None,
            "dismiss" => EventReportRecommendedAction.Dismiss,
            "light_moderate" or "light_moderation" => EventReportRecommendedAction.LightModerate,
            "heavy_redact" or "heavy_redaction" or "recommend_heavy_redact" => EventReportRecommendedAction.HeavyRedact,
            "escalate" or "escalation" => EventReportRecommendedAction.Escalate,
            _ => null
        };
    }

    private static string ResolveCallbackCorrelationId(OspreySignalCallbackRequestDto request)
    {
        return NormalizeCorrelationId(
            NormalizeOptional(request.CorrelationId)
            ?? NormalizeOptional(request.ProviderSignalId)
            ?? $"osprey-callback:{request.ReportId:N}");
    }

    private static (EventReportProviderTargetScope Scope, string TargetId) ResolveProviderTarget(
        Guid tenantId,
        string? rawScope,
        string? rawTargetId)
    {
        var scope = string.Equals(rawScope, "tenant", StringComparison.OrdinalIgnoreCase)
            ? EventReportProviderTargetScope.Tenant
            : EventReportProviderTargetScope.Instance;
        var targetId = NormalizeOptional(rawTargetId)
            ?? (scope == EventReportProviderTargetScope.Tenant ? tenantId.ToString("N") : "instance");
        return (scope, targetId.Length <= 200 ? targetId : targetId[..200]);
    }

    private static bool IsSameTarget(
        EventReportProviderTargetScope leftScope,
        string leftTargetId,
        EventReportProviderTargetScope rightScope,
        string rightTargetId) =>
        leftScope == rightScope && string.Equals(leftTargetId, rightTargetId, StringComparison.Ordinal);

    private static bool MatchesOptional(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && string.Equals(left, right, StringComparison.Ordinal);

    private static string NormalizeCorrelationId(string value)
    {
        const int maxLength = 100;
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().Replace('-', '_').ToLowerInvariant();
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string message,
        IEnumerable<string> errors,
        string? failureCode = null) => failureCode is null
            ? BaseCommandResponse.Validation<Guid>(errors, message, id)
            : BaseCommandResponse.Failure<Guid>(failureCode, message, errors, id);

}
