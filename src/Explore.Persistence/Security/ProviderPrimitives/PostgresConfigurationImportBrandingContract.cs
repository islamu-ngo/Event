using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed branding create/CAS update authority on the sealed Global import owner's
/// existing serializable transaction. Reads use the existing catalog snapshot authority.
/// </summary>
public static partial class PostgresConfigurationImportBrandingContract
{
    public const string OwnerRole = "event_configuration_import_branding_owner";
    public const string MigratorRole = "event_configuration_import_branding_migrator";
    public const string GlobalRole = "event_configuration_import_branding_global";

    internal static void ValidateShape(int schemaVersion, string defaultsVersion, string payloadJson)
    {
        ValidateCatalog();
        if (schemaVersion != TenantBrandingSettingsDocumentDefaults.SchemaVersion
            || defaultsVersion != TenantBrandingSettingsDocumentDefaults.DefaultsVersion)
            throw new ArgumentException("Branding import versions must match the manifest catalog.");
        using JsonDocument payload = JsonDocument.Parse(payloadJson);
        if (payload.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Branding import payload must be an object.", nameof(payloadJson));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in payload.RootElement.EnumerateObject())
        {
            if (!names.Add(property.Name)
                || property.Name is not ("displayName" or "logoUrl" or "faviconUrl" or "customCssUrl")
                || property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new ArgumentException("Branding import payload does not match the catalog shape.", nameof(payloadJson));
            if (property.Value.ValueKind == JsonValueKind.Null)
                continue;
            string value = property.Value.GetString()!;
            if (value.Length > (property.Name == "displayName" ? 200 : 2048)
                || property.Name != "displayName" && value.Length > 0
                    && (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                        || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                        || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
                        || !string.IsNullOrEmpty(uri.Fragment)))
                throw new ArgumentException("Branding import payload contains an invalid value.", nameof(payloadJson));
        }
    }

    private static void ValidateCatalog()
    {
        if (!ConfigurationManifestCatalog.TenantDocuments.TryGetValue(
                SettingsDocumentKeys.Tenant.Branding, out var entry)
            || entry.Scope != ConfigurationManifestScope.Tenant
            || entry.DocumentKey != SettingsDocumentKeys.Tenant.Branding
            || entry.SchemaVersion != TenantBrandingSettingsDocumentDefaults.SchemaVersion
            || entry.DefaultsVersion != TenantBrandingSettingsDocumentDefaults.DefaultsVersion
            || entry.PayloadType != typeof(BrandingSettings)
            || entry.Storage != ConfigurationManifestDocumentStorage.TenantSettingsDocument)
            throw new InvalidOperationException("The branding import authority does not match the manifest registry.");
    }

    public static async Task<TenantSettingsDocument> WriteAsync(
        ExploreDbContext context, Guid tenantId, int schemaVersion, string defaultsVersion,
        string payloadJson, Guid actorUserId, DateTime occurredAt, CancellationToken ct = default)
    {
        DbTransaction transaction = ValidateOwner(context);
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(actorUserId, Guid.Empty);
        if (occurredAt == default || occurredAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A nondefault UTC timestamp is required.", nameof(occurredAt));
        ValidateShape(schemaVersion, defaultsVersion, payloadJson);
        if (context.ChangeTracker.Entries<TenantSettingsDocument>().Any(entry =>
                entry.Entity.TenantId == tenantId && entry.Entity.DocumentKey == SettingsDocumentKeys.Tenant.Branding
                && entry.State != EntityState.Unchanged))
            throw new InvalidOperationException("Branding import cannot replace a pending tracked mutation.");

        TenantSettingsDocument? current = (await PostgresConfigurationImportSnapshotContract.ReadTenantDocumentsAsync(
            context, tenantId, [SettingsDocumentKeys.Tenant.Branding], ct)).SingleOrDefault();
        Guid? expectedStamp = current?.ConcurrencyStamp;
        if (current is null)
        {
            current = TenantSettingsDocument.Create(
                tenantId, SettingsDocumentKeys.Tenant.Branding, schemaVersion, defaultsVersion, payloadJson);
            current.Id = Guid.CreateVersion7();
        }
        else
        {
            if (current.SchemaVersion != schemaVersion || current.DefaultsVersion != defaultsVersion)
                throw new InvalidOperationException("Persisted branding versions do not match the manifest registry.");
            current.UpdatePayload(schemaVersion, defaultsVersion, payloadJson);
        }
        Guid nextStamp = Guid.CreateVersion7();
        // Npgsql persists microseconds; all fidelity reads use that same persisted instant.
        DateTime instant = new(occurredAt.Ticks - occurredAt.Ticks % 10, DateTimeKind.Utc);
        DbConnection connection = context.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {FunctionSql(context, "write")}(
                @tenant, @id, @expected, @stamp, @key, @schema, @defaults, @payload, @actor, @at)
            """;
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenantId });
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = current.Id });
        command.Parameters.Add(new NpgsqlParameter("expected", NpgsqlDbType.Uuid)
        { Value = expectedStamp is { } stamp ? stamp : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("stamp", NpgsqlDbType.Uuid) { Value = nextStamp });
        command.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Text) { Value = current.DocumentKey });
        command.Parameters.Add(new NpgsqlParameter("schema", NpgsqlDbType.Integer) { Value = schemaVersion });
        command.Parameters.Add(new NpgsqlParameter("defaults", NpgsqlDbType.Text) { Value = defaultsVersion });
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = current.PayloadJson });
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = actorUserId });
        command.Parameters.Add(new NpgsqlParameter("at", NpgsqlDbType.TimestampTz) { Value = instant });
        if (context.Database.GetCommandTimeout() is { } timeout)
            command.CommandTimeout = timeout;
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new DbUpdateConcurrencyException("Branding changed since the import snapshot was read.");
        if (!ReferenceEquals(transaction, ValidateOwner(context))
            || !ReferenceEquals(connection, context.Database.GetDbConnection()))
            throw new InvalidOperationException("Branding import must retain its original owner connection and transaction.");

        TenantSettingsDocument persisted = (await PostgresConfigurationImportSnapshotContract.ReadTenantDocumentsAsync(
            context, tenantId, [SettingsDocumentKeys.Tenant.Branding], ct)).Single();
        foreach (var entry in context.ChangeTracker.Entries<TenantSettingsDocument>()
                     .Where(entry => entry.Entity.TenantId == tenantId
                         && entry.Entity.DocumentKey == SettingsDocumentKeys.Tenant.Branding))
        {
            entry.CurrentValues.SetValues(persisted);
            entry.State = EntityState.Unchanged;
        }
        return persisted;
    }

    private static DbTransaction ValidateOwner(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || !context.IsConfigurationInstanceImportOwner
            || context.TenantFilterTenantId is not null || context.Database.CurrentTransaction is null
            || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new InvalidOperationException(
                "Branding import requires the sealed tenant-null Global owner and its active transaction.");
        DbTransaction transaction = context.Database.CurrentTransaction.GetDbTransaction();
        if (!ReferenceEquals(transaction.Connection, context.Database.GetDbConnection())
            || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Branding import requires its serializable owner connection.");
        return transaction;
    }
}
