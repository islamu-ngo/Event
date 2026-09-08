namespace Explore.Application.DTOs.EventReporting;

public sealed record UpdateReportingProviderLocksDto
{
    public ReportingProviderLockUpdateDto? General { get; init; }
    public ReportingProviderLockUpdateDto? Osprey { get; init; }
    public ReportingProviderLockUpdateDto? Coop { get; init; }
}

public sealed record ReportingProviderLockUpdateDto
{
    public bool Locked { get; init; }
}
