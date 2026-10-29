using Explore.Persistence.Database;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class PrimaryDatabasePublicIdentityTests
{
    [Test]
    public async Task CredentialRotationNeverChangesThePublicDatabaseIdentity()
    {
        var original = Options() with
        {
            Username = SecretsTestValues.CreateSecret(),
            Password = SecretsTestValues.CreateSecret()
        };
        var changed = original with
        {
            Username = SecretsTestValues.CreateSecret(),
            Password = SecretsTestValues.CreateSecret()
        };
        await Assert.That(PrimaryDatabasePublicIdentity.Digest(original))
            .IsEqualTo(PrimaryDatabasePublicIdentity.Digest(changed));
    }

    [Test]
    [Arguments("host")]
    [Arguments("port")]
    [Arguments("database")]
    [Arguments("schema")]
    [Arguments("tls")]
    [Arguments("certificate")]
    [Arguments("role")]
    [Arguments("provider")]
    [Arguments("flavor")]
    [Arguments("version")]
    public async Task PublicCoordinatesRemainIndependentlyBoundToTheMeasuredDatabase(string change)
    {
        var original = Options();
        var changed = change switch
        {
            "host" => original with { Host = "other.example.test" },
            "port" => original with { Port = 6543 },
            "database" => original with { Database = "isolated_measurement" },
            "schema" => original with { Schema = "other_schema" },
            "tls" => original with { TlsMode = PrimaryDatabaseTlsMode.Required },
            "certificate" => original with { TrustServerCertificate = true },
            "role" => original with { Role = PrimaryDatabaseRole.Migrator },
            "provider" => original with { Provider = PrimaryDatabaseProvider.Sqlite },
            "flavor" => original with { ServerFlavor = PrimaryDatabaseServerFlavor.MySql },
            "version" => original with { ServerVersion = new Version(18, 1) },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        await Assert.That(PrimaryDatabasePublicIdentity.Digest(original))
            .IsNotEqualTo(PrimaryDatabasePublicIdentity.Digest(changed));
    }

    private static PrimaryDatabaseConnectionOptions Options() => new()
    {
        Role = PrimaryDatabaseRole.Runtime,
        Provider = PrimaryDatabaseProvider.PostgreSql,
        Host = "primary.example.test",
        Port = 5432,
        Database = "primary_authority",
        Schema = PrimaryDatabaseConnectionOptions.DefaultSchema
    };
}
