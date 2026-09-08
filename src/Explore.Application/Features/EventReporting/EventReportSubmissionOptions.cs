namespace Explore.Application.Features.EventReporting;

public sealed class EventReportSubmissionOptions
{
    public const string SectionName = "Reporting";
    public const int MinCaseSlaHours = 1;
    public const int MaxCaseSlaHours = 720;
    public const int DefaultCaseSlaHours = 48;

    public bool RequireAuthenticatedReporter { get; set; } = true;
    public int MaxReportsPerUserPerHour { get; set; } = 10;
    public int MaxReportsPerEventPerUserPerDay { get; set; } = 3;
    public int DuplicateWindowHours { get; set; } = 24;
    public int ReporterTextRetentionDays { get; set; } = 180;
    public int MaxReporterTextLength { get; set; } = 4000;
    public string DefaultQueueCode { get; set; } = "default";
    public int CaseSlaHours { get; set; } = DefaultCaseSlaHours;
    public string? ReporterFingerprintPepper { get; set; }

    public static bool IsValidCaseSlaHours(int caseSlaHours) =>
        caseSlaHours is >= MinCaseSlaHours and <= MaxCaseSlaHours;
}
