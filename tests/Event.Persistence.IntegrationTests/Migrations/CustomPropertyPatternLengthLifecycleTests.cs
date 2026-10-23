#nullable enable

using System.Data.Common;
using System.Globalization;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using TUnit.Assertions.Enums;

namespace Event.Persistence.IntegrationTests.Migrations;

[NotInParallel("CustomPropertyPatternLengthLifecycleDb")]
public sealed class CustomPropertyPatternLengthLifecycleTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task MySql_ActualGeneratedDowngradePreservesDataAndHistory(bool exportedScript) =>
        AssertLifecycleAsync(PrimaryDatabaseProvider.MySql, exportedScript);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task MariaDb_ActualGeneratedDowngradePreservesDataAndHistory(bool exportedScript) =>
        AssertLifecycleAsync(PrimaryDatabaseProvider.MariaDb, exportedScript);

    private static async Task AssertLifecycleAsync(PrimaryDatabaseProvider provider, bool exportedScript)
    {
        // Each history lifecycle owns an empty database; no synthetic history or reset SQL is needed.
        await using var fixture = new MySqlMigrationGuardFixture();
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            await fixture.CreateOptionsAsync(provider));
        var tenantIdentity = new TenantIdentity(Guid.CreateVersion7());
        await using var context = new ExploreDbContext(options.Options) { TenantContext = tenantIdentity };
        context.BindTenantLease();
        IMigrationsAssembly assembly = context.GetService<IMigrationsAssembly>();
        string[] migrations = context.Database.GetMigrations().ToArray();
        string target = migrations.Single(id => id.EndsWith(
            "_ReconcileCustomPropertyPatternLength", StringComparison.Ordinal));
        int targetIndex = Array.IndexOf(migrations, target);
        await Assert.That(targetIndex).IsGreaterThan(0);
        string predecessor = migrations[targetIndex - 1];
        string[] beforeHistory = migrations[..targetIndex];
        // This lifecycle targets the reconciliation, not subsequently added migrations.
        string[] throughTargetHistory = migrations[..(targetIndex + 1)];
        Migration generated = assembly.CreateMigration(assembly.Migrations[target], context.Database.ProviderName!);
        AlterColumnOperation[] reductions = generated.DownOperations.OfType<AlterColumnOperation>().ToArray();
        await Assert.That(reductions.Length).IsEqualTo(5);
        foreach (AlterColumnOperation reduction in reductions)
        {
            await Assert.That(reduction.Name).IsEqualTo("regex_pattern");
            await Assert.That(reduction.MaxLength).IsEqualTo(1000);
            await Assert.That(reduction.OldColumn.MaxLength).IsEqualTo(2048);
        }
        string definitionTable = context.Model.FindEntityType(typeof(CustomPropertyDefinition))!.GetTableName()!;
        await Assert.That(reductions[^1].Table).IsEqualTo(definitionTable);

        await context.Database.OpenConnectionAsync();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(predecessor);
        await AssertHistoryAsync(context, beforeHistory);
        await AssertWidthsAsync(context, reductions, 1000);

        TenantStatus status = await context.Set<TenantStatus>().FindAsync((int)TenantStatusEnum.Active)
            ?? new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active,
                MasterCode = "ACTIVE",
                FullName = "Active",
                IsActiveState = true
            };
        var tenant = new Tenant
        {
            Id = tenantIdentity.TenantId,
            Slug = "pattern-lifecycle-" + tenantIdentity.TenantId.ToString("N"),
            FullName = "Pattern migration lifecycle",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        string fitting = new('a', 1000);
        var definition = new CustomPropertyDefinition
        {
            Id = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            EntityTypeName = EntityTypeName.Organization,
            Namespace = "migration",
            Key = "preserved-pattern",
            DisplayName = "Preserved pattern",
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.Internal,
            IsActive = true,
            RegexPattern = fitting
        };
        context.Add(definition);
        await context.SaveChangesAsync();
        await AssertPatternAsync(context, definitionTable, definition.Id, fitting);

        await TransitionAsync(context, migrator, predecessor, target, exportedScript);
        await AssertHistoryAsync(context, throughTargetHistory);
        await AssertWidthsAsync(context, reductions, 2048);
        await AssertPatternAsync(context, definitionTable, definition.Id, fitting);

        string overflow = fitting + " ";
        definition.RegexPattern = overflow;
        await context.SaveChangesAsync();
        await AssertPatternAsync(context, definitionTable, definition.Id, overflow);
        DbConnection connection = context.Database.GetDbConnection();
        await ExecuteAsync(connection, "SET SESSION sql_mode = ''");
        await Assert.That(await ScalarAsync(connection, "SELECT @@SESSION.sql_mode")).IsEqualTo(string.Empty);
        if (provider == PrimaryDatabaseProvider.MariaDb)
            await ExecuteAsync(connection, "SET SESSION check_constraint_checks = OFF");

        DbException? rejection = null;
        try
        {
            await TransitionAsync(context, migrator, target, predecessor, exportedScript);
        }
        catch (DbException exception)
        {
            rejection = exception;
        }
        await Assert.That(rejection).IsNotNull();
        await Assert.That(rejection!.Message).Contains("ck_varchar_narrowing_");
        await AssertHistoryAsync(context, throughTargetHistory);
        await AssertWidthsAsync(context, reductions, 2048);
        await AssertPatternAsync(context, definitionTable, definition.Id, overflow);
        await Assert.That(await ScalarAsync(connection, "SELECT @@SESSION.sql_mode")).IsEqualTo(string.Empty);

        // Explicit test-owned correction after proving preservation; the migrator never normalizes data.
        definition.RegexPattern = fitting;
        await context.SaveChangesAsync();
        await TransitionAsync(context, migrator, target, predecessor, exportedScript);
        await AssertHistoryAsync(context, beforeHistory);
        await AssertWidthsAsync(context, reductions, 1000);
        await AssertPatternAsync(context, definitionTable, definition.Id, fitting);
        await AssertNoGuardsAsync(context, reductions);

        await TransitionAsync(context, migrator, predecessor, target, exportedScript);
        await AssertHistoryAsync(context, throughTargetHistory);
        await AssertWidthsAsync(context, reductions, 2048);
        await AssertPatternAsync(context, definitionTable, definition.Id, fitting);
        await AssertNoGuardsAsync(context, reductions);
        await Assert.That(await context.Tenants.Where(row => row.Id == tenant.Id)
            .Select(row => row.Slug).SingleAsync()).IsEqualTo(tenant.Slug);
    }

    private static Task TransitionAsync(ExploreDbContext context, IMigrator migrator,
        string from, string to, bool exportedScript) => exportedScript
        ? ExecuteAsync(context.Database.GetDbConnection(), migrator.GenerateScript(from, to))
        : migrator.MigrateAsync(to);

    private static async Task AssertHistoryAsync(ExploreDbContext context, string[] expected) =>
        await Assert.That(await context.Database.GetAppliedMigrationsAsync())
            .IsEquivalentTo(expected, CollectionOrdering.Matching);

    private static async Task AssertWidthsAsync(ExploreDbContext context,
        IEnumerable<AlterColumnOperation> reductions, long expected)
    {
        foreach (AlterColumnOperation reduction in reductions)
            await Assert.That(Convert.ToInt64(await ScalarAsync(context.Database.GetDbConnection(),
                "SELECT character_maximum_length FROM information_schema.columns "
                + "WHERE table_schema = DATABASE() AND table_name = @table AND column_name = @column",
                ("@table", reduction.Table), ("@column", reduction.Name)), CultureInfo.InvariantCulture))
                .IsEqualTo(expected);
    }

    private static async Task AssertNoGuardsAsync(ExploreDbContext context,
        IEnumerable<AlterColumnOperation> reductions)
    {
        foreach (AlterColumnOperation reduction in reductions)
            await Assert.That(Convert.ToInt32(await ScalarAsync(context.Database.GetDbConnection(),
                "SELECT COUNT(*) FROM information_schema.table_constraints "
                + "WHERE constraint_schema = DATABASE() AND table_name = @table "
                + "AND constraint_type = 'CHECK' AND constraint_name LIKE 'ck_varchar_narrowing_%'",
                ("@table", reduction.Table)), CultureInfo.InvariantCulture)).IsEqualTo(0);
    }

    private static async Task AssertPatternAsync(ExploreDbContext context, string table,
        Guid definitionId, string expected)
    {
        ISqlGenerationHelper sql = context.GetService<ISqlGenerationHelper>();
        await Assert.That(await ScalarAsync(context.Database.GetDbConnection(),
            $"SELECT HEX(regex_pattern) FROM {sql.DelimitIdentifier(table)} WHERE id = @id",
            ("@id", definitionId.ToString("D"))))
            .IsEqualTo(Convert.ToHexString(Encoding.UTF8.GetBytes(expected)));
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        return await command.ExecuteScalarAsync();
    }

    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
