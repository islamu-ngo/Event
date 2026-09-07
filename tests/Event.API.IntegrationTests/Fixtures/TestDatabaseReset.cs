// Preserves lookup tables and migration history; resets all other data to a clean state.

using Npgsql;
using Respawn;
using Respawn.Graph;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Deterministic database reset using Respawn. Preserves seed/lookup data,
/// clears all transactional data between tests.
/// </summary>
public sealed class TestDatabaseReset
{
    private const string DefaultApplicationSchema = "islamu_event";
    private readonly Respawner _respawner;
    private readonly string _connectionString;

    private TestDatabaseReset(Respawner respawner, string connectionString)
    {
        _respawner = respawner;
        _connectionString = connectionString;
    }

    /// <summary>
    /// Creates a new <see cref="TestDatabaseReset"/> initialized against the target database.
    /// Must be called after migrations and lookup table seeding are complete.
    /// </summary>
    public static async Task<TestDatabaseReset> CreateAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        string applicationSchema = GetApplicationSchema(connectionString);

        var respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = [applicationSchema],
            TablesToIgnore = CreateLookupTables(applicationSchema),
        });

        return new TestDatabaseReset(respawner, connectionString);
    }

    /// <summary>
    /// Resets the database to its post-migration, post-seed state.
    /// Call at the start of each test for deterministic isolation.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    /// <summary>
    /// Tables preserved across resets: migration history and all lookup/seed tables.
    /// Derived from LookupTableSeeder entity types with snake_case naming convention.
    /// </summary>
    private static Table[] CreateLookupTables(string schema) => UnqualifiedLookupTables
        .Select(table => new Table(table.Name, schema))
        .ToArray();

    private static string GetApplicationSchema(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString).SearchPath?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
        ?? DefaultApplicationSchema;

    private static readonly Table[] UnqualifiedLookupTables =
    [
        new("__EFMigrationsHistory"),
        new("account_authority_kinds"),
        new("ai_conversation_statuses"),
        new("ai_message_roles"),
        new("ai_proposed_action_kinds"),
        new("ai_proposed_action_statuses"),
        new("ai_reference_kinds"),
        new("ai_run_statuses"),
        new("actor_types"),
        new("analytics_providers"),
        new("approval_statuses"),
        new("audience_ages"),
        new("audience_genders"),
        new("did_custody_types"),
        new("event_formats"),
        new("event_registration_policies"),
        new("event_session_kinds"),
        new("event_session_statuses"),
        new("event_statuses"),
        new("event_types"),
        new("external_api_key_credit_periods"),
        new("external_api_key_owner_types"),
        new("external_api_key_statuses"),
        new("external_workflow_provider_kinds"),
        new("file_types"),
        new("group_positions"),
        new("languages"),
        new("madhabs"),
        new("module_definitions"),
        new("notification_categories"),
        new("notification_delivery_policies"),
        new("notification_delivery_statuses"),
        new("notification_entity_types"),
        new("notification_external_delegation_statuses"),
        new("notification_intent_statuses"),
        new("notification_ownership_types"),
        new("notification_preference_categories"),
        new("notification_preference_channels"),
        new("notification_recipient_kinds"),
        new("notification_reasons"),
        new("notification_scope_types"),
        new("notification_types"),
        new("organization_positions"),
        new("permissions"),
        new("registration_modes"),
        new("registration_scopes"),
        new("role_scopes"),
        new("roles"),
        new("role_permissions"),
        new("schedule_item_kinds"),
        new("secret_source_types"),
        new("secret_validation_statuses"),
        new("setting_scopes"),
        new("setting_value_types"),
        new("system_settings"),
        new("tag_types"),
        new("tenant_footer_link_groups"),
        new("tenant_footer_links"),
        new("tenant_statuses"),
        new("ui_theme_presets"),
        new("visibility_types"),
    ];
}
