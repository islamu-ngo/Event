using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Policies;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.EventReporting.Mappers;

internal static class MyEventReportDtoMapper
{
    public static MyEventReportDto Map(EventReport report)
    {
        var reasonOption = EventReportReasonCodePolicy.FindReasonOption(report.ReasonCode);

        return new MyEventReportDto
        {
            Id = report.Id,
            EventId = report.EventId,
            StatusId = (int)report.Status,
            StatusCode = ToStatusCode(report.Status),
            StatusName = report.Status.ToString(),
            ReasonId = reasonOption?.Id,
            ReasonCode = reasonOption?.Code ?? report.ReasonCode,
            ReasonName = reasonOption?.DisplayName ?? report.ReasonCode,
            SubcategoryCode = report.SubcategoryCode,
            SubmittedAtUtc = report.CreatedAt,
            LastUpdatedAtUtc = report.UpdatedAt,
            ClosedAtUtc = report.ClosedAt,
            ReportCaseUpdatesConsent = report.ReportCaseUpdatesConsent,
            ReportFollowUpContactConsent = report.ReportFollowUpContactConsent
        };
    }

    private static string ToStatusCode(EventReportStatus status)
    {
        return status switch
        {
            EventReportStatus.Submitted => "submitted",
            EventReportStatus.Triaged => "triaged",
            EventReportStatus.UnderReview => "under_review",
            EventReportStatus.Actioned => "actioned",
            EventReportStatus.Dismissed => "dismissed",
            EventReportStatus.Duplicate => "duplicate",
            EventReportStatus.Escalated => "escalated",
            EventReportStatus.Closed => "closed",
            _ => "unknown"
        };
    }
}
