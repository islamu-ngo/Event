using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

public sealed class PostgresMappedOwnershipTests
{
    [Test]
    public async Task UnrelatedForeignKeyDoesNotReplaceTenantFilterOwnershipNavigation()
    {
        var tables = PostgresTenantRowLevelSecurityModel.GetTenantTables(CreateModel("ownership"));
        var branch = tables.Single(table => table.TableName == "branches");
        var leaf = tables.Single(table => table.TableName == "leaves");

        await Assert.That(branch.ParentJoin).IsEqualTo(
            new TenantParentJoinMetadata("roots", "ownership", "owner_id", "id", "tenant_id", false));
        await Assert.That(leaf.ParentJoin).IsEqualTo(
            new TenantParentJoinMetadata("branches", "ownership", "branch_id", "id", null, false,
                new TenantParentJoinMetadata("roots", "ownership", "owner_id", "id", "tenant_id", false)));
    }

    [Test]
    [Arguments(InvalidPath.Ambiguous)]
    [Arguments(InvalidPath.Cyclic)]
    [Arguments(InvalidPath.OptionalIntermediate)]
    [Arguments(InvalidPath.Unmapped)]
    public async Task UnsupportedOwnershipPathFailsClosedBeforePolicyInstallation(InvalidPath path)
    {
        var model = CreateModel("ownership", path);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            PostgresTenantRowLevelSecurityModel.GetTenantTables(model);
            return Task.CompletedTask;
        });
    }

    internal static IModel CreateModel(string schema, InvalidPath? invalidPath = null)
    {
        var builder = new ModelBuilder();
        var root = builder.Entity<OwnershipRoot>();
        root.ToTable("roots", schema);
        root.HasKey(entity => entity.Id);
        root.Property(entity => entity.Id).HasColumnName("id");
        root.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        root.HasQueryFilter(QueryFilterNames.Tenant, entity => entity.TenantId == Guid.Empty);

        var branch = builder.Entity<OwnershipBranch>();
        branch.ToTable("branches", schema);
        branch.HasKey(entity => entity.Id);
        branch.Property(entity => entity.Id).HasColumnName("id");
        branch.Property(entity => entity.OwnerId).HasColumnName("owner_id");
        branch.Property(entity => entity.DecoyId).HasColumnName("decoy_id");
        branch.HasOne(entity => entity.Owner).WithMany().HasForeignKey(entity => entity.OwnerId).IsRequired();
        branch.HasOne(entity => entity.Decoy).WithMany().HasForeignKey(entity => entity.DecoyId).IsRequired();
        branch.HasQueryFilter(QueryFilterNames.Tenant, entity => entity.Owner.TenantId == Guid.Empty);

        var leaf = builder.Entity<OwnershipLeaf>();
        leaf.ToTable("leaves", schema);
        leaf.HasKey(entity => entity.Id);
        leaf.Property(entity => entity.Id).HasColumnName("id");
        leaf.Property(entity => entity.BranchId).HasColumnName("branch_id");
        leaf.HasOne(entity => entity.Branch).WithMany().HasForeignKey(entity => entity.BranchId)
            .IsRequired(invalidPath != InvalidPath.OptionalIntermediate);
        leaf.Ignore(entity => entity.Unmapped);
        branch.Ignore(entity => entity.Leaf);
        branch.Ignore(entity => entity.LeafId);

        switch (invalidPath)
        {
            case InvalidPath.Ambiguous:
                leaf.HasQueryFilter(QueryFilterNames.Tenant,
                    entity => entity.Branch.Owner.TenantId == Guid.Empty ||
                        entity.Branch.Decoy.TenantId == Guid.Empty);
                break;
            case InvalidPath.Cyclic:
                branch.Property(entity => entity.LeafId).HasColumnName("leaf_id");
                branch.HasOne(entity => entity.Leaf).WithMany().HasForeignKey(entity => entity.LeafId)
                    .IsRequired();
                leaf.HasQueryFilter(QueryFilterNames.Tenant,
                    entity => entity.Branch.Leaf.Branch.Owner.TenantId == Guid.Empty);
                break;
            case InvalidPath.Unmapped:
                leaf.HasQueryFilter(QueryFilterNames.Tenant, entity => entity.Unmapped.TenantId == Guid.Empty);
                break;
            default:
                leaf.HasQueryFilter(QueryFilterNames.Tenant,
                    entity => entity.Branch.Owner.TenantId == Guid.Empty);
                break;
        }

        return (IModel)builder.Model;
    }

    public enum InvalidPath
    {
        Ambiguous,
        Cyclic,
        OptionalIntermediate,
        Unmapped
    }

    private sealed class OwnershipRoot
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
    }

    private sealed class OwnershipBranch
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid DecoyId { get; set; }
        public OwnershipRoot Owner { get; set; } = null!;
        public OwnershipRoot Decoy { get; set; } = null!;
        public int LeafId { get; set; }
        public OwnershipLeaf Leaf { get; set; } = null!;
    }

    private sealed class OwnershipLeaf
    {
        public int Id { get; set; }
        public Guid? BranchId { get; set; }
        public OwnershipBranch Branch { get; set; } = null!;
        public OwnershipRoot Unmapped { get; set; } = null!;
    }
}

