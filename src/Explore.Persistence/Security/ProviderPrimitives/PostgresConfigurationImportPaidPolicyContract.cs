using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed paid-policy reads and Domain revision persistence on the sealed Global
/// import owner's existing serializable transaction. No aggregate is EF-tracked.
/// </summary>
public static partial class PostgresConfigurationImportPaidPolicyContract
{
    public const string OwnerRole = "event_configuration_import_paid_policy_owner";
    public const string MigratorRole = "event_configuration_import_paid_policy_migrator";
    public const string GlobalRole = "event_configuration_import_paid_policy_global";

    private static readonly ConditionalWeakTable<ExploreDbContext, Dictionary<string, ReadOrigin>> Reads = new();

    public static async Task<PaidEventPolicyVersion?> ReadActiveAsync(
        ExploreDbContext context, Guid? tenantId, CancellationToken ct = default)
    {
        DbTransaction transaction = ValidateOwner(context);
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A tenant cannot be empty.", nameof(tenantId));
        string scope = ScopeKey(tenantId);
        RejectTrackedPolicies(context);
        await using DbCommand command = Command(context, transaction, "read",
            Parameter("tenant", NpgsqlDbType.Uuid, tenantId));
        object? result = await command.ExecuteScalarAsync(ct);
        PaidEventPolicyVersion? policy = result is null or DBNull ? null : Restore(context, (string)result);
        if (policy is not null && policy.TenantId != tenantId)
            throw new InvalidOperationException("Paid-policy import read returned another scope.");
        Reads.GetOrCreateValue(context)[scope] = new ReadOrigin(
            context.Database.GetDbConnection(), transaction, policy, policy?.Id,
            policy?.VersionNumber, policy is null ? null : Content(policy));
        return policy;
    }

