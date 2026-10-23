using Explore.Persistence.Services;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel]
public sealed class PostgresEventResourceAuditRetentionLeaseTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("grant-option")]
    [Arguments("column")]
    [Arguments("public")]
    public async Task RetentionSourcePrivilegeDriftIsRejectedWithoutAutomaticRepair(string drift)
    {
        await using var migration = CreateMigrator();
        await InstallProfileAsync(migration);
        var entity = migration.Model.FindEntityType(typeof(EventResourceAuditEntry))!;
        var mapping = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = migration.GetService<ISqlGenerationHelper>();
        string table = names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        string tenant = names.DelimitIdentifier(entity.FindProperty(nameof(EventResourceAuditEntry.TenantId))!.GetColumnName(mapping)!);
        string attribution = names.DelimitIdentifier(entity.FindProperty(nameof(EventResourceAuditEntry.ResponsibleManagerUserId))!.GetColumnName(mapping)!);
        string globalLogin = names.DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);
        string inject = drift switch
        {
            "grant-option" => $"GRANT SELECT ({tenant}) ON {table} TO {globalLogin} WITH GRANT OPTION",
            "column" => $"GRANT SELECT ({attribution}) ON {table} TO {globalLogin}",
            "public" => $"GRANT SELECT ({tenant}) ON {table} TO PUBLIC",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        string restore = drift switch
        {
            "grant-option" => $"REVOKE GRANT OPTION FOR SELECT ({tenant}) ON {table} FROM {globalLogin}",
            "column" => $"REVOKE SELECT ({attribution}) ON {table} FROM {globalLogin}",
            _ => $"REVOKE SELECT ({tenant}) ON {table} FROM PUBLIC"
        };
        await migration.Database.ExecuteSqlRawAsync(inject);
        try
        {
            string injectedImage = await ReadAuditAclAsync(migration, table);
            var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
            await using var global = await factory.CreateDbContextAsync();
            await global.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresEventResourceAuditReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment));
            var reinstall = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallProfileAsync(migration));
            await Assert.That(reinstall.Message).IsEqualTo("event_resource_audit_read_permissions_drift");
            await Assert.That(await ReadAuditAclAsync(migration, table)).IsEqualTo(injectedImage);
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(restore);
        }
        await InstallProfileAsync(migration);
        await InstallProfileAsync(migration);
    }

    [Test]
    public async Task GlobalOwnerDiscoveryMustDeleteThroughIndependentOrdinaryTenantAuthority()
    {
        await using (var migration = CreateMigrator())
            await InstallProfileAsync(migration);
        DateTime now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Guid[] owners = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
        Array.Sort(owners);
        string suffix = Guid.CreateVersion7().ToString("N");
        string runtimeRole = fixture.NativeQueueEnrollment.RuntimeLogin;
        string globalRole = fixture.NativeQueueEnrollment.GlobalLogin;
        string schema;
        string auditTable;
        string auditTenantColumn;
        string tenantTable;
        string[] policyTables;
        await using (var model = fixture.CreateDbContext())
        {
            var audit = model.Model.FindEntityType(typeof(EventResourceAuditEntry))!;
            schema = audit.GetSchema()!;
            auditTable = $"\"{schema}\".\"{audit.GetTableName()}\"";
            var auditMapping = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(audit.GetTableName()!, schema);
            auditTenantColumn = model.GetService<ISqlGenerationHelper>().DelimitIdentifier(
                audit.FindProperty(nameof(EventResourceAuditEntry.TenantId))!.GetColumnName(auditMapping)!);
            var tenant = model.Model.FindEntityType(typeof(Tenant))!;
            tenantTable = $"\"{tenant.GetSchema()}\".\"{tenant.GetTableName()}\"";
            policyTables = new[] { typeof(SystemSetting), typeof(TenantSetting) }.Select(type =>
            {
                var entity = model.Model.FindEntityType(type)!;
                return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
            }).ToArray();
        }
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using (var grant = administration.CreateCommand())
        {
            grant.CommandText = $"""
                GRANT USAGE ON SCHEMA "{schema}" TO "{runtimeRole}";
                GRANT SELECT, DELETE ON {auditTable} TO "{runtimeRole}";
                GRANT SELECT ON {string.Join(", ", policyTables)} TO "{runtimeRole}";
                """;
            await grant.ExecuteNonQueryAsync();
        }
        var runtimeOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime, schema);
        var globalOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global, schema);
        string runtimeConnection = PrimaryDatabaseConfiguration.BuildConnectionString(runtimeOptions).ConnectionString;
        string globalConnection = PrimaryDatabaseConfiguration.BuildConnectionString(globalOptions).ConnectionString;
        var runtimeFactory = new TenantExploreDbContextFactory(
            runtimeOptions, poolSize: 1);
        var globalFactory = new GlobalExploreDbContextFactory(
            globalOptions, poolSize: 1);
        Guid userId = Guid.CreateVersion7(), actorId = Guid.CreateVersion7();
        Guid retentionSettingId = Guid.CreateVersion7();
        SystemSetting? originalRetention = null;
        bool retentionSeeded = false;
        var events = owners.Select(owner => new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = owner,
            Tenant = null!,
            ActorId = actorId,
            Actor = null!,
            Title = "Audit retention",
            PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        }).ToArray();
        var resources = events.Select(owner =>
            EventResourcePersistenceTests.CreateDraft(owner.TenantId, owner.Id)).ToArray();
        EventResourceAuditEntry[] rows =
        [
            Audit(resources[0], now.AddDays(-31)),
            Audit(resources[0], now.AddDays(-30)),
            Audit(resources[0], now.AddDays(-30).AddTicks(10)),
            Audit(resources[1], now.AddDays(1)),
            Audit(resources[2], now.AddDays(-30))
        ];
        try
        {
            await using (var seed = CreateMigrator())
            {
                await using var humanGate = new HumanSecuritySourceGate(seed);
                await using var human = await humanGate.AcquireAsync();
                await using var gate = new EventDiscoveryEnrollmentGate(seed);
                await using var enrollment = await gate.AcquireAsync();
                seed.Tenants.AddRange(owners.Select(owner => new Tenant
                {
                    Id = owner,
                    FullName = "Audit retention",
                    Slug = "audit-" + owner.ToString("N"),
                    TenantStatusId = owner == owners[2] ? (int)TenantStatusEnum.Suspended : (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                }));
                seed.Users.Add(new User
                {
                    Id = userId,
                    Pii = new UserPii
                    {
                        Email = $"audit-{suffix}@example.test",
                        FirstName = "Audit",
                        LastName = "Owner"
                    }
                });
                await seed.SaveChangesAsync();
                seed.Actors.Add(new Actor
                {
                    Id = actorId,
                    ActorTypeId = 1,
                    ActorType = null!,
                    UserId = userId,
                    Pii = new ActorPii { DisplayName = "Audit Owner" }
                });
                await seed.SaveChangesAsync();
                seed.Events.AddRange(events);
                await seed.SaveChangesAsync();
                seed.EventResources.AddRange(resources);
                seed.Set<EventResourceAuditEntry>().AddRange(rows);
                originalRetention = await seed.SystemSettings.AsNoTracking().SingleOrDefaultAsync(
                    row => row.SettingKey == GovernanceSettingKeys.EventResources.AuditRetentionDays);
                if (originalRetention is null)
                {
                    seed.SystemSettings.Add(new SystemSetting
                    {
                        Id = retentionSettingId,
                        SettingKey = GovernanceSettingKeys.EventResources.AuditRetentionDays,
                        Value = "90",
                        ValueType = SettingValueType.Integer
                    });
                }
                else
                {
                    await seed.SystemSettings.Where(row => row.Id == originalRetention.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "90")
                            .SetProperty(row => row.IsLocked, false));
                    retentionSeeded = true;
                }
                seed.TenantSettingOverrides.AddRange(
                    new TenantSetting
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = owners[0],
                        Tenant = null!,
                        SettingKey = GovernanceSettingKeys.EventResources.AuditRetentionDays,
                        Value = "30"
                    },
                    new TenantSetting
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = owners[1],
                        Tenant = null!,
                        SettingKey = GovernanceSettingKeys.EventResources.AuditRetentionDays,
                        Value = "0"
                    });
                await seed.SaveChangesAsync();
                retentionSeeded = true;
            }

            // Exact ordinary acquisition proves foreign rows stay hidden even
            // when EF filters are bypassed. PostgreSQL, not a mock, owns this bound.
            await using (var ordinary = await runtimeFactory.CreateDbContextAsync(owners[0]))
            {
                await AssertAuthorityAsync(ordinary, runtimeRole);
                await Assert.That(await ordinary.Set<EventResourceAuditEntry>().IgnoreQueryFilters()
                    .CountAsync(row => row.TenantId == owners[2])).IsEqualTo(0);
                await Assert.That(await ordinary.Set<EventResourceAuditEntry>().IgnoreQueryFilters()
                    .Where(row => row.TenantId == owners[2]).ExecuteDeleteAsync()).IsEqualTo(0);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                {
                    ordinary.TenantContext = new FixedTenant(owners[2]);
                    return Task.CompletedTask;
                });
                var bounded = new EventResourceAuditRetentionRepository(ordinary);
                int deleted = await new EfCoreUnitOfWork(ordinary).ExecuteSerializableAsync(
                    token => bounded.DeleteExpiredBatchAsync(owners[0], now.AddDays(-30), 1, token));
                await Assert.That(deleted).IsEqualTo(1);
            }
            await using var global = await globalFactory.CreateDbContextAsync();
            await AssertAuthorityAsync(global, globalRole);
            PostgresException? denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.Set<EventResourceAuditEntry>().IgnoreQueryFilters()
                    .Select(row => row.ResponsibleManagerUserId).ToArrayAsync());
            await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            PostgresException? deniedDelete = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.Set<EventResourceAuditEntry>().IgnoreQueryFilters()
                    .Where(row => row.TenantId == owners[0]).ExecuteDeleteAsync());
            await Assert.That(deniedDelete?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await Assert.That(await global.Database.SqlQueryRaw<bool>(
                $"SELECT has_table_privilege(current_user, '{auditTable}', 'SELECT') AS \"Value\"")
                .SingleAsync()).IsFalse();
            await Assert.That(await global.Database.SqlQueryRaw<bool>(
                "SELECT NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname='event_resource_audit_retention_owner') AS \"Value\"")
                .SingleAsync()).IsTrue();
            await Assert.That(await global.Database.SqlQueryRaw<bool>(
                $"SELECT has_table_privilege(current_user, '{tenantTable}', 'SELECT') AS \"Value\"")
                .SingleAsync()).IsFalse();
            var repository = new EventResourceAuditOwnerDiscoveryRepository(globalFactory, fixture.NativeQueueEnrollment);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetTenantIdsWithAuditAsync(
                owners[0], EventResourceAuditRetentionService.TenantPageSize + 1, CancellationToken.None));
            await global.Database.OpenConnectionAsync();
            await using (var command = global.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = $"SELECT {auditTenantColumn} AS tenant_id FROM {auditTable} LIMIT 1";
                await using var reader = await command.ExecuteReaderAsync();
                await Assert.That(reader.FieldCount).IsEqualTo(1);
                await Assert.That(reader.GetName(0)).IsEqualTo("tenant_id");
                await Assert.That(reader.GetFieldType(0)).IsEqualTo(typeof(Guid));
            }
            var firstPage = await repository.GetTenantIdsWithAuditAsync(null, 1, CancellationToken.None);
            await Assert.That(firstPage).IsEquivalentTo([owners[0]]);
            var rest = await repository.GetTenantIdsWithAuditAsync(owners[0], 2, CancellationToken.None);
            await Assert.That(rest).IsEquivalentTo([owners[1], owners[2]]);

            List<Guid> acquiredOwners = [];
            var executionFactory = new EventResourceAuditRetentionExecutionScopeFactory(
                new RecordingTenantFactory(runtimeFactory, runtimeRole, acquiredOwners));
            await using var ambient = await global.Database.BeginTransactionAsync();
            var service = new EventResourceAuditRetentionService(
                repository, executionFactory, new FixedClock(now));
            await Assert.That(await service.CleanupAsync(CancellationToken.None)).IsEqualTo(2L);
            await ambient.RollbackAsync();
            await Assert.That(acquiredOwners).IsEquivalentTo(owners);
            await using var verify = fixture.CreateDbContext();
            var remaining = await verify.Set<EventResourceAuditEntry>().AsNoTracking()
                .Where(row => owners.Contains(row.TenantId)).ToArrayAsync();
            await Assert.That(remaining.Select(row => row.Id)).IsEquivalentTo([rows[2].Id, rows[4].Id]);
            await Assert.That(remaining.Single(row => row.Id == rows[2].Id).ResponsibleManagerUserId)
                .IsEqualTo(rows[2].ResponsibleManagerUserId);
            await Assert.That(await verify.EventResources.CountAsync(row => owners.Contains(row.TenantId)))
                .IsEqualTo(3);
            await using (var invalid = fixture.CreateDbContext())
            {
                invalid.TenantSettingOverrides.Add(new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = owners[2],
                    Tenant = null!,
                    SettingKey = GovernanceSettingKeys.EventResources.AuditRetentionDays,
                    Value = "not-json"
                });
                await invalid.SaveChangesAsync();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CleanupAsync(CancellationToken.None));
            await Assert.That(await verify.Set<EventResourceAuditEntry>().AsNoTracking()
                .Where(row => owners.Contains(row.TenantId)).Select(row => row.Id).ToArrayAsync())
                .IsEquivalentTo([rows[2].Id, rows[4].Id]);
        }
        finally
        {
            using var ordinaryPool = new NpgsqlConnection(runtimeConnection);
            using var globalPool = new NpgsqlConnection(globalConnection);
            NpgsqlConnection.ClearPool(ordinaryPool);
            NpgsqlConnection.ClearPool(globalPool);
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE SELECT, DELETE ON {auditTable} FROM "{runtimeRole}";
                REVOKE SELECT ON {string.Join(", ", policyTables)} FROM "{runtimeRole}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{runtimeRole}";
                """;
            await revoke.ExecuteNonQueryAsync();
            await using var cleanup = fixture.CreateDbContext();
            await cleanup.Set<EventResourceAuditEntry>()
                .Where(row => owners.Contains(row.TenantId)).ExecuteDeleteAsync();
            await cleanup.Set<EventResourceAudienceRule>()
                .Where(row => owners.Contains(row.TenantId)).ExecuteDeleteAsync();
            await cleanup.EventResources.Where(row => owners.Contains(row.TenantId)).ExecuteDeleteAsync();
            var root = cleanup.Model.FindEntityType(typeof(Explore.Domain.Event))!.GetRootType();
            string rootTable = cleanup.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(root.GetTableName()!, root.GetSchema());
            string deleteEvents = $"DELETE FROM {rootTable} WHERE id = ANY({{0}})";
            await cleanup.Database.ExecuteSqlRawAsync(deleteEvents, [events.Select(source => source.Id).ToArray()]);
            await cleanup.Set<EventDiscoveryRevision>().Where(row => owners.Contains(row.TenantId)).ExecuteDeleteAsync();
            await cleanup.Actors.Where(row => row.Id == actorId).ExecuteDeleteAsync();
            await cleanup.Users.Where(row => row.Id == userId).ExecuteDeleteAsync();
            await cleanup.TenantSettingOverrides.Where(row => owners.Contains(row.TenantId)).ExecuteDeleteAsync();
            await cleanup.Tenants.Where(row => owners.Contains(row.Id)).ExecuteDeleteAsync();
            if (retentionSeeded && originalRetention is not null)
            {
                await cleanup.SystemSettings.Where(row => row.Id == originalRetention.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, originalRetention.Value)
                        .SetProperty(row => row.IsLocked, originalRetention.IsLocked));
            }
            else if (originalRetention is null)
            {
                await cleanup.SystemSettings.Where(row => row.Id == retentionSettingId).ExecuteDeleteAsync();
            }
        }

    }

    private static EventResourceAuditEntry Audit(EventResource resource, DateTime timestamp) =>
        EventResourceAuditEntry.Create(resource.TenantId, resource.Id, Guid.CreateVersion7(),
            EventResourceAuditAction.UpdateMetadata, EventResourceAuditOutcome.Succeeded,
            EventResourceAuditReason.OrganizerMutation, timestamp);

    private ExploreDbContext CreateMigrator()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private Task InstallProfileAsync(ExploreDbContext context) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            PostgresEventResourceAuditReadPermissions.ApplyAsync(context, fixture.NativeQueueEnrollment));

    private static async Task<string> ReadAuditAclAsync(ExploreDbContext context, string table)
    {
        if (context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COALESCE(string_agg(a.attnum::text || ':' || COALESCE(a.attacl::text,''),',' ORDER BY a.attnum),'')
            FROM pg_catalog.pg_attribute a
            WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum>0 AND NOT a.attisdropped
            """;
        command.Parameters.Add(new NpgsqlParameter("table", table));
        return (string)(await command.ExecuteScalarAsync())!;
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

    private sealed class RecordingTenantFactory(
        ITenantExploreDbContextFactory production,
        string expectedRole,
        List<Guid> acquiredOwners) : ITenantExploreDbContextFactory
    {
        public async Task<ExploreDbContext> CreateDbContextAsync(
            Guid tenantId, CancellationToken cancellationToken = default)
        {
            var context = await production.CreateDbContextAsync(tenantId, cancellationToken);
            try
            {
                await AssertAuthorityAsync(context, expectedRole);
                await Assert.That(context.TenantFilterTenantId).IsEqualTo(tenantId);
                acquiredOwners.Add(tenantId);
                return context;
            }
            catch
            {
                await context.DisposeAsync();
                throw;
            }
        }
    }

    private sealed record FixedTenant(Guid TenantId) : Explore.Application.Contracts.Infrastructure.ITenantContext;
    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
