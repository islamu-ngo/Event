using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class LocalIdentityPersistenceInvariantTests
{
    [Test]
    public async Task IdentityAggregatesCreateUuidVersionSevenKeys()
    {
        var user = new LocalIdentityUser();
        var role = new LocalIdentityRole();

        await Assert.That(user.Id.Version).IsEqualTo(7);
        await Assert.That(role.Id.Version).IsEqualTo(7);
    }

    [Test]
    public async Task ProviderPersistenceUsesLookupForeignKeysInsteadOfProviderStrings()
    {
        await using ExploreDbContext context = CreateSqliteContext();
        IEntityType user = context.Model.FindEntityType(typeof(User))!;
        IEntityType externalLogin = context.Model.FindEntityType(typeof(UserExternalLogin))!;

        await Assert.That(user.FindProperty("AuthProvider")).IsNull();
        await Assert.That(user.FindProperty("AuthProviderId")).IsNull();
        await Assert.That(externalLogin.FindProperty("Provider")).IsNull();
        await Assert.That(externalLogin.FindProperty(nameof(UserExternalLogin.AuthenticationProviderId))!.ClrType)
            .IsEqualTo(typeof(int));
        await Assert.That(externalLogin.GetForeignKeys().Any(foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(AuthenticationProvider))).IsTrue();
    }

    [Test]
    public async Task RelationalProvidersDeriveCollisionFreeNamesFromClrAndDbSetConventions()
    {
        await AssertNamesAsync(CreatePostgreSqlContext(), "local_identity_users", "authentication_providers", "islamu_event");
        await AssertNamesAsync(CreateSqlServerContext(), "local_identity_users", "authentication_providers", "islamu_event");
        await AssertNamesAsync(CreateSqliteContext(), "ie_local_identity_users", "ie_authentication_providers", null);
        await AssertNamesAsync(CreateMySqlContext(), "ie_local_identity_users", "ie_authentication_providers", null);
    }

    [Test]
    public async Task ProviderLookupSeederRepairsStableEnumRowsIdempotently()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"authentication-providers-{Guid.NewGuid():N}")
            .Options;
        await using var context = new ExploreDbContext(options);

        await LookupTableSeeder.SeedAuthenticationProvidersAsync(context, default);
        context.AuthenticationProviders.Remove(await context.AuthenticationProviders.SingleAsync(
            provider => provider.Id == (int)AuthenticationProviderKind.Local));
        await context.SaveChangesAsync();

        await LookupTableSeeder.SeedAuthenticationProvidersAsync(context, default);
        await LookupTableSeeder.SeedAuthenticationProvidersAsync(context, default);

        var providers = await context.AuthenticationProviders
            .OrderBy(provider => provider.Id)
            .Select(provider => new { provider.Id, provider.MasterCode })
            .ToArrayAsync();

        await Assert.That(providers.Select(provider => (provider.Id, provider.MasterCode)).SequenceEqual(
        [
            ((int)AuthenticationProviderKind.Keycloak, "KEYCLOAK"),
            ((int)AuthenticationProviderKind.Atproto, "ATPROTO"),
            ((int)AuthenticationProviderKind.Google, "GOOGLE"),
            ((int)AuthenticationProviderKind.Local, "LOCAL"),
            ((int)AuthenticationProviderKind.Development, "DEVELOPMENT"),
        ])).IsTrue();
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task ExternalIdentityTopologyOwnsMigrationsForEveryProvider(
        PrimaryDatabaseProvider provider)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IdentityDatabase:Topology"] = "external",
                ["IdentityDatabase:Provider"] = provider.ToString(),
                ["IdentityDatabase:ConnectionString"] = ConnectionStringFor(provider),
                ["IdentityDatabase:ServerVersion"] = provider == PrimaryDatabaseProvider.MySql
                    ? "8.4"
                    : null,
            })
            .Build();
        // External topology probes require independent provider-specific migration services.
        var options = TestDbContextOptions.Create<ExternalIdentityDbContext>().EnableServiceProviderCaching(false);
        IdentityDatabaseProviderComposition.Configure(
            options,
            configuration,
            PrimaryDatabaseRole.Migrator);
        await using var context = new ExternalIdentityDbContext(options.Options);

        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();

        await Assert.That(migrations.Migrations).IsNotEmpty();
        await Assert.That(migrations.Migrations.Values.All(
            migration => migration.GetCustomAttributes(typeof(DbContextAttribute), inherit: false)
                .Cast<DbContextAttribute>()
                .Any(attribute => attribute.ContextType == typeof(ExternalIdentityDbContext))))
            .IsTrue();
    }

    [Test]
    public async Task ExternalIdentitySqliteMigrationCreatesCredentialSchemaAndIsIdempotent()
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(),
            $"external-identity-{Guid.NewGuid():N}.db");
        try
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["IdentityDatabase:Topology"] = "external",
                    ["IdentityDatabase:Provider"] = "Sqlite",
                    ["IdentityDatabase:ConnectionString"] = $"Data Source={databasePath}",
                })
                .Build();
            ILogger logger = Substitute.For<ILogger>();
            await Assert.That(await ExternalIdentityDatabaseMigrator
                .MigrateIfExternalAsync(
                    configuration,
                    logger,
                    CancellationToken.None))
                .IsTrue();
            await Assert.That(await ExternalIdentityDatabaseMigrator
                .MigrateIfExternalAsync(
                    configuration,
                    logger,
                    CancellationToken.None))
                .IsTrue();

            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync();
            var tableNames = new List<string>();
            while (await reader.ReadAsync())
            {
                tableNames.Add(reader.GetString(0));
            }

            await Assert.That(tableNames).Contains("ie_local_identity_users");
            await Assert.That(tableNames).Contains("ie_local_identity_roles");
            await Assert.That(tableNames).Contains(
                "ie___EFIdentityMigrationsHistory");
            await Assert.That(tableNames).DoesNotContain("ie_users");
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static ExploreDbContext CreatePostgreSqlContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=identity_model;Username=test;Password=test")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }

    private static ExploreDbContext CreateSqlServerContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlServer("Server=localhost;Database=identity_model;User Id=test;Password=test;TrustServerCertificate=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }

    private static ExploreDbContext CreateSqliteContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite("Data Source=:memory:")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }

    private static ExploreDbContext CreateMySqlContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseMySql(
                "Server=localhost;Database=identity_model;User=test;Password=test",
                new MySqlServerVersion(new Version(8, 4)))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }

    private static string ConnectionStringFor(PrimaryDatabaseProvider provider) =>
        provider switch
        {
            PrimaryDatabaseProvider.PostgreSql =>
                "Host=localhost;Database=identity_model;Username=test;Password=test",
            PrimaryDatabaseProvider.Sqlite => "Data Source=identity-model.db",
            PrimaryDatabaseProvider.SqlServer =>
                "Server=localhost;Database=identity_model;User Id=test;Password=test;TrustServerCertificate=true",
            PrimaryDatabaseProvider.MySql =>
                "Server=localhost;Database=identity_model;User=test;Password=test",
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };

    private static async Task AssertNamesAsync(
        ExploreDbContext context,
        string userTable,
        string providerTable,
        string? schema)
    {
        await using (context)
        {
            IEntityType user = context.Model.FindEntityType(typeof(LocalIdentityUser))!;
            IEntityType provider = context.Model.FindEntityType(typeof(AuthenticationProvider))!;
            await Assert.That(user.GetTableName()).IsEqualTo(userTable);
            await Assert.That(provider.GetTableName()).IsEqualTo(providerTable);
            await Assert.That(user.GetSchema()).IsEqualTo(schema);
            await Assert.That(provider.GetSchema()).IsEqualTo(schema);
        }
    }
}
