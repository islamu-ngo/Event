using System.Security.Cryptography;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class PrimaryDatabaseInstanceKeyAuthorityConfigurationTests
{
    [Test]
    [Arguments((PrimaryDatabaseRole)4, "INSTANCE_KEY_READ")]
    [Arguments((PrimaryDatabaseRole)5, "INSTANCE_KEY_WRITE")]
    [Arguments((PrimaryDatabaseRole)6, "INSTANCE_KEY_ISSUANCE")]
    [Arguments((PrimaryDatabaseRole)7, "PLATFORM_KEY_REPORT")]
    public async Task DedicatedRoleSelectsOnlyItsExplicitEnvironmentCredential(
        PrimaryDatabaseRole role, string environmentRole)
    {
        var values = ConfigurationValues();
        values.Remove($"Database:{role}:Username");
        values.Remove($"Database:{role}:Password");
        string username = $"purpose_{Guid.CreateVersion7():N}";
        string password = Secret();
        values[$"DATABASE_{environmentRole}_USERNAME"] = username;
        values[$"DATABASE_{environmentRole}_PASSWORD"] = password;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var selected = PrimaryDatabaseConfiguration.Bind(configuration, role);

        await Assert.That(selected.Role).IsEqualTo(role);
        await Assert.That(selected.Username).IsEqualTo(username);
        await Assert.That(selected.Password == password).IsTrue();
    }

    [Test]
    [Arguments((PrimaryDatabaseRole)4, "Username")]
    [Arguments((PrimaryDatabaseRole)4, "Password")]
    [Arguments((PrimaryDatabaseRole)5, "Username")]
    [Arguments((PrimaryDatabaseRole)5, "Password")]
    [Arguments((PrimaryDatabaseRole)6, "Username")]
    [Arguments((PrimaryDatabaseRole)6, "Password")]
    [Arguments((PrimaryDatabaseRole)7, "Username")]
    [Arguments((PrimaryDatabaseRole)7, "Password")]
    public async Task MissingDedicatedCredentialCannotInheritAnyOtherAuthority(
        PrimaryDatabaseRole role, string missing)
    {
        var values = ConfigurationValues();
        values.Remove($"Database:{role}:{missing}");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        await Assert.That(() => PrimaryDatabaseConfiguration.Bind(configuration, role))
            .Throws<OptionsValidationException>();
    }

    [Test]
    [Arguments((PrimaryDatabaseRole)4, "INSTANCE_KEY_READ")]
    [Arguments((PrimaryDatabaseRole)5, "INSTANCE_KEY_WRITE")]
    [Arguments((PrimaryDatabaseRole)6, "INSTANCE_KEY_ISSUANCE")]
    [Arguments((PrimaryDatabaseRole)7, "PLATFORM_KEY_REPORT")]
    public async Task ExplicitEmptyDedicatedCredentialMasksEnvironmentAlias(
        PrimaryDatabaseRole role, string environmentRole)
    {
        var values = ConfigurationValues();
        values[$"Database:{role}:Password"] = string.Empty;
        values[$"DATABASE_{environmentRole}_PASSWORD"] = Secret();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        await Assert.That(() => PrimaryDatabaseConfiguration.Bind(configuration, role))
            .Throws<OptionsValidationException>();
    }

    private static Dictionary<string, string?> ConfigurationValues()
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "127.0.0.1",
            ["Database:Database"] = $"roles_{Guid.CreateVersion7():N}",
            ["Database:Schema"] = "islamu_event",
            ["Database:TlsMode"] = "Disabled",
            ["Database:Username"] = $"root_{Guid.CreateVersion7():N}",
            ["Database:Password"] = Secret()
        };
        for (int role = 1; role <= 7; role++)
        {
            values[$"Database:{(PrimaryDatabaseRole)role}:Username"] = $"role_{Guid.CreateVersion7():N}";
            values[$"Database:{(PrimaryDatabaseRole)role}:Password"] = Secret();
        }
        return values;
    }

    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
}