    public static async Task<PaidEventPolicyVersion[]> ReadActiveTenantPageAsync(
        ExploreDbContext context, int offset, int limit, CancellationToken ct = default)
    {
        DbTransaction transaction = ValidateOwner(context);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > IPaidEventPolicyRepository.MaximumActiveTenantPolicyPageSize)
            throw new ArgumentOutOfRangeException(nameof(limit));
        await using DbCommand command = Command(context, transaction, "page",
            Parameter("offset", NpgsqlDbType.Integer, offset),
            Parameter("limit", NpgsqlDbType.Integer, limit));
        var policies = new List<PaidEventPolicyVersion>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            PaidEventPolicyVersion policy = Restore(context, reader.GetString(0));
            if (policy.TenantId is null)
                throw new InvalidOperationException("A ceiling page must contain tenant policies.");
            policies.Add(policy);
        }
        return policies.ToArray();
    }

    /// <summary>
    /// Accepts the revision produced by the existing mutation boundary after its
    /// recorded active aggregate has been retired, or its first absent tenant policy.
    /// Native CAS retires only that original identity/version and retains all history.
    /// </summary>
    public static async Task WriteAsync(
        ExploreDbContext context, PaidEventPolicyVersion revision, CancellationToken ct = default)
    {
        DbTransaction transaction = ValidateOwner(context);
        ArgumentNullException.ThrowIfNull(revision);
        RejectTrackedPolicies(context);
        string scope = ScopeKey(revision.TenantId);
        if (!Reads.TryGetValue(context, out Dictionary<string, ReadOrigin>? reads)
            || !reads.TryGetValue(scope, out ReadOrigin? origin)
            || !ReferenceEquals(origin.Connection, context.Database.GetDbConnection())
            || !ReferenceEquals(origin.Transaction, transaction))
            throw new InvalidOperationException("Paid-policy import must read and write on the same owner transaction.");
        if (revision.Id == Guid.Empty || revision.TenantId == Guid.Empty
            || !revision.IsActive || revision.ActiveUniquenessSlot != 0
            || revision.PolicyScopeKey != scope || revision.Id == origin.Id
            || revision.VersionNumber != (origin.Version is { } version ? checked(version + 1) : 1)
            || origin.Policy is null && revision.TenantId is null
            || origin.Policy is { } current && (current.IsActive
                || current.ActiveUniquenessSlot != origin.Version
                || current.Id != origin.Id || current.VersionNumber != origin.Version
                || Content(current) != origin.Content)
            || revision.CreatedAt != default || revision.CreatedBy is not null
            || revision.UpdatedAt is not null || revision.UpdatedBy is not null)
            throw new InvalidOperationException("Only a Domain-created paid-policy revision may be imported.");

        DateTime now = DateTime.UtcNow;
        // JSON timestamps and Npgsql parameters must carry the same microsecond
        // value; PostgreSQL rounds text while Npgsql truncates .NET ticks.
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        Guid? actor = context.CurrentUserService?.UserId;
        if (actor == Guid.Empty)
            throw new InvalidOperationException("An audit actor cannot be an empty UUID.");
        revision.CreatedAt = now;
        revision.CreatedBy = actor;
        var payload = new Dictionary<string, object?>
        {
            ["root"] = MappedValues(context, revision)
        };
        foreach (var child in ChildRelations)
        {
            object[] rows = context.Entry(revision).Collection(child.Navigation).CurrentValue!
                .Cast<object>().ToArray();
            payload[child.Operation] = rows.Select(row => MappedValues(context, row)).ToArray();
        }
        await using DbCommand command = Command(context, transaction, "write",
            Parameter("tenant", NpgsqlDbType.Uuid, revision.TenantId),
            Parameter("revision", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload)),
            Parameter("expected_id", NpgsqlDbType.Uuid, origin.Id),
            Parameter("expected_version", NpgsqlDbType.Integer, origin.Version),
            Parameter("actor", NpgsqlDbType.Uuid, actor),
            Parameter("at", NpgsqlDbType.TimestampTz, now));
        await command.ExecuteNonQueryAsync(ct);
        if (origin.Policy is { } retired)
        {
            retired.UpdatedAt ??= now;
            if (actor.HasValue)
                retired.UpdatedBy = actor;
        }
        reads.Remove(scope);
    }

    private static string ScopeKey(Guid? tenant) => tenant is { } id ? $"tenant:{id:N}" : "instance";

    private static string Content(PaidEventPolicyVersion policy) => JsonSerializer.Serialize(new
    {
        policy.Id,
        policy.TenantId,
        policy.PolicyScopeKey,
        policy.VersionNumber,
        policy.IsPaymentsEnabled,
        policy.RequiresLocalVerification,
        policy.AllowedOrganizerKinds,
        policy.AllowedCurrencyCodes,
        policy.DefaultCurrencyCode,
        policy.RefundProtections,
        policy.CurrencyRiskLimits,
        policy.RequiresFirstPaidEventReview,
        policy.FarFutureReviewThresholdDays,
        policy.CreatedAt,
        policy.CreatedBy,
        policy.UpdatedAt,
        policy.UpdatedBy
    });

    private static Dictionary<string, object?> MappedValues(ExploreDbContext context, object entity) =>
        context.Entry(entity).Properties.ToDictionary(
            property => ColumnName(context, entity.GetType(), property.Metadata.Name),
            property => property.CurrentValue is Enum value
                ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : property.CurrentValue,
            StringComparer.Ordinal);

    private static PaidEventPolicyVersion Restore(ExploreDbContext context, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement data = document.RootElement;
        JsonElement root = data.GetProperty("root");
        JsonElement P(string property) => root.GetProperty(ColumnName(context, typeof(PaidEventPolicyVersion), property));
        JsonElement C(JsonElement row, Type type, string property) => row.GetProperty(ColumnName(context, type, property));
        long? Long(JsonElement row, string property)
        {
            JsonElement value = C(row, typeof(PaidEventPolicyCurrencyRiskLimitRow), property);
            return value.ValueKind == JsonValueKind.Null ? null : value.GetInt64();
        }
        int? Int(JsonElement row, string property)
        {
            JsonElement value = C(row, typeof(PaidEventPolicyCurrencyRiskLimitRow), property);
            return value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
        }
        Guid? tenant = P("TenantId").ValueKind == JsonValueKind.Null ? null : P("TenantId").GetGuid();
        if (!P("IsActive").GetBoolean() || P("ActiveUniquenessSlot").GetInt32() != 0
            || P("PolicyScopeKey").GetString() != ScopeKey(tenant))
            throw new InvalidOperationException("Paid-policy import active aggregate is invalid.");
        var policy = PaidEventPolicyVersion.RestoreActiveForPolicyEvaluation(
            P("Id").GetGuid(), tenant, P("VersionNumber").GetInt32(), P("IsPaymentsEnabled").GetBoolean(),
            data.GetProperty("kinds").EnumerateArray().Select(row =>
                (ActorTypeEnum)C(row, typeof(PaidEventPolicyAllowedOrganizerKind), "ActorTypeId").GetInt32()),
            P("RequiresLocalVerification").GetBoolean(),
            data.GetProperty("currencies").EnumerateArray().Select(row =>
                C(row, typeof(PaidEventPolicyAllowedCurrency), "CurrencyCode").GetString()!),
            P("DefaultCurrencyCode").GetString(),
            data.GetProperty("protections").EnumerateArray().Select(row =>
                (PaidEventRefundProtection)C(row, typeof(PaidEventPolicyRefundProtection), "RefundProtectionId").GetInt32()),
            data.GetProperty("risks").EnumerateArray().Select(row => PaidEventPolicyCurrencyRiskLimit.Create(
                C(row, typeof(PaidEventPolicyCurrencyRiskLimitRow), "CurrencyCode").GetString()!,
                Long(row, "PerEventSalesCeilingMinor"), Int(row, "PerEventSalesCountCeiling"),
                Long(row, "RollingOrganizerSalesCeilingMinor"), Int(row, "RollingOrganizerSalesCountCeiling"),
                Int(row, "RollingOrganizerWindowDays"), Long(row, "HighValueReviewThresholdMinor"))),
            P("RequiresFirstPaidEventReview").GetBoolean(),
            P("FarFutureReviewThresholdDays").ValueKind == JsonValueKind.Null
                ? null : P("FarFutureReviewThresholdDays").GetInt32());
        policy.CreatedAt = P("CreatedAt").GetDateTime().ToUniversalTime();
        policy.CreatedBy = P("CreatedBy").ValueKind == JsonValueKind.Null ? null : P("CreatedBy").GetGuid();
        policy.UpdatedAt = P("UpdatedAt").ValueKind == JsonValueKind.Null ? null : P("UpdatedAt").GetDateTime().ToUniversalTime();
        policy.UpdatedBy = P("UpdatedBy").ValueKind == JsonValueKind.Null ? null : P("UpdatedBy").GetGuid();
        return policy;
    }

    private static DbTransaction ValidateOwner(ExploreDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || !context.IsConfigurationInstanceImportOwner
            || context.TenantFilterTenantId is not null || context.Database.CurrentTransaction is null
            || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new InvalidOperationException("Paid-policy import requires its sealed tenant-null Global owner transaction.");
        DbTransaction transaction = context.Database.CurrentTransaction.GetDbTransaction();
        if (transaction.IsolationLevel != IsolationLevel.Serializable
            || !ReferenceEquals(transaction.Connection, context.Database.GetDbConnection()))
            throw new InvalidOperationException("Paid-policy import requires the same serializable owner connection.");
        return transaction;
    }

    private static void RejectTrackedPolicies(ExploreDbContext context)
    {
        if (context.ChangeTracker.Entries().Any(entry => entry.Entity is PaidEventPolicyVersion
                or PaidEventPolicyAllowedOrganizerKind or PaidEventPolicyAllowedCurrency
                or PaidEventPolicyRefundProtection or PaidEventPolicyCurrencyRiskLimitRow))
            throw new InvalidOperationException("Native paid-policy imports cannot coexist with EF-tracked policy DML.");
    }

    private static DbCommand Command(
        ExploreDbContext context, DbTransaction transaction, string operation, params NpgsqlParameter[] parameters)
    {
        DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT * FROM {FunctionSql(context, operation)}(" +
            string.Join(", ", parameters.Select(parameter => "@" + parameter.ParameterName)) + ")";
        foreach (NpgsqlParameter parameter in parameters)
            command.Parameters.Add(parameter);
        if (context.Database.GetCommandTimeout() is { } timeout)
            command.CommandTimeout = timeout;
        return command;
    }

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };

    private sealed record ReadOrigin(
        DbConnection Connection, DbTransaction Transaction, PaidEventPolicyVersion? Policy,
        Guid? Id, int? Version, string? Content);
}
