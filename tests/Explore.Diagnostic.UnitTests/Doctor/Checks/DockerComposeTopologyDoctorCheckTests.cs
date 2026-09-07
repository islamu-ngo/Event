using Explore.Diagnostic.Doctor;
using Explore.Diagnostic.Doctor.Checks;

namespace Explore.Diagnostic.UnitTests.Doctor.Checks;

public class DockerComposeTopologyDoctorCheckTests
{
    private const string Root = "/repo";
    private static readonly string ComposePath = Path.Combine(Root, "docker-compose.yml");

    [Test]
    public async Task RunAsync_WhenApiEndpointDefaultUsesStaleServiceName_ReturnsWarn()
    {
        var fileSystem = new FakeDoctorFileSystem();
        fileSystem.AddFile(ComposePath, RequiredServices() + "\n      API_ENDPOINT: ${API_ENDPOINT:-http://eventapi:8080/}");
        var check = new DockerComposeTopologyDoctorCheck(fileSystem, Root);

        var result = await check.RunAsync(CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(DoctorCheckStatus.Warn);
        await Assert.That(result.Remediation).Contains("http://islamu-event-api:8080/");
    }

    [Test]
    public async Task RunAsync_WhenComposeUsesIslamuServiceNames_ReturnsPass()
    {
        var fileSystem = new FakeDoctorFileSystem();
        fileSystem.AddFile(ComposePath, RequiredServices() + "\n      API_ENDPOINT: ${API_ENDPOINT:-http://islamu-event-api:8080/}");
        var check = new DockerComposeTopologyDoctorCheck(fileSystem, Root);

        var result = await check.RunAsync(CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(DoctorCheckStatus.Pass);
    }

    [Test]
    public async Task RunAsync_WhenRequiredServiceMissing_ReturnsFail()
    {
        var fileSystem = new FakeDoctorFileSystem();
        fileSystem.AddFile(ComposePath, RequiredServices().Replace("  redis:\n", string.Empty, StringComparison.Ordinal));
        var check = new DockerComposeTopologyDoctorCheck(fileSystem, Root);

        var result = await check.RunAsync(CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(DoctorCheckStatus.Fail);
        await Assert.That(result.Remediation).Contains("redis");
    }

    private static string RequiredServices() =>
        "services:\n" +
        "  postgres:\n" +
        "  redis:\n" +
        "  keycloak:\n" +
        "  islamu-event-api:\n" +
        "  islamu-event-ui:\n";
}
