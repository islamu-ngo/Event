using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>Loads and settles only the selected Runtime context's original native outbox claim.</summary>
public static partial class PostgresOutboxExecutionContract
{
    public const string OwnerRole = "event_outbox_execution_owner";
    public const string MigratorRole = "event_outbox_execution_migrator";
    public const string RuntimeRole = "event_outbox_execution_runtime";
    private const string Routine = "outbox_execution";
    private const string Signature = "jsonb,boolean,integer,integer,text,boolean,integer,timestamp with time zone,text,text";

    internal static RepositoryOperations? For(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new(context) : null;

    internal sealed class RepositoryOperations(ExploreDbContext context)
    {
        internal Task<OutboxMessage?> ReadAsync(PostgresNativeQueueEnrollment enrollment,
            Guid id, DateTime expiry, OutboxMessageStatus status, bool notification, CancellationToken token) =>
            PostgresOutboxExecutionContract.ReadAsync(context, enrollment, id, expiry, status, notification, token);

        internal Task<int> SettleAsync(PostgresNativeQueueEnrollment enrollment,
            Guid id, DateTime expiry, OutboxMessageStatus status, int operation, string? error,
            bool retryable, int delay, DateTime? failedAt, string? expected, string? replacement, CancellationToken token) =>
            PostgresOutboxExecutionContract.SettleAsync(context, enrollment, id, expiry, status, operation,
                error, retryable, delay, failedAt, expected, replacement, token);

        internal Task<Event?> ReadPublicationSourceAsync(
            PostgresNativeQueueEnrollment enrollment, Guid eventId, CancellationToken token) =>
            PostgresOutboxExecutionContract.ReadPublicationSourceAsync(context, enrollment, eventId, token);
    }

    internal static async Task<OutboxMessage?> ReadAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid id, DateTime expiry, OutboxMessageStatus status, bool notification, CancellationToken token)
    {
        string? result = await ExecuteAsync(context, enrollment, id, expiry, status, notification, 0,
            null, false, 0, null, null, null, token);
        return result is null ? null : JsonSerializer.Deserialize<OutboxMessage>(result);
    }

    internal static async Task<int> SettleAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid id, DateTime expiry, OutboxMessageStatus status, int operation, string? error,
        bool retryable, int delay, DateTime? failedAt, string? expected, string? replacement, CancellationToken token)
    {
        bool notification = context.NotificationOutboxExecutionClaim is not null;
        string? result = await ExecuteAsync(context, enrollment, id, expiry, status, notification, operation,
            error, retryable, delay, failedAt, expected, replacement, token);
        return result is null ? 0 : int.Parse(result, CultureInfo.InvariantCulture);
    }

    internal static async Task<Event?> ReadPublicationSourceAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, Guid eventId, CancellationToken token)
    {
        var claim = context.NotificationOutboxExecutionClaim;
        if (claim is null || claim.EventType != EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType
            || claim.AggregateType != nameof(Event) || claim.AggregateId != eventId || claim.SourceId != eventId)
            throw new UnauthorizedAccessException("outbox_execution_publication_source_required");
        string? result = await ExecuteAsync(context, enrollment, claim.MessageId, claim.ProcessingLeaseExpiresAt,
            OutboxMessageStatus.Processing, true, 5, null, false, 0, null, eventId.ToString("D"), null, token);
        if (result is null) return null;
        using var document = JsonDocument.Parse(result);
        return PublicEventDiscoveryProviderOperations.Materialize<Event>(context, document.RootElement);
    }

    private static async Task<string?> ExecuteAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid id, DateTime expiry, OutboxMessageStatus status, bool notification, int operation,
        string? error, bool retryable, int delay, DateTime? failedAt,
        string? expected, string? replacement, CancellationToken token)
    {
        context.ValidateTenantLease();
        object original;
        Guid? tenant;
        if (notification && context.NotificationOutboxExecutionClaim is { } notice)
        {
            if (notice.MessageId != id || notice.ProcessingLeaseExpiresAt != expiry
                || status != OutboxMessageStatus.Processing)
                throw new UnauthorizedAccessException("outbox_execution_original_claim_required");
            original = notice;
            tenant = notice.TenantId;
        }
        else if (!notification && context.GenericOutboxExecutionClaim is { } generic)
        {
            if (generic.MessageId != id || generic.LeaseExpiresAt != expiry
                || status != generic.Status && !(generic.Status == OutboxMessageStatus.Processing
                    && status == OutboxMessageStatus.DeadLettered && GenericOutboxRoutes.SupportsDeadLetter(generic.EventType)))
                throw new UnauthorizedAccessException("outbox_execution_original_claim_required");
            original = generic;
            tenant = generic.TenantId;
        }
        else throw new UnauthorizedAccessException("outbox_execution_original_claim_required");
        if (context.IsTenantFilterBypassed || context.BindTenantLease() != tenant)
            throw new UnauthorizedAccessException("outbox_execution_original_tenant_required");

        bool openedHere = context.Database.GetDbConnection().State != ConnectionState.Open;
        if (openedHere) await context.Database.OpenConnectionAsync(token);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            string name = new HumanPrincipalSourceSql(context).Name(Routine);
            command.CommandText = $"""
                DO $ready$ BEGIN
                    {Guard(context, enrollment)}
                    {ExactBody(context, enrollment)}
                END $ready$;
                SELECT {name}(@claim,@notification,@operation,@status,@error,@retryable,@delay,@failed,@expected,@replacement)::text;
                """;
            Add("claim", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(original, original.GetType()));
            Add("notification", NpgsqlDbType.Boolean, notification);
            Add("operation", NpgsqlDbType.Integer, operation);
            Add("status", NpgsqlDbType.Integer, (int)status);
            Add("error", NpgsqlDbType.Text, error);
            Add("retryable", NpgsqlDbType.Boolean, retryable);
            Add("delay", NpgsqlDbType.Integer, Math.Max(0, delay));
            Add("failed", NpgsqlDbType.TimestampTz, failedAt);
            Add("expected", NpgsqlDbType.Text, expected);
            Add("replacement", NpgsqlDbType.Text, replacement);
            return await command.ExecuteScalarAsync(token) as string;

            void Add(string parameter, NpgsqlDbType type, object? value) =>
                command.Parameters.Add(new NpgsqlParameter(parameter, type) { Value = value ?? DBNull.Value });
        }
        finally
        {
            if (openedHere) await context.Database.CloseConnectionAsync();
        }
    }

    public static async Task ApplyAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken token = default)
    {
        enrollment.Validate();
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(token) : null;
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), token);
        if (owned is not null) await owned.CommitAsync(token);
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    private static string ExactBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
            WHERE f.oid = pg_catalog.to_regprocedure({Text(new HumanPrincipalSourceSql(context).Name(Routine) + "(" + Signature + ")")})
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex')
                    = {Text(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Body(context, enrollment)))))})
        THEN RAISE EXCEPTION 'outbox_execution_body_drift' USING ERRCODE = '55000'; END IF;
        """;
}
