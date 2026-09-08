using System.Diagnostics;

namespace Explore.Diagnostic.Doctor.Infrastructure;

public sealed class DefaultDoctorProcessRunner : IDoctorProcessRunner
{
    public async Task<DoctorProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new DoctorProcessResult(-1, string.Empty, ex.Message);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new DoctorProcessResult(process.ExitCode, await stdout, await stderr);
    }
}
