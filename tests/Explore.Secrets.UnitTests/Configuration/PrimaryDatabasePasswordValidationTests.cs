using System.Security.Cryptography;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class PrimaryDatabasePasswordValidationTests
{
    [Test]
    [Arguments("PostgreSql")]
    [Arguments("SqlServer")]
    [Arguments("MySql")]
    [Arguments("MariaDb")]
    public async Task ServerRolesRejectRetiredDefaultAndUsernamePasswords(string provider)
    {
        string username = $"runtime_{Guid.CreateVersion7():N}";
        foreach (string password in new[] { "explore", "ExPlOrE", " explore ", username, username.ToUpperInvariant() })
        {
            var configuration = Configuration(provider, username, password);
            await Assert.That(() => PrimaryDatabaseConfiguration.Bind(configuration, PrimaryDatabaseRole.Runtime))
                .Throws<OptionsValidationException>();
        }
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("SqlServer")]
    [Arguments("MySql")]
    [Arguments("MariaDb")]
    public async Task IndependentPasswordPreservesSignificantWhitespace(string provider)
    {
        string password = $" {Convert.ToHexString(RandomNumberGenerator.GetBytes(24))} ";
        var configuration = Configuration(provider, $"runtime_{Guid.CreateVersion7():N}", password);
        var selected = PrimaryDatabaseConfiguration.Bind(configuration, PrimaryDatabaseRole.Runtime);
        await Assert.That(selected.Password == password).IsTrue();
    }

    [Test]
    public async Task SqliteRequiresNoServerCredentials()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Database"] = "event-password-policy.db",
            ["Database:Schema"] = "islamu_event"
        }).Build();
        var selected = PrimaryDatabaseConfiguration.Bind(configuration, PrimaryDatabaseRole.Runtime);
        await Assert.That(string.IsNullOrEmpty(selected.Password)).IsTrue();
        await Assert.That(string.IsNullOrEmpty(selected.Username)).IsTrue();
    }

    private static IConfiguration Configuration(string provider, string username, string password) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = provider,
            ["Database:Host"] = "127.0.0.1",
            ["Database:Database"] = $"password_policy_{Guid.CreateVersion7():N}",
            ["Database:Schema"] = "islamu_event",
            ["Database:TlsMode"] = "Disabled",
            ["Database:Runtime:Username"] = username,
            ["Database:Runtime:Password"] = password
        }).Build();
}
