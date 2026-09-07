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

public sealed class ProcessCoopDecisionCallbackCommandHandler(
    IEventReportRepository eventReportRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IMediator mediator) : IRequestHandler<ProcessCoopDecisionCallbackCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ProcessCoopDecisionCallbackCommand request,
        CancellationToken cancellationToken)
    {
        var validationResult = await new ProcessCoopDecisionCallbackCommandValidator().ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                request.Request is null
                    ? Guid.Empty
                    : ProcessCoopDecisionCallbackCommandValidator.ResolveReportId(request.Request),
                "Coop decision callback is invalid.",
                validationResult.Errors.Select(error => error.ErrorMessage),
                EventReportFailureCodes.ValidationFailed);
        }

        if (tenantContext.TenantId == Guid.Empty)
        {
            return Failure(Guid.Empty, "Tenant context could not be resolved.", ["Tenant context is required."], EventReportFailureCodes.TenantUnresolved);
        }

        var decision = NormalizeDecision(request);
        if (tenantContext.TenantId != decision.TenantId)
        {
            return Failure(
                decision.ReportId,
                "Coop callback tenant does not match the request tenant.",
                ["Coop callback tenant does not match the request tenant."],
                EventReportFailureCodes.TenantUnresolved);
        }

        if (decision.DecisionKind == EventReportDecisionKind.Duplicate && decision.DuplicateGroupId is null)
        {
            return Failure(
                decision.ReportId,
                "Duplicate Coop decisions require a duplicate group.",
                ["DuplicateGroupId is required for duplicate Coop decisions."],
                EventReportFailureCodes.DuplicateGroupRequired);
        }

        Guid decisionId = Guid.CreateVersion7();
        Guid executionId = Guid.CreateVersion7();
        DateTime capturedAtUtc = DateTime.UtcNow;
        var stage = await CaptureDecisionAsync(
            decision,
            decisionId,
            executionId,
            capturedAtUtc,
            cancellationToken);
        if (!stage.Response.IsSuccess || !stage.ShouldExecute)
        {
            return stage.Response;
        }

        var execution = await mediator.Send(new ExecuteReportDecisionCommand
        {
            EventId = decision.EventId,
            ReportId = decision.ReportId,
            CaseId = decision.CaseId,
            DecisionId = stage.DecisionId,
            ExpectedCaseConcurrencyStamp = stage.CaseConcurrencyStamp,
            CorrelationId = decision.CorrelationId
        }, cancellationToken);

        return execution.IsSuccess
            ? Success(stage.DecisionId, "Coop decision callback processed successfully.")
            : Failure(
                stage.DecisionId,
                "Coop decision execution failed.",
                execution.Errors ?? ["Decision execution failed."],
                execution.FailureCode ?? EventReportFailureCodes.DecisionExecutionFailed);
    }

    private async Task<CoopDecisionStageResult> CaptureDecisionAsync(
        NormalizedCoopDecision decision,
        Guid decisionId,
        Guid executionId,
        DateTime capturedAtUtc,
        CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var report = await eventReportRepository.GetByIdForUpdateAsync(decision.TenantId, decision.ReportId, token);
            var target = ValidateReportCase(report, decision);
            if (!target.Response.IsSuccess)
            {
                return CoopDecisionStageResult.NoExecution(target.Response);
            }

            var reportCase = target.Case!;
            var targetValidation = ValidateProviderTarget(report!, decision);
            if (!targetValidation.Response.IsSuccess)
            {
                return CoopDecisionStageResult.NoExecution(targetValidation.Response);
            }

            var existingDecision = FindExistingDecision(report!, decision);
            if (existingDecision is not null)
            {
                bool exactReplay = existingDecision.CaseId == decision.CaseId
                    && existingDecision.DecisionKind == decision.DecisionKind
                    && existingDecision.DuplicateGroupId == decision.DuplicateGroupId
                    && existingDecision.ModeratorUserId is null
                    && string.Equals(existingDecision.ReasonCode, decision.ReasonCode, StringComparison.Ordinal)
                    && string.Equals(existingDecision.SafeNote, decision.SafeNote, StringComparison.Ordinal);
                if (!exactReplay)
                {
                    return CoopDecisionStageResult.NoExecution(Failure(
                        existingDecision.Id,
                        "Coop decision replay does not match the captured decision.",
                        ["The provider decision id cannot be reused for another case or outcome."],
                        EventReportFailureCodes.DecisionInvalid));
                }

                if (existingDecision.Execution.State == EventReportDecisionExecutionState.Completed)
                {
                    return CoopDecisionStageResult.NoExecution(Success(existingDecision.Id, "Coop decision was already executed."));
                }

                if (reportCase.CurrentDecisionId == existingDecision.Id
                    && reportCase.Status == EventReportCaseStatus.DecisionReady)
                {
                    return CoopDecisionStageResult.Execute(
                        Success(existingDecision.Id, "Coop decision was already recorded."),
                        existingDecision.Id,
                        reportCase.ConcurrencyStamp);
                }

                return CoopDecisionStageResult.NoExecution(Failure(
                    existingDecision.Id,
                    "Coop decision is no longer the current executable decision.",
                    ["A stale provider decision cannot replace or reopen the current case decision."],
                    EventReportFailureCodes.DecisionInvalid));
            }

            if (!decision.ExpectedCaseConcurrencyStamp.HasValue)
            {
                return CoopDecisionStageResult.NoExecution(Failure(
                    decision.ReportId,
                    "A new Coop decision requires the current case concurrency stamp.",
                    ["ExpectedCaseConcurrencyStamp is required for a new provider decision."],
                    EventReportFailureCodes.CaseConcurrencyConflict));
            }

            if (reportCase.ConcurrencyStamp != decision.ExpectedCaseConcurrencyStamp.Value)
            {
                return CoopDecisionStageResult.NoExecution(Failure(
                    decision.ReportId,
                    "Event report case was changed by another request.",
                    ["Refresh the report case and try again."],
                    EventReportFailureCodes.CaseConcurrencyConflict));
            }

            if (!reportCase.CanSelectNewDecision())
            {
                return CoopDecisionStageResult.NoExecution(Failure(
                    decision.ReportId,
                    "The report case cannot accept this new Coop decision.",
                    ["Only an undecided case or a completed nonterminal current decision can be replaced."],
                    EventReportFailureCodes.CaseInvalidStatus));
            }

            if (report!.IsTerminal)
            {
                return CoopDecisionStageResult.NoExecution(Failure(
                    decision.ReportId,
                    "Terminal event reports cannot receive new Coop decisions.",
                    ["Terminal event reports cannot receive new decisions."],
                    EventReportFailureCodes.ReportInvalidStatus));
            }

            var providerDecision = EventReportDecision.Create(
                decision.TenantId,
                decision.CaseId,
                decision.ReportId,
                EventReportDecisionSource.CoopReviewer,
                decision.DecisionKind,
                decision.ReasonCode,
                decision.SafeNote,
                moderatorUserId: null,
                decision.ExternalDecisionId,
                capturedAtUtc,
                decision.ProviderTargetScope,
                decision.ProviderTargetId,
                decisionId,
                executionId,
                decision.DuplicateGroupId);

            reportCase.SelectDecision(providerDecision, capturedAtUtc);
            report.Decisions.Add(providerDecision);
            MarkCoopLinkSynced(report, decision, capturedAtUtc);
            await eventReportRepository.PersistDecisionCaptureAsync(report, providerDecision, token);

            return CoopDecisionStageResult.Execute(
                Success(providerDecision.Id, "Coop decision recorded successfully."),
                providerDecision.Id,
                reportCase.ConcurrencyStamp);
        }, cancellationToken);
    }

    private static (BaseCommandResponse<Guid> Response, EventReport? Report, EventReportCase? Case) ValidateReportCase(
        EventReport? report,
        NormalizedCoopDecision decision)
    {
        if (report is null)
        {
            return (Failure(decision.ReportId, "Event report was not found.", ["Event report was not found."], EventReportFailureCodes.ReportNotFound), null, null);
        }

        if (report.EventId != decision.EventId)
        {
            return (Failure(decision.ReportId, "Event report does not belong to the requested event.", ["Event report does not belong to the requested event."], EventReportFailureCodes.EventMismatch), null, null);
        }

        var reportCase = report.Cases.FirstOrDefault(candidate => candidate.Id == decision.CaseId);
        if (reportCase is null)
        {
            return (Failure(decision.ReportId, "Event report case was not found.", ["Event report case was not found."], EventReportFailureCodes.CaseNotFound), null, null);
        }

        return (Success(decision.ReportId, "Coop decision target is valid."), report, reportCase);
    }

    private static (BaseCommandResponse<Guid> Response, EventReport? Report) ValidateProviderTarget(
        EventReport report,
        NormalizedCoopDecision decision)
    {
        var conflictingDecision = report.Decisions.FirstOrDefault(candidate =>
            candidate.DecisionSource == EventReportDecisionSource.CoopReviewer &&
            string.Equals(candidate.ExternalDecisionId, decision.ExternalDecisionId, StringComparison.Ordinal) &&
            !IsSameTarget(candidate.ProviderTargetScope, candidate.ProviderTargetId, decision.ProviderTargetScope, decision.ProviderTargetId));
        if (conflictingDecision is not null)
        {
            return (Failure(
                decision.ReportId,
                "Coop callback target does not match the existing provider decision.",
                ["Coop callback target does not match the existing provider decision."],
                EventReportFailureCodes.ValidationFailed), null);
        }

        var conflictingLink = report.ExternalLinks.FirstOrDefault(candidate =>
            candidate.Provider == EventReportExternalProvider.Coop &&
            (MatchesOptional(candidate.ProviderCaseId, decision.ProviderCaseId) || MatchesOptional(candidate.CorrelationId, decision.CorrelationId)) &&
            !IsSameTarget(candidate.ProviderTargetScope, candidate.ProviderTargetId, decision.ProviderTargetScope, decision.ProviderTargetId));
        if (conflictingLink is not null)
        {
            return (Failure(
                decision.ReportId,
                "Coop callback target does not match the existing provider link.",
                ["Coop callback target does not match the existing provider link."],
                EventReportFailureCodes.ValidationFailed), null);
        }

        return (Success(decision.ReportId, "Coop provider target is valid."), report);
    }

    private static EventReportDecision? FindExistingDecision(EventReport report, NormalizedCoopDecision decision)
    {
        return report.Decisions.FirstOrDefault(candidate =>
            candidate.DecisionSource == EventReportDecisionSource.CoopReviewer &&
            string.Equals(candidate.ExternalDecisionId, decision.ExternalDecisionId, StringComparison.Ordinal) &&
            IsSameTarget(candidate.ProviderTargetScope, candidate.ProviderTargetId, decision.ProviderTargetScope, decision.ProviderTargetId));
    }

    private static void MarkCoopLinkSynced(
        EventReport report,
        NormalizedCoopDecision decision,
        DateTime utcNow)
    {
        var link = report.ExternalLinks
            .Where(candidate =>
                candidate.Provider == EventReportExternalProvider.Coop &&
                IsSameTarget(candidate.ProviderTargetScope, candidate.ProviderTargetId, decision.ProviderTargetScope, decision.ProviderTargetId))
            .OrderBy(candidate => candidate.CaseId != decision.CaseId)
            .ThenByDescending(candidate => string.Equals(candidate.ProviderCaseId, decision.ProviderCaseId, StringComparison.Ordinal))
            .FirstOrDefault();

        if (link is null)
        {
            link = EventReportExternalLink.CreatePending(
                report.TenantId,
                report.Id,
                decision.CaseId,
                EventReportExternalProvider.Coop,
                decision.CorrelationId,
                utcNow,
                decision.ProviderTargetScope,
                decision.ProviderTargetId);
            report.ExternalLinks.Add(link);
        }

        link.MarkSynced(
            decision.ProviderCaseId ?? link.ProviderCaseId,
            link.ProviderSignalId,
            decision.ProviderUrl ?? link.ProviderUrl,
            utcNow);
    }

    private static NormalizedCoopDecision NormalizeDecision(ProcessCoopDecisionCallbackCommand command)
    {
        var request = command.Request;
        var tenantId = ProcessCoopDecisionCallbackCommandValidator.ResolveTenantId(request);
        var reportId = ProcessCoopDecisionCallbackCommandValidator.ResolveReportId(request);
        var eventId = ProcessCoopDecisionCallbackCommandValidator.ResolveEventId(request);
        var caseId = ProcessCoopDecisionCallbackCommandValidator.ResolveCaseId(request);
        var actionId = ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(request.Action?.Id) ?? "coop_decision";
        var externalDecisionId = ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(
            request.ProviderDecisionId,
            request.ProviderDecisionIdSnake)
            ?? throw new InvalidOperationException("Validated Coop callbacks require a provider decision identifier.");
        var providerCaseId = NormalizeOptional(ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(
            request.ProviderCaseId,
            request.ProviderCaseIdSnake,
            request.Item?.Id));
        var providerUrl = NormalizeOptional(ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(request.ProviderUrl, request.ProviderUrlSnake));
        var correlationId = NormalizeCorrelationId(
            NormalizeOptional(ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(request.CorrelationId, request.CorrelationIdSnake))
            ?? externalDecisionId);
        var providerTarget = ResolveProviderTarget(
            tenantId,
            ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(request.ProviderTargetScope, request.ProviderTargetScopeSnake),
            ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(request.ProviderTargetId, request.ProviderTargetIdSnake));

        return new NormalizedCoopDecision(
            tenantId,
            eventId,
            reportId,
            caseId,
            ProcessCoopDecisionCallbackCommandValidator.ResolveExpectedCaseConcurrencyStamp(request),
            MapDecisionKind(actionId),
            ResolveReasonCode(request, actionId),
            ResolveSafeNote(request, actionId),
            ProcessCoopDecisionCallbackCommandValidator.ResolveDuplicateGroupId(request),
            Truncate(externalDecisionId, EventReportDecision.MaxExternalDecisionIdLength),
            TruncateNullable(providerCaseId, 200),
            TruncateNullable(providerUrl, 500),
            providerTarget.Scope,
            providerTarget.TargetId,
            correlationId);
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
        return (scope, Truncate(targetId, 200));
    }

    private static bool IsSameTarget(
        EventReportProviderTargetScope leftScope,
        string leftTargetId,
        EventReportProviderTargetScope rightScope,
        string rightTargetId) =>
        leftScope == rightScope && string.Equals(leftTargetId, rightTargetId, StringComparison.Ordinal);

    private static bool MatchesOptional(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && string.Equals(left, right, StringComparison.Ordinal);

    private static EventReportDecisionKind MapDecisionKind(string? actionId)
    {
        return NormalizeCode(actionId) switch
        {
            "allow" or "approve" or "approved" or "no_action" or "no_violation" or "dismiss" => EventReportDecisionKind.NoViolation,
            "duplicate" or "mark_duplicate" => EventReportDecisionKind.Duplicate,
            "needs_more_info" or "request_more_info" => EventReportDecisionKind.NeedsMoreInfo,
            "escalate" or "escalation" => EventReportDecisionKind.Escalate,
            "warn_organizer" or "warn" => EventReportDecisionKind.WarnOrganizer,
            "delete_content" or "remove_content" or "delete_event" or "remove_event" or "heavy_redact" or "heavy_redaction" or "redact" => EventReportDecisionKind.HeavyRedact,
            "light_moderate" or "light_moderation" or "hide_content" or "hide_event" or "suppress" or "moderate" => EventReportDecisionKind.LightModerate,
            _ => EventReportDecisionKind.Escalate
        };
    }

    private static string ResolveReasonCode(
        CoopDecisionCallbackRequestDto request,
        string actionId)
    {
        return Truncate(
            NormalizeOptional(ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(request.ReasonCode, request.ReasonCodeSnake))
            ?? NormalizeOptional(request.Policies.FirstOrDefault()?.Id)
            ?? NormalizeCode(actionId)
            ?? "coop_decision",
            EventReportDecision.MaxReasonCodeLength);
    }

    private static string? ResolveSafeNote(
        CoopDecisionCallbackRequestDto request,
        string actionId)
    {
        var explicitNote = NormalizeOptional(ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(request.SafeNote, request.SafeNoteSnake));
        if (!string.IsNullOrWhiteSpace(explicitNote))
        {
            return Truncate(explicitNote, EventReportDecision.MaxSafeNoteLength);
        }

        var policyIds = request.Policies
            .Select(policy => NormalizeOptional(policy.Id))
            .Where(policy => policy is not null)
            .Take(5)
            .ToArray();
        var ruleIds = request.Rules
            .Select(rule => NormalizeOptional(rule.Id))
            .Where(rule => rule is not null)
            .Take(5)
            .ToArray();
        if (policyIds.Length == 0 && ruleIds.Length == 0)
        {
            return null;
        }

        var builder = new StringBuilder($"Coop action {NormalizeCode(actionId)}.");
        if (policyIds.Length > 0)
        {
            builder.Append(" Policies: ").AppendJoin(", ", policyIds).Append('.');
        }

        if (ruleIds.Length > 0)
        {
            builder.Append(" Rules: ").AppendJoin(", ", ruleIds).Append('.');
        }

        return Truncate(builder.ToString(), EventReportDecision.MaxSafeNoteLength);
    }

    private static string NormalizeCorrelationId(string value) =>
        Truncate(value.Trim(), 100);

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

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? TruncateNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maxLength);

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