[NotInParallel("PersistenceDb")]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresMappedOwnershipNativeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RequiredMultiHopPoliciesEnforceReadAndWriteIsolationAcrossMappedSchemas(bool remapSchema)
    {
        string schema = "mapped_ownership_" + Guid.CreateVersion7().ToString("N");
        string qualifiedSchema = Quote(schema);
        string roots = qualifiedSchema + ".\"roots\"";
        string branches = qualifiedSchema + ".\"branches\"";
        string leaves = qualifiedSchema + ".\"leaves\"";
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await using var migration = CreateContext(PrimaryDatabaseRole.Migrator, schema);
        await migration.Database.OpenConnectionAsync();
        try
        {
            string runtimeLogin = Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
            await ExecuteAsync(migration, $"""
                CREATE SCHEMA {qualifiedSchema};
                CREATE TABLE {roots} (id uuid PRIMARY KEY, tenant_id uuid NOT NULL);
                CREATE TABLE {branches} (id uuid PRIMARY KEY,
                    owner_id uuid NOT NULL REFERENCES {roots}(id),
                    decoy_id uuid NOT NULL REFERENCES {roots}(id));
                CREATE TABLE {leaves} (id integer PRIMARY KEY,
                    branch_id uuid NOT NULL REFERENCES {branches}(id));
                INSERT INTO {roots} VALUES ('{tenantA}', '{tenantA}'), ('{tenantB}', '{tenantB}');
                INSERT INTO {branches} VALUES ('{tenantA}', '{tenantA}', '{tenantB}'),
                    ('{tenantB}', '{tenantB}', '{tenantA}');
                INSERT INTO {leaves} VALUES (1, '{tenantA}'), (2, '{tenantB}');
                GRANT USAGE ON SCHEMA {qualifiedSchema} TO {runtimeLogin};
                GRANT SELECT ON {roots}, {branches} TO {runtimeLogin};
                GRANT SELECT, INSERT, UPDATE, DELETE ON {leaves} TO {runtimeLogin};
                """);
            var model = PostgresMappedOwnershipTests.CreateModel(remapSchema ? schema + "_source" : schema);
            foreach (var table in PostgresTenantRowLevelSecurityModel.GetTenantTables(model))
            {
                await ExecuteAsync(migration,
                    PostgresTenantRowLevelSecurityModel.BuildEnableRlsSql(table, remapSchema ? schema : null));
            }

            await using var runtime = CreateContext(PrimaryDatabaseRole.Runtime, schema);
            await runtime.Database.OpenConnectionAsync();
            await Assert.That(await ScalarAsync<bool>(runtime, """
                SELECT NOT rolsuper AND NOT rolbypassrls FROM pg_catalog.pg_roles
                WHERE rolname = current_user
                """)).IsTrue();
            await BindTenantAsync(runtime, tenantA);
            await Assert.That(await VisibleIdsAsync(runtime, leaves)).IsEqualTo("1");
            await Assert.That(await ExecuteAsync(runtime,
                $"UPDATE {leaves} SET id = 20 WHERE id = 2")).IsEqualTo(0);
            await Assert.That(await ExecuteAsync(runtime,
                $"DELETE FROM {leaves} WHERE id = 2")).IsEqualTo(0);
            await Assert.That(await ExecuteAsync(runtime,
                $"INSERT INTO {leaves} VALUES (3, '{tenantA}')")).IsEqualTo(1);
            await Assert.That(await VisibleIdsAsync(runtime, leaves)).IsEqualTo("1,3");

            var insert = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteAsync(runtime, $"INSERT INTO {leaves} VALUES (4, '{tenantB}')"));
            await Assert.That(insert!.SqlState).IsEqualTo("42501");
            var update = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteAsync(runtime, $"UPDATE {leaves} SET branch_id = '{tenantB}' WHERE id = 1"));
            await Assert.That(update!.SqlState).IsEqualTo("42501");
            await Assert.That(await VisibleIdsAsync(runtime, leaves)).IsEqualTo("1,3");

            await BindTenantAsync(runtime, tenantB);
            await Assert.That(await VisibleIdsAsync(runtime, leaves)).IsEqualTo("2");
            await BindTenantAsync(runtime, null);
            await Assert.That(await VisibleIdsAsync(runtime, leaves)).IsEqualTo(string.Empty);
            var unbound = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteAsync(runtime, $"INSERT INTO {leaves} VALUES (5, '{tenantA}')"));
            await Assert.That(unbound!.SqlState).IsEqualTo("42501");
        }
        finally
        {
            await ExecuteAsync(migration, $"DROP SCHEMA IF EXISTS {qualifiedSchema} CASCADE");
        }
    }

    private ExploreDbContext CreateContext(PrimaryDatabaseRole role, string schema)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role, schema));
        return new ExploreDbContext(options.Options);
    }

    private static async Task BindTenantAsync(ExploreDbContext context, Guid? tenant)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_catalog.set_config('app.current_tenant_id', @tenant, false)";
        command.Parameters.Add(new NpgsqlParameter("tenant", tenant?.ToString() ?? string.Empty));
        await command.ExecuteScalarAsync();
    }

    private static Task<string> VisibleIdsAsync(ExploreDbContext context, string table) =>
        ScalarAsync<string>(context,
            $"SELECT coalesce(string_agg(id::text, ',' ORDER BY id), '') FROM {table}");

    private static async Task<int> ExecuteAsync(ExploreDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(ExploreDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
