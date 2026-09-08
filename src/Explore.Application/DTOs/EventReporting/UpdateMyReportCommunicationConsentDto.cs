namespace Explore.Application.DTOs.EventReporting;

public sealed record UpdateMyReportCommunicationConsentDto
{
    public required ReportCommunicationConsentUpdateDto Consent { get; init; }
}

public sealed record ReportCommunicationConsentUpdateDto
{
    public bool ReportCaseUpdatesConsent { get; init; }
    public bool ReportFollowUpContactConsent { get; init; }
}
