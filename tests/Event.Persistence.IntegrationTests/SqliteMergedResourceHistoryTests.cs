using Event.Persistence.IntegrationTests.Database;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Event.Persistence.IntegrationTests;

// Generation-only regressions: no connection is opened and no migration SQL is executed.
public sealed class SqliteMergedResourceHistoryTests
{
    private const string Imports = "20261013111447_AllowRepeatedFailedConfigurationImports";
    private const string Sources = "20261019145153_AddNativeModerationSourceOwners";
    private const string BeforeImports = "20261009045410_AddModerationMachineSubjectBindings";
    private const string RelativeOrder = "ck_event_resources_relative_availability_order";

    [Test]
    public async Task Source_owner_upgrade_rebuild_keeps_both_branches_resource_constraints()
    {
        using var context = CreateContext();
        Migration migration = ReadMigration(context, Sources);
        string script = Generate(context, migration.UpOperations, TargetModel(context, migration));
        string rebuild = ResourceRebuild(script);

        await Assert.That(rebuild).Contains(RelativeOrder);
        await Assert.That(rebuild).Contains("ck_event_resources_material");
        await Assert.That(rebuild).Contains("ck_event_resources_quarantine");
        await Assert.That(rebuild).Contains("\"material_version_id\"");
        await Assert.That(rebuild).Contains("\"moderation_quarantine_count\"");
        await Assert.That(script).Contains("pragma_table_xinfo('ie_event_resources')");
        await Assert.That(PreflightPrecedesMutation(script)).IsTrue();
    }

    [Test]
    public async Task Late_upstream_upgrade_guards_feature_columns_before_any_destructive_rebuild()
    {
        using var context = CreateContext();
        Migration migration = ReadMigration(context, Imports);
        string script = Generate(context, migration.UpOperations, TargetModel(context, migration));
        string preflight = Preflight(script);

        // These existing feature columns must reject the old upstream copy, not be reset to defaults.
        await Assert.That(preflight).Contains("pragma_table_xinfo('ie_event_resources')");
        await Assert.That(preflight).Contains("NOT IN (");
        await Assert.That(preflight.Contains("'material_version_id'", StringComparison.Ordinal)).IsFalse();
        await Assert.That(preflight.Contains("'moderation_quarantine_count'", StringComparison.Ordinal)).IsFalse();
        await Assert.That(ResourceRebuild(script)).Contains(RelativeOrder);
        await Assert.That(PreflightPrecedesMutation(script)).IsTrue();
    }

    [Test]
    public async Task Source_owner_downgrade_removes_only_feature_fields_and_keeps_upstream_constraint()
    {
        using var context = CreateContext();
        Migration migration = ReadMigration(context, Sources);
        string script = Generate(context, ResourceOperations(migration.DownOperations),
            TargetModel(context, ReadMigration(context, Imports)));
        string rebuild = ResourceRebuild(script);

        await Assert.That(rebuild).Contains(RelativeOrder);
        await Assert.That(rebuild.Contains("\"material_version_id\"", StringComparison.Ordinal)).IsFalse();
        await Assert.That(rebuild.Contains("\"moderation_quarantine_count\"", StringComparison.Ordinal)).IsFalse();
        await Assert.That(Preflight(script)).Contains("'material_version_id'");
        await Assert.That(Preflight(script)).Contains("'moderation_quarantine_count'");
        await Assert.That(PreflightPrecedesMutation(script)).IsTrue();
    }

    [Test]
    public async Task Upstream_downgrade_does_not_reintroduce_intentionally_removed_constraint_or_feature_fields()
    {
        using var context = CreateContext();
        Migration migration = ReadMigration(context, Imports);
        string script = Generate(context, migration.DownOperations,
            TargetModel(context, ReadMigration(context, BeforeImports)));
        string rebuild = ResourceRebuild(script);

        await Assert.That(rebuild.Contains(RelativeOrder, StringComparison.Ordinal)).IsFalse();
        await Assert.That(rebuild.Contains("\"material_version_id\"", StringComparison.Ordinal)).IsFalse();
        await Assert.That(rebuild.Contains("\"moderation_quarantine_count\"", StringComparison.Ordinal)).IsFalse();
        await Assert.That(PreflightPrecedesMutation(script)).IsTrue();
    }

    [Test]
    public async Task Explicit_merged_constraint_correction_copies_existing_feature_values_instead_of_defaults()
    {
        using var context = CreateContext();
        IModel merged = context.GetService<IDesignTimeModel>().Model;
        var table = merged.GetRelationalModel().FindTable("ie_event_resources", null)!;
        var constraint = table.CheckConstraints.Single(check => check.Name == RelativeOrder);
        string script = Generate(context,
            [new AddCheckConstraintOperation { Name = RelativeOrder, Table = table.Name, Sql = constraint.Sql }], merged);
        int copy = script.IndexOf("INSERT INTO \"ef_temp_ie_event_resources\"", StringComparison.Ordinal);
        if (copy < 0) throw new InvalidOperationException("Expected native resource evidence copy was not generated.");
        string select = script[(script.IndexOf("SELECT ", copy, StringComparison.Ordinal))..script.IndexOf(';', copy)];

        await Assert.That(select).Contains("\"material_version_id\"");
        await Assert.That(select).Contains("\"moderation_quarantine_count\"");
        await Assert.That(Preflight(script)).Contains("'material_version_id'");
        await Assert.That(Preflight(script)).Contains("'moderation_quarantine_count'");
        await Assert.That(ResourceRebuild(script)).Contains(RelativeOrder);
    }

