using Explore.Diagnostic.Doctor;
using Explore.Diagnostic.Doctor.Checks;
using Explore.Diagnostic.Doctor.Infrastructure;

namespace Explore.Diagnostic.UnitTests.Doctor.Checks;

public class GlobalJsonSdkDoctorCheckTests
{
    private const string Root = "/repo";
    private static readonly string GlobalJsonPath = Path.Combine(Root, "global.json");

    [Test]
    public async Task RunAsync_WhenInstalledSdkMatchesGlobalJson_ReturnsPass()
    {
        var fileSystem = new FakeDoctorFileSystem();
        fileSystem.AddFile(GlobalJsonPath, "{ \"sdk\": { \"version\": \"10.0.300\" } }");
        var processRunner = new FakeDoctorProcessRunner();
        processRunner.AddResult("dotnet", "--version", new DoctorProcessResult(0, "10.0.300\n", string.Empty));
        var check = new GlobalJsonSdkDoctorCheck(fileSystem, processRunner, Root);

        var result = await check.RunAsync(CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(DoctorCheckStatus.Pass);
    }

    [Test]
    public async Task RunAsync_WhenInstalledSdkDiffers_ReturnsWarn()
    {
        var fileSystem = new FakeDoctorFileSystem();
        fileSystem.AddFile(GlobalJsonPath, "{ \"sdk\": { \"version\": \"10.0.300\" } }");
        var processRunner = new FakeDoctorProcessRunner();
        processRunner.AddResult("dotnet", "--version", new DoctorProcessResult(0, "10.0.301\n", string.Empty));
        var check = new GlobalJsonSdkDoctorCheck(fileSystem, processRunner, Root);

        var result = await check.RunAsync(CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(DoctorCheckStatus.Warn);
        await Assert.That(result.Remediation).Contains("pinned SDK");
    }
}
