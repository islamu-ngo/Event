namespace Explore.Blazor.Client.Contracts.Services.Admissions;

public sealed class AdmissionCheckInUiResult
{
    private string _code = AdmissionCheckInUiCodes.Rejected;

    public AdmissionCheckInUiStatus Status { get; set; }

    public string Code
    {
        get => _code;
        set => _code = AdmissionCheckInUiCodes.Normalize(value);
    }

    public string ResultCode
    {
        get => _code;
        set => _code = AdmissionCheckInUiCodes.Normalize(value);
    }

    public string? Message { get; set; }
}

public enum AdmissionCheckInUiStatus
{
    Completed,
    OnlineRequired,
    Saturated
}

public static class AdmissionCheckInUiCodes
{
    public const string CheckedIn = "CheckedIn";
    public const string AlreadyCheckedIn = "AlreadyCheckedIn";
    public const string Rejected = "Rejected";

    public static string Normalize(string? code) => code switch
    {
        CheckedIn => CheckedIn,
        AlreadyCheckedIn => AlreadyCheckedIn,
        _ => Rejected
    };
}
