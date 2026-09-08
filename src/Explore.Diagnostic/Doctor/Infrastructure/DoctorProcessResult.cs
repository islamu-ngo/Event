namespace Explore.Diagnostic.Doctor.Infrastructure;

public sealed record DoctorProcessResult(int ExitCode, string StandardOutput, string StandardError);
