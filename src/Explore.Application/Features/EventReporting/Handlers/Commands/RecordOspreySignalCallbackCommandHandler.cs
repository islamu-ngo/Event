using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Validators;
using Explore.Application.Responses;
using Explore.Application.Serialization;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.EventReporting.Handlers.Commands;

public sealed class RecordOspreySignalCallbackCommandHandler(
    IEventReportRepository eventReportRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext) : ICommandHandler<RecordOspreySignalCallbackCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        RecordOspreySignalCallbackCommand request,
        CancellationToken cancellationToken)
    {
        var validationResult = await new RecordOspreySignalCallbackCommandValidator().ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                request.Request?.ReportId ?? Guid.Empty,
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

        return await unitOfWork.ExecuteSerializableAsync(async token =>
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

            if (report.TenantId != tenantContext.TenantId || report.EventId != request.Request.EventId)
            {
                return Failure(
                    request.Request.ReportId,
                    "Event report does not belong to the requested event.",
                    ["Event report does not belong to the requested event."],
                    EventReportFailureCodes.EventMismatch);
            }

            var reportCase = report.Cases.SingleOrDefault(candidate => candidate.Id == request.Request.CaseId);
            if (reportCase is null)
            {
                return Failure(
                    request.Request.ReportId,
                    "Event report case was not found.",
                    ["Event report case was not found."],
                    EventReportFailureCodes.CaseNotFound);
            }

            var now = DateTime.UtcNow;
            var providerSignalId = NormalizeOptional(request.Request.ProviderSignalId);
            var correlationId = NormalizeOptional(request.Request.CorrelationId);
            var scope = NormalizeOptional(request.Request.ProviderTargetScope);
            var targetId = NormalizeOptional(request.Request.ProviderTargetId);
            var links = report.ExternalLinks.Where(link =>
                link.Provider == EventReportExternalProvider.Osprey &&
                link.TenantId == report.TenantId && link.ReportId == report.Id && link.CaseId == reportCase.Id &&
                (providerSignalId is not null || correlationId is not null) &&
                (providerSignalId is null || link.ProviderSignalId == providerSignalId) &&
                (correlationId is null || link.CorrelationId == correlationId) &&
                (scope is null || string.Equals(link.ProviderTargetScope.ToString(), scope, StringComparison.OrdinalIgnoreCase)) &&
                (targetId is null || link.ProviderTargetId == targetId)).ToArray();
            if (links.Length != 1)
            {
                return Failure(report.Id, "Osprey advice requires one exact existing native provider binding.",
                    ["The callback cannot create or widen a provider dispatch binding."], EventReportFailureCodes.ValidationFailed);
            }

            var link = links[0];
            var identity = providerSignalId ?? correlationId!;
            var materialHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(request.Request, ExploreJsonContext.Default.OspreySignalCallbackRequestDto)));
            var existing = await eventReportRepository.GetProviderAdviceAsync(
                report.TenantId, EventReportSignalProvider.Osprey, identity, token);
            if (existing.Count != 0)
            {
                return existing.Count == request.Request.Signals.Count &&
                    existing.Select((signal, index) => signal.AdvisoryOrdinal == index &&
                        signal.MatchesAdvisorySource(report.Id, reportCase.Id, link.Id, materialHash, null,
                            link.ProviderTargetScope, link.ProviderTargetId, link.CaseConcurrencyStamp)).All(matches => matches)
                    ? Success(report.Id, "Osprey advice was already recorded.")
                    : Failure(report.Id, "The Osprey identity was reused with different source, target or material.",
                        ["Previously retained provider advice cannot be replaced."], EventReportFailureCodes.DecisionInvalid);
            }

            if (link.SyncState != EventReportSyncState.Synced)
            {
                return Failure(report.Id, "The native provider dispatch is not active for new advice.",
                    ["New advice requires a synced native provider dispatch."], EventReportFailureCodes.ValidationFailed);
            }

            if (link.CaseConcurrencyStamp != reportCase.ConcurrencyStamp)
            {
                return Failure(report.Id, "The advisory dispatch does not match the current native case version.",
                    ["New advice requires its original current native dispatch generation."],
                    EventReportFailureCodes.CaseConcurrencyConflict);
            }

            var advice = new List<EventReportSignal>(request.Request.Signals.Count);
            var urgentRecommendationReceived = false;

            for (var index = 0; index < request.Request.Signals.Count; index++)
            {
                var callbackSignal = request.Request.Signals[index];
                var normalized = NormalizeSignal(request.Request, callbackSignal, index, now, link.ProviderTargetScope, link.ProviderTargetId);
                urgentRecommendationReceived |= IsUrgentRecommendation(normalized.Verdict, normalized.RecommendedAction);

                advice.Add(EventReportSignal.CreateProviderAdvice(
                    report, reportCase, link, identity, index, materialHash,
                    normalized.SignalType,
                    normalized.PolicyCode,
                    normalized.Score,
                    normalized.Verdict,
                    normalized.RecommendedAction,
                    normalized.SafeSummary,
                    normalized.ExternalSignalId,
                    normalized.CorrelationId,
                    normalized.CreatedAtUtc));
            }

            if (urgentRecommendationReceived)
            {
                PromoteForHumanReview(report, reportCase, now);
            }

            await eventReportRepository.PersistProviderAdviceAsync(advice, token);
            return Success(report.Id, "Osprey advice recorded for local human review.");
        }, cancellationToken);
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
