using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresTenantRuntimeActivationTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false, "none")]
    [Arguments(true, "none")]
    [Arguments(false, "divergent")]
    [Arguments(false, "missing")]
    [Arguments(false, "permissive")]
    [Arguments(false, "transitive")]
    public async Task RegisteredRuntimeBoundaryRequiresJointSessionAndOrdinaryAuthority(
        bool privileged, string fault)
    {
        bool divergentPolicy = fault == "divergent";
        Guid tenant = Guid.CreateVersion7();
        string key = $"lease.activation.{Guid.CreateVersion7():N}";
        string role = $"lease_activation_{Guid.CreateVersion7():N}";
        string globalRole = $"lease_global_{Guid.CreateVersion7():N}";
        string membershipRole = $"lease_membership_{Guid.CreateVersion7():N}";
        string elevatedRole = $"lease_elevated_{Guid.CreateVersion7():N}";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string globalPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string table;
        string schema;
        TenantTableMetadata policyTable;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Tenants.Add(new Tenant
            {
                Id = tenant,
                FullName = key,
                Slug = $"lease-{tenant:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                TenantId = tenant,
                Tenant = null!,
                SettingKey = key,
                Value = "{}"
            });
            await seed.SaveChangesAsync();
            var entity = seed.Model.FindEntityType(typeof(TenantSetting))!;
            table = entity.GetTableName()!;
            schema = entity.GetSchema()!;
            policyTable = PostgresTenantRowLevelSecurityModel.GetTenantTables(seed.Model)
                .Single(candidate => candidate.TableName == table && candidate.Schema == schema);
        }
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using (var grant = administration.CreateCommand())
        {
            grant.CommandText = $"""
                CREATE ROLE "{role}" LOGIN {(privileged ? "SUPERUSER BYPASSRLS" : "NOSUPERUSER NOBYPASSRLS")} PASSWORD '{password}';
                CREATE ROLE "{globalRole}" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{globalPassword}';
                GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
                GRANT SELECT ON "{schema}"."{table}" TO "{role}";
                """;
            await grant.ExecuteNonQueryAsync();
        }
        var selected = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        selected.Username = role;
        selected.Password = password;
        selected.MaxPoolSize = 1;
        try
        {
            if (divergentPolicy)
            {
                await using var alter = administration.CreateCommand();
                alter.CommandText = $"""
                    ALTER POLICY tenant_isolation ON "{schema}"."{table}" USING (true) WITH CHECK (true);
                    """;
                await alter.ExecuteNonQueryAsync();
            }
            else if (fault == "missing")
            {
                await using var drop = administration.CreateCommand();
                drop.CommandText = $"DROP POLICY tenant_isolation ON \"{schema}\".\"{table}\"";
                await drop.ExecuteNonQueryAsync();
            }
            else if (fault == "permissive")
            {
                await using var add = administration.CreateCommand();
                add.CommandText = $"""
                    CREATE POLICY lease_permissive ON "{schema}"."{table}" FOR ALL
                        USING (true) WITH CHECK (true);
                    """;
                await add.ExecuteNonQueryAsync();
            }
            else if (fault == "transitive")
            {
                await using var elevate = administration.CreateCommand();
                elevate.CommandText = $"""
                    CREATE ROLE "{membershipRole}" NOLOGIN NOINHERIT;
                    CREATE ROLE "{elevatedRole}" NOLOGIN BYPASSRLS;
                    GRANT "{elevatedRole}" TO "{membershipRole}" WITH INHERIT FALSE, SET TRUE;
                    GRANT "{membershipRole}" TO "{role}" WITH INHERIT FALSE, SET TRUE;
                    """;
                await elevate.ExecuteNonQueryAsync();
            }
            // Prove the chosen credentials work and identify their actual authority.
            await using (var probe = new NpgsqlConnection(selected.ConnectionString))
            {
                await probe.OpenAsync();
                await using var authority = probe.CreateCommand();
                authority.CommandText = """
                    SELECT current_user, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user;
                    """;
                await using var reader = await authority.ExecuteReaderAsync();
                await Assert.That(await reader.ReadAsync()).IsTrue();
                await Assert.That(reader.GetString(0)).IsEqualTo(selected.Username);
                await Assert.That(reader.GetBoolean(1)).IsEqualTo(privileged);
                if (!privileged)
                    await Assert.That(reader.GetBoolean(2)).IsFalse();
            }
            var values = new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSql",
                ["Database:Host"] = selected.Host,
                ["Database:Port"] = selected.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Database:Database"] = selected.Database,
                ["Database:Schema"] = schema,
                ["Database:Runtime:Username"] = selected.Username,
                ["Database:Runtime:Password"] = selected.Password,
                ["Database:Migrator:Username"] = fixture.NativeQueueEnrollment.MigratorLogin,
                ["Database:Global:Username"] = globalRole,
                ["Database:Global:Password"] = globalPassword,
                ["Database:TlsMode"] = "Disabled",
                ["PrivacyErasure:Authority:Topology"] = "CoLocated"
                // Deliberately no optional Persistence:EnableRlsTenantSession opt-in.
            };
            foreach (var purpose in new[]
            {
                PrimaryDatabaseRole.EmailPolicyInstanceMutation,
                PrimaryDatabaseRole.EmailPolicyTenantMutation,
                PrimaryDatabaseRole.InstanceKeyRead,
                PrimaryDatabaseRole.InstanceKeyWrite,
                PrimaryDatabaseRole.InstanceKeyIssuance,
                PrimaryDatabaseRole.PlatformKeyReport
            })
            {
                var options = fixture.GetNativeQueueOptions(purpose);
                values[$"Database:{purpose}:Username"] = options.Username;
                values[$"Database:{purpose}:Password"] = options.Password;
            }
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(values).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(configuration);
            services.ConfigurePersistenceServices(
                configuration, skipLookupCacheInitializer: true, environmentName: "Testing");
            await using var provider = services.BuildServiceProvider();
            var contexts = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>();
            await using var context = await contexts.CreateDbContextAsync();
            context.TenantContext = new FixedTenant(tenant);

            if (privileged || fault != "none")
            {
                bool rejected = false;
                try { await context.Database.OpenConnectionAsync(); }
                catch (InvalidOperationException) { rejected = true; }
                await Assert.That(rejected).IsTrue()
                    .Because("privileged authority or a divergent policy cannot acquire a tenant lease");
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await context.TenantSettingOverrides.IgnoreQueryFilters().CountAsync());
            }
            else
            {
                var rows = await context.TenantSettingOverrides.IgnoreQueryFilters().AsNoTracking()
                    .Where(row => row.SettingKey == key).Select(row => row.TenantId).ToListAsync();
                await Assert.That(rows).IsEquivalentTo([tenant])
                    .Because("registered PostgreSQL tenant isolation must bind its session without optional opt-in");
            }
        }
        finally
        {
            if (divergentPolicy || fault == "missing")
            {
                await using var restore = administration.CreateCommand();
                restore.CommandText = PostgresTenantRowLevelSecurityModel.BuildEnableRlsSql(policyTable);
                await restore.ExecuteNonQueryAsync();
            }
            if (fault == "permissive")
            {
                await using var drop = administration.CreateCommand();
                drop.CommandText = $"DROP POLICY IF EXISTS lease_permissive ON \"{schema}\".\"{table}\"";
                await drop.ExecuteNonQueryAsync();
            }
            if (fault == "transitive")
            {
                await using var revokeMembership = administration.CreateCommand();
                revokeMembership.CommandText = $"""
                    REVOKE "{membershipRole}" FROM "{role}";
                    REVOKE "{elevatedRole}" FROM "{membershipRole}";
                    DROP ROLE IF EXISTS "{membershipRole}", "{elevatedRole}";
                    """;
                await revokeMembership.ExecuteNonQueryAsync();
            }
            using var pool = new NpgsqlConnection(selected.ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var cleanup = administration.CreateCommand();
            cleanup.CommandText = $"""
                REVOKE SELECT ON "{schema}"."{table}" FROM "{role}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{role}";
                DROP ROLE "{role}";
                DROP ROLE "{globalRole}";
                """;
            await cleanup.ExecuteNonQueryAsync();
            await using var rows = fixture.CreateDbContext();
            await rows.TenantSettingOverrides.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            await rows.Tenants.Where(row => row.Id == tenant).ExecuteDeleteAsync();
        }
    }

    private sealed record FixedTenant(Guid TenantId) : ITenantContext;
}
