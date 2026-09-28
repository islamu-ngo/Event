using Explore.Domain;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>
/// Purges application-model transactional tables, never a catalog-wide wildcard. Domain integer-key
/// tables without transactional relationships are the lookup class; RolePermission is its integer-key
/// join. Integer-key transaction children (such as EventSessionLanguage) are NOT lookups. Other contexts own
/// migration history, data protection, scheduler and erasure authority tables and are not reset here.
/// TRUNCATE RESTRICT fails atomically if an unclassified table references a purge target.
/// </summary>
public static class AgentBrowserDatabaseReset
{
    public static async Task PurgeAsync(ExploreDbContext database, CancellationToken cancellationToken)
    {
        if (!database.Database.IsNpgsql()
            || database.Database.GetDbConnection().Database != "islamu_event_agent")
            throw new InvalidOperationException("agent_browser_reset_database_mismatch");
        var sql = database.GetService<ISqlGenerationHelper>();
        var tables = database.Model.GetRelationalModel().Tables
            .Where(table => !table.EntityTypeMappings.All(mapping => IsLookup(mapping.TypeBase)))
            .OrderBy(table => table.Schema, StringComparer.Ordinal)
            .ThenBy(table => table.Name, StringComparer.Ordinal)
            .ToArray();
        if (tables.Length == 0 || tables.Any(table => string.IsNullOrWhiteSpace(table.Schema)))
            throw new InvalidOperationException("agent_browser_reset_schema_unclassified");
        string purge = "TRUNCATE TABLE " + string.Join(", ", tables.Select(table =>
            sql.DelimitIdentifier(table.Name, table.Schema))) + " RESTART IDENTITY RESTRICT";
        var presetType = database.Model.FindEntityType(typeof(UiThemePreset))!;
        string presets = sql.DelimitIdentifier(presetType.GetTableName()!, presetType.GetSchema());
        // Identifiers cannot be SQL parameters. Only EF model identifiers quoted by the provider's
        // ISqlGenerationHelper enter these commands; there are no caller-controlled SQL fragments.
        string preservePresets = "CREATE TEMP TABLE agent_reset_ui_presets ON COMMIT DROP AS SELECT * FROM "
            + presets + " WHERE tenant_id IS NULL AND is_system";
        string restorePresets = "INSERT INTO " + presets + " SELECT * FROM pg_temp.agent_reset_ui_presets";

        // Only purge + transactional defaults share this transaction. Credential provisioning MUST run later,
        // in fresh scopes, because each native credential operation owns its own transaction.
        await database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            database.ChangeTracker.Clear();
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            // This table mixes immutable instance presets with tenant-owned presets and has a tenant
            // FK. Preserve only approved instance rows inside the same transaction, including palettes
            // and audit fields; excluding the whole table would make TRUNCATE RESTRICT impossible.
            await database.Database.ExecuteSqlRawAsync(preservePresets, cancellationToken);
            await database.Database.ExecuteSqlRawAsync(purge, cancellationToken);
            await database.Database.ExecuteSqlRawAsync(restorePresets, cancellationToken);
            // Reuse native default construction without running lookup reconciliation: the general
            // seeder also updates existing presets, which must remain byte-for-byte preserved here.
            await LookupTableSeeder.SeedPlatformMonetizationDefaultsAsync(database, cancellationToken);
            await LookupTableSeeder.SeedSystemSettingsAsync(database, cancellationToken);
            await LookupTableSeeder.SeedDefaultFooterLinkGroupsAsync(database, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static bool IsLookup(ITypeBase type) => type is IEntityType entity
        && entity.ClrType.Assembly == typeof(Role).Assembly
        && (entity.ClrType == typeof(Explore.Domain.Modules.ModuleDefinition)
            || (entity.FindPrimaryKey() is { } key
                && key.Properties.All(property => property.ClrType == typeof(int))
                && entity.GetForeignKeys().All(foreignKey => foreignKey.PrincipalKey.Properties
                    .All(property => property.ClrType == typeof(int)))));
}
