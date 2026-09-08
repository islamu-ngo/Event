using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Policies;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventReporting.Handlers.Queries;

public sealed class GetModerationReportDetailRequestHandler(
    IEventReportRepository eventReportRepository,
    ITenantContext tenantContext,
    IEventReportEvidenceProtector evidenceProtector)
    : IRequestHandler<GetModerationReportDetailRequest, ModerationReportDetailDto?>
{
    public async Task<ModerationReportDetailDto?> Handle(
        GetModerationReportDetailRequest request,
        CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId == Guid.Empty || request.EventId == Guid.Empty || request.ReportId == Guid.Empty)
        {
            return null;
        }

        var report = await eventReportRepository.GetByIdWithEvidenceAsync(
            tenantContext.TenantId,
            request.ReportId,
            cancellationToken);

        return report is null || report.EventId != request.EventId
            ? null
            : Map(report, evidenceProtector);
    }

    private static ModerationReportDetailDto Map(
        EventReport report,
        IEventReportEvidenceProtector evidenceProtector)
    {
        var reasonOption = EventReportReasonCodePolicy.FindReasonOption(report.ReasonCode);
        var currentCase = SelectCurrentCase(report);

        return new ModerationReportDetailDto
        {
            Id = report.Id,
            EventId = report.EventId,
            ReporterKindId = (int)report.ReporterKind,
            ReporterKindCode = ToCode(report.ReporterKind),
            ReporterKindName = report.ReporterKind.ToString(),
            SourceKindId = (int)report.SourceKind,
            SourceKindCode = ToCode(report.SourceKind),
            SourceKindName = report.SourceKind.ToString(),
            StatusId = (int)report.Status,
            StatusCode = ToCode(report.Status),
            StatusName = report.Status.ToString(),
            PriorityId = (int)report.Priority,
            PriorityCode = ToCode(report.Priority),
            PriorityName = report.Priority.ToString(),
            SeverityHintId = report.SeverityHint.HasValue ? (int)report.SeverityHint.Value : null,
            SeverityHintCode = report.SeverityHint.HasValue ? ToCode(report.SeverityHint.Value) : null,
            SeverityHintName = report.SeverityHint?.ToString(),
            ReasonId = reasonOption?.Id,
            ReasonCode = reasonOption?.Code ?? report.ReasonCode,
            ReasonName = reasonOption?.DisplayName ?? report.ReasonCode,
            SubcategoryCode = report.SubcategoryCode,
            DuplicateGroupId = report.DuplicateGroupId,
            ReportCaseUpdatesConsent = report.ReportCaseUpdatesConsent,
            ReportFollowUpContactConsent = report.ReportFollowUpContactConsent,
            ReporterLocale = report.ReporterLocale,
            SubmittedAtUtc = report.CreatedAt,
            LastUpdatedAtUtc = report.UpdatedAt,
            ClosedAtUtc = report.ClosedAt,
            ConcurrencyStamp = report.ConcurrencyStamp,
            CurrentCase = currentCase is null ? null : MapCase(currentCase),
            Targets = report.Targets.OrderBy(target => target.Id).Select(MapTarget).ToArray(),
            EvidenceItems = report.EvidenceItems.OrderBy(evidence => evidence.CreatedAt).ThenBy(evidence => evidence.Id).Select(evidence => MapEvidence(evidence, evidenceProtector)).ToArray(),
            Cases = report.Cases.OrderBy(reportCase => reportCase.CreatedAt).ThenBy(reportCase => reportCase.Id).Select(MapCase).ToArray(),
            Decisions = report.Decisions.OrderBy(decision => decision.CreatedAt).ThenBy(decision => decision.Id).Select(MapDecision).ToArray(),
            Signals = report.Signals.OrderBy(signal => signal.CreatedAt).ThenBy(signal => signal.Id).Select(MapSignal).ToArray(),
            ExternalLinks = report.ExternalLinks.OrderBy(link => link.CreatedAt).ThenBy(link => link.Id).Select(MapExternalLink).ToArray()
        };
    }

    private static ModerationReportTargetDto MapTarget(EventReportTarget target)
    {
        return new ModerationReportTargetDto
        {
            Id = target.Id,
            ReportId = target.ReportId,
            TargetKindId = (int)target.TargetKind,
            TargetKindCode = ToCode(target.TargetKind),
            TargetKindName = target.TargetKind.ToString(),
            TargetId = target.TargetId,
            FieldPath = target.FieldPath,
            StorageObjectId = target.StorageObjectId
        };
    }

    private static ModerationReportEvidenceDto MapEvidence(
        EventReportEvidence evidence,
        IEventReportEvidenceProtector evidenceProtector)
    {
        var hasText = !string.IsNullOrWhiteSpace(evidence.TextBodyEncrypted);
        var textBody = hasText ? TryUnprotect(evidenceProtector, evidence.TextBodyEncrypted!) : null;

        return new ModerationReportEvidenceDto
        {
            Id = evidence.Id,
            ReportId = evidence.ReportId,
            EvidenceKindId = (int)evidence.EvidenceKind,
            EvidenceKindCode = ToCode(evidence.EvidenceKind),
            EvidenceKindName = evidence.EvidenceKind.ToString(),
            TextBody = textBody,
            HasTextBody = hasText,
            IsTextUnavailable = hasText && textBody is null,
            StorageObjectId = evidence.StorageObjectId,
            ContentHash = evidence.ContentHash,
            ClassificationId = (int)evidence.Classification,
            ClassificationCode = ToCode(evidence.Classification),
            ClassificationName = evidence.Classification.ToString(),
            RetentionUntilUtc = evidence.RetentionUntil,
            CreatedAtUtc = evidence.CreatedAt
        };
    }

    private static ModerationReportCaseDto MapCase(EventReportCase reportCase)
    {
        return new ModerationReportCaseDto
        {
            Id = reportCase.Id,
            ReportId = reportCase.ReportId,
            QueueCode = reportCase.QueueCode,
            StatusId = (int)reportCase.Status,
            StatusCode = ToCode(reportCase.Status),
            StatusName = reportCase.Status.ToString(),
            PriorityId = (int)reportCase.Priority,
            PriorityCode = ToCode(reportCase.Priority),
            PriorityName = reportCase.Priority.ToString(),
            SlaDueAtUtc = reportCase.SlaDueAt,
            CreatedAtUtc = reportCase.CreatedAt,
            LastUpdatedAtUtc = reportCase.UpdatedAt,
            ConcurrencyStamp = reportCase.ConcurrencyStamp
        };
    }

    private static ModerationReportDecisionDto MapDecision(EventReportDecision decision)
    {
        return new ModerationReportDecisionDto
        {
            Id = decision.Id,
            CaseId = decision.CaseId,
            ReportId = decision.ReportId,
            DecisionSourceId = (int)decision.DecisionSource,
            DecisionSourceCode = ToCode(decision.DecisionSource),
            DecisionSourceName = decision.DecisionSource.ToString(),
            DecisionKindId = (int)decision.DecisionKind,
            DecisionKindCode = ToCode(decision.DecisionKind),
            DecisionKindName = decision.DecisionKind.ToString(),
            ReasonCode = decision.ReasonCode,
            SafeNote = decision.SafeNote,
            ExternalDecisionId = decision.ExternalDecisionId,
            CreatedAtUtc = decision.CreatedAt
        };
    }

    private static ModerationReportSignalDto MapSignal(EventReportSignal signal)
    {
        return new ModerationReportSignalDto
        {
            Id = signal.Id,
            ReportId = signal.ReportId,
            EventId = signal.EventId,
            ProviderId = (int)signal.Provider,
            ProviderCode = ToCode(signal.Provider),
            ProviderName = signal.Provider.ToString(),
            SignalType = signal.SignalType,
            PolicyCode = signal.PolicyCode,
            Score = signal.Score,
            VerdictId = (int)signal.Verdict,
            VerdictCode = ToCode(signal.Verdict),
            VerdictName = signal.Verdict.ToString(),
            RecommendedActionId = signal.RecommendedAction.HasValue ? (int)signal.RecommendedAction.Value : null,
            RecommendedActionCode = signal.RecommendedAction.HasValue ? ToCode(signal.RecommendedAction.Value) : null,
            RecommendedActionName = signal.RecommendedAction?.ToString(),
            SafeSummary = signal.SafeSummary,
            CreatedAtUtc = signal.CreatedAt
        };
    }

    private static ModerationReportExternalLinkDto MapExternalLink(EventReportExternalLink link)
    {
        return new ModerationReportExternalLinkDto
        {
            Id = link.Id,
            ReportId = link.ReportId,
            CaseId = link.CaseId,
            ProviderId = (int)link.Provider,
            ProviderCode = ToCode(link.Provider),
            ProviderName = link.Provider.ToString(),
            SyncStateId = (int)link.SyncState,
            SyncStateCode = ToCode(link.SyncState),
            SyncStateName = link.SyncState.ToString(),
            LastSyncedAtUtc = link.LastSyncedAt,
            LastErrorCategory = link.LastErrorCategory,
            RetryCount = link.RetryCount,
            CreatedAtUtc = link.CreatedAt
        };
    }

    private static EventReportCase? SelectCurrentCase(EventReport report)
    {
        return report.Cases
            .OrderBy(reportCase => reportCase.Status == EventReportCaseStatus.Closed)
            .ThenByDescending(reportCase => reportCase.UpdatedAt ?? reportCase.CreatedAt)
            .ThenByDescending(reportCase => reportCase.Id)
            .FirstOrDefault();
    }

    private static string? TryUnprotect(
        IEventReportEvidenceProtector evidenceProtector,
        string protectedText)
    {
        try
        {
            var plaintext = evidenceProtector.Unprotect(protectedText);
            return string.IsNullOrWhiteSpace(plaintext) ? null : plaintext;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
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
}
