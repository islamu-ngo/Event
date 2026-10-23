using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Append-only instance import receipts and their fresh deferred effects on the sealed
/// Global owner. Successful writes require the caller's serializable transaction.
/// Failure evidence alone may open a transaction after that transaction was rolled back.
/// </summary>
public static partial class PostgresConfigurationImportReceiptContract
{
    public const string OwnerRole = "event_configuration_import_receipt_owner";
    public const string MigratorRole = "event_configuration_import_receipt_migrator";
    public const string GlobalRole = "event_configuration_import_receipt_global";

    private const string EmptyDigest =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string SafeFailureReason =
        "Configuration import failed and no selected section was committed.";
    private static readonly ConditionalWeakTable<ExploreDbContext, Dictionary<Guid, StoredReceipt>> Receipts = new();

    public static async Task StoreOperationAsync(
        ExploreDbContext context, ConfigurationImportOperation operation, CancellationToken ct)
    {
        ValidateOwner(context);
        ArgumentNullException.ThrowIfNull(operation);
        ValidateOperation(context, operation);
        bool failed = operation.Status == ConfigurationImportOperationStatus.Failed;
        IDbContextTransaction? owned = null;
        if (context.Database.CurrentTransaction is null)
        {
            if (!failed)
                throw new InvalidOperationException("Successful import receipts require the existing owner transaction.");
            owned = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        }
        await using (owned)
        {
            DbTransaction transaction = Transaction(context);
            await ExecuteAsync(context, transaction, "store_operation", MappedValues(context, operation), ct);
            if (owned is not null)
                await owned.CommitAsync(ct);
            // Accept only the entity written here, never unrelated application state.
            // Failed writes throw before this point and retain their original EF state.
            context.Entry(operation).State = EntityState.Unchanged;
            if (!failed)
                Receipts.GetOrCreateValue(context)[operation.Id] = new StoredReceipt(
                    context.Database.GetDbConnection(), transaction, operation);
        }
    }

