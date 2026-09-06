// ABOUTME: Verifies portable domain-host lookup and provider-specific projection lock coordination.
// ABOUTME: Covers bounded JSON lookup, lock command contracts, and real SQLite lock contention.

using System.Data.Common;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence;
using Explore.Persistence.Projections;
using Explore.Persistence.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class PrimaryPersistencePortabilityTests
{
    [Test]
    public async Task DomainHostLookup_OnSqlite_MatchesJsonStringCaseInsensitivelyAndIgnoresTrailingDot()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using ExploreDbContext context = CreateContext(connection);
        await CreateTenantSettingTableAsync(context);
        Guid expectedTenantId = Guid.CreateVersion7();
        await InsertSettingAsync(
            context,
            Guid.CreateVersion7(),
            "domains.tenant_subdomain",
            "not-valid-json");
        await InsertSettingAsync(
            context,
            expectedTenantId,
            "domains.tenant_custom_domain",
            SettingValueSerializer.Serialize("Events.Example.COM."));
        var repository = new TenantSettingRepository(context);

        TenantSetting? match = await repository.GetByDomainHostAsync("  events.example.com.  ");

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.TenantId).IsEqualTo(expectedTenantId);
        await Assert.That(context.ChangeTracker.Entries<TenantSetting>()).IsEmpty();
    }

    [Test]
    public async Task DomainHostLookup_OnSqlite_DoesNotUseRawLegacyValueFallback()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using ExploreDbContext context = CreateContext(connection);
        await CreateTenantSettingTableAsync(context);
        await InsertSettingAsync(
            context,
            Guid.CreateVersion7(),
            "domains.tenant_custom_domain",
            "events.example.com");
        var repository = new TenantSettingRepository(context);

        TenantSetting? match = await repository.GetByDomainHostAsync("events.example.com");

        await Assert.That(match).IsNull();
    }

    [Test]
    public async Task ProjectionLocks_OnSqlite_RejectSharedProbeDuringExclusiveTransaction()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"projection-lock-{Guid.NewGuid():N}.db");
        try
        {
            await using SqliteConnection ownerConnection = await OpenConnectionAsync(databasePath);
            await using SqliteConnection contenderConnection = await OpenConnectionAsync(databasePath);
            await using ExploreDbContext owner = CreateContext(ownerConnection);
            await using ExploreDbContext contender = CreateContext(contenderConnection);
            Guid tenantId = Guid.CreateVersion7();
            await using var transaction = await owner.Database.BeginTransactionAsync();

            bool exclusive = await ProjectionInfrastructure.TryAcquireAdvisoryLockAsync(
                owner,
                projectionLockKey: 82001,
                tenantId,
                exclusive: true,
                CancellationToken.None);
            bool blockedShared = await ProjectionInfrastructure.TryAcquireAdvisoryLockAsync(
                contender,
                projectionLockKey: 82001,
                tenantId,
                exclusive: false,
                CancellationToken.None);

            await Assert.That(exclusive).IsTrue();
            await Assert.That(blockedShared).IsFalse();
            await Assert.That(owner.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();

            bool sharedAfterRelease = await ProjectionInfrastructure.TryAcquireAdvisoryLockAsync(
                contender,
                projectionLockKey: 82001,
                tenantId,
                exclusive: false,
                CancellationToken.None);
            await Assert.That(sharedAfterRelease).IsTrue();
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public async Task ProjectionLocks_OnSqlite_DisposedTransactionReleasesExclusiveLease()
    {
        await using SqliteConnection ownerConnection = await OpenConnectionAsync();
        await using SqliteConnection contenderConnection = await OpenConnectionAsync();
        await using ExploreDbContext owner = CreateContext(ownerConnection);
        await using ExploreDbContext contender = CreateContext(contenderConnection);
        Guid tenantId = Guid.CreateVersion7();
        var transaction = await owner.Database.BeginTransactionAsync();

        bool exclusive = await ProjectionInfrastructure.TryAcquireAdvisoryLockAsync(
            owner,
            projectionLockKey: 82001,
            tenantId,
            exclusive: true,
            CancellationToken.None);
        await transaction.DisposeAsync();
        bool sharedAfterDispose = await ProjectionInfrastructure.TryAcquireAdvisoryLockAsync(
            contender,
            projectionLockKey: 82001,
            tenantId,
            exclusive: false,
            CancellationToken.None);

        await Assert.That(exclusive).IsTrue();
        await Assert.That(sharedAfterDispose).IsTrue();
    }

    [Test]
    [Arguments("SqlServer", true, "sp_getapplock", "Exclusive")]
    [Arguments("SqlServer", false, "sp_getapplock", "Shared")]
    [Arguments("MariaDb", true, "GET_LOCK", null)]
    [Arguments("MariaDb", false, "GET_LOCK", null)]
    [Arguments("MySql", true, "GET_LOCK", null)]
    [Arguments("MySql", false, "GET_LOCK", null)]
    public async Task ProjectionLocks_OnServerProviders_UseNonblockingTransactionOwnedCommands(
        string provider,
        bool exclusive,
        string commandFragment,
        string? expectedMode)
    {
        await using DbConnection connection = provider switch
        {
            "SqlServer" => new SqlConnection(),
            "MariaDb" or "MySql" => new MySqlConnection(),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
        string providerName = provider == "SqlServer"
            ? RelationalNamedLock.SqlServerProvider
            : RelationalNamedLock.MySqlProvider;

        await using DbCommand command = RelationalProjectionLock.CreateServerTryAcquireCommand(
            connection,
            transaction: null,
            providerName,
            "projection-resource",
            exclusive);

        await Assert.That(command.CommandText).Contains(commandFragment);
        await Assert.That(command.CommandText).Contains("0");
        if (provider == "SqlServer")
        {
            await Assert.That(command.CommandText).Contains("@LockOwner = 'Transaction'");
            await Assert.That(command.Parameters.Cast<DbParameter>()
                .Single(parameter => parameter.ParameterName.Contains("lockMode", StringComparison.OrdinalIgnoreCase))
                .Value).IsEqualTo(expectedMode);
        }
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<SqliteConnection> OpenConnectionAsync(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath};Default Timeout=0");
        await connection.OpenAsync();
        return connection;
    }

    private static ExploreDbContext CreateContext(SqliteConnection connection) =>
        new(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(SqliteProjectionLockTransactionInterceptor.Instance)
            .Options);

    private static Task CreateTenantSettingTableAsync(ExploreDbContext context) =>
        context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE ie_tenant_setting_overrides (
                id TEXT NOT NULL PRIMARY KEY,
                tenant_id TEXT NOT NULL,
                setting_key TEXT NOT NULL,
                value TEXT NOT NULL,
                is_locked INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                created_by TEXT NULL,
                updated_at TEXT NULL,
                updated_by TEXT NULL
            )
            """);

    private static Task InsertSettingAsync(
        ExploreDbContext context,
        Guid tenantId,
        string key,
        string value) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO ie_tenant_setting_overrides
                (id, tenant_id, setting_key, value, is_locked, created_at)
            VALUES
                ({Guid.CreateVersion7()}, {tenantId}, {key}, {value}, {false}, {DateTime.UtcNow})
            """);
}
