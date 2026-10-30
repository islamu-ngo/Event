using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresSnapshotMaintenanceLeaseTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GlobalEnumerationMustPurgeEachOwnerThroughSeparateOrdinaryTenantAuthority()
    {
        Guid[] owners = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
        string suffix = Guid.CreateVersion7().ToString("N");
        string ordinaryRole = "purge_tenant_" + suffix;
        string globalRole = "purge_global_" + suffix;
        string ordinaryPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string globalPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        List<string> tables = [];
        string schema;
        DateTime now;
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using (var seed = fixture.CreateDbContext())
        {
            await PostgresDiscoverySnapshotMaintenanceContract.ApplyAsync(seed);
            now = await seed.Database.SqlQueryRaw<DateTime>(
                """SELECT statement_timestamp() AS "Value" """).SingleAsync();
            var header = seed.Model.FindEntityType(typeof(EventDiscoverySnapshot))!;
            schema = header.GetSchema()!;
            foreach (var type in new[]
            {
                typeof(EventDiscoverySnapshot), typeof(EventDiscoverySnapshotItem),
                typeof(EventDiscoverySnapshotReservation), typeof(EventDiscoveryRevision)
            })
            {
                var entity = seed.Model.FindEntityType(type)!;
                tables.Add($"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"");
            }
        }
        await using (var grant = administration.CreateCommand())
        {
            grant.CommandText = $"""
                CREATE ROLE "{ordinaryRole}" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{ordinaryPassword}';
                CREATE ROLE "{globalRole}" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{globalPassword}';
                GRANT USAGE ON SCHEMA "{schema}" TO "{ordinaryRole}", "{globalRole}";
                GRANT SELECT, INSERT, UPDATE, DELETE ON {string.Join(", ", tables)} TO "{ordinaryRole}";
                GRANT {PostgresDiscoverySnapshotMaintenanceContract.RuntimeRole} TO "{globalRole}";
                """;
            await grant.ExecuteNonQueryAsync();
        }
        string ordinaryConnection = Connection(ordinaryRole, ordinaryPassword);
        string globalConnection = Connection(globalRole, globalPassword);
        var tenantFactory = new TenantExploreDbContextFactory(
            Options(ordinaryConnection, PrimaryDatabaseRole.Runtime, schema), poolSize: 1);
        var globalFactory = new GlobalExploreDbContextFactory(
            Options(globalConnection, PrimaryDatabaseRole.Global, schema), poolSize: 1);
        var repository = new EventDiscoverySnapshotMaintenanceRepository(globalFactory, tenantFactory);
        try
        {
            // These ownership headers intentionally outlive public Tenant rows.
            // The two handler owners have no current Tenant row, covering deleted owners.
            foreach (Guid owner in owners)
            {
                await using var seed = fixture.CreateTenantFilteredDbContext(new FixedTenant(owner));
                var snapshots = new EventDiscoverySnapshotRepository(seed);
                await new EfCoreUnitOfWork(seed).ExecuteSerializableAsync(async token =>
                {
                    await snapshots.AcquireFenceAsync(owner, token);
                    foreach (bool expired in new[] { true, false })
                    {
                        Guid id = Guid.CreateVersion7();
                        DateTime expiry = expired ? now.AddMinutes(-1) : now.AddMinutes(5);
                        var snapshot = EventDiscoverySnapshot.Create(
                            id, owner, id.ToString("N") + id.ToString("N"), 0, 0,
                            expiry.AddMinutes(-1), expiry, false, true, true, [], new());
                        await Assert.That(await snapshots.CaptureAsync(
                            snapshot, new(), snapshot.CreatedAtUtc, token)).IsNotNull();
                    }
                    return true;
                }, CancellationToken.None);
            }

            await using (var ordinary = await tenantFactory.CreateDbContextAsync(owners[2]))
            {
                await AssertAuthorityAsync(ordinary, ordinaryRole);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                {
                    ordinary.TenantContext = new FixedTenant(owners[0]);
                    return Task.CompletedTask;
                });
            }
            int deleted = await repository.PurgeTenantBatchAsync(owners[2], now, 10, CancellationToken.None);
            await Assert.That(deleted).IsEqualTo(1);
            await using var global = await globalFactory.CreateDbContextAsync();
            await AssertAuthorityAsync(global, globalRole);
            var enumerated = await repository.GetExpiredOwnersAsync(null, now, 5, CancellationToken.None);
            await Assert.That(enumerated.Select(row => row.TenantId))
                .IsEquivalentTo([owners[0], owners[1]]);
            string? deniedPrivateRead = null;
            try
            {
                _ = await global.Set<EventDiscoverySnapshot>().IgnoreQueryFilters()
                    .Where(row => owners.Contains(row.TenantId)).CountAsync();
            }
            catch (PostgresException exception) { deniedPrivateRead = exception.SqlState; }
            await Assert.That(deniedPrivateRead).IsEqualTo("42501");

            // An unrelated Global transaction cannot enclose the handler's tenant purges.
            await using var ambient = await global.Database.BeginTransactionAsync();
            var handler = new PurgeEventDiscoverySnapshotsCommandHandler(
                repository, new FixedClock(now));
            var result = await handler.ExecuteAsync(new(), CancellationToken.None);
            await Assert.That(result.DeletedSnapshots).IsEqualTo(2);
            await ambient.RollbackAsync();
            await using var verify = fixture.CreateDbContext();
            await Assert.That(await verify.Set<EventDiscoverySnapshot>().AsNoTracking()
                .Where(row => owners.Contains(row.TenantId) && row.ExpiresAtUtc <= now).CountAsync())
                .IsEqualTo(0);
            await Assert.That(await verify.Set<EventDiscoverySnapshot>().AsNoTracking()
                .Where(row => owners.Contains(row.TenantId) && row.ExpiresAtUtc > now).CountAsync())
                .IsEqualTo(3);
        }
        finally
        {
            using var ordinaryPool = new NpgsqlConnection(ordinaryConnection);
            using var globalPool = new NpgsqlConnection(globalConnection);
            NpgsqlConnection.ClearPool(ordinaryPool);
            NpgsqlConnection.ClearPool(globalPool);
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE SELECT, INSERT, UPDATE, DELETE ON {string.Join(", ", tables)} FROM "{ordinaryRole}";
                REVOKE {PostgresDiscoverySnapshotMaintenanceContract.RuntimeRole} FROM "{globalRole}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{ordinaryRole}", "{globalRole}";
                DROP ROLE "{ordinaryRole}";
                DROP ROLE "{globalRole}";
                """;
            await revoke.ExecuteNonQueryAsync();
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.Set<EventDiscoverySnapshotItem>()
                .Where(row => owners.Contains(row.TenantId)).ExecuteDeleteAsync();
            await cleanup.Set<EventDiscoverySnapshot>()
                .Where(row => owners.Contains(row.TenantId)).ExecuteDeleteAsync();
            await cleanup.Set<EventDiscoverySnapshotReservation>()
                .Where(row => owners.Contains(row.TenantId)).ExecuteDeleteAsync();
            await cleanup.Set<EventDiscoveryRevision>()
                .Where(row => owners.Contains(row.TenantId)).ExecuteDeleteAsync();
        }

        string Connection(string role, string password) =>
            new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                Username = role,
                Password = password,
                MaxPoolSize = 1
            }.ConnectionString;
    }

    private static PrimaryDatabaseConnectionOptions Options(
        string connection, PrimaryDatabaseRole role, string schema)
    {
        var builder = new NpgsqlConnectionStringBuilder(connection);
        return new()
        {
            Role = role,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = builder.Host,
            Port = builder.Port,
            Database = builder.Database,
            Username = builder.Username,
            Password = builder.Password,
            Schema = schema,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        };
    }

    private static async Task AssertAuthorityAsync(ExploreDbContext context, string expected)
    {
        await context.Database.OpenConnectionAsync();
        await using var query = context.Database.GetDbConnection().CreateCommand();
        query.CommandText = """
            SELECT current_user, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user;
            """;
        await using var reader = await query.ExecuteReaderAsync();
        await Assert.That(await reader.ReadAsync()).IsTrue();
        await Assert.That(reader.GetString(0)).IsEqualTo(expected);
        await Assert.That(reader.GetBoolean(1)).IsFalse();
        await Assert.That(reader.GetBoolean(2)).IsFalse();
    }

    private sealed record FixedTenant(Guid TenantId) : ITenantContext;
    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
