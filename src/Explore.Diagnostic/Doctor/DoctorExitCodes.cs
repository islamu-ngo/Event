namespace Explore.Diagnostic.Doctor;

public static class DoctorExitCodes
{
    public const int Success = 0;
    public const int HardFailure = 1;

    public static int FromReport(DoctorReport report) => report.HasFailures ? HardFailure : Success;
}
