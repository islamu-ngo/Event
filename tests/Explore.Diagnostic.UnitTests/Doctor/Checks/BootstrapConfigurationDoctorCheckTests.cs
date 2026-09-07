using Explore.Diagnostic.Doctor;
using Explore.Diagnostic.Doctor.Checks;

namespace Explore.Diagnostic.UnitTests.Doctor.Checks;

public class BootstrapConfigurationDoctorCheckTests
{
    private const string Root = "/repo";
    private static readonly string ComposePath = Path.Combine(Root, "docker-compose.yml");

    [Test]
    public async Task RunAsync_WithStructuredDatabaseVariables_ReturnsPass()
    {
        var fileSystem = new FakeDoctorFileSystem();
        fileSystem.AddFile(ComposePath, ComposeWithDatabaseVariables() + "\n# Never pre-build ConnectionStrings__DefaultConnection here.");
        var check = new BootstrapConfigurationDoctorCheck(fileSystem, Root);

        var result = await check.RunAsync(CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(DoctorCheckStatus.Pass);
    }

    [Test]
    public async Task RunAsync_WithPrebuiltDefaultConnection_ReturnsFail()
    {
        var fileSystem = new FakeDoctorFileSystem();
        fileSystem.AddFile(ComposePath, ComposeWithDatabaseVariables() + "\nConnectionStrings__DefaultConnection: Host=db;Password=secret");
        var check = new BootstrapConfigurationDoctorCheck(fileSystem, Root);

        var result = await check.RunAsync(CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(DoctorCheckStatus.Fail);
        await Assert.That(result.Summary).Contains("raw ConnectionStrings__DefaultConnection");
        await Assert.That(result.Summary).DoesNotContain("secret");
    }

    [Test]
    public async Task RunAsync_WhenMigratorCredentialsAreMissing_ReturnsFail()
    {
        var fileSystem = new FakeDoctorFileSystem();
        fileSystem.AddFile(ComposePath, ComposeWithDatabaseVariables().Replace("Database__Migrator__Password: migrator", string.Empty, StringComparison.Ordinal));
        var check = new BootstrapConfigurationDoctorCheck(fileSystem, Root);

        var result = await check.RunAsync(CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(DoctorCheckStatus.Fail);
        await Assert.That(result.Remediation).Contains("Database__Migrator__Password");
    }

    private static string ComposeWithDatabaseVariables() =>
        "Database__Provider: PostgreSql\n" +
        "Database__Host: postgres\n" +
        "Database__Port: 5432\n" +
        "Database__Database: explore\n" +
        "Database__Runtime__Username: runtime\n" +
        "Database__Runtime__Password: runtime\n" +
        "Database__Migrator__Username: migrator\n" +
        "Database__Migrator__Password: migrator\n";
}