    public static async Task StoreEffectAsync(
        ExploreDbContext context, OutboxMessage message, CancellationToken ct)
    {
        DbTransaction transaction = Transaction(context);
        ArgumentNullException.ThrowIfNull(message);
        if (!Receipts.TryGetValue(context, out Dictionary<Guid, StoredReceipt>? receipts)
            || !receipts.TryGetValue(message.AggregateId, out StoredReceipt? stored)
            || !ReferenceEquals(stored.Connection, context.Database.GetDbConnection())
            || !ReferenceEquals(stored.Transaction, transaction))
            throw new InvalidOperationException("Import effects require a receipt inserted on this owner transaction.");
        ConfigurationImportOperation receipt = stored.Operation;
        RequireVersion7(message.Id);
        RequireUtc(message.CreatedAt);
        if (message.Id != receipt.EffectOutboxId || message.AggregateId != receipt.Id
            || message.AggregateType != ConfigurationImportEffectOutbox.AggregateType
            || message.EventType != ConfigurationImportEffectOutbox.EventType
            || message.Status != OutboxMessageStatus.Pending || message.MaxRetries != 5
            || message.RetryCount != 0 || message.LastError is not null || message.NextRetryAt is not null
            || message.ProcessedAt is not null || message.DeadLetteredAt is not null
            || receipt.CompletedAt is not { } completed || Precision(message.CreatedAt) != Precision(completed))
            throw new ArgumentException("Only the fresh terminal receipt effect can be inserted.", nameof(message));

        ValidateNotifications(receipt, message.Payload);
        if (receipt.EffectPayloadDigest != Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(message.Payload ?? string.Empty))))
            throw new ArgumentException("The effect bytes do not match the committed receipt.", nameof(message));
        Dictionary<string, object?> values = MappedValues(context, message);
        await ExecuteAsync(context, transaction, "store_effect", values, ct);
        context.Entry(message).State = EntityState.Unchanged;
        receipts.Remove(receipt.Id);
    }

    public static async Task<ConfigurationImportOperation?> ReadOperationAsync(
        ExploreDbContext context, Guid operationId, string targetAuthorityKey, CancellationToken ct)
    {
        ValidateOwner(context);
        RequireAuthority(targetAuthorityKey);
        RequireVersion7(operationId);
        List<ConfigurationImportOperation> rows = await context.Set<ConfigurationImportOperation>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "read_operation")}(@id, @authority)",
                Parameter("id", NpgsqlDbType.Uuid, operationId),
                Parameter("authority", NpgsqlDbType.Text, targetAuthorityKey))
            .IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking().ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    public static async Task<IReadOnlyList<ConfigurationImportOperation>> ListOperationsAsync(
        ExploreDbContext context, string targetAuthorityKey, int maximumCount, CancellationToken ct)
    {
        ValidateOwner(context);
        RequireAuthority(targetAuthorityKey);
        if (maximumCount is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        return await context.Set<ConfigurationImportOperation>()
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "list_operations")}(@authority, @maximum)",
                Parameter("authority", NpgsqlDbType.Text, targetAuthorityKey),
                Parameter("maximum", NpgsqlDbType.Integer, maximumCount))
            .IgnoreQueryFilters([QueryFilterNames.Tenant]).AsNoTracking().ToListAsync(ct);
    }

    public static async Task<OutboxMessage?> ReadEffectAsync(
        ExploreDbContext context, Guid messageId, CancellationToken ct)
    {
        ValidateOwner(context);
        RequireVersion7(messageId);
        return await context.OutboxMessages
            .FromSqlRaw($"SELECT * FROM {FunctionSql(context, "read_effect")}(@id)",
                Parameter("id", NpgsqlDbType.Uuid, messageId))
            .AsNoTracking().SingleOrDefaultAsync(ct);
    }

    private static void ValidateOperation(ExploreDbContext context, ConfigurationImportOperation operation)
    {
        RequireAuthority(operation.TargetAuthorityKey);
        if (operation.TargetTenantId is not null || operation.CompletedAt is null
            || operation.ApplyMode is < 1 or > 4
            || operation.SelectedSectionKeys.Any(section =>
                !ConfigurationPortabilityRegistry.Sections.TryGetValue(section, out var descriptor)
                || !descriptor.SupportsApply))
            throw new ArgumentException("The receipt is not a terminal instance import.");

        ConfigurationImportOperation canonical;
        if (operation.Status == ConfigurationImportOperationStatus.Failed)
        {
            if (operation.FailureReason != SafeFailureReason
                || operation.FailureCode is not { Length: > 0 and <= 200 } code
                || code.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
                throw new ArgumentException("Only sanitized application failure evidence is accepted.");
            canonical = ConfigurationImportOperation.CreateFailed(
                operation.Id, operation.SessionId, operation.TargetAuthorityKey, operation.TargetTenantId,
                operation.ActorUserId, operation.SourceOperationId, operation.ArtifactDigest, operation.ApplyMode,
                operation.SelectedSectionKeys, operation.FailureCode, operation.FailureReason,
                operation.StartedAt, operation.CompletedAt.Value);
        }
        else
        {
            if (operation.Status is not (ConfigurationImportOperationStatus.Applied or ConfigurationImportOperationStatus.RolledBack)
                || operation.SnapshotArtifactHandleId is null || operation.SnapshotExpiresAt is null
                || operation.EffectOutboxId is null
                || operation.EffectPayloadDigest is not { Length: 64 } effectDigest
                || !effectDigest.All(char.IsAsciiHexDigitUpper)
                || operation.SnapshotExpiresAt <= operation.CompletedAt
                || operation.SelectedSectionsDigest != ConfigurationImportDigest.Compute(operation.SelectedSectionKeys))
                throw new ArgumentException("Successful receipts require complete snapshot, selection and effect evidence.");
            canonical = ConfigurationImportOperation.CreateApplied(
                operation.Id, operation.SessionId, operation.TargetAuthorityKey, operation.TargetTenantId,
                operation.ActorUserId, operation.SourceOperationId, operation.ArtifactDigest,
                operation.TargetRevisionDigest, operation.SelectedSectionsDigest, operation.MappingDigest,
                operation.ApprovalDigest, operation.ApplyMode, operation.SelectedSectionKeys,
                operation.SnapshotArtifactHandleId.Value, operation.SnapshotDigest!,
                operation.SnapshotExpiresAt.Value, operation.EffectOutboxId.Value,
                operation.FidelityVerified, operation.FidelityDigest, operation.OmittedSectionKeys,
                operation.StartedAt, operation.CompletedAt.Value);
        }
        Dictionary<string, object?> expected = MappedValues(context, canonical);
        // The native effect writer proves this captured digest against the original bytes.
        if (operation.Status != ConfigurationImportOperationStatus.Failed)
            expected[ColumnName(context, typeof(ConfigurationImportOperation),
                nameof(ConfigurationImportOperation.EffectPayloadDigest))] = operation.EffectPayloadDigest;
        if (MappedValues(context, operation).Any(pair => !Equals(pair.Value, expected[pair.Key])))
            throw new ArgumentException("Receipt fields must exactly match the Domain terminal shape.");
    }

    private static void ValidateNotifications(ConfigurationImportOperation receipt, string? payload)
    {
        if (payload is null)
            return;
        if (Encoding.UTF8.GetByteCount(payload) > ConfigurationImportSessionLimits.MaximumArtifactBytes)
            throw new ArgumentException("Import notification payload exceeds the artifact bound.");
        using JsonDocument document = JsonDocument.Parse(payload);
        var notifications = ConfigurationImportEffectOutbox.ReadNotifications(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Array
            || notifications.Length > MaximumNotifications
            || notifications.Length != document.RootElement.GetArrayLength())
            throw new ArgumentException("Invalid import notification array.");
        var identities = new HashSet<(string Key, Guid? Tenant)>();
        for (int index = 0; index < notifications.Length; index++)
        {
            SettingChangedNotification notification = notifications[index];
            JsonElement input = document.RootElement[index];
            if (notification is null || input.ValueKind != JsonValueKind.Object
                || !input.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(NotificationFields.Order(StringComparer.Ordinal))
                || notification.IsSensitive || input.GetProperty("IsSensitive").ValueKind != JsonValueKind.False
                || notification.ActorUserId != receipt.ActorUserId
                || notification.ChangedAt.Kind != DateTimeKind.Utc
                || Precision(notification.ChangedAt) < Precision(receipt.StartedAt)
                || Precision(notification.ChangedAt) > Precision(receipt.CompletedAt!.Value)
                || !identities.Add((notification.Key, notification.TenantId)))
                throw new ArgumentException("Invalid import notification provenance.");
            bool tenant = notification.Scope == SettingSource.TenantOverride;
            if (tenant ? notification.TenantId is null || notification.TenantId == Guid.Empty
                : notification.Scope != SettingSource.SystemDefault || notification.TenantId is not null)
                throw new ArgumentException("Invalid import notification scope.");
            if (!receipt.SelectedSectionKeys.Contains(tenant ? "tenant.settings" : "instance.settings", StringComparer.Ordinal))
                throw new ArgumentException("Notification does not belong to a selected section.");
            var catalog = tenant ? ConfigurationManifestCatalog.TenantSettings : ConfigurationManifestCatalog.InstanceSettings;
            if (!catalog.TryGetValue(notification.Key, out var entry) || entry.Definition.IsSensitive)
                throw new ArgumentException("Notification key is not a public manifest setting.");
            ValidatePublicValue(entry, notification.OldValue);
            ValidatePublicValue(entry, notification.NewValue);
        }
    }

    private static void ValidatePublicValue(ConfigurationManifestSettingCatalogEntry entry, string? value)
    {
        if (value is null)
            return;
        using JsonDocument document = JsonDocument.Parse(value);
        JsonElement scalar = document.RootElement;
        bool valid = entry.Definition.ValueType switch
        {
            SettingValueType.Boolean => scalar.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SettingValueType.String => scalar.ValueKind == JsonValueKind.String
                && (entry.MaximumStringLength is not { } maximum || scalar.GetString()!.Length <= maximum)
                && (entry.Definition.AllowedValues is null
                    || entry.Definition.AllowedValues.Contains(scalar.GetString()!, StringComparer.Ordinal)),
            SettingValueType.Integer => scalar.ValueKind == JsonValueKind.Number && scalar.TryGetInt32(out _),
            SettingValueType.Long => scalar.ValueKind == JsonValueKind.Number && scalar.TryGetInt64(out _),
            SettingValueType.Json => entry.StringArray is { } array && scalar.ValueKind == JsonValueKind.Array
                && scalar.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String
                    && (array.AllowedValues is null
                        || array.AllowedValues.Value.Contains(item.GetString()!, StringComparer.Ordinal))),
            _ => false
        };
        if (!valid)
            throw new ArgumentException("Notification value does not match its public catalog type.");
    }

    private static async Task ExecuteAsync(
        ExploreDbContext context, DbTransaction transaction, string function,
        Dictionary<string, object?> values, CancellationToken ct)
    {
        DbConnection connection = context.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {FunctionSql(context, function)}(@value)";
        command.Parameters.Add(Parameter("value", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(values)));
        if (context.Database.GetCommandTimeout() is { } timeout)
            command.CommandTimeout = timeout;
        await command.ExecuteNonQueryAsync(ct);
        if (!ReferenceEquals(transaction, Transaction(context))
            || !ReferenceEquals(connection, context.Database.GetDbConnection()))
            throw new InvalidOperationException("Import receipt owner transaction changed.");
    }

    private static Dictionary<string, object?> MappedValues(ExploreDbContext context, object entity) =>
        context.Entry(entity).Properties.ToDictionary(
            property => ColumnName(context, entity.GetType(), property.Metadata.Name),
            property => property.CurrentValue is Enum value
                ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : property.CurrentValue,
            StringComparer.Ordinal);

    private static void ValidateOwner(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || !context.IsConfigurationInstanceImportOwner
            || context.TenantFilterTenantId is not null)
            throw new InvalidOperationException("Import receipts require the sealed tenant-null Global owner.");
    }

    private static DbTransaction Transaction(ExploreDbContext context)
    {
        ValidateOwner(context);
        if (context.Database.CurrentTransaction is null
            || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new InvalidOperationException("Import receipt mutation requires an open owner transaction.");
        DbTransaction transaction = context.Database.CurrentTransaction.GetDbTransaction();
        if (!ReferenceEquals(transaction.Connection, context.Database.GetDbConnection())
            || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Import receipt mutation requires the same serializable owner transaction.");
        return transaction;
    }

    private static void RequireAuthority(string value)
    {
        if (value != "instance")
            throw new ArgumentException("Only exact instance receipt authority is admitted.", nameof(value));
    }

    private static void RequireVersion7(Guid value)
    {
        if (value == Guid.Empty || value.Version != 7)
            throw new ArgumentException("UUIDv7 identity required.");
    }

    private static void RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.");
    }

    private static long Precision(DateTime value) => value.Ticks / 10;
    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };
    private sealed record StoredReceipt(DbConnection Connection, DbTransaction Transaction, ConfigurationImportOperation Operation);
}
