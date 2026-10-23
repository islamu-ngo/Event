using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Data-only capability for the original operator editor's existing Global connection.
/// Enrollment is never an HTTP human, administrator or setup-secret authority.
/// </summary>
public static partial class PostgresOperatorIdentityEditorContract
{
    public const string OwnerRole = "event_operator_identity_editor_owner";
    public const string MigratorRole = "event_operator_identity_editor_migrator";
    public const string GlobalRole = "event_operator_identity_editor_global";
    private const string Stamp = "operator-identity-editor:v1:";
    private const string WriteSignature = "uuid,jsonb,jsonb,uuid,text,timestamp with time zone,boolean";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, GlobalRole];
    private static readonly string[] SettingColumns =
    [
        "Id", "SettingKey", "Value", "SettingValueTypeId", "IsLocked", "AllowedValues",
        "Description", "Category", "DisplayOrder", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"
    ];
    private static readonly string[] SettingUpdates = ["Value", "SettingValueTypeId", "UpdatedAt", "UpdatedBy"];
    private static readonly string[] BootstrapColumns =
    [
        "Id", "Status", "Mode", "ProviderKind", "DeploymentMode", "Generation",
        "ConfigurationFingerprint", "SelectorFingerprint", "CompletedIdentityFingerprint",
        "CreatedAt", "SupersededAt", "CompletedAt", "CompletedByUserId"
    ];
    private static readonly string[] AuditColumns =
    [
        "Id", "AggregateType", "AggregateId", "EventType", "Payload", "Status", "CreatedAt",
        "ProcessedAt", "RetryCount", "LastError", "NextRetryAt", "MaxRetries", "DeadLetteredAt"
    ];
    private static readonly (Type Type, string[] Columns)[] Sources =
        [(typeof(SystemSetting), SettingColumns), (typeof(InstanceBootstrapState), BootstrapColumns),
            (typeof(OutboxMessage), AuditColumns)];

    internal static string FunctionSql(ExploreDbContext context, string operation) =>
        new HumanPrincipalSourceSql(context).Name("operator_identity_editor_" + operation);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new InvalidOperationException("Operator editor admission requires PostgreSQL.");
        if (context.Database.GetDbConnection().State != ConnectionState.Open)
            await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"DO $admission$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $admission$;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<OperatorIdentitySettingSnapshot?> ReadSettingAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, CancellationToken token)
    {
        string? value = await ReadAsync(context, enrollment, "read", [], token);
        return value is null ? null : JsonSerializer.Deserialize<OperatorIdentitySettingSnapshot>(value)
            ?? throw new UnauthorizedAccessException("The operator editor source projection is missing.");
    }

    internal static Task<string?> ReadPendingAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, bool acquire, CancellationToken token) =>
        ReadAsync(context, enrollment, "pending", [Parameter("acquire", NpgsqlDbType.Boolean, acquire)], token);

    internal static async Task<OutboxMessage?> ReadAuditAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, Guid id, CancellationToken token)
    {
        string? value = await ReadAsync(context, enrollment, "audit", [Parameter("id", NpgsqlDbType.Uuid, id)], token);
        return value is null ? null : JsonSerializer.Deserialize<OutboxMessage>(value)
            ?? throw new UnauthorizedAccessException("The operator editor audit projection is missing.");
    }

    internal static async Task WriteAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        OperatorIdentityMutation mutation, CancellationToken token)
    {
        mutation.RequireActive();
        RequireContext(context);
        DbTransaction transaction = context.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new UnauthorizedAccessException("Operator editing requires its physical transaction.");
        if (!context.HumanCommitAttemptOwnsTransaction || transaction.IsolationLevel != IsolationLevel.Serializable
            || !ReferenceEquals(transaction.Connection, context.Database.GetDbConnection()))
            throw new UnauthorizedAccessException("Operator editing requires the original single Serializable attempt.");
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"SELECT {FunctionSql(context, "write")}(@actor,@before,@after,@audit,@payload,@occurred,@import)";
        command.Parameters.Add(Parameter("actor", NpgsqlDbType.Uuid, mutation.Actor));
        command.Parameters.Add(Parameter("before", NpgsqlDbType.Jsonb,
            mutation.Before is null ? null : JsonSerializer.Serialize(mutation.Before)));
        command.Parameters.Add(Parameter("after", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(mutation.After)));
        command.Parameters.Add(Parameter("audit", NpgsqlDbType.Uuid, mutation.AuditId));
        command.Parameters.Add(Parameter("payload", NpgsqlDbType.Text, mutation.AuditPayload));
        command.Parameters.Add(Parameter("occurred", NpgsqlDbType.TimestampTz, mutation.OccurredAt));
        command.Parameters.Add(Parameter("import", NpgsqlDbType.Boolean,
            mutation.OriginalCommand is ImportInstanceOperatorIdentityCommand));
        await command.ExecuteNonQueryAsync(token);
        RequireContext(context);
        if (!ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction()))
            throw new UnauthorizedAccessException("The operator editor native transaction changed.");
    }

    private static async Task<string?> ReadAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        string operation, NpgsqlParameter[] parameters, CancellationToken token)
    {
        RequireContext(context);
        await RequireReadyAsync(context, enrollment, token);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"SELECT {FunctionSql(context, operation)}({string.Join(",", parameters.Select(p => "@" + p.ParameterName))})::pg_catalog.text";
        command.Parameters.AddRange(parameters);
        object? value = await command.ExecuteScalarAsync(token);
        RequireContext(context);
        return value is null or DBNull ? null : (string)value;
    }

    private static void RequireContext(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || context.TenantFilterTenantId is not null
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("The operator editor requires its ordinary tenant-null Global owner.");
    }

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
