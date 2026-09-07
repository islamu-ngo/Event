using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Policies;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Specifications.EventReports;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventReporting.Handlers.Queries;

public sealed class GetModerationReportQueueRequestHandler(
    IEventReportRepository eventReportRepository,
    ITenantContext tenantContext)
    : IRequestHandler<GetModerationReportQueueRequest, PaginatedResult<ModerationReportQueueItemDto>>
{
    public async Task<PaginatedResult<ModerationReportQueueItemDto>> Handle(
        GetModerationReportQueueRequest request,
        CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<ModerationReportQueueItemDto>.NormalizeParameters(
            request.PageNumber,
            request.PageSize);

        if (tenantContext.TenantId == Guid.Empty || request.EventId == Guid.Empty)
        {
            return PaginatedResult<ModerationReportQueueItemDto>.Create([], 0, pageNumber, pageSize);
        }

        var specification = BuildSpecification(request);
        if (specification is null)
        {
            return PaginatedResult<ModerationReportQueueItemDto>.Create([], 0, pageNumber, pageSize);
        }

        var (items, totalCount) = await eventReportRepository.GetReportQueueAsync(
            tenantContext.TenantId,
            pageNumber,
            pageSize,
            specification,
            cancellationToken);

        var dtos = items.Select(Map).ToList();
        return PaginatedResult<ModerationReportQueueItemDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }

    private static EventReportQuerySpecification? BuildSpecification(GetModerationReportQueueRequest request)
    {
        if (ContainsUndefined(request.Statuses) ||
            ContainsUndefined(request.CaseStatuses) ||
            (request.Priority.HasValue && !Enum.IsDefined(request.Priority.Value)))
        {
            return null;
        }

        var specification = new EventReportQuerySpecification()
            .And(EventReportFilter.Event(request.EventId));

        if (request.Statuses.Count > 0)
        {
            specification = specification.And(EventReportFilter.Statuses(request.Statuses));
        }

        if (request.CaseStatuses.Count > 0)
        {
            specification = specification.And(EventReportFilter.CaseStatuses(request.CaseStatuses));
        }
        else if (request.OpenOnly)
        {
            specification = specification.And(EventReportFilter.OpenQueueItems());
        }

        if (request.Priority is { } priority)
        {
            specification = specification.And(EventReportFilter.Priority(priority));
        }

        if (!string.IsNullOrWhiteSpace(request.QueueCode))
        {
            specification = specification.And(EventReportFilter.QueueCode(request.QueueCode));
        }

        if (request.AssignedModeratorUserId is { } assignedModeratorUserId)
        {
            if (assignedModeratorUserId == Guid.Empty)
            {
                return null;
            }

            specification = specification.And(EventReportFilter.AssignedTo(assignedModeratorUserId));
        }

        if (request.UnassignedOnly)
        {
            specification = specification.And(EventReportFilter.Unassigned());
        }

        if (!string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            if (!EventReportReasonCodePolicy.TryNormalize(request.ReasonCode, out var normalizedReasonCode, out _))
            {
                return null;
            }

            specification = specification.And(EventReportFilter.ReasonCode(normalizedReasonCode));
        }

        return ApplySort(specification, request.SortBy, request.SortDescending);
    }

    private static EventReportQuerySpecification ApplySort(
        EventReportQuerySpecification specification,
        string? sortBy,
        bool sortDescending)
    {
        var sort = sortBy?.Trim().ToLowerInvariant();
        var sortSpecification = sort switch
        {
            "created_at" => EventReportSort.CreatedAt,
            "updated_at" => EventReportSort.UpdatedAt,
            "priority" => EventReportSort.Priority,
            "status" => EventReportSort.Status,
            "reason_code" => EventReportSort.ReasonCode,
            _ => null
        };

        if (sortSpecification is null)
        {
            return specification;
        }

        return sortDescending
            ? specification.SortByDescending(sortSpecification)
            : specification.SortBy(sortSpecification);
    }

    private static ModerationReportQueueItemDto Map(EventReport report)
    {
        var reasonOption = EventReportReasonCodePolicy.FindReasonOption(report.ReasonCode);
        var currentCase = SelectCurrentCase(report);

        return new ModerationReportQueueItemDto
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
            ReportCaseUpdatesConsent = report.ReportCaseUpdatesConsent,
            ReportFollowUpContactConsent = report.ReportFollowUpContactConsent,
            SubmittedAtUtc = report.CreatedAt,
            LastUpdatedAtUtc = report.UpdatedAt,
            ClosedAtUtc = report.ClosedAt,
            CurrentCase = currentCase is null ? null : MapCase(currentCase),
            DecisionCount = report.Decisions.Count,
            SignalCount = report.Signals.Count,
            ExternalLinkCount = report.ExternalLinks.Count
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
            AssignedModeratorUserId = reportCase.AssignedModeratorUserId,
            SlaDueAtUtc = reportCase.SlaDueAt,
            CreatedAtUtc = reportCase.CreatedAt,
            LastUpdatedAtUtc = reportCase.UpdatedAt,
            ConcurrencyStamp = reportCase.ConcurrencyStamp
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

    private static bool ContainsUndefined<TEnum>(IReadOnlyCollection<TEnum> values)
        where TEnum : struct, Enum
    {
        return values.Any(value => !Enum.IsDefined(value));
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
