using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class LocalIdentityWriterConfigurationTests
{
    private const PrimaryDatabaseRole Writer = PrimaryDatabaseRole.LocalIdentityWriter;

    [Test]
    public async Task ExplicitWriterDoesNotInheritRequestOrMigrationCredentials()
    {
        var values = Values();
        string username = $"identity_{Guid.CreateVersion7():N}";
        string password = SecretsTestValues.CreateSecret();
        values["Database:LocalIdentityWriter:Username"] = username;
        values["Database:LocalIdentityWriter:Password"] = password;

        var selected = PrimaryDatabaseConfiguration.Bind(Configuration(values), Writer);

        await Assert.That(selected.Role).IsEqualTo(Writer);
        await Assert.That(selected.Username).IsEqualTo(username);
        await Assert.That(selected.Password == password).IsTrue();
    }

    [Test]
    public async Task WriterSelectsItsExplicitEnvironmentAliases()
    {
        var values = Values();
        string username = $"identity_{Guid.CreateVersion7():N}";
        string password = SecretsTestValues.CreateSecret();
        values["DATABASE_LOCAL_IDENTITY_WRITER_USERNAME"] = username;
        values["DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"] = password;

        var selected = PrimaryDatabaseConfiguration.Bind(Configuration(values), Writer);

        await Assert.That(selected.Username).IsEqualTo(username);
        await Assert.That(selected.Password == password).IsTrue();
    }

    [Test]
    [Arguments("Username")]
    [Arguments("Password")]
    public async Task MissingWriterCredentialCannotFallBackToAnotherAuthority(string missing)
    {
        var values = Values();
        values["Database:LocalIdentityWriter:Username"] = $"identity_{Guid.CreateVersion7():N}";
        values["Database:LocalIdentityWriter:Password"] = SecretsTestValues.CreateSecret();
        values.Remove($"Database:LocalIdentityWriter:{missing}");

        await Assert.That(() => PrimaryDatabaseConfiguration.Bind(Configuration(values), Writer))
            .Throws<OptionsValidationException>();
    }

    [Test]
    [Arguments("Username")]
    [Arguments("Password")]
    public async Task EmptyWriterCredentialMasksItsEnvironmentAlias(string empty)
    {
        var values = Values();
        values["Database:LocalIdentityWriter:Username"] = $"identity_{Guid.CreateVersion7():N}";
        values["Database:LocalIdentityWriter:Password"] = SecretsTestValues.CreateSecret();
        values[$"Database:LocalIdentityWriter:{empty}"] = string.Empty;
        values[$"DATABASE_LOCAL_IDENTITY_WRITER_{empty.ToUpperInvariant()}"] = SecretsTestValues.CreateSecret();

        await Assert.That(() => PrimaryDatabaseConfiguration.Bind(Configuration(values), Writer))
            .Throws<OptionsValidationException>();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> Values()
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "localhost",
            ["Database:Database"] = $"identity_{Guid.CreateVersion7():N}",
            ["Database:Schema"] = "islamu_event",
            ["Database:TlsMode"] = "Disabled",
            ["Database:Username"] = $"shared_{Guid.CreateVersion7():N}",
            ["Database:Password"] = SecretsTestValues.CreateSecret(),
        };
        foreach (var role in new[] { PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Migrator, PrimaryDatabaseRole.Global })
        {
            values[$"Database:{role}:Username"] = $"role_{Guid.CreateVersion7():N}";
            values[$"Database:{role}:Password"] = SecretsTestValues.CreateSecret();
        }
        return values;
    }
}
