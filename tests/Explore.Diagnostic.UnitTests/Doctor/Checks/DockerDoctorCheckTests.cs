using Explore.Diagnostic.Doctor;
using Explore.Diagnostic.Doctor.Checks;
using Explore.Diagnostic.Doctor.Infrastructure;

namespace Explore.Diagnostic.UnitTests.Doctor.Checks;

public class DockerDoctorCheckTests
{
    [Test]
    public async Task RunAsync_UsesOnlyReadOnlyVersionCommands()
    {
        var processRunner = new FakeDoctorProcessRunner();
        processRunner.AddResult("docker", "--version", new DoctorProcessResult(0, "Docker version 28", string.Empty));
        processRunner.AddResult("docker", "compose version", new DoctorProcessResult(0, "Docker Compose version v2", string.Empty));
        var check = new DockerDoctorCheck(processRunner);

        var result = await check.RunAsync(CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(DoctorCheckStatus.Pass);
        await Assert.That(processRunner.Calls.Count).IsEqualTo(2);
        await Assert.That(processRunner.Calls[0]).IsEqualTo(("docker", "--version"));
        await Assert.That(processRunner.Calls[1]).IsEqualTo(("docker", "compose version"));
    }

    [Test]
    public async Task RunAsync_WhenComposeUnavailable_ReturnsFail()
    {
        var processRunner = new FakeDoctorProcessRunner();
        processRunner.AddResult("docker", "--version", new DoctorProcessResult(0, "Docker version 28", string.Empty));
        processRunner.AddResult("docker", "compose version", new DoctorProcessResult(1, string.Empty, "compose missing"));
        var check = new DockerDoctorCheck(processRunner);

        var result = await check.RunAsync(CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(DoctorCheckStatus.Fail);
        await Assert.That(result.Summary).Contains("docker compose");
    }
}
