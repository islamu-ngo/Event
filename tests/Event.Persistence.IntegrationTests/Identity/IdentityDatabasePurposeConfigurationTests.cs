using System.Security.Cryptography;
using Explore.Persistence.Identity;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class IdentityDatabasePurposeConfigurationTests
{
    private const PrimaryDatabaseRole Writer = PrimaryDatabaseRole.LocalIdentityWriter;

    [Test]
    public async Task ExplicitWriterEnvironmentAliasesSupportTheMigrationCompositionWithoutApiMapping()
    {
        string username = $"writer_{Guid.CreateVersion7():N}";
        string password = Secret();
        var values = new Dictionary<string, string?>
        {
            ["IDENTITY_DATABASE_TOPOLOGY"] = "external",
            ["IDENTITY_DATABASE_PROVIDER"] = "PostgreSql",
            ["IDENTITY_DATABASE_HOST"] = "localhost",
            ["IDENTITY_DATABASE_NAME"] = $"identity_{Guid.CreateVersion7():N}",
            ["IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME"] = username,
            ["IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"] = password,
        };
        var selected = IdentityDatabaseConfiguration.BindExternal(Configuration(values), Writer);
        var connection = new NpgsqlConnectionStringBuilder(selected.ConnectionString);
        await Assert.That(connection.Username).IsEqualTo(username);
        await Assert.That(connection.Password == password).IsTrue();

        values["IdentityDatabase:LocalIdentityWriter:Password"] = string.Empty;
        await Assert.That(() => IdentityDatabaseConfiguration.BindExternal(Configuration(values), Writer))
            .Throws<OptionsValidationException>();
    }

    [Test]
    public async Task WriterIgnoresSharedConnectionAndSelectsOnlyItsExplicitCredentials()
    {
        var values = Values();
        string username = $"writer_{Guid.CreateVersion7():N}";
        string password = Secret();
        values["IdentityDatabase:LocalIdentityWriter:Username"] = username;
        values["IdentityDatabase:LocalIdentityWriter:Password"] = password;

        var selected = IdentityDatabaseConfiguration.BindExternal(Configuration(values), Writer);
        var connection = new NpgsqlConnectionStringBuilder(selected.ConnectionString);

        await Assert.That(connection.Username).IsEqualTo(username);
        await Assert.That(connection.Password == password).IsTrue();
    }

    [Test]
    [Arguments("Username")]
    [Arguments("Password")]
    public async Task MissingWriterCredentialCannotBorrowTheSharedConnection(string missing)
    {
        var values = Values();
        values["IdentityDatabase:LocalIdentityWriter:Username"] = $"writer_{Guid.CreateVersion7():N}";
        values["IdentityDatabase:LocalIdentityWriter:Password"] = Secret();
        values.Remove($"IdentityDatabase:LocalIdentityWriter:{missing}");

        await Assert.That(() => IdentityDatabaseConfiguration.BindExternal(Configuration(values), Writer))
            .Throws<OptionsValidationException>();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.InstanceKeyWrite)]
    public async Task UnsupportedIdentityPurposeCannotBorrowMigratorCredentials(PrimaryDatabaseRole role)
    {
        await Assert.That(() => IdentityDatabaseConfiguration.BindExternal(Configuration(Values()), role))
            .Throws<InvalidOperationException>();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> Values() => new()
    {
        ["IdentityDatabase:Topology"] = "external",
        ["IdentityDatabase:Provider"] = "PostgreSql",
        ["IdentityDatabase:Host"] = "localhost",
        ["IdentityDatabase:Name"] = $"identity_{Guid.CreateVersion7():N}",
        ["IdentityDatabase:Schema"] = "islamu_identity",
        ["IdentityDatabase:TlsMode"] = "Disabled",
        ["IdentityDatabase:Runtime:Username"] = $"runtime_{Guid.CreateVersion7():N}",
        ["IdentityDatabase:Runtime:Password"] = Secret(),
        ["IdentityDatabase:Migrator:Username"] = $"migrator_{Guid.CreateVersion7():N}",
        ["IdentityDatabase:Migrator:Password"] = Secret(),
        ["IdentityDatabase:ConnectionString"] = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = $"shared_{Guid.CreateVersion7():N}",
            Username = $"shared_{Guid.CreateVersion7():N}",
            Password = Secret(),
        }.ConnectionString,
    };

    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
