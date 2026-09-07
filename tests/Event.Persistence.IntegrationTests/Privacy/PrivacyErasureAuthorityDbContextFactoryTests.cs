using System.Data;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Privacy;

[NotInParallel]
public sealed class PrivacyErasureAuthorityDbContextFactoryTests
{
    [Test]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public async Task CreateDbContext_UsesStructuredMigratorTargetWithoutOpeningIt()
    {
        IConfiguration configuration = StructuredMigratorConfiguration();

        await using PrivacyErasureAuthorityDbContext context =
            new PrivacyErasureAuthorityDbContextFactory().CreateDbContext(configuration);
        var target = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());

        await Assert.That(target.Host).IsEqualTo("127.0.0.1");
        await Assert.That(target.Port).IsEqualTo(3);
        await Assert.That(target.Database).IsEqualTo("explicit_authority_canary");
        await Assert.That(target.Username).IsEqualTo("migrator");
        await Assert.That(context.Database.GetDbConnection().State).IsEqualTo(ConnectionState.Closed);
    }

    [Test]
    [TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]
    public async Task CreateDbContext_UsesDistinctAuthorityMigrationHistoryTable()
    {
        await using PrivacyErasureAuthorityDbContext context =
            new PrivacyErasureAuthorityDbContextFactory().CreateDbContext(
                StructuredMigratorConfiguration());
        string script = context.GetService<IHistoryRepository>().GetCreateIfNotExistsScript();

        await Assert.That(script).Contains(
            PrivacyErasureAuthorityDatabaseConfiguration.MigrationsHistoryTable);
        await Assert.That(script).DoesNotContain("__EFMigrationsHistory\"");
    }

    [Test]
    public async Task CreateDbContext_RawConnectionArgumentDoesNotBypassStructuredContract()
    {
        const string secret = "raw-connection-secret";

        const string structuredProvider = "PrivacyErasureAuthorityDatabase__Provider";
        const string providerAlias = "PRIVACY_ERASURE_AUTHORITY_PROVIDER";
        string? originalStructured = Environment.GetEnvironmentVariable(structuredProvider);
        string? originalAlias = Environment.GetEnvironmentVariable(providerAlias);
        try
        {
            Environment.SetEnvironmentVariable(structuredProvider, null);
            Environment.SetEnvironmentVariable(providerAlias, null);
            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
                new PrivacyErasureAuthorityDbContextFactory().CreateDbContext(
                [
                    "--SecretProvider:Provider", "Environment",
                    "--PrivacyErasureAuthorityDatabase:Provider", "Unsupported",
                    "--connection", $"Host=127.0.0.1;Database=raw;Username=raw;Password={secret}"
                ]));

            await Assert.That(exception.Message).DoesNotContain(secret);
            await Assert.That(exception.OptionsName)
                .IsEqualTo(PrivacyErasureAuthorityDatabaseConfiguration.SectionName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(structuredProvider, originalStructured);
            Environment.SetEnvironmentVariable(providerAlias, originalAlias);
        }
    }

    private static IConfiguration StructuredMigratorConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PrivacyErasureAuthorityDatabase:Provider"] = "PostgreSql",
            ["PrivacyErasureAuthorityDatabase:Host"] = "127.0.0.1",
            ["PrivacyErasureAuthorityDatabase:Port"] = "3",
            ["PrivacyErasureAuthorityDatabase:Database"] = "explicit_authority_canary",
            ["PrivacyErasureAuthorityDatabase:TlsMode"] = "Prefer",
            ["PrivacyErasureAuthorityDatabase:TrustServerCertificate"] = "false",
            ["PrivacyErasureAuthorityDatabase:Migrator:Username"] = "migrator",
            ["PrivacyErasureAuthorityDatabase:Migrator:Password"] = "migrator-secret",
        }).Build();
}
