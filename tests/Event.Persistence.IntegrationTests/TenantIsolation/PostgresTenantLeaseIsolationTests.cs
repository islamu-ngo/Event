using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresTenantLeaseIsolationTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AlreadyOpenOrdinaryLeaseRejectsTenantReplacementBeforeApplicationRead(
        bool mutateIdentity, bool synchronousOpen)
    {
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        string key = $"lease.{Guid.CreateVersion7():N}";
        string role = $"lease_reader_{Guid.CreateVersion7():N}";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string table;
        string schema;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Tenants.AddRange(CreateTenant(tenantA), CreateTenant(tenantB));
            seed.TenantSettingOverrides.AddRange(
                new TenantSetting { TenantId = tenantA, Tenant = null!, SettingKey = key, Value = "{}" },
                new TenantSetting { TenantId = tenantB, Tenant = null!, SettingKey = key, Value = "{}" });
            await seed.SaveChangesAsync();
            var entity = seed.Model.FindEntityType(typeof(TenantSetting))!;
            table = entity.GetTableName()!;
            schema = entity.GetSchema()!;
        }

        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using (var grant = administration.CreateCommand())
        {
            // The disposable login is generated, never a production credential.
            grant.CommandText = $"""
                CREATE ROLE "{role}" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{password}';
                GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
                GRANT SELECT ON "{schema}"."{table}" TO "{role}";
                """;
            await grant.ExecuteNonQueryAsync();
        }

        var connectionOptions = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Username = role,
            Password = password,
            MaxPoolSize = 1
        };
        try
        {
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseNpgsql(connectionOptions.ConnectionString)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(PostgresTenantSessionInterceptor.Instance)
                .Options;
            await using var context = new ExploreDbContext(options);
            var ambient = new MutableTenantContext(tenantA);
            context.TenantContext = ambient;
            if (synchronousOpen)
                context.Database.OpenConnection();
            else
                await context.Database.OpenConnectionAsync();

            var connection = context.Database.GetDbConnection();
            await using (var authority = connection.CreateCommand())
            {
                authority.CommandText = """
                    SELECT current_user, rolsuper, rolbypassrls
                    FROM pg_roles WHERE rolname = current_user;
                    """;
                await using var reader = await authority.ExecuteReaderAsync();
                await Assert.That(await reader.ReadAsync()).IsTrue();
                await Assert.That(reader.GetString(0)).IsEqualTo(role);
                await Assert.That(reader.GetBoolean(1)).IsFalse();
                await Assert.That(reader.GetBoolean(2)).IsFalse();
            }

            // Removing EF predicates must still leave the real database boundary effective.
            var originalRows = await context.TenantSettingOverrides.IgnoreQueryFilters()
                .AsNoTracking().Where(setting => setting.SettingKey == key)
                .Select(setting => setting.TenantId).ToListAsync();
            await Assert.That(originalRows).IsEquivalentTo([tenantA]);

            bool rejected = false;
            try
            {
                if (mutateIdentity)
                    ambient.TenantId = tenantB;
                else
                    context.TenantContext = new MutableTenantContext(tenantB);
                _ = await context.TenantSettingOverrides.IgnoreQueryFilters()
                    .AsNoTracking().Where(setting => setting.SettingKey == key)
                    .Select(setting => setting.TenantId).ToListAsync();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            await Assert.That(rejected).IsTrue()
                .Because("an already-open tenant lease must reject identity divergence before application SQL");
        }
        finally
        {
            using var pool = new NpgsqlConnection(connectionOptions.ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var cleanup = administration.CreateCommand();
            cleanup.CommandText = $"""
                REVOKE SELECT ON "{schema}"."{table}" FROM "{role}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{role}";
                DROP ROLE "{role}";
                """;
            await cleanup.ExecuteNonQueryAsync();
            await using var ownedRows = fixture.CreateDbContext();
            await ownedRows.TenantSettingOverrides.Where(setting => setting.SettingKey == key)
                .ExecuteDeleteAsync();
            await ownedRows.Tenants.Where(tenant => tenant.Id == tenantA || tenant.Id == tenantB)
                .ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task PooledContextReturnClearsTenantAuthorityBeforeUnboundApplicationRead()
    {
        Guid tenantId = Guid.CreateVersion7();
        string key = $"lease.pool.{Guid.CreateVersion7():N}";
        string role = $"lease_pool_{Guid.CreateVersion7():N}";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string table;
        string schema;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Tenants.Add(CreateTenant(tenantId));
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = key,
                Value = "{}"
            });
            await seed.SaveChangesAsync();
            var entity = seed.Model.FindEntityType(typeof(TenantSetting))!;
            table = entity.GetTableName()!;
            schema = entity.GetSchema()!;
        }

        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using (var grant = administration.CreateCommand())
        {
            grant.CommandText = $"""
                CREATE ROLE "{role}" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{password}';
                GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
                GRANT SELECT ON "{schema}"."{table}" TO "{role}";
                """;
            await grant.ExecuteNonQueryAsync();
        }
        var connectionOptions = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Username = role,
            Password = password,
            MaxPoolSize = 1
        };
        try
        {
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseNpgsql(connectionOptions.ConnectionString)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(PostgresTenantSessionInterceptor.Instance)
                .Options;
            var contexts = new PooledDbContextFactory<ExploreDbContext>(options, poolSize: 1);
            await using (var first = await contexts.CreateDbContextAsync())
            {
                first.TenantContext = new MutableTenantContext(tenantId);
                var ownRows = await first.TenantSettingOverrides.AsNoTracking()
                    .Where(setting => setting.SettingKey == key)
                    .Select(setting => setting.TenantId).ToListAsync();
                await Assert.That(ownRows).IsEquivalentTo([tenantId]);
            }

            // A fresh caller has supplied no tenant; pool reuse cannot inherit one.
            await using var unbound = await contexts.CreateDbContextAsync();
            var leakedRows = await unbound.TenantSettingOverrides.AsNoTracking()
                .Where(setting => setting.SettingKey == key)
                .Select(setting => setting.TenantId).ToListAsync();
            await Assert.That(leakedRows).IsEmpty()
                .Because("returning a pooled context must revoke the previous tenant lease");
        }
        finally
        {
            using var pool = new NpgsqlConnection(connectionOptions.ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var cleanup = administration.CreateCommand();
            cleanup.CommandText = $"""
                REVOKE SELECT ON "{schema}"."{table}" FROM "{role}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{role}";
                DROP ROLE "{role}";
                """;
            await cleanup.ExecuteNonQueryAsync();
            await using var ownedRows = fixture.CreateDbContext();
            await ownedRows.TenantSettingOverrides.Where(setting => setting.SettingKey == key)
                .ExecuteDeleteAsync();
            await ownedRows.Tenants.Where(tenant => tenant.Id == tenantId).ExecuteDeleteAsync();
        }
    }

    private static Tenant CreateTenant(Guid id) => new()
    {
        Id = id,
        FullName = $"lease-{id:N}",
        Slug = $"lease-{id:N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private sealed class MutableTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; set; } = tenantId;
    }
}
