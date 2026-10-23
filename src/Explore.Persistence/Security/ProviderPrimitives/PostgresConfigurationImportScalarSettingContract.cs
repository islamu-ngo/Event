using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Writes only ordinary catalog-admitted instance settings on the sealed Global import
/// transaction. Specialized policy boundaries and post-commit effects remain with the caller.
/// No setting aggregate is read, tracked, or synthesized by this capability.
/// </summary>
public static partial class PostgresConfigurationImportScalarSettingContract
{
    public const string OwnerRole = "event_configuration_import_scalar_owner";
    public const string MigratorRole = "event_configuration_import_scalar_migrator";
    public const string GlobalRole = "event_configuration_import_scalar_global";

    /// <summary>
    /// Returns the previous stored value, including when the supplied value is unchanged.
    /// Updates preserve the persisted Id, CreatedAt and CreatedBy.
    /// </summary>
    public static async Task<string?> UpsertAsync(
        ExploreDbContext context, SystemSetting setting, CancellationToken ct)
    {
        DbTransaction transaction = ValidateOwner(context);
        ArgumentNullException.ThrowIfNull(setting);
        ConfigurationManifestSettingCatalogEntry entry = OrdinaryEntries()
            .SingleOrDefault(candidate => candidate.Definition.Key == setting.SettingKey)
            ?? throw new ArgumentException("The scalar import key is not admitted.", nameof(setting));
        SettingDefinition definition = entry.Definition;
        if (setting.ValueType != definition.ValueType
            || setting.Description != definition.Description || setting.Category != definition.Category
            || setting.IsLocked || setting.AllowedValues is not null || setting.DisplayOrder != 0
            || setting.Id != Guid.Empty
            || setting.CreatedBy is null || setting.CreatedBy == Guid.Empty
            || setting.UpdatedBy != setting.CreatedBy
            || setting.CreatedAt == default || setting.CreatedAt.Kind != DateTimeKind.Utc
            || setting.UpdatedAt != setting.CreatedAt || setting.UpdatedAt?.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Scalar import metadata must match the ordinary registry mutation.", nameof(setting));
        ValidateValue(entry, setting.Value);

        DbConnection connection = context.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {FunctionSql(context, "upsert")}(
                @key, @value, @value_type, @description, @category, @locked,
                @allowed, @display_order, @id, @actor, @created, @updated)
            """;
        command.Parameters.Add(Parameter("key", NpgsqlDbType.Text, setting.SettingKey));
        command.Parameters.Add(Parameter("value", NpgsqlDbType.Text, setting.Value));
        command.Parameters.Add(Parameter("value_type", NpgsqlDbType.Integer, setting.SettingValueTypeId));
        command.Parameters.Add(Parameter("description", NpgsqlDbType.Text, setting.Description));
        command.Parameters.Add(Parameter("category", NpgsqlDbType.Text, setting.Category));
        command.Parameters.Add(Parameter("locked", NpgsqlDbType.Boolean, setting.IsLocked));
        command.Parameters.Add(Parameter("allowed", NpgsqlDbType.Jsonb, setting.AllowedValues));
        command.Parameters.Add(Parameter("display_order", NpgsqlDbType.Integer, setting.DisplayOrder));
        command.Parameters.Add(Parameter("id", NpgsqlDbType.Uuid, Guid.CreateVersion7()));
        command.Parameters.Add(Parameter("actor", NpgsqlDbType.Uuid, setting.CreatedBy));
        command.Parameters.Add(Parameter("created", NpgsqlDbType.Text, setting.CreatedAt.ToString("O", CultureInfo.InvariantCulture)));
        command.Parameters.Add(Parameter("updated", NpgsqlDbType.Text, setting.UpdatedAt!.Value.ToString("O", CultureInfo.InvariantCulture)));
        if (context.Database.GetCommandTimeout() is { } timeout)
            command.CommandTimeout = timeout;
        object? previous = await command.ExecuteScalarAsync(ct);
        if (!ReferenceEquals(transaction, ValidateOwner(context))
            || !ReferenceEquals(connection, context.Database.GetDbConnection()))
            throw new InvalidOperationException("Scalar import must retain its original owner connection and transaction.");
        return previous is null or DBNull ? null : (string)previous;
    }

    private static DbTransaction ValidateOwner(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || !context.IsConfigurationInstanceImportOwner
            || context.TenantFilterTenantId is not null || context.Database.CurrentTransaction is null
            || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new InvalidOperationException("Scalar import requires the sealed tenant-null Global owner and its active transaction.");
        DbTransaction transaction = context.Database.CurrentTransaction.GetDbTransaction();
        if (!ReferenceEquals(transaction.Connection, context.Database.GetDbConnection()))
            throw new InvalidOperationException("Scalar import transaction connection changed.");
        return transaction;
    }

    private static ConfigurationManifestSettingCatalogEntry[] OrdinaryEntries() =>
        ConfigurationManifestCatalog.InstanceSettings.Values
            .Where(entry => !entry.Definition.RequiresCoordinatedMutation
                && !entry.Definition.IsSensitive
                && entry.Definition.MinScope <= SettingScope.Instance
                && entry.Definition.MaxScope >= SettingScope.Instance
                && !PublicationPolicySettingKeys.All.Contains(entry.Definition.Key, StringComparer.Ordinal)
                && !EventResourceSettingMutationGuard.Handles(entry.Definition.Key)
                && entry.Definition.Key != EventResourceProviderBindingDocument.SettingKey
                && !VisitorAccessSettingMutationGuard.Handles(entry.Definition.Key)
                && !EmailDeliverySettingKeys.Contains(entry.Definition.Key)
                && !InstanceOperatorIdentitySettingKeys.Contains(entry.Definition.Key)
                && entry.Definition.Key is not (GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations
                    or GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress
                    or GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates
                    or GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience
                    or GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset))
            .OrderBy(entry => entry.Definition.Key, StringComparer.Ordinal).ToArray();

    private static void ValidateValue(ConfigurationManifestSettingCatalogEntry entry, string value)
    {
        using JsonDocument document = JsonDocument.Parse(value);
        JsonElement scalar = document.RootElement;
        bool valid = entry.Definition.ValueType switch
        {
            SettingValueType.Boolean => scalar.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SettingValueType.String => scalar.ValueKind == JsonValueKind.String
                && (entry.MaximumStringLength is not { } maximum || scalar.GetString()!.Length <= maximum)
                && (entry.Definition.AllowedValues is null
                    || entry.Definition.AllowedValues.Contains(scalar.GetString()!, StringComparer.Ordinal)),
            _ => false
        };
        if (!valid)
            throw new ArgumentException("Scalar import value does not match its catalog definition.", nameof(value));
    }

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };
}
