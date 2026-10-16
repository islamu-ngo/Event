using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Exact-tenant receipt custody and bounded Global cleanup discovery. Receipt rows
/// are permanent rejection fences; completion authority is private native evidence.
/// The caller owns the already-open backend and every non-installation transaction.
/// </summary>
public static partial class PostgresIdempotencyReceiptContract
{
    public const string OwnerRole = "event_idempotency_receipt_owner";
    public const string DiscoveryOwnerRole = "event_idempotency_discovery_owner";
    public const string MigratorRole = "event_idempotency_receipt_migrator";
    public const string RuntimeRole = "event_idempotency_receipt_runtime";
    public const string GlobalRole = "event_idempotency_receipt_global";
    public const string OwnershipTable = "idempotency_receipt_ownership";
    // Capture is capped before envelope encoding and Data Protection. This bound
    // deliberately allows JSON escaping, encryption and base64 expansion.
    internal const int MaximumProtectedPayloadBytes = 16 * 1024 * 1024;
    private const string Prefix = "idempotency_receipt_";
    private const string Stamp = "idempotency-receipt:v1:";
    private static readonly string[] Roles = [OwnerRole, DiscoveryOwnerRole, MigratorRole, RuntimeRole, GlobalRole];
    private static readonly (string Name, string Signature, string Arguments, string Result, bool Discovery)[] Routines =
    [
        ("claim", "uuid,jsonb,bytea,boolean", "p_tenant uuid,p_proposal jsonb,p_nonce bytea,p_bound boolean", "jsonb", false),
        ("read", "uuid,jsonb,timestamp with time zone", "p_tenant uuid,p_request jsonb,p_now timestamp with time zone", "jsonb", false),
        ("complete", "uuid,uuid,bytea,integer,text,text,timestamp with time zone",
            "p_tenant uuid,p_id uuid,p_nonce bytea,p_status integer,p_body text,p_content_type text,p_now timestamp with time zone", "boolean", false),
        ("consume", "uuid,text,timestamp with time zone,timestamp with time zone",
            "p_tenant uuid,p_jti text,p_created timestamp with time zone,p_expires timestamp with time zone", "boolean", false),
        ("tenants", "timestamp with time zone,integer", "p_cutoff timestamp with time zone,p_take integer", "jsonb", true),
        ("count", "uuid,timestamp with time zone,integer", "p_tenant uuid,p_cutoff timestamp with time zone,p_take integer", "integer", false),
        ("scrub", "uuid,timestamp with time zone,integer", "p_tenant uuid,p_cutoff timestamp with time zone,p_take integer", "integer", false)
    ];

    internal sealed record NativeClaim(IdempotencyLookup Lookup, bool IsOwner);

    internal static string FunctionSql(ExploreDbContext context, string operation) =>
        new IdempotencyReceiptSql(context).Name(Prefix + operation);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("idempotency_receipt_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(token) : null;
        await using var command = Command(context);
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(token);
        if (owned is not null) await owned.CommitAsync(token);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token = default)
    {
        RequireConnection(context);
        await using var command = Command(context);
        string tenant = context.TenantFilterTenantId is { } id ? Text(id.ToString()) + "::uuid" : "NULL::uuid";
        command.CommandText = $"""
            DO $ready$ BEGIN
                {Guard(context, enrollment)}
                {ExactBodies(context, enrollment)}
                IF session_user = {Text(enrollment.MigratorLogin)} AND current_user = session_user
                    AND pg_catalog.current_setting('role') IN ('none',session_user::text) THEN NULL;
                ELSIF session_user = {Text(enrollment.RuntimeLogin)} THEN
                    {Caller(enrollment, false, tenant, false)}
                ELSIF session_user = {Text(enrollment.GlobalLogin)} THEN
                    {Caller(enrollment, true, "NULL::uuid", false)}
                ELSE RAISE EXCEPTION 'idempotency_receipt_readiness_caller' USING ERRCODE = '42501'; END IF;
            END $ready$;
            """;
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task<NativeClaim> ClaimAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        IdempotencyRecord proposal, ReadOnlyMemory<byte> nonce, bool transactionBound, CancellationToken token)
    {
        if (System.Transactions.Transaction.Current is not null
            || transactionBound != (context.Database.CurrentTransaction is not null))
            throw new UnauthorizedAccessException("The receipt acquisition must retain its declared original transaction mode.");
        if (nonce.Length != 32) throw new ArgumentException("Receipt completion nonces require 32 bytes.", nameof(nonce));
        if (proposal.StatusCode != 0 || proposal.ResponseBody is not null || proposal.ContentType is not null)
            throw new UnauthorizedAccessException("Acquisition cannot insert a completed response.");
        await RequireReadyAsync(context, enrollment, token);
        await using var command = Command(context);
        command.CommandText = $"SELECT {FunctionSql(context, "claim")}(@tenant,@proposal,@nonce,@bound)::text";
        Add(command, "tenant", NpgsqlDbType.Uuid, proposal.TenantId);
        var input = new NativeProposal(proposal.Id, IdempotencyRequestDescriptor.FromRecord(proposal),
            Instant(proposal.CreatedAt), Instant(proposal.ExpiresAt));
        Add(command, "proposal", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(input, ReceiptJsonContext.Default.NativeProposal));
        Add(command, "nonce", NpgsqlDbType.Bytea, nonce.ToArray());
        Add(command, "bound", NpgsqlDbType.Boolean, transactionBound);
        var result = JsonSerializer.Deserialize(await JsonResultAsync(command, token), ReceiptJsonContext.Default.NativeResult)
            ?? throw new UnauthorizedAccessException("The native receipt result is missing.");
        return new(Lookup(result), result.IsOwner);
    }