    [Test]
    public async Task Partial_or_unknown_resource_transitions_fail_before_SQL_is_returned()
    {
        using var context = CreateContext();
        Migration migration = ReadMigration(context, Sources);
        var operations = migration.UpOperations.Where(operation => operation is not AddColumnOperation
        { Table: "ie_event_resources", Name: "material_version_id" }).ToArray();

        await Assert.That(() => Generate(context, operations, TargetModel(context, migration)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Missing_historical_target_is_rejected_instead_of_using_the_current_model_for_everything()
    {
        using var context = CreateContext();
        Migration migration = ReadMigration(context, Sources);

        await Assert.That(() => context.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Unrelated_SQL_does_not_require_a_resource_target_or_emit_a_schema_preflight()
    {
        using var context = CreateContext();
        string script = string.Join('\n', context.GetService<IMigrationsSqlGenerator>()
            .Generate([new SqlOperation { Sql = "SELECT 1;" }]).Select(command => command.CommandText));

        await Assert.That(script.Trim()).IsEqualTo("SELECT 1;");
    }

    [Test]
    public async Task Repeated_generation_is_stable_and_does_not_mutate_historical_models()
    {
        using var context = CreateContext();
        Migration migration = ReadMigration(context, Sources);
        IModel target = TargetModel(context, migration);
        string first = Generate(context, migration.UpOperations, target);

        await Assert.That(Generate(context, migration.UpOperations, target)).IsEqualTo(first);
        await Assert.That(target.FindEntityType(typeof(EventResource).FullName!)!.GetCheckConstraints()
            .Any(check => check.Name == RelativeOrder)).IsFalse();
    }

    private static ExploreDbContext CreateContext()
    {
        using var provider = ExploreDbContextModelProviderTests.CreateContext("Sqlite");
        var options = TestDbContextOptions.Create((DbContextOptions<ExploreDbContext>)provider.GetService<IDbContextOptions>());
        options.UseSqlite(provider.Database.GetConnectionString(),
            sqlite => sqlite.MigrationsAssembly("Explore.Persistence.Migrations.Sqlite"));
        options.ReplaceService<IMigrationsSqlGenerator, ConfigurableSqliteMigrationsSqlGenerator>();
        return new ExploreDbContext(options.Options);
    }

    private static Migration ReadMigration(ExploreDbContext context, string id)
    {
        IMigrationsAssembly assembly = context.GetService<IMigrationsAssembly>();
        return assembly.CreateMigration(assembly.Migrations[id], context.Database.ProviderName!);
    }

    private static IModel TargetModel(ExploreDbContext context, Migration migration) =>
        context.GetService<IModelRuntimeInitializer>().Initialize(migration.TargetModel, designTime: true);

    private static string Generate(ExploreDbContext context, IReadOnlyList<MigrationOperation> operations, IModel model) =>
        string.Join('\n', context.GetService<IMigrationsSqlGenerator>().Generate(operations, model)
            .Select(command => command.CommandText));

    private static MigrationOperation[] ResourceOperations(IReadOnlyList<MigrationOperation> operations) =>
        operations.Where(operation => operation switch
        {
            ColumnOperation column => column.Table == "ie_event_resources",
            DropColumnOperation column => column.Table == "ie_event_resources",
            AddCheckConstraintOperation check => check.Table == "ie_event_resources",
            DropCheckConstraintOperation check => check.Table == "ie_event_resources",
            _ => false
        }).ToArray();

    private static string ResourceRebuild(string script)
    {
        int start = script.IndexOf("CREATE TABLE \"ef_temp_ie_event_resources\"", StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Expected native SQLite resource rebuild was not generated.");
        return script[start..script.IndexOf(';', start)];
    }

    private static string Preflight(string script)
    {
        int start = script.IndexOf("INSERT INTO \"ie_event_resources_merge_guard\"", StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Expected resource schema preflight was not generated.");
        return script[start..script.IndexOf(';', start)];
    }

    private static bool PreflightPrecedesMutation(string script)
    {
        int validation = script.IndexOf(Preflight(script), StringComparison.Ordinal);
        int mutation = script.IndexOf("CREATE TABLE \"ef_temp_", StringComparison.Ordinal);
        int pragma = script.IndexOf("PRAGMA foreign_keys = OFF", StringComparison.OrdinalIgnoreCase);
        return mutation > validation && (pragma < 0 || pragma > validation);
    }
}
