using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Validators;
using Explore.Application.Responses;
using Explore.Application.Serialization;
using Explore.Application.Services.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.EventReporting.Handlers.Commands;

public sealed class ProcessCoopDecisionCallbackCommandHandler(
    IEventReportRepository eventReportRepository,
    IIncomingWebhookMessageRepository messageRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext)
    : ICommandHandler<ProcessCoopDecisionCallbackCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ProcessCoopDecisionCallbackCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await new ProcessCoopDecisionCallbackCommandValidator().ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Failure<Guid>(EventReportFailureCodes.ValidationFailed,
                "Coop advisory callback is invalid.", validation.Errors.Select(error => error.ErrorMessage));
        }

        var callback = request.Request;
        var tenantId = ProcessCoopDecisionCallbackCommandValidator.ResolveTenantId(callback);
        var reportId = ProcessCoopDecisionCallbackCommandValidator.ResolveReportId(callback);
        var caseId = ProcessCoopDecisionCallbackCommandValidator.ResolveCaseId(callback);
        var eventId = ProcessCoopDecisionCallbackCommandValidator.ResolveEventId(callback);
        var identity = ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(
            callback.ProviderDecisionId, callback.ProviderDecisionIdSnake)!;
        if (tenantContext.TenantId == Guid.Empty || tenantContext.TenantId != tenantId)
        {
            return Reject(reportId, EventReportFailureCodes.TenantUnresolved, "The callback tenant is not the authenticated tenant.");
        }

        if (request.IncomingWebhookMessageId == Guid.Empty)
        {
            return Reject(reportId, EventReportFailureCodes.ValidationFailed, "Coop advice requires its authenticated native inbox message.");
        }

        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var message = await messageRepository.GetByTenantAndIdForUpdateAsync(
                tenantId, request.IncomingWebhookMessageId, token);
            if (message is null || message.TenantId != tenantId || message.Provider != "coop" ||
                message.EventType != CoopDecisionIncomingWebhookHandler.StableEffectKind ||
                message.ProviderMessageId != identity || message.Status == IncomingWebhookMessageStatus.PayloadConflict ||
                message.PayloadBytes.IsEmpty || message.PayloadProvenanceId != (int)WebhookPayloadProvenance.ExactBytes ||
                !string.Equals(message.PayloadHash,
                    "sha256:" + Convert.ToHexStringLower(SHA256.HashData(message.PayloadBytes.Span)), StringComparison.Ordinal))
            {
                return Reject(reportId, EventReportFailureCodes.ValidationFailed, "The Coop callback does not match an authenticated exact-byte inbox envelope.");
            }

            CoopDecisionCallbackRequestDto? retained;
            try
            {
                retained = JsonSerializer.Deserialize(message.PayloadBytes.Span,
                    ExploreJsonContext.Default.CoopDecisionCallbackRequestDto);
            }
            catch (JsonException)
            {
                return Reject(reportId, EventReportFailureCodes.ValidationFailed, "The retained Coop callback is invalid.");
            }

            if (retained is null || !JsonSerializer.SerializeToUtf8Bytes(retained,
                    ExploreJsonContext.Default.CoopDecisionCallbackRequestDto).AsSpan()
                .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(callback,
                    ExploreJsonContext.Default.CoopDecisionCallbackRequestDto)))
            {
                return Reject(reportId, EventReportFailureCodes.ValidationFailed, "The callback material differs from its authenticated inbox message.");
            }

            var report = await eventReportRepository.GetByIdForUpdateAsync(tenantId, reportId, token);
            if (report is null)
                return Reject(reportId, EventReportFailureCodes.ReportNotFound, "Event report was not found.");
            if (report.TenantId != tenantId || report.EventId != eventId)
                return Reject(reportId, EventReportFailureCodes.EventMismatch, "The callback does not name the bound native event.");
            var reportCase = report.Cases.SingleOrDefault(candidate => candidate.Id == caseId);
            if (reportCase is null)
                return Reject(reportId, EventReportFailureCodes.CaseNotFound, "Event report case was not found.");

            var providerCaseId = ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(
                callback.ProviderCaseId, callback.ProviderCaseIdSnake, callback.Item?.Id);
            var correlationId = ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(
                callback.CorrelationId, callback.CorrelationIdSnake);
            var scope = ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(
                callback.ProviderTargetScope, callback.ProviderTargetScopeSnake);
            var targetId = ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(
                callback.ProviderTargetId, callback.ProviderTargetIdSnake);
            var links = report.ExternalLinks.Where(link =>
                link.Provider == EventReportExternalProvider.Coop &&
                link.TenantId == tenantId && link.ReportId == reportId && link.CaseId == caseId &&
                (providerCaseId is not null || correlationId is not null) &&
                (providerCaseId is null || link.ProviderCaseId == providerCaseId) &&
                (correlationId is null || link.CorrelationId == correlationId) &&
                (scope is null || string.Equals(link.ProviderTargetScope.ToString(), scope, StringComparison.OrdinalIgnoreCase)) &&
                (targetId is null || link.ProviderTargetId == targetId)).ToArray();
            if (links.Length != 1)
            {
                return Reject(reportId, EventReportFailureCodes.ValidationFailed, "Coop advice requires one exact existing native provider binding.");
            }

            var link = links[0];
            var existing = await eventReportRepository.GetProviderAdviceAsync(
                tenantId, EventReportSignalProvider.Coop, identity, token);
            if (existing.Count != 0)
            {
                return existing.Count == 1 && existing[0].AdvisoryOrdinal == 0 &&
                    existing[0].MatchesAdvisorySource(reportId, caseId, link.Id, message.PayloadHash, message.Id,
                        link.ProviderTargetScope, link.ProviderTargetId, link.CaseConcurrencyStamp)
                    ? BaseCommandResponse.Success(existing[0].Id, "Coop advice was already recorded.")
                    : Reject(reportId, EventReportFailureCodes.DecisionInvalid, "The provider identity was reused with different source, target or material.");
            }

            if (link.SyncState != EventReportSyncState.Synced)
            {
                return Reject(reportId, EventReportFailureCodes.ValidationFailed, "The native provider dispatch is not active for new advice.");
            }

            var expectedStamp = ProcessCoopDecisionCallbackCommandValidator.ResolveExpectedCaseConcurrencyStamp(callback);
            if (link.CaseConcurrencyStamp != reportCase.ConcurrencyStamp
                || expectedStamp is { } stamp && stamp != link.CaseConcurrencyStamp)
            {
                return Reject(reportId, EventReportFailureCodes.CaseConcurrencyConflict,
                    "The new advisory callback does not match the current native case version.");
            }

            var action = NormalizeCode(callback.Action?.Id ??
                throw new InvalidOperationException("Validated Coop callbacks require an action identifier."));
            var recommendation = MapRecommendation(action);
            var policy = ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(
                callback.ReasonCode, callback.ReasonCodeSnake, callback.Policies.FirstOrDefault()?.Id) ?? action;
            var summary = ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(callback.SafeNote, callback.SafeNoteSnake);
            if (summary is null)
            {
                var evidence = new StringBuilder();
                evidence.AppendJoin(", ", callback.Policies.Select(item => item.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Take(5));
                if (callback.Rules.Count > 0)
                    evidence.Append(" Rules: ").AppendJoin(", ", callback.Rules.Select(item => item.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Take(5));
                summary = evidence.Length == 0 ? null : evidence.ToString();
            }

            var advice = EventReportSignal.CreateProviderAdvice(
                report, reportCase, link, identity, 0, message.PayloadHash,
                action, policy, null,
                recommendation == EventReportRecommendedAction.Dismiss
                    ? EventReportSignalVerdict.NoSignal : EventReportSignalVerdict.NeedsReview,
                recommendation, summary is { Length: > 500 } ? summary[..500] : summary,
                identity, correlationId ?? identity[..Math.Min(identity.Length, 100)],
                DateTime.UtcNow, message);
            await eventReportRepository.PersistProviderAdviceAsync([advice], token);
            return BaseCommandResponse.Success(advice.Id, "Coop recommendation recorded for local human review.");
        }, cancellationToken);
    }

    private static EventReportRecommendedAction MapRecommendation(string action) => action switch
    {
        "allow" or "approve" or "approved" or "no_action" or "no_violation" or "dismiss" => EventReportRecommendedAction.Dismiss,
        "delete_content" or "remove_content" or "delete_event" or "remove_event" or "heavy_redact" or "heavy_redaction" or "redact" => EventReportRecommendedAction.HeavyRedact,
        "light_moderate" or "light_moderation" or "hide_content" or "hide_event" or "suppress" or "moderate" => EventReportRecommendedAction.LightModerate,
        _ => EventReportRecommendedAction.Escalate
    };

    private static string NormalizeCode(string value) => value.Trim().Replace('-', '_').ToLowerInvariant();

    private static BaseCommandResponse<Guid> Reject(Guid id, string code, string message) =>
        BaseCommandResponse.Failure<Guid>(code, message, [message], id);
}
