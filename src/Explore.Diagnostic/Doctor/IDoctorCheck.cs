namespace Explore.Diagnostic.Doctor;

public interface IDoctorCheck
{
    string Code { get; }
    DoctorCheckCategory Category { get; }
    Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken);
}
