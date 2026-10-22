using Explore.Domain;
using Explore.Persistence.Identity;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Event.Persistence.IntegrationTests.Identity;

/// <summary>Scaffolding builds the real provider model without acquiring connection authority.</summary>
[NotInParallel]
public sealed class ExternalIdentityModelScaffoldingTests
{
    private const string Schema = "identity_scaffold";
    private const string RejectedAuthority = "--SecretProvider:Provider=InvalidScaffoldingAuthority";

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql, "Explore.Persistence")]
    [Arguments(PrimaryDatabaseProvider.Sqlite, "Explore.Persistence.Migrations.Sqlite")]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "Explore.Persistence.Migrations.SqlServer")]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "Explore.Persistence.Migrations.MySql")]
    [Arguments(PrimaryDatabaseProvider.MySql, "Explore.Persistence.Migrations.MySql")]
    public async Task ExplicitScaffoldingRetainsIdentityModelAndMigrationAssemblyWithoutCredentials(
        PrimaryDatabaseProvider provider, string migrationsAssembly)
    {
        await using var context = new ExternalIdentityDbContextFactory().CreateDbContext(
        [
            "--DesignTime:ModelOnly=true", "--DOTNET_ENVIRONMENT=Testing",
            $"--IdentityDatabase:Provider={provider}", $"--IdentityDatabase:Schema={Schema}",
            $"--IdentityDatabase:ServerVersion={(provider == PrimaryDatabaseProvider.MariaDb ? "10.11.0" : "8.4.0")}",
            RejectedAuthority
        ]);

        await Assert.That(context.Database.GetConnectionString()).IsNull();
        await Assert.That(provider switch
        {
            PrimaryDatabaseProvider.PostgreSql => context.Database.IsNpgsql(),
            PrimaryDatabaseProvider.Sqlite => context.Database.IsSqlite(),
            PrimaryDatabaseProvider.SqlServer => context.Database.IsSqlServer(),
            _ => context.Database.IsMySql()
        }).IsTrue();
        await Assert.That(context.GetService<IMigrationsAssembly>().Assembly.GetName().Name)
            .IsEqualTo(migrationsAssembly);
        var user = context.Model.FindEntityType(typeof(LocalIdentityUser));
        var eligibility = context.Model.FindEntityType(typeof(LocalIdentityEligibility));
        await Assert.That(user).IsNotNull();
        await Assert.That(eligibility).IsNotNull();
        bool schemaProvider = provider is PrimaryDatabaseProvider.PostgreSql or PrimaryDatabaseProvider.SqlServer;
        await Assert.That(user!.GetSchema()).IsEqualTo(schemaProvider ? Schema : null);
        await Assert.That(eligibility!.GetSchema()).IsEqualTo(schemaProvider ? Schema : null);
        await Assert.That(eligibility.GetTableName())
            .IsEqualTo(schemaProvider ? "local_identity_eligibilities" : "ie_local_identity_eligibilities");
        await Assert.That(eligibility.GetForeignKeys().Any(key =>
            key.PrincipalEntityType.ClrType == typeof(LocalIdentityUser))).IsTrue();
    }

    [Test]
    public async Task ProductionRejectsModelOnlyBeforeSecretAuthorityAcquisition()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            using var context = new ExternalIdentityDbContextFactory().CreateDbContext(
            [
                "--DesignTime:ModelOnly=true", "--DOTNET_ENVIRONMENT=Production",
                "--IdentityDatabase:Provider=Sqlite", $"--IdentityDatabase:Schema={Schema}", RejectedAuthority
            ]);
            return Task.CompletedTask;
        });

        await Assert.That(failure.Message).IsEqualTo("Model-only scaffolding requires Development or Testing.");
    }

    [Test]
    public async Task EnvironmentOnlyModelFlagCannotBypassTheRequiredSecretAuthority()
    {
        const string flag = "DesignTime__ModelOnly";
        string? previous = Environment.GetEnvironmentVariable(flag);
        try
        {
            Environment.SetEnvironmentVariable(flag, "true");
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            {
                using var context = new ExternalIdentityDbContextFactory().CreateDbContext(
                [
                    "--DOTNET_ENVIRONMENT=Testing", "--IdentityDatabase:Provider=Sqlite",
                    $"--IdentityDatabase:Schema={Schema}", RejectedAuthority
                ]);
                return Task.CompletedTask;
            });
            await Assert.That(failure.Message)
                .IsEqualTo("SecretProvider:Provider must explicitly select Environment, Infisical, or UserSecrets.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(flag, previous);
        }
    }

    [Test]
    public async Task UndefinedProviderCannotProduceAScaffoldingModel()
    {
        await RequireModelRejectionAsync(
            ["--IdentityDatabase:Provider=999", $"--IdentityDatabase:Schema={Schema}"],
            "Model-only scaffolding requires an explicit supported Identity provider.");
    }

    [Test]
    public async Task MissingSchemaCannotBorrowAnAmbientDatabaseNamespace()
    {
        await RequireModelRejectionAsync(
            ["--IdentityDatabase:Provider=Sqlite", "--IdentityDatabase:Schema="],
            "Model-only scaffolding requires an explicit valid Identity schema.");
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task MySqlFamilyCannotDiscoverServerVersionThroughALiveConnection(PrimaryDatabaseProvider provider)
    {
        await RequireModelRejectionAsync(
            [$"--IdentityDatabase:Provider={provider}", $"--IdentityDatabase:Schema={Schema}",
                "--IdentityDatabase:ServerVersion="],
            "Model-only Identity scaffolding requires an explicit MySQL or MariaDB server version.");
    }

    private static async Task RequireModelRejectionAsync(string[] selection, string expected)
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            using var context = new ExternalIdentityDbContextFactory().CreateDbContext(
                ["--DesignTime:ModelOnly=true", "--DOTNET_ENVIRONMENT=Testing", RejectedAuthority, .. selection]);
            return Task.CompletedTask;
        });
        await Assert.That(failure.Message).IsEqualTo(expected);
    }
}