    internal static async Task<IdempotencyLookup> ReadAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        IdempotencyRequestDescriptor request, DateTime utcNow, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        await using var command = Command(context);
        command.CommandText = $"SELECT {FunctionSql(context, "read")}(@tenant,@request,@now)::text";
        Add(command, "tenant", NpgsqlDbType.Uuid, request.TenantId);
        Add(command, "request", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(request, ReceiptJsonContext.Default.IdempotencyRequestDescriptor));
        Add(command, "now", NpgsqlDbType.TimestampTz, Instant(utcNow));
        return Lookup(JsonSerializer.Deserialize(await JsonResultAsync(command, token), ReceiptJsonContext.Default.NativeResult)
            ?? throw new UnauthorizedAccessException("The native receipt lookup is missing."));
    }

    internal static async Task<bool> CompleteAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid tenant, Guid recordId, ReadOnlyMemory<byte> nonce, int status, string? body, string? contentType,
        DateTime utcNow, CancellationToken token)
    {
        if (nonce.Length != 32) throw new ArgumentException("Receipt completion nonces require 32 bytes.", nameof(nonce));
        await RequireReadyAsync(context, enrollment, token);
        await using var command = Command(context);
        command.CommandText = $"SELECT {FunctionSql(context, "complete")}(@tenant,@id,@nonce,@status,@body,@content_type,@now)";
        Add(command, "tenant", NpgsqlDbType.Uuid, tenant);
        Add(command, "id", NpgsqlDbType.Uuid, recordId);
        Add(command, "nonce", NpgsqlDbType.Bytea, nonce.ToArray());
        Add(command, "status", NpgsqlDbType.Integer, status);
        Add(command, "body", NpgsqlDbType.Text, body);
        Add(command, "content_type", NpgsqlDbType.Text, contentType);
        Add(command, "now", NpgsqlDbType.TimestampTz, Instant(utcNow));
        return (bool)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The native receipt completion result is missing."));
    }

    internal static async Task<bool> ConsumeAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid tenant, string jti, DateTime createdUtc, DateTime expiresUtc, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        await using var command = Command(context);
        command.CommandText = $"SELECT {FunctionSql(context, "consume")}(@tenant,@jti,@created,@expires)";
        Add(command, "tenant", NpgsqlDbType.Uuid, tenant);
        Add(command, "jti", NpgsqlDbType.Text, jti);
        Add(command, "created", NpgsqlDbType.TimestampTz, Instant(createdUtc));
        Add(command, "expires", NpgsqlDbType.TimestampTz, Instant(expiresUtc));
        return (bool)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The native consumption result is missing."));
    }

    internal static async Task<IReadOnlyList<Guid>> GetExpiredTenantIdsAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, DateTime cutoff, int maximumRows, CancellationToken token)
    {
        RequireLimit(maximumRows);
        await RequireReadyAsync(context, enrollment, token);
        await using var command = Command(context);
        command.CommandText = $"SELECT {FunctionSql(context, "tenants")}(@cutoff,@take)::text";
        Add(command, "cutoff", NpgsqlDbType.TimestampTz, Instant(cutoff));
        Add(command, "take", NpgsqlDbType.Integer, maximumRows);
        var result = JsonSerializer.Deserialize(await JsonResultAsync(command, token), ReceiptJsonContext.Default.GuidArray)
            ?? throw new UnauthorizedAccessException("The native cleanup tenant result is missing.");
        return Array.AsReadOnly(result);
    }

    internal static Task<int> CountExpiredAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid tenant, DateTime cutoff, int maximumRows, CancellationToken token) =>
        CleanupAsync(context, enrollment, "count", tenant, cutoff, maximumRows, token);

    internal static Task<int> ScrubExpiredAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid tenant, DateTime cutoff, int maximumRows, CancellationToken token) =>
        CleanupAsync(context, enrollment, "scrub", tenant, cutoff, maximumRows, token);

    private static async Task<int> CleanupAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        string operation, Guid tenant, DateTime cutoff, int maximumRows, CancellationToken token)
    {
        RequireLimit(maximumRows);
        await RequireReadyAsync(context, enrollment, token);
        await using var command = Command(context);
        command.CommandText = $"SELECT {FunctionSql(context, operation)}(@tenant,@cutoff,@take)";
        Add(command, "tenant", NpgsqlDbType.Uuid, tenant);
        Add(command, "cutoff", NpgsqlDbType.TimestampTz, Instant(cutoff));
        Add(command, "take", NpgsqlDbType.Integer, maximumRows);
        return (int)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The native cleanup count is missing."));
    }

    private static IdempotencyLookup Lookup(NativeResult result)
    {
        if (!Enum.IsDefined(result.Outcome)
            || (result.Outcome == IdempotencyLookupOutcome.Existing) != (result.Record is not null)
            || result.IsOwner && result.Outcome != IdempotencyLookupOutcome.Existing)
            throw new UnauthorizedAccessException("The native receipt result has an invalid closed shape.");
        return new(result.Outcome, result.Record?.ToEntity());
    }

    private static DateTime Instant(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Native receipt instants must be UTC.", nameof(value));
        return value.AddTicks(-(value.Ticks % 10));
    }

    private static void RequireLimit(int maximumRows)
    {
        if (maximumRows is < 1 or > IdempotencyCleanupLimits.MaximumRows)
            throw new ArgumentOutOfRangeException(nameof(maximumRows));
    }

    private static void RequireConnection(ExploreDbContext context)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection() is not NpgsqlConnection
            || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Receipt custody requires the original already-open PostgreSQL backend.");
        if (System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("Ambient transactions cannot own native receipt custody.");
    }

    private static DbCommand Command(ExploreDbContext context)
    {
        RequireConnection(context);
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        return command;
    }

    private static void Add(DbCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
    private static async Task<string> JsonResultAsync(DbCommand command, CancellationToken token) =>
        (string)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The native receipt JSON result is missing."));
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    internal sealed record NativeProposal(Guid Id, IdempotencyRequestDescriptor Request,
        [property: JsonConverter(typeof(NativeInstantConverter))] DateTime CreatedAt,
        [property: JsonConverter(typeof(NativeInstantConverter))] DateTime ExpiresAt);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    internal sealed record NativeResult(
        [property: JsonRequired] IdempotencyLookupOutcome Outcome,
        [property: JsonRequired] NativeRow? Record,
        [property: JsonRequired] bool IsOwner);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    internal sealed record NativeRow(
        [property: JsonRequired] Guid Id,
        [property: JsonRequired] string Key,
        [property: JsonRequired] Guid TenantId,
        [property: JsonRequired] string? UserId,
        [property: JsonRequired] string RequestMethod,
        [property: JsonRequired] string RequestTarget,
        [property: JsonRequired] string? RequestContentType,
        [property: JsonRequired] string RequestBodyHash,
        [property: JsonRequired] string PrincipalFingerprint,
        [property: JsonRequired] int StatusCode,
        [property: JsonRequired] string? ResponseBody,
        [property: JsonRequired] string? ContentType,
        [property: JsonRequired] DateTime CreatedAt,
        [property: JsonRequired] DateTime ExpiresAt)
    {
        internal IdempotencyRecord ToEntity() => new()
        {
            Id = Id,
            Key = Key,
            TenantId = TenantId,
            UserId = UserId,
            RequestMethod = RequestMethod,
            RequestTarget = RequestTarget,
            RequestContentType = RequestContentType,
            RequestBodyHash = RequestBodyHash,
            PrincipalFingerprint = PrincipalFingerprint,
            StatusCode = StatusCode,
            ResponseBody = ResponseBody,
            ContentType = ContentType,
            CreatedAt = CreatedAt,
            ExpiresAt = ExpiresAt
        };
    }

    [JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
    [JsonSerializable(typeof(NativeProposal))]
    [JsonSerializable(typeof(NativeResult))]
    [JsonSerializable(typeof(IdempotencyRequestDescriptor))]
    [JsonSerializable(typeof(Guid[]))]
    internal partial class ReceiptJsonContext : JsonSerializerContext;

    internal sealed class NativeInstantConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTime();
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(Instant(value).ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }
}
