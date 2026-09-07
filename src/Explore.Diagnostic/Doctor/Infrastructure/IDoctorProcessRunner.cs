namespace Explore.Diagnostic.Doctor.Infrastructure;

public interface IDoctorProcessRunner
{
    Task<DoctorProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken);
}
