using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Infrastructure;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresTenantApplicationTableIsolationTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task AtprotoGovernanceRejectsUnknownExecutorBeforeReadAndReplacement()
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        string key = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;
        await using var migrator = fixture.CreateDbContext();
        migrator.TenantSettingOverrides.Add(
            new TenantSetting { TenantId = data.A, Tenant = null!, SettingKey = key, Value = "true" });
        await migrator.SaveChangesAsync();
        await using var outsider = await GlobalLookupAuthority.CreateAsync(fixture);
        await using var ordinary = await GlobalLookupAuthority.CreateAsync(fixture, atprotoGovernance: true);
        string function = PostgresAtprotoGovernanceContract.FunctionSql(migrator);
        await migrator.Database.ExecuteSqlRawAsync(
            $"GRANT EXECUTE ON FUNCTION {function}(text) TO \"{outsider.Role}\"");
        try
        {
            PostgresException entry = await Assert.ThrowsAsync<PostgresException>(async () =>
                await new AtprotoGovernanceSettingRepository(outsider.Factory).GetOverridesAsync(key));
            await Assert.That(entry.SqlState).IsEqualTo("55000");
            PostgresException reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresAtprotoGovernanceContract.ApplyAsync(migrator));
            await Assert.That(reinstall.SqlState).IsEqualTo("55000");
            await Assert.That(await migrator.Database.SqlQueryRaw<bool>(
                $"SELECT pg_catalog.has_function_privilege('{outsider.Role}', '{function}(text)', 'EXECUTE') AS \"Value\"")
                .SingleAsync()).IsTrue();
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync(
                $"REVOKE EXECUTE ON FUNCTION {function}(text) FROM \"{outsider.Role}\"");
        }
        await PostgresAtprotoGovernanceContract.ApplyAsync(migrator);
        List<TenantSetting> settings = await new AtprotoGovernanceSettingRepository(ordinary.Factory)
            .GetOverridesAsync(key);
        await Assert.That(settings.Single(row => row.TenantId == data.A).Value).IsEqualTo("true");
    }

    [Test]
    public async Task RuntimeAdmissionPolicyIntegrityDoesNotDependOnConnectionSearchPath()
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        await using var context = data.CreateContext(data.A);
        await data.AssertOrdinaryAuthorityAsync(context);
        string schema = context.Model.FindEntityType(typeof(TenantSetting))!.GetSchema()!;
        foreach (string searchPath in new[] { "pg_catalog", schema })
        {
            await using var configure = context.Database.GetDbConnection().CreateCommand();
            configure.CommandText = "SELECT set_config('search_path', @path, false)";
            var parameter = configure.CreateParameter();
            parameter.ParameterName = "path";
            parameter.Value = searchPath;
            configure.Parameters.Add(parameter);
            await configure.ExecuteScalarAsync();
            await using var readiness = PostgresTenantRuntimeReadiness.CreateCommand(
                context.Database.GetDbConnection(), context);

            await Assert.That(await readiness.ExecuteScalarAsync()).IsEqualTo(true);
        }
    }

    [Test]
    [Arguments(GovernanceSettingKeys.Federation.AtprotoEventsEnabled)]
    [Arguments(GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled)]
    [Arguments(GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode)]
    public async Task AtprotoGovernanceDiscoveryReturnsCompleteFixedKeyAudienceWithoutRequestTenant(string key)
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.TenantSettingOverrides.AddRange(
                new TenantSetting { TenantId = data.A, Tenant = null!, SettingKey = key, Value = "true" },
                new TenantSetting { TenantId = data.B, Tenant = null!, SettingKey = key, Value = "false" });
            await seed.SaveChangesAsync();
        }
        await using var request = data.CreateContext(null);
        await data.AssertOrdinaryAuthorityAsync(request);
        await Assert.That(await request.TenantSettingOverrides.IgnoreQueryFilters()
            .CountAsync(row => row.SettingKey == key)).IsEqualTo(0);

        await using var global = await GlobalLookupAuthority.CreateAsync(fixture, atprotoGovernance: true);
        List<TenantSetting> settings = await new AtprotoGovernanceSettingRepository(global.Factory)
            .GetOverridesAsync(key);

        await Assert.That(settings.Where(row => row.TenantId == data.A || row.TenantId == data.B)
            .Select(row => row.TenantId)).IsEquivalentTo([data.A, data.B]);
        await Assert.That(settings.All(row => row.SettingKey == key)).IsTrue();
        await Assert.That(settings.Single(row => row.TenantId == data.A).Value).IsEqualTo("true");
        await Assert.That(settings.Single(row => row.TenantId == data.B).Value).IsEqualTo("false");
        await Assert.That(request.TenantFilterTenantId).IsNull();
        await using var globalContext = await global.Factory.CreateDbContextAsync();
        await globalContext.Database.OpenConnectionAsync();
        await Assert.That(globalContext.TenantFilterTenantId).IsNull();
        var entity = globalContext.Model.FindEntityType(typeof(TenantSetting))!;
        string privateRead = $"SELECT count(*)::integer AS \"Value\" FROM \"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
        PostgresException denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await globalContext.Database.SqlQueryRaw<int>(privateRead).SingleAsync());
        await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        string invalidKey = $"SELECT * FROM {PostgresAtprotoGovernanceContract.FunctionSql(globalContext)}({{0}})";
        PostgresException invalid = await Assert.ThrowsAsync<PostgresException>(async () =>
            await globalContext.Database.ExecuteSqlRawAsync(invalidKey, data.Key));
        await Assert.That(invalid.SqlState).IsEqualTo(PostgresErrorCodes.InvalidParameterValue);
        await Assert.That(await globalContext.Database
            .SqlQueryRaw<bool>("SELECT pg_has_role(current_user, 'event_atproto_governance_owner', 'MEMBER') AS \"Value\"")
            .SingleAsync()).IsFalse();
    }

    [Test]
    [Arguments("a")]
    [Arguments("b")]
    [Arguments("absent")]
    public async Task ApplicationBoundaryPreservesDirectParentAndSharedLookupVisibility(string scope)
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        Guid? tenant = scope switch { "a" => data.A, "b" => data.B, _ => null };
        await using var context = data.CreateContext(tenant);
        await data.AssertOrdinaryAuthorityAsync(context);
        var settings = await context.TenantSettingOverrides.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.SettingKey == data.Key).Select(row => row.TenantId).ToListAsync();
        var quotas = await context.ExternalApiKeyQuotas.IgnoreQueryFilters().AsNoTracking()
            .Where(row => data.QuotaIds.Contains(row.Id)).Select(row => row.Id).ToListAsync();
        var types = await context.EventTypes.IgnoreQueryFilters().AsNoTracking()
            .Where(row => data.TypeIds.Contains(row.Id)).Select(row => row.Id).ToListAsync();

        await Assert.That(settings).IsEquivalentTo(tenant is { } id ? [id] : Array.Empty<Guid>());
        await Assert.That(quotas).IsEquivalentTo(scope switch
        {
            "a" => new[] { data.QuotaIds[0] },
            "b" => new[] { data.QuotaIds[1] },
            _ => Array.Empty<Guid>()
        });
        await Assert.That(types).IsEquivalentTo(scope switch
        {
            "a" => new[] { data.TypeIds[0], data.TypeIds[2] },
            "b" => new[] { data.TypeIds[1], data.TypeIds[2] },
            _ => new[] { data.TypeIds[2] }
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ApplicationBoundaryRejectsForeignInsertUpdateAndDelete(bool parentDerived)
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        await using var context = data.CreateContext(data.A);
        await data.AssertOrdinaryAuthorityAsync(context);
        int updated;
        int deleted;
        if (parentDerived)
        {
            updated = await context.ExternalApiKeyQuotas.IgnoreQueryFilters()
                .Where(row => row.Id == data.QuotaIds[1])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RequestCount, 99));
            deleted = await context.ExternalApiKeyQuotas.IgnoreQueryFilters()
                .Where(row => row.Id == data.QuotaIds[1]).ExecuteDeleteAsync();
            context.ExternalApiKeyQuotas.Add(new ExternalApiKeyQuota
            {
                Id = Guid.CreateVersion7(),
                ExternalApiKeyId = data.KeyIds[1],
                ExternalApiKey = null!,
                PeriodStart = new DateOnly(2026, 2, 1),
                PeriodEnd = new DateOnly(2026, 2, 28)
            });
        }
        else
        {
            updated = await context.TenantSettingOverrides.IgnoreQueryFilters()
                .Where(row => row.TenantId == data.B && row.SettingKey == data.Key)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "{\"foreign\":true}"));
            deleted = await context.TenantSettingOverrides.IgnoreQueryFilters()
                .Where(row => row.TenantId == data.B && row.SettingKey == data.Key).ExecuteDeleteAsync();
            context.TenantSettingOverrides.Add(new TenantSetting
            {
                TenantId = data.B,
                Tenant = null!,
                SettingKey = data.Key + ".foreign",
                Value = "{}"
            });
        }
        await Assert.That(updated).IsEqualTo(0);
        await Assert.That(deleted).IsEqualTo(0);
        string? rejection = null;
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres)
        {
            rejection = postgres.SqlState;
        }
        await Assert.That(rejection).IsEqualTo("42501");
        await using var verify = fixture.CreateDbContext();
        await Assert.That(await verify.ExternalApiKeyQuotas
            .Where(row => row.Id == data.QuotaIds[1]).Select(row => row.RequestCount).SingleAsync())
            .IsEqualTo(0);
        await Assert.That(await verify.TenantSettingOverrides
            .Where(row => row.TenantId == data.B && row.SettingKey == data.Key)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("{}");
        await Assert.That(await verify.TenantSettingOverrides
            .AnyAsync(row => row.SettingKey == data.Key + ".foreign")).IsFalse();
        await Assert.That(await verify.ExternalApiKeyQuotas
            .CountAsync(row => row.ExternalApiKeyId == data.KeyIds[1])).IsEqualTo(1);
    }

    [Test]
    public async Task ApplicationBoundaryAllowsSameTenantCrudWithoutForeignVisibility()
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        await using var context = data.CreateContext(data.A);
        await data.AssertOrdinaryAuthorityAsync(context);
        string key = data.Key + ".own";
        context.TenantSettingOverrides.Add(new TenantSetting
        {
            TenantId = data.A,
            Tenant = null!,
            SettingKey = key,
            Value = "{}"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
            .Where(row => row.SettingKey == key).Select(row => row.TenantId).SingleAsync()).IsEqualTo(data.A);
        await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
            .Where(row => row.SettingKey == key)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "{\"own\":true}")))
            .IsEqualTo(1);
        await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
            .Where(row => row.SettingKey == key).Select(row => row.Value).SingleAsync())
            .IsEqualTo("{\"own\":true}");
        await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
            .Where(row => row.SettingKey == key).ExecuteDeleteAsync()).IsEqualTo(1);
        await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
            .Where(row => row.SettingKey == data.Key).Select(row => row.TenantId).ToListAsync())
            .IsEquivalentTo([data.A]);
    }

    [Test]
    public async Task ApplicationBoundaryCancellationCannotLeaveTenantAuthorityInReturnedPool()
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        var gate = new CancellationGate();
        var contexts = new PooledDbContextFactory<ExploreDbContext>(data.Options(gate), poolSize: 1);
        using var cancellation = new CancellationTokenSource();
        await using (var first = await contexts.CreateDbContextAsync())
        {
            first.TenantContext = new FixedTenant(data.A);
            await data.AssertOrdinaryAuthorityAsync(first);
            Task<List<Guid>> pending = first.TenantSettingOverrides.TagWith(CancellationGate.Tag)
                .AsNoTracking().Where(row => row.SettingKey == data.Key)
                .Select(row => row.TenantId).ToListAsync(cancellation.Token);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            bool canceled = false;
            try { _ = await pending; }
            catch (OperationCanceledException) { canceled = true; }
            await Assert.That(canceled).IsTrue();
        }
        await using (var unbound = await contexts.CreateDbContextAsync())
        {
            await Assert.That(await unbound.TenantSettingOverrides.AsNoTracking()
                .Where(row => row.SettingKey == data.Key).Select(row => row.TenantId).ToListAsync())
                .IsEmpty();
        }
        await using var next = await contexts.CreateDbContextAsync();
        next.TenantContext = new FixedTenant(data.B);
        await Assert.That(await next.TenantSettingOverrides.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.SettingKey == data.Key).Select(row => row.TenantId).ToListAsync())
            .IsEquivalentTo([data.B]);
    }

    private sealed class CancellationGate : DbCommandInterceptor
    {
        internal const string Tag = "tenant-lease-cancellation";
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(Tag, StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    [Test]
    public async Task PublicDirectoryRejectsUnknownDirectExecutorBeforeReadAndReplacement()
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        await using var outsider = await GlobalLookupAuthority.CreateAsync(fixture);
        await using var ordinary = await GlobalLookupAuthority.CreateAsync(fixture);
        await using var migrator = fixture.CreateDbContext();
        var lookup = new TenantLookupSource(ordinary.Factory);
        var before = (await lookup.GetTenantLookupsAsync())
            .Where(row => row.TenantId == data.A || row.TenantId == data.B)
            .Select(row => (row.TenantId, row.Slug, row.Subdomain, row.CustomDomain)).ToArray();
        await Assert.That(before.Select(row => row.TenantId)).IsEquivalentTo([data.A, data.B]);
        await Assert.That(before.Single(row => row.TenantId == data.A).Subdomain).IsEqualTo("lease-a");
        await Assert.That(before.Single(row => row.TenantId == data.B).Subdomain).IsEqualTo("lease-b");
        string signature = PostgresTenantLookupContract.FunctionSql(migrator) + "()";
        await migrator.Database.ExecuteSqlRawAsync(
            $"GRANT EXECUTE ON FUNCTION {signature} TO \"{outsider.Role}\"");
        try
        {
            var entry = await Assert.ThrowsAsync<PostgresException>(async () =>
                await lookup.GetTenantLookupsAsync());
            await Assert.That(entry?.SqlState).IsEqualTo("55000");
            await Assert.That(entry?.MessageText).IsEqualTo("native_routine_acl_drift");
            var reinstall = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresTenantLookupContract.ApplyAsync(migrator));
            await Assert.That(reinstall?.SqlState).IsEqualTo("55000");
            await Assert.That(reinstall?.MessageText).IsEqualTo("native_routine_acl_drift");
            await Assert.That(await migrator.Database.SqlQueryRaw<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM pg_catalog.pg_proc routine
                    CROSS JOIN LATERAL pg_catalog.aclexplode(routine.proacl) acl
                    WHERE routine.oid = pg_catalog.to_regprocedure('{signature}')
                        AND acl.grantee = pg_catalog.to_regrole('{outsider.Role}')::oid
                        AND acl.privilege_type = 'EXECUTE') AS "Value"
                """).SingleAsync()).IsTrue();
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync(
                $"REVOKE EXECUTE ON FUNCTION {signature} FROM \"{outsider.Role}\"");
        }
        await PostgresTenantLookupContract.ApplyAsync(migrator);
        var after = (await lookup.GetTenantLookupsAsync())
            .Where(row => row.TenantId == data.A || row.TenantId == data.B)
            .Select(row => (row.TenantId, row.Slug, row.Subdomain, row.CustomDomain)).ToArray();
        await Assert.That(after).IsEquivalentTo(before);
    }

    [Test]
    public async Task ApplicationBoundaryPublicLookupCannotSubstituteRequestTenantAuthorityForGlobalDiscovery()
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        await using var context = data.CreateContext(data.A);
        await data.AssertOrdinaryAuthorityAsync(context);
        await using var global = await GlobalLookupAuthority.CreateAsync(fixture);
        var lookup = new TenantLookupSource(global.Factory);
        var records = (await lookup.GetTenantLookupsAsync())
            .Where(row => row.TenantId == data.A || row.TenantId == data.B)
            .ToDictionary(row => row.TenantId);
        await Assert.That(records.Keys).IsEquivalentTo([data.A, data.B]);
        await Assert.That(records[data.A].Subdomain).IsEqualTo("lease-a");
        await Assert.That(records[data.B].Subdomain).IsEqualTo("lease-b")
            .Because("public directory lookup must use bounded global authority, not the caller's tenant RLS session");
    }

    [Test]
    public async Task PublicLookupGlobalPoolCannotAcquireTenantIdentityOrReadAndWritePrivateTables()
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        await using var global = await GlobalLookupAuthority.CreateAsync(fixture);
        await using (var context = await global.Factory.CreateDbContextAsync())
        {
            await context.Database.OpenConnectionAsync();
            await using (var command = context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = """
                    SELECT current_user, rolsuper, rolbypassrls,
                        current_setting('app.current_tenant_id', true)
                    FROM pg_roles WHERE rolname = current_user;
                    """;
                await using var reader = await command.ExecuteReaderAsync();
                await Assert.That(await reader.ReadAsync()).IsTrue();
                await Assert.That(reader.GetString(0)).IsEqualTo(global.Role);
                await Assert.That(reader.GetBoolean(1)).IsFalse();
                await Assert.That(reader.GetBoolean(2)).IsFalse();
                await Assert.That(reader.IsDBNull(3) || reader.GetString(3) == "").IsTrue();
            }
            bool sealedIdentity = false;
            try { context.TenantContext = new FixedTenant(data.A); }
            catch (InvalidOperationException exception) when (exception.Message == "tenant_lease_identity_changed")
            {
                sealedIdentity = true;
            }
            await Assert.That(sealedIdentity).IsTrue();
            var entity = context.Model.FindEntityType(typeof(TenantSetting))!;
            var sql = context.GetService<Microsoft.EntityFrameworkCore.Storage.ISqlGenerationHelper>();
            string table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            var mapping = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(
                entity.GetTableName()!, entity.GetSchema());
            string value = sql.DelimitIdentifier(entity.FindProperty(nameof(TenantSetting.Value))!.GetColumnName(mapping)!);
            foreach (string statement in new[]
            {
                $"SELECT * FROM {table}",
                $"UPDATE {table} SET {value} = {value} WHERE false",
                $"DELETE FROM {table} WHERE false",
                $"INSERT INTO {table} SELECT * FROM {table} WHERE false",
                $"SET ROLE {PostgresTenantLookupContract.OwnerRole}",
                $"SET ROLE {PostgresTenantLookupContract.MigratorRole}"
            })
            {
                string? denial = null;
                try { await context.Database.ExecuteSqlRawAsync(statement); }
                catch (PostgresException exception) { denial = exception.SqlState; }
                await Assert.That(denial).IsEqualTo("42501");
            }
            context.EnableTenantFilterBypass("returned-global-lease");
        }
        await using var returned = await global.Factory.CreateDbContextAsync();
        await Assert.That(returned.TenantContext).IsNull();
        await Assert.That(returned.CurrentUserService).IsNull();
        await Assert.That(returned.IsTenantFilterBypassed).IsFalse();
        await using var ordinary = data.CreateContext(data.A);
        await Assert.That(await ordinary.TenantSettingOverrides.IgnoreQueryFilters()
            .Where(row => row.SettingKey == data.Key).Select(row => row.TenantId).ToListAsync())
            .IsEquivalentTo([data.A]);
    }

    [Test]
    public async Task OrdinaryRequestCannotInvokePublicDirectoryCapability()
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        await using var global = await GlobalLookupAuthority.CreateAsync(fixture);
        await using var context = data.CreateContext(data.A);
        string? denial = null;
        string lookupSql = $"SELECT * FROM {PostgresTenantLookupContract.FunctionSql(context)}()";
        try
        {
            await context.Database.ExecuteSqlRawAsync(lookupSql);
        }
        catch (PostgresException exception) { denial = exception.SqlState; }
        await Assert.That(denial).IsEqualTo("42501");
    }

    [Test]
    public async Task ApplicationBoundaryOrdinaryRequestCannotInvokeGlobalMaintenanceCapability()
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        await using var context = data.CreateContext(data.A);
        await data.AssertOrdinaryAuthorityAsync(context);
        string? denied = null;
        string capabilitySql = $"SELECT * FROM {PostgresDiscoverySnapshotMaintenanceContract.FunctionSql(context)}" +
            "({0}::uuid, {1}, {2})";
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                capabilitySql,
                Guid.Empty, new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc), 5);
        }
        catch (PostgresException exception)
        {
            denied = exception.SqlState;
        }
        await Assert.That(denied).IsEqualTo("42501")
            .Because("bounded global enumeration is a separate capability, not ordinary request authority");
    }

    [Test]
    public async Task ApplicationBoundaryWorkerClaimAndCompletionRemainInExactTenantAuthority()
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        await using var context = data.CreateContext(data.A);
        await data.AssertOrdinaryAuthorityAsync(context);
        var queue = new IntegrationSyncOutboxRepository(context);
        DateTime now = DateTime.UtcNow;
        DateTime stale = now.AddMinutes(-5);
        var visible = await queue.GetPendingBatch(5, now, stale, CancellationToken.None);
        await Assert.That(visible.Select(row => row.Id)).IsEquivalentTo([data.QueueIds[0]]);
        await Assert.That(await queue.GetPendingAsync(data.QueueIds[1], now, stale, CancellationToken.None)).IsNull();
        await Assert.That(await queue.TryClaimAsync(
            new(data.B, data.QueueIds[1], Guid.CreateVersion7(), now, stale), CancellationToken.None)).IsFalse();
        Guid lease = Guid.CreateVersion7();
        await Assert.That(await queue.TryClaimAsync(
            new(data.A, data.QueueIds[0], lease, now, stale), CancellationToken.None)).IsTrue();
        var identity = new IntegrationSyncClaimIdentity(data.A, data.QueueIds[0], lease, now);
        await Assert.That(await queue.GetActiveClaimAsync(identity, CancellationToken.None)).IsNotNull();
        await Assert.That(await queue.GetActiveClaimAsync(
            identity with { TenantId = data.B }, CancellationToken.None)).IsNull();
        await Assert.That(await queue.MarkProviderHandoffStartedAsync(identity, now, CancellationToken.None)).IsTrue();
        await Assert.That(await queue.CompleteAsync(identity, now, CancellationToken.None)).IsTrue();
        await using var verify = fixture.CreateDbContext();
        await Assert.That(await verify.IntegrationSyncOutbox.Where(row => row.Id == data.QueueIds[0])
            .Select(row => row.Status).SingleAsync()).IsEqualTo(IntegrationSyncStatus.Completed);
        await Assert.That(await verify.IntegrationSyncOutbox.Where(row => row.Id == data.QueueIds[1])
            .Select(row => row.Status).SingleAsync()).IsEqualTo(IntegrationSyncStatus.Pending);
    }

    [Test]
    public async Task ApplicationBoundaryWorkerDiscoveryRequiresBoundedGlobalAuthorityBeforeTenantClaim()
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        await using (var tenant = data.CreateContext(data.A))
        {
            await data.AssertOrdinaryAuthorityAsync(tenant);
            var own = await new IntegrationSyncOutboxRepository(tenant).GetPendingBatch(
                5, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-5), CancellationToken.None);
            await Assert.That(own.Select(row => row.Id)).IsEquivalentTo([data.QueueIds[0]]);
        }
        await using (var unbound = data.CreateContext(null))
        {
            await data.AssertOrdinaryAuthorityAsync(unbound);
            var hidden = await new IntegrationSyncOutboxRepository(unbound).GetPendingBatch(
                5, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-5), CancellationToken.None);
            await Assert.That(hidden).IsEmpty();
        }
        await using var global = await GlobalIntegrationSyncAuthority.CreateAsync(fixture);
        var discovery = new IntegrationSyncQueueDiscovery(global.Factory);
        var discovered = await discovery.GetPendingAsync(
            5, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-5), CancellationToken.None);
        await Assert.That(discovered.Select(row => row.OutboxId)).IsEquivalentTo(data.QueueIds)
            .Because("worker discovery must use a bounded global capability before acquiring exact tenant execution leases");
        await Assert.That(discovered.Select(row => row.TenantId)).IsEquivalentTo([data.A, data.B]);
        var bounded = await discovery.GetPendingAsync(
            1, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-5), CancellationToken.None);
        await Assert.That(bounded.Count).IsEqualTo(1);

        await using var executor = await global.Factory.CreateDbContextAsync();
        await executor.Database.OpenConnectionAsync();
        await using (var role = executor.Database.GetDbConnection().CreateCommand())
        {
            role.CommandText = $"""
                SELECT rolsuper OR rolbypassrls
                    OR pg_has_role(current_user, '{PostgresIntegrationSyncDiscoveryContract.OwnerRole}', 'MEMBER')
                    OR pg_has_role(current_user, '{PostgresIntegrationSyncDiscoveryContract.MigratorRole}', 'MEMBER')
                FROM pg_roles WHERE rolname = current_user
                """;
            await Assert.That(await role.ExecuteScalarAsync()).IsEqualTo(false);
        }
        var deniedRead = await Assert.ThrowsAsync<PostgresException>(async () =>
            await executor.IntegrationSyncOutbox.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        await Assert.That(deniedRead?.SqlState).IsEqualTo("42501");
        await using var ordinary = data.CreateContext(data.A);
        string function = PostgresIntegrationSyncDiscoveryContract.FunctionSql(ordinary);
        string discoverySql = $"SELECT * FROM {function}({{0}}, {{1}}, {{2}})";
        var deniedDiscovery = await Assert.ThrowsAsync<PostgresException>(async () =>
            await ordinary.Database.ExecuteSqlRawAsync(
                discoverySql,
                DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-5), 5));
        await Assert.That(deniedDiscovery?.SqlState).IsEqualTo("42501");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1001)]
    public async Task ApplicationBoundaryIntegrationDiscoveryRejectsUnboundedSqlRequests(int take)
    {
        await using var global = await GlobalIntegrationSyncAuthority.CreateAsync(fixture);
        await using var executor = await global.Factory.CreateDbContextAsync();
        string function = PostgresIntegrationSyncDiscoveryContract.FunctionSql(executor);
        string discoverySql = $"SELECT * FROM {function}({{0}}, {{1}}, {{2}})";
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await executor.Database.ExecuteSqlRawAsync(
                discoverySql,
                DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-5), take));
        await Assert.That(denied?.SqlState).IsEqualTo("22023");
    }

    [Test]
    public async Task ApplicationBoundaryIntegrationDiscoveryExcludesDeletedAndFutureWork()
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        await using (var setup = fixture.CreateDbContext())
        {
            await setup.IntegrationSyncOutbox.Where(row => row.Id == data.QueueIds[0])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
            await setup.IntegrationSyncOutbox.Where(row => row.Id == data.QueueIds[1])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.NextAttemptAt, DateTime.UtcNow.AddDays(2)));
        }
        await using var global = await GlobalIntegrationSyncAuthority.CreateAsync(fixture);
        var discovery = new IntegrationSyncQueueDiscovery(global.Factory);
        var candidates = await discovery.GetPendingAsync(
            1000, DateTime.UtcNow.AddDays(3), DateTime.UtcNow, CancellationToken.None);
        await Assert.That(candidates.Any(row => data.QueueIds.Contains(row.OutboxId))).IsFalse()
            .Because("caller-supplied future time cannot widen database-clock eligibility or reveal soft-deleted work");
    }

    [Test]
    [Arguments("missing-user")]
    [Arguments("in-doubt")]
    [Arguments("missing-lease")]
    [Arguments("missing-start")]
    public async Task ApplicationBoundaryIntegrationWorkerBindsEveryScopeAndPreservesDurableRecovery(string scenario)
    {
        await using var data = await OrdinaryRows.CreateAsync(fixture);
        bool recovery = scenario != "missing-user";
        if (recovery)
        {
            await using var setup = fixture.CreateDbContext();
            await setup.IntegrationSyncOutbox.Where(row => data.QueueIds.Contains(row.Id))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Status, IntegrationSyncStatus.Processing)
                    .SetProperty(row => row.AttemptCount, 2)
                    .SetProperty(row => row.ProcessingStartedAt,
                        scenario == "missing-start" ? (DateTime?)null : DateTime.UtcNow.AddHours(-1))
                    .SetProperty(row => row.ProcessingLeaseToken,
                        scenario == "missing-lease" ? (Guid?)null : Guid.CreateVersion7())
                    .SetProperty(row => row.LastError, IntegrationSyncFailureCodes.ProviderHandoffInDoubt));
        }
        await using var global = await GlobalIntegrationSyncAuthority.CreateAsync(fixture);
        await using var services = data.CreateIntegrationWorkerServices(global.Factory);
        var worker = new IntegrationSyncDrainService(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new IntegrationSyncProcessorSettings { BatchSize = 1000 }),
            NullLogger<IntegrationSyncDrainService>.Instance);

        var result = await worker.ProcessBatchAsync(CancellationToken.None);
        await Assert.That(result.Processed).IsEqualTo(2);
        await Assert.That(result.AlreadyClaimed).IsEqualTo(0);
        await Assert.That(result.Ambiguous).IsEqualTo(recovery ? 2 : 0);
        await Assert.That(result.DeadLettered).IsEqualTo(recovery ? 0 : 2);
        await using (var verify = fixture.CreateDbContext())
        {
            var rows = await verify.IntegrationSyncOutbox.AsNoTracking()
                .Where(row => data.QueueIds.Contains(row.Id)).ToListAsync();
            await Assert.That(rows.Count).IsEqualTo(2);
            foreach (var row in rows)
            {
                await Assert.That(row.Status).IsEqualTo(IntegrationSyncStatus.DeadLettered);
                await Assert.That(row.AttemptCount).IsEqualTo(recovery ? 2 : 1);
                await Assert.That(row.ProcessingLeaseToken).IsNull();
                await Assert.That(row.ProcessingStartedAt).IsNull();
                await Assert.That(row.NextAttemptAt).IsNull();
                if (recovery)
                    await Assert.That(row.LastError).IsEqualTo(IntegrationSyncFailureCodes.ProviderOutcomeAmbiguous);
                else
                    await Assert.That(row.LastFailureAt).IsNotNull();
            }
        }
        var replay = await worker.ProcessBatchAsync(CancellationToken.None);
        await Assert.That(replay.Processed).IsEqualTo(0)
            .Because("ambiguous provider handoffs and terminal failures must never be automatically replayed");
        await using var unbound = services.CreateAsyncScope();
        var unboundContext = unbound.ServiceProvider.GetRequiredService<ExploreDbContext>();
        int leaked = await unboundContext.IntegrationSyncOutbox.IgnoreQueryFilters()
            .CountAsync(row => data.QueueIds.Contains(row.Id));
        await Assert.That(leaked).IsEqualTo(0)
            .Because("a returned worker pool lease cannot carry the prior tenant into an unbound scope");
    }

    private sealed record FixedTenant(Guid TenantId) : ITenantContext;

    private sealed class GlobalIntegrationSyncAuthority(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        private readonly string _role = $"integration_global_{Guid.CreateVersion7():N}";
        private string _connectionString = null!;
        internal GlobalExploreDbContextFactory Factory { get; private set; } = null!;

        internal static async Task<GlobalIntegrationSyncAuthority> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            var authority = new GlobalIntegrationSyncAuthority(fixture);
            await using var migrator = fixture.CreateDbContext();
            await PostgresIntegrationSyncDiscoveryContract.ApplyAsync(migrator);
            string schema = migrator.Model.FindEntityType(typeof(IntegrationSyncOutbox))!.GetSchema()!;
            string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var grant = administration.CreateCommand();
            grant.CommandText = $"""
                CREATE ROLE "{authority._role}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
                GRANT {PostgresIntegrationSyncDiscoveryContract.RuntimeRole} TO "{authority._role}";
                """;
            await grant.ExecuteNonQueryAsync();
            var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
            var options = new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Global,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Schema = schema,
                Username = authority._role,
                Password = password,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            };
            authority._connectionString = PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString;
            authority.Factory = new GlobalExploreDbContextFactory(options, poolSize: 1);
            return authority;
        }

        public async ValueTask DisposeAsync()
        {
            using var pool = new NpgsqlConnection(_connectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE {PostgresIntegrationSyncDiscoveryContract.RuntimeRole} FROM "{_role}";
                DROP ROLE "{_role}";
                """;
            await revoke.ExecuteNonQueryAsync();
        }
    }

    internal sealed class GlobalLookupAuthority : IAsyncDisposable
    {
        internal string Role { get; } = $"lookup_global_{Guid.CreateVersion7():N}";
        internal GlobalExploreDbContextFactory Factory { get; private set; } = null!;
        private string _connectionString = null!;
        private readonly PostgreSqlContainerFixture _fixture;

        private readonly bool _atprotoGovernance;
        private GlobalLookupAuthority(PostgreSqlContainerFixture fixture, bool atprotoGovernance)
        {
            _fixture = fixture;
            _atprotoGovernance = atprotoGovernance;
        }

        internal static async Task<GlobalLookupAuthority> CreateAsync(
            PostgreSqlContainerFixture fixture, bool atprotoGovernance = false)
        {
            var authority = new GlobalLookupAuthority(fixture, atprotoGovernance);
            await using var migrator = fixture.CreateDbContext();
            await PostgresTenantLookupContract.ApplyAsync(migrator);
            string schema = migrator.Model.FindEntityType(typeof(Tenant))!.GetSchema()!;
            string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var grant = administration.CreateCommand();
            grant.CommandText = $"""
                CREATE ROLE "{authority.Role}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
                GRANT {PostgresTenantLookupContract.GlobalRole} TO "{authority.Role}";
                {(atprotoGovernance ? $"GRANT {PostgresAtprotoGovernanceContract.RuntimeRole} TO \"{authority.Role}\" WITH INHERIT TRUE, SET FALSE;" : "")}
                """;
            await grant.ExecuteNonQueryAsync();
            var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                Username = authority.Role,
                Password = password
            };
            var options = new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Global,
                Provider = PrimaryDatabaseProvider.PostgreSql,
                Host = connection.Host,
                Port = connection.Port,
                Database = connection.Database,
                Schema = schema,
                Username = authority.Role,
                Password = password,
                TlsMode = PrimaryDatabaseTlsMode.Disabled
            };
            authority._connectionString = PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString;
            authority.Factory = new GlobalExploreDbContextFactory(options, poolSize: 1);
            return authority;
        }

        public async ValueTask DisposeAsync()
        {
            using var pool = new NpgsqlConnection(_connectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var administration = new NpgsqlConnection(_fixture.ConnectionString);
            await administration.OpenAsync();
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE {PostgresTenantLookupContract.GlobalRole} FROM "{Role}";
                {(_atprotoGovernance ? $"REVOKE {PostgresAtprotoGovernanceContract.RuntimeRole} FROM \"{Role}\";" : "")}
                DROP ROLE "{Role}";
                """;
            await revoke.ExecuteNonQueryAsync();
        }
    }

    private sealed class OrdinaryRows(PostgreSqlContainerFixture fixture) : IAsyncDisposable
    {
        internal Guid A { get; } = Guid.CreateVersion7();
        internal Guid B { get; } = Guid.CreateVersion7();
        internal string Key { get; } = $"lease.boundary.{Guid.CreateVersion7():N}";
        internal Guid[] KeyIds { get; } = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        internal Guid[] QuotaIds { get; } = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        internal Guid[] QueueIds { get; } = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        internal int[] TypeIds { get; private set; } = [];
        private readonly string _role = $"lease_boundary_{Guid.CreateVersion7():N}";
        private readonly string _password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        private readonly List<string> _tables = [];
        private string _schema = null!;
        private readonly List<string> _readTables = [];
        private string _connectionString = null!;

        internal static async Task<OrdinaryRows> CreateAsync(PostgreSqlContainerFixture fixture)
        {
            var data = new OrdinaryRows(fixture);
            await using var seed = fixture.CreateDbContext();
            seed.Tenants.AddRange(Tenant(data.A), Tenant(data.B));
            seed.TenantSettingOverrides.AddRange(
                Setting(data.A, data.Key), Setting(data.B, data.Key));
            seed.TenantSettingOverrides.AddRange(
                new TenantSetting
                {
                    TenantId = data.A,
                    Tenant = null!,
                    SettingKey = GovernanceSettingKeys.Domains.TenantSubdomain,
                    Value = JsonSerializer.Serialize("lease-a")
                },
                new TenantSetting
                {
                    TenantId = data.B,
                    Tenant = null!,
                    SettingKey = GovernanceSettingKeys.Domains.TenantSubdomain,
                    Value = JsonSerializer.Serialize("lease-b")
                });
            for (int i = 0; i < 2; i++)
            {
                Guid tenant = i == 0 ? data.A : data.B;
                seed.ExternalApiKeys.Add(new ExternalApiKey
                {
                    Id = data.KeyIds[i],
                    TenantId = tenant,
                    Name = data.Key,
                    KeyId = Guid.CreateVersion7().ToString("N"),
                    SecretHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                    Scopes = "events:read",
                    OwnerType = ExternalApiKeyOwnerType.Tenant,
                    OwnerId = tenant,
                    ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
                    ExternalApiKeyStatus = null!,
                    ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.Monthly,
                    ExternalApiKeyCreditPeriod = null!
                });
                seed.ExternalApiKeyQuotas.Add(new ExternalApiKeyQuota
                {
                    Id = data.QuotaIds[i],
                    ExternalApiKeyId = data.KeyIds[i],
                    ExternalApiKey = null!,
                    PeriodStart = new DateOnly(2026, 1, 1),
                    PeriodEnd = new DateOnly(2026, 1, 31)
                });
                seed.IntegrationSyncOutbox.Add(new IntegrationSyncOutbox
                {
                    Id = data.QueueIds[i],
                    TenantId = tenant,
                    Kind = IntegrationKind.Listmonk,
                    SourceType = "lease-test",
                    SourceId = Guid.CreateVersion7(),
                    SubscriberEmail = $"{data.QueueIds[i]:N}@example.test",
                    SubscriberPayloadJson = "{}",
                    ListmonkListId = 1,
                    Status = IntegrationSyncStatus.Pending
                });
            }
            EventType[] types = [Type(data.A, data.Key + ".a"), Type(data.B, data.Key + ".b"), Type(null, data.Key + ".global")];
            int nextTypeId = await seed.EventTypes.MaxAsync(type => type.Id) + 1;
            foreach (var type in types)
                type.Id = nextTypeId++;
            seed.EventTypes.AddRange(types);
            await seed.SaveChangesAsync();
            await PostgresDiscoverySnapshotMaintenanceContract.ApplyAsync(seed);
            data.TypeIds = types.Select(type => type.Id).ToArray();
            foreach (System.Type entityType in new[]
                { typeof(TenantSetting), typeof(EventType), typeof(ExternalApiKey), typeof(ExternalApiKeyQuota),
                    typeof(IntegrationSyncOutbox) })
            {
                var entity = seed.Model.FindEntityType(entityType)!;
                data._schema = entity.GetSchema()!;
                data._tables.Add($"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"");
            }
            foreach (var readType in new[] { typeof(StorageObject), typeof(Tenant) })
            {
                var entity = seed.Model.FindEntityType(readType)!;
                data._readTables.Add($"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"");
            }
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var grant = administration.CreateCommand();
            grant.CommandText = $"""
                CREATE ROLE "{data._role}" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{data._password}';
                GRANT USAGE ON SCHEMA "{data._schema}" TO "{data._role}";
                GRANT SELECT, INSERT, UPDATE, DELETE ON {string.Join(", ", data._tables)} TO "{data._role}";
                GRANT SELECT ON {string.Join(", ", data._readTables)} TO "{data._role}";
                """;
            await grant.ExecuteNonQueryAsync();
            data._connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
            {
                Username = data._role,
                Password = data._password,
                MaxPoolSize = 1
            }.ConnectionString;
            return data;
        }

        internal DbContextOptions<ExploreDbContext> Options(params IInterceptor[] interceptors) =>
            TestDbContextOptions.Create<ExploreDbContext>().UseNpgsql(_connectionString)
                .UseSnakeCaseNamingConvention().AddInterceptors(PostgresTenantSessionInterceptor.Instance)
                .AddInterceptors(interceptors).Options;

        internal ExploreDbContext CreateContext(Guid? tenant) => new(Options())
        {
            TenantContext = tenant is { } id ? new FixedTenant(id) : null
        };

        internal ServiceProvider CreateIntegrationWorkerServices(IGlobalExploreDbContextFactory globalFactory)
        {
            var services = new ServiceCollection();
            services.AddHttpContextAccessor();
            services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
            services.AddSingleton(globalFactory);
            services.AddScoped<IIntegrationSyncQueueDiscovery, IntegrationSyncQueueDiscovery>();
            services.AddPooledDbContextFactory<ExploreDbContext>(options => options
                .UseNpgsql(_connectionString)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance), poolSize: 1);
            services.AddScoped(provider =>
            {
                var context = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                Guid? tenant = provider.GetRequiredService<ITenantContextAccessor>().TenantId;
                context.TenantContext = tenant is { } id ? new FixedTenant(id) : null;
                context.Database.OpenConnection();
                return context;
            });
            services.AddScoped<IIntegrationSyncOutboxRepository, IntegrationSyncOutboxRepository>();
            return services.BuildIsolatedServiceProvider();
        }

        internal async Task AssertOrdinaryAuthorityAsync(ExploreDbContext context)
        {
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT current_user, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.GetString(0)).IsEqualTo(_role);
            await Assert.That(reader.GetBoolean(1)).IsFalse();
            await Assert.That(reader.GetBoolean(2)).IsFalse();
        }

        public async ValueTask DisposeAsync()
        {
            using var pool = new NpgsqlConnection(_connectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var administration = new NpgsqlConnection(fixture.ConnectionString);
            await administration.OpenAsync();
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE ALL ON {string.Join(", ", _tables)} FROM "{_role}";
                REVOKE SELECT ON {string.Join(", ", _readTables)} FROM "{_role}";
                REVOKE USAGE ON SCHEMA "{_schema}" FROM "{_role}";
                DROP ROLE "{_role}";
                """;
            await revoke.ExecuteNonQueryAsync();
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.IntegrationSyncOutbox.IgnoreQueryFilters().Where(row => QueueIds.Contains(row.Id)).ExecuteDeleteAsync();
            await cleanup.ExternalApiKeyQuotas.Where(row => KeyIds.Contains(row.ExternalApiKeyId)).ExecuteDeleteAsync();
            await cleanup.ExternalApiKeys.Where(row => KeyIds.Contains(row.Id)).ExecuteDeleteAsync();
            await cleanup.EventTypes.Where(row => TypeIds.Contains(row.Id)).ExecuteDeleteAsync();
            await cleanup.TenantSettingOverrides.Where(row => row.TenantId == A || row.TenantId == B).ExecuteDeleteAsync();
            await cleanup.Set<EventDiscoveryRevision>().IgnoreQueryFilters()
                .Where(row => row.TenantId == A || row.TenantId == B).ExecuteDeleteAsync();
            await cleanup.Tenants.Where(row => row.Id == A || row.Id == B).ExecuteDeleteAsync();
        }

        private static Tenant Tenant(Guid id) => new()
        {
            Id = id,
            FullName = $"lease-{id:N}",
            Slug = $"lease-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        private static TenantSetting Setting(Guid tenant, string key) => new()
        {
            TenantId = tenant,
            Tenant = null!,
            SettingKey = key,
            Value = "{}"
        };
        private static EventType Type(Guid? tenant, string code) => new()
        {
            TenantId = tenant,
            FullName = code,
            MasterCode = code
        };
    }
}
