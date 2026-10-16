using System.Security.Cryptography;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class PrimaryDatabaseGlobalAuthorityConfigurationTests
{
    [Test]
    public async Task ExplicitGlobalRoleSelectsOnlyItsOwnCredentialAuthority()
    {
        var values = ConfigurationValues();
        string username = $"global_{Guid.CreateVersion7():N}";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        values["Database:Global:Username"] = username;
        values["Database:Global:Password"] = password;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var selected = PrimaryDatabaseConfiguration.Bind(configuration, (PrimaryDatabaseRole)3);
        await Assert.That(selected.Username).IsEqualTo(username);
        await Assert.That(selected.Password == password).IsTrue();
        await Assert.That(selected.Role).IsEqualTo((PrimaryDatabaseRole)3);
    }

    [Test]
    [Arguments("Username")]
    [Arguments("Password")]
    public async Task MissingGlobalCredentialCannotFallBackToOtherRoleOrRoot(string missing)
    {
        var values = ConfigurationValues();
        values["Database:Global:Username"] = $"global_{Guid.CreateVersion7():N}";
        values["Database:Global:Password"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        values.Remove("Database:Global:" + missing);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        await Assert.That(() => PrimaryDatabaseConfiguration.Bind(configuration, (PrimaryDatabaseRole)3))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UnknownRoleCannotAliasMigrationAuthority()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(ConfigurationValues()).Build();
        await Assert.That(() => PrimaryDatabaseConfiguration.Bind(configuration, (PrimaryDatabaseRole)99))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static Dictionary<string, string?> ConfigurationValues() => new()
    {
        ["Database:Provider"] = "PostgreSql",
        ["Database:Host"] = "127.0.0.1",
        ["Database:Database"] = $"roles_{Guid.CreateVersion7():N}",
        ["Database:Schema"] = "islamu_event",
        ["Database:TlsMode"] = "Disabled",
        ["Database:Username"] = $"root_{Guid.CreateVersion7():N}",
        ["Database:Password"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
        ["Database:Runtime:Username"] = $"runtime_{Guid.CreateVersion7():N}",
        ["Database:Runtime:Password"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
        ["Database:Migrator:Username"] = $"migrator_{Guid.CreateVersion7():N}",
        ["Database:Migrator:Password"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(24))
    };
}
