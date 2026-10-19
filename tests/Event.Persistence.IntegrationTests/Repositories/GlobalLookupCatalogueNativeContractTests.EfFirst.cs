using System.Data;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Caching;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class GlobalLookupCatalogueNativeContractTests
{
    [Test]
    public async Task EfFirstRuntimeMaterializesAllSeventeenReferencesAndRefreshesTheCache()
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        int id = await NextEfFirstLookupId(writer);
        var rows = LookupTypes.Select(type =>
        {
            object row = Activator.CreateInstance(type)!;
            type.GetProperty("Id")!.SetValue(row, id);
            type.GetProperty("MasterCode")!.SetValue(row, "EF_CATALOGUE_" + id);
            type.GetProperty("FullName")!.SetValue(row, "EF reference");
            type.GetProperty("Description")!.SetValue(row, "Persisted EF metadata");
            if (row is AudienceAge age) { age.MinAge = 8; age.MaxAge = 15; }
            if (row is Role role) { role.RoleScopeId = (int)RoleScopeEnum.Platform; role.IsSystem = false; }
            return row;
        }).ToArray();
        writer.AddRange(rows);
        await writer.SaveChangesAsync();
        try
        {
            await using var reader = Open(PrimaryDatabaseRole.Runtime);
            await using var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var connection = reader.Database.GetDbConnection();
            await Assert.That(await reader.Database.SqlQueryRaw<bool>(
                "SELECT session_user = {0} AND current_user = session_user AS \"Value\"",
                fixture.NativeQueueEnrollment.RuntimeLogin).SingleAsync()).IsTrue();

            var services = new ServiceCollection();
            services.AddSingleton(fixture.NativeQueueEnrollment);
            services.AddSingleton<IDbContextFactory<ExploreDbContext>>(
                new EfFirstCatalogueContextFactory(() => Open(PrimaryDatabaseRole.Runtime)));
            await using var provider = services.BuildServiceProvider();
            using var cache = new LookupDataCache(provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<LookupDataCache>.Instance);

            // Direct EF materialization is the accepted data path, not a function projection.
            await Read<EventType>();
            await Read<EventFormat>();
            await Read<EventStatus>();
            await Read<AudienceAge>();
            await Read<AudienceGender>();
            await Read<Madhab>();
            await Read<Language>();
            await Read<VisibilityType>();
            await Read<ApprovalStatus>();
            await Read<RegistrationMode>();
            await Read<Role>();
            await Read<OrganizationPosition>();
            await Read<ActorType>();
            await Read<DidCustodyType>();
            await Read<FileType>();
            await Read<TagType>();
            await Read<OwnerType>();
            await Assert.That(reader.ChangeTracker.Entries().Count()).IsEqualTo(0);
            await Assert.That(reader.TenantFilterTenantId).IsNull();
            await Assert.That(reader.IsTenantFilterBypassed).IsFalse();
            await Assert.That(reader.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(reader.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();

            await cache.InitializeAsync();
            await AssertCached<EventType>();
            await AssertCached<EventFormat>();
            await AssertCached<EventStatus>();
            await AssertCached<AudienceAge>();
            await AssertCached<AudienceGender>();
            await AssertCached<Madhab>();
            await AssertCached<Language>();
            await AssertCached<VisibilityType>();
            await AssertCached<ApprovalStatus>();
            await AssertCached<RegistrationMode>();
            await AssertCached<Role>();
            await AssertCached<OrganizationPosition>();
            await AssertCached<ActorType>();
            await AssertCached<DidCustodyType>();
            await AssertCached<FileType>();
            await AssertCached<TagType>();
            await AssertCached<OwnerType>();
            await Assert.That(cache.GetById<AudienceAge>(id)!.MinAge).IsEqualTo(8);
            await Assert.That(cache.GetById<AudienceAge>(id)!.MaxAge).IsEqualTo(15);
            await Assert.That(cache.GetById<Role>(id)!.Scope).IsEqualTo(RoleScopeEnum.Platform);
            await Assert.That(cache.GetById<Role>(id)!.IsSystem).IsFalse();

            var owner = rows.OfType<OwnerType>().Single();
            owner.FullName = "Refreshed EF reference";
            owner.Description = null;
            await writer.SaveChangesAsync();
            await cache.RefreshAsync();
            await Assert.That(cache.GetById<OwnerType>(id)!.FullName).IsEqualTo("Refreshed EF reference");
            await Assert.That(cache.GetById<OwnerType>(id)!.Description).IsNull();

            async Task Read<T>() where T : class
            {
                T row = await reader.Set<T>().AsNoTracking()
                    .SingleAsync(value => EF.Property<int>(value, "Id") == id);
                await Assert.That(typeof(T).GetProperty("MasterCode")!.GetValue(row)).IsEqualTo("EF_CATALOGUE_" + id);
                await Assert.That(typeof(T).GetProperty("FullName")!.GetValue(row)).IsEqualTo("EF reference");
                await Assert.That(typeof(T).GetProperty("Description")!.GetValue(row)).IsEqualTo("Persisted EF metadata");
            }

            async Task AssertCached<T>() where T : class
            {
                await Assert.That(cache.GetById<T>(id)).IsNotNull();
                await Assert.That(typeof(T).GetProperty("FullName")!.GetValue(cache.GetById<T>(id)))
                    .IsEqualTo("EF reference");
            }
        }
        finally
        {
            writer.RemoveRange(rows);
            await writer.SaveChangesAsync();
        }
    }

    [Test]
    public async Task EfFirstRuntimeHasOnlyTheFixedReferenceColumnsWithoutWriteOrPromotionAuthority()
    {
        await using var reader = Open(PrimaryDatabaseRole.Runtime);
        var names = new HumanPrincipalSourceSql(reader);
        foreach (Type type in LookupTypes)
        {
            string table = names.Table(type);
            string[] expected = EfFirstProperties(type).Select(property => names.ColumnName(type, property)).ToArray();
            var actual = await reader.Database.SqlQueryRaw<string>(
                """
                SELECT a.attname::text AS "Value" FROM pg_catalog.pg_attribute a
                WHERE a.attrelid = pg_catalog.to_regclass({0}) AND a.attnum > 0 AND NOT a.attisdropped
                    AND pg_catalog.has_column_privilege(current_user, a.attrelid, a.attnum, 'SELECT')
                """, table).ToListAsync();
            await Assert.That(string.Join(",", actual.Order(StringComparer.Ordinal)))
                .IsEqualTo(string.Join(",", expected.Order(StringComparer.Ordinal)));
            await Assert.That(await reader.Database.SqlQueryRaw<bool>(
                """
                SELECT NOT EXISTS (
                    SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE pg_catalog.pg_has_role(current_user, r.oid, 'MEMBER')
                    AND (pg_catalog.has_table_privilege(r.oid, pg_catalog.to_regclass({0}),
                            'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            WHERE a.attrelid = pg_catalog.to_regclass({0}) AND a.attnum <> 0 AND NOT a.attisdropped
                            AND (pg_catalog.has_column_privilege(r.oid, a.attrelid, a.attnum,
                                    'INSERT,UPDATE,REFERENCES,SELECT WITH GRANT OPTION')
                                OR (NOT (a.attname::text = ANY({1}))
                                    AND pg_catalog.has_column_privilege(r.oid, a.attrelid, a.attnum, 'SELECT')))))) AS "Value"
                """, table, expected).SingleAsync()).IsTrue();
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                reader.Database.ExecuteSqlRawAsync($"SELECT xmin FROM {table} LIMIT 1"));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }

        foreach (Type type in new[] { typeof(RolePermission), typeof(UserPii) })
        {
            string table = names.Table(type);
            await Assert.That(await reader.Database.SqlQueryRaw<bool>(
                """
                SELECT NOT EXISTS (
                    SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE pg_catalog.pg_has_role(current_user, r.oid, 'MEMBER')
                    AND (pg_catalog.has_table_privilege(r.oid, pg_catalog.to_regclass({0}),
                            'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR pg_catalog.has_any_column_privilege(r.oid, pg_catalog.to_regclass({0}),
                            'SELECT,INSERT,UPDATE,REFERENCES'))) AS "Value"
                """, table).SingleAsync()).IsTrue();
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                reader.Database.ExecuteSqlRawAsync($"SELECT * FROM {table} LIMIT 1"));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }
    }

    [Test]
    public async Task EfFirstFreshDatabaseHasNoCatalogueBusinessRoutineOrDedicatedRoles()
    {
        await using var reader = Open(PrimaryDatabaseRole.Runtime);
        var names = new HumanPrincipalSourceSql(reader);
        await Assert.That(await reader.Database.SqlQueryRaw<int>(
            """
            SELECT count(*)::integer AS "Value" FROM pg_catalog.pg_proc p
            JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = {0} AND p.proname = 'global_lookup_catalogue'
            """, names.Schema).SingleAsync()).IsEqualTo(0);
        await Assert.That(await reader.Database.SqlQueryRaw<int>(
            """
            SELECT count(*)::integer AS "Value" FROM pg_catalog.pg_roles
            WHERE rolname IN ('event_global_lookup_owner', 'event_global_lookup_migrator', 'event_global_lookup_runtime')
            """).SingleAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task EfFirstRuntimeCannotReadTenantPrivateEventTypesEvenWithoutEfFilters()
    {
        await using var writer = Open(PrimaryDatabaseRole.Migrator);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(), FullName = "EF catalogue isolation",
            Slug = Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        };
        int id = await NextEfFirstLookupId(writer);
        var privateType = new EventType
        {
            Id = id, MasterCode = "EF_PRIVATE_" + id, FullName = "Tenant-private EF reference",
            TenantId = tenant.Id, Tenant = tenant
        };
        var globalType = new EventType
        {
            Id = checked(id + 1), MasterCode = "EF_GLOBAL_" + id, FullName = "Global EF reference"
        };
        writer.AddRange(tenant, privateType, globalType);
        await writer.SaveChangesAsync();
        try
        {
            await Assert.That(await writer.Set<EventType>().IgnoreQueryFilters()
                .AnyAsync(value => value.Id == id && value.TenantId == tenant.Id)).IsTrue();
            await using var reader = Open(PrimaryDatabaseRole.Runtime);
            await Assert.That(await reader.Set<EventType>().AsNoTracking()
                .AnyAsync(value => value.Id == id)).IsFalse();
            // Removing the EF predicate must still leave the database tenant boundary intact.
            var visible = await reader.Set<EventType>().IgnoreQueryFilters().AsNoTracking().ToListAsync();
            await Assert.That(visible.Any(value => value.Id == globalType.Id)).IsTrue();
            await Assert.That(visible.Any(value => value.Id == id)).IsFalse();
            await Assert.That(visible.All(value => value.TenantId is null)).IsTrue();
            await Assert.That(await reader.Database.SqlQueryRaw<bool>(
                """
                SELECT c.relrowsecurity AND c.relforcerowsecurity
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = c.oid AND p.polname = 'tenant_isolation'
                            AND p.polcmd = '*' AND p.polpermissive AND p.polroles = ARRAY[0::oid]
                            AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL) AS "Value"
                FROM pg_catalog.pg_class c WHERE c.oid = pg_catalog.to_regclass({0})
                """, new HumanPrincipalSourceSql(reader).Table(typeof(EventType))).SingleAsync()).IsTrue();
            await Assert.That(reader.TenantFilterTenantId).IsNull();
            await Assert.That(reader.IsTenantFilterBypassed).IsFalse();
        }
        finally
        {
            writer.RemoveRange(privateType, globalType);
            await writer.SaveChangesAsync();
        }
    }

    private static string[] EfFirstProperties(Type type) => type == typeof(EventType)
        ? ["Id", "MasterCode", "FullName", "Description", "TenantId"]
        : type == typeof(AudienceAge)
            ? ["Id", "MasterCode", "FullName", "Description", "MinAge", "MaxAge"]
            : type == typeof(Role)
                ? ["Id", "MasterCode", "FullName", "Description", "RoleScopeId", "IsSystem"]
                : ["Id", "MasterCode", "FullName", "Description"];

    private static async Task<int> NextEfFirstLookupId(ExploreDbContext writer)
    {
        var names = new HumanPrincipalSourceSql(writer);
        int id = 1_000_000;
        foreach (Type type in LookupTypes)
        {
            int maximum = await writer.Database.SqlQueryRaw<int>(
                $"SELECT COALESCE(MAX({names.Column(type, "Id")}),0) AS \"Value\" FROM {names.Table(type)}").SingleAsync();
            id = Math.Max(id, checked(maximum + 1));
        }
        return id;
    }

    private sealed class EfFirstCatalogueContextFactory(Func<ExploreDbContext> create)
        : IDbContextFactory<ExploreDbContext>
    {
        public ExploreDbContext CreateDbContext() => create();
    }
}
