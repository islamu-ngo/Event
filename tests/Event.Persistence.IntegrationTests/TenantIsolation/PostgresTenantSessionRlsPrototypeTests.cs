// ABOUTME: PostgreSQL RLS prototype tests for tenant session variables on EF Core connection open.
// ABOUTME: Proves forced RLS policies honor app.current_tenant_id across pooled EF Core/Npgsql connections.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class PostgresTenantSessionRlsPrototypeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ConnectionInterceptor_ShouldBindCurrentTenantForForcedRlsPolicies()
    {
        var tableName = $"rls_tenant_session_probe_{Guid.NewGuid():N}";
        var roleName = $"rls_tenant_session_reader_{Guid.NewGuid():N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await CreateForcedRlsProbeTableAsync(tableName, roleName, tenantA, tenantB);

        try
        {
            var dbContextFactory = CreateDbContextFactory();

            var tenantAResult = await ReadVisibleProbeRowsAsync(dbContextFactory, tenantA, tableName, roleName);
            var tenantBResult = await ReadVisibleProbeRowsAsync(dbContextFactory, tenantB, tableName, roleName);
            var noTenantResult = await ReadVisibleProbeRowsAsync(dbContextFactory, null, tableName, roleName);

            await Assert.That(tenantAResult.SessionTenantId).IsEqualTo(tenantA.ToString());
            await Assert.That(tenantAResult.Names).IsEquivalentTo(["tenant-a"]);
            await Assert.That(tenantBResult.SessionTenantId).IsEqualTo(tenantB.ToString());
            await Assert.That(tenantBResult.Names).IsEquivalentTo(["tenant-b"]);
            await Assert.That(noTenantResult.SessionTenantId).IsEqualTo(string.Empty);
            await Assert.That(noTenantResult.Names).IsEmpty();
        }
        finally
        {
            await DropProbeTableAsync(tableName, roleName);
        }
    }

    [Test]
    public async Task WithoutRls_RawSql_ExposesOtherTenantsData()
    {
        var tableName = $"probe_no_rls_{Guid.NewGuid():N}";
        var roleName = $"probe_no_rls_reader_{Guid.NewGuid():N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await CreateUnprotectedProbeTableAsync(tableName, roleName, tenantA, tenantB);

        try
        {
            var dbContextFactory = CreateDbContextFactory();

            // Invariant breaker demonstration:
            // When RLS is NOT enabled, querying without a WHERE clause returns rows from ALL tenants!
            var result = await ReadVisibleProbeRowsAsync(dbContextFactory, tenantA, tableName, roleName);

            await Assert.That(result.Names).IsEquivalentTo(["tenant-a", "tenant-b"]);
        }
        finally
        {
            await DropProbeTableAsync(tableName, roleName);
        }
    }

    [Test]
    public async Task WithForcedRls_TenantIsolation_EnforcesStrictBoundariesAndFailsClosed()
    {
        var tableName = $"probe_forced_rls_{Guid.NewGuid():N}";
        var roleName = $"probe_forced_rls_role_{Guid.NewGuid():N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await CreateForcedRlsProbeTableAsync(tableName, roleName, tenantA, tenantB);

        try
        {
            var dbContextFactory = CreateDbContextFactory();

            // 1. Tenant A reads ONLY Tenant A data
            var tenantAResult = await ReadVisibleProbeRowsAsync(dbContextFactory, tenantA, tableName, roleName);
            await Assert.That(tenantAResult.Names).IsEquivalentTo(["tenant-a"]);

            // 2. Tenant B reads ONLY Tenant B data
            var tenantBResult = await ReadVisibleProbeRowsAsync(dbContextFactory, tenantB, tableName, roleName);
            await Assert.That(tenantBResult.Names).IsEquivalentTo(["tenant-b"]);

            // 3. Unbound/null tenant context fails closed (0 rows returned)
            var noTenantResult = await ReadVisibleProbeRowsAsync(dbContextFactory, null, tableName, roleName);
            await Assert.That(noTenantResult.Names).IsEmpty();

            // 4. Invariant breaker: Attempting to insert a row for Tenant B while bound to Tenant A
            // MUST fail closed with a PostgreSQL RLS check violation
            await using var context = await dbContextFactory.CreateDbContextAsync();
            context.TenantContext = new TestTenantContext(tenantA);
            await context.Database.OpenConnectionAsync();

            try
            {
                var connection = context.Database.GetDbConnection();
                using var setRoleCmd = connection.CreateCommand();
                setRoleCmd.CommandText = $"set role {QuoteIdentifier(roleName)}";
                await setRoleCmd.ExecuteNonQueryAsync();

                try
                {
                    using var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = $"insert into public.{QuoteIdentifier(tableName)} (id, tenant_id, name) values (@id, @tenant_id, @name)";
                    var idParam = insertCmd.CreateParameter();
                    idParam.ParameterName = "id";
                    idParam.Value = Guid.NewGuid();
                    insertCmd.Parameters.Add(idParam);

                    var tenantParam = insertCmd.CreateParameter();
                    tenantParam.ParameterName = "tenant_id";
                    tenantParam.Value = tenantB; // Attempting to insert row for Tenant B while session is Tenant A!
                    insertCmd.Parameters.Add(tenantParam);

                    var nameParam = insertCmd.CreateParameter();
                    nameParam.ParameterName = "name";
                    nameParam.Value = "rogue-tenant-b-insert";
                    insertCmd.Parameters.Add(nameParam);

                    PostgresException? caught = null;
                    try
                    {
                        await insertCmd.ExecuteNonQueryAsync();
                    }
                    catch (PostgresException ex)
                    {
                        caught = ex;
                    }

                    await Assert.That(caught).IsNotNull()
                        .Because("cross-tenant insert must be rejected by PostgreSQL RLS WITH CHECK clause");
                    await Assert.That(caught!.SqlState).IsEqualTo("42501");
                }
                finally
                {
                    using var resetRoleCmd = connection.CreateCommand();
                    resetRoleCmd.CommandText = "reset role";
                    await resetRoleCmd.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            await DropProbeTableAsync(tableName, roleName);
        }
    }

    [Test]
    public async Task ModelDerivedTenantTables_HaveForcedRlsAndIsolationPolicyInPostgresCatalog()
    {
        await using var context = fixture.CreateDbContext();
        var model = context.Model;
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(model);

        await Assert.That(tenantTables.Count).IsGreaterThan(200);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.relname, c.relrowsecurity, c.relforcerowsecurity,
                   EXISTS(SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid AND p.polname = 'tenant_isolation') AS has_policy
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'islamu_event' AND c.relkind = 'r';
            """;

        var catalogInfo = new Dictionary<string, (bool RlsEnabled, bool RlsForced, bool HasPolicy)>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var tableName = reader.GetString(0);
            var rlsEnabled = reader.GetBoolean(1);
            var rlsForced = reader.GetBoolean(2);
            var hasPolicy = reader.GetBoolean(3);
            catalogInfo[tableName] = (rlsEnabled, rlsForced, hasPolicy);
        }

        var missingOrIncomplete = new List<string>();
        foreach (var table in tenantTables)
        {
            if (!catalogInfo.TryGetValue(table.TableName, out var info))
            {
                missingOrIncomplete.Add($"{table.TableName}: table not found in islamu_event schema");
            }
            else if (!info.RlsEnabled || !info.RlsForced || !info.HasPolicy)
            {
                missingOrIncomplete.Add($"{table.TableName}: RlsEnabled={info.RlsEnabled}, RlsForced={info.RlsForced}, HasPolicy={info.HasPolicy}");
            }
        }

        await Assert.That(missingOrIncomplete).IsEmpty()
            .Because($"every model-derived tenant table must have RLS enabled, forced, and the tenant_isolation policy applied in PostgreSQL: {string.Join("; ", missingOrIncomplete)}");
    }

    [Test]
    public async Task RuntimePostgresRole_DoesNotHaveBypassRlsPrivilege()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rolname, rolsuper, rolbypassrls
            FROM pg_roles
            WHERE rolname NOT IN ('postgres', 'pg_database_owner')
              AND rolname NOT LIKE 'pg_%';
            """;

        var rolesWithBypass = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var roleName = reader.GetString(0);
            var isSuperuser = reader.GetBoolean(1);
            var bypassRls = reader.GetBoolean(2);

            if (bypassRls || isSuperuser)
            {
                rolesWithBypass.Add($"{roleName} (Superuser={isSuperuser}, BypassRls={bypassRls})");
            }
        }

        await Assert.That(rolesWithBypass).IsEmpty()
            .Because("no application-managed or runtime role may have SUPERUSER or BYPASSRLS privilege");
    }

    private PooledDbContextFactory<ExploreDbContext> CreateDbContextFactory()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(PostgresTenantSessionInterceptor.Instance)
            .Options;

        return new PooledDbContextFactory<ExploreDbContext>(options, poolSize: 1);
    }

    private async Task CreateUnprotectedProbeTableAsync(string tableName, string roleName, Guid tenantA, Guid tenantB)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        var tableIdentifier = QuoteIdentifier(tableName);
        var roleIdentifier = QuoteIdentifier(roleName);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            create role {roleIdentifier};

            create table public.{tableIdentifier}
            (
                id uuid primary key,
                tenant_id uuid not null,
                name text not null
            );

            insert into public.{tableIdentifier} (id, tenant_id, name)
            values
                (@tenant_a_row_id, @tenant_a_id, @tenant_a_name),
                (@tenant_b_row_id, @tenant_b_id, @tenant_b_name);

            grant usage on schema public to {roleIdentifier};
            grant select on public.{tableIdentifier} to {roleIdentifier};
            """;
        command.Parameters.AddWithValue("tenant_a_row_id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant_a_id", tenantA);
        command.Parameters.AddWithValue("tenant_a_name", "tenant-a");
        command.Parameters.AddWithValue("tenant_b_row_id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant_b_id", tenantB);
        command.Parameters.AddWithValue("tenant_b_name", "tenant-b");

        await command.ExecuteNonQueryAsync();
    }

    private async Task CreateForcedRlsProbeTableAsync(string tableName, string roleName, Guid tenantA, Guid tenantB)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        var tableIdentifier = QuoteIdentifier(tableName);
        var roleIdentifier = QuoteIdentifier(roleName);
        var policyIdentifier = QuoteIdentifier($"{tableName}_tenant_isolation");

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            create role {roleIdentifier};

            create table public.{tableIdentifier}
            (
                id uuid primary key,
                tenant_id uuid not null,
                name text not null
            );

            insert into public.{tableIdentifier} (id, tenant_id, name)
            values
                (@tenant_a_row_id, @tenant_a_id, @tenant_a_name),
                (@tenant_b_row_id, @tenant_b_id, @tenant_b_name);

            alter table public.{tableIdentifier} enable row level security;
            alter table public.{tableIdentifier} force row level security;

            create policy {policyIdentifier}
                on public.{tableIdentifier}
                for all
                using (tenant_id = nullif(current_setting('{PostgresTenantSessionInterceptor.CurrentTenantSettingName}', true), '')::uuid)
                with check (tenant_id = nullif(current_setting('{PostgresTenantSessionInterceptor.CurrentTenantSettingName}', true), '')::uuid);

            grant usage on schema public to {roleIdentifier};
            grant select, insert on public.{tableIdentifier} to {roleIdentifier};
            """;
        command.Parameters.AddWithValue("tenant_a_row_id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant_a_id", tenantA);
        command.Parameters.AddWithValue("tenant_a_name", "tenant-a");
        command.Parameters.AddWithValue("tenant_b_row_id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant_b_id", tenantB);
        command.Parameters.AddWithValue("tenant_b_name", "tenant-b");

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<ProbeReadResult> ReadVisibleProbeRowsAsync(
        IDbContextFactory<ExploreDbContext> dbContextFactory,
        Guid? tenantId,
        string tableName,
        string roleName)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync();
        context.TenantContext = tenantId.HasValue ? new TestTenantContext(tenantId.Value) : null;

        return await ReadVisibleProbeRowsAsync(context, tableName, roleName);
    }

    private static async Task<ProbeReadResult> ReadVisibleProbeRowsAsync(ExploreDbContext context, string tableName, string roleName)
    {
        await context.Database.OpenConnectionAsync();

        try
        {
            var connection = context.Database.GetDbConnection();
            using var setRoleCommand = connection.CreateCommand();
            setRoleCommand.CommandText = $"set role {QuoteIdentifier(roleName)}";
            await setRoleCommand.ExecuteNonQueryAsync();

            try
            {
                using var settingCommand = connection.CreateCommand();
                settingCommand.CommandText = $"select current_setting('{PostgresTenantSessionInterceptor.CurrentTenantSettingName}', true)";
                var sessionTenantId = (string?)await settingCommand.ExecuteScalarAsync();

                using var rowsCommand = connection.CreateCommand();
                rowsCommand.CommandText = $"select name from public.{QuoteIdentifier(tableName)} order by name";

                var names = new List<string>();
                await using var reader = await rowsCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    names.Add(reader.GetString(0));
                }

                return new ProbeReadResult(sessionTenantId, names);
            }
            finally
            {
                using var resetRoleCommand = connection.CreateCommand();
                resetRoleCommand.CommandText = "reset role";
                await resetRoleCommand.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private async Task DropProbeTableAsync(string tableName, string roleName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            drop table if exists public.{QuoteIdentifier(tableName)};
            revoke usage on schema public from {QuoteIdentifier(roleName)};
            drop role if exists {QuoteIdentifier(roleName)};
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record ProbeReadResult(string? SessionTenantId, IReadOnlyList<string> Names);
}
