using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Closed support DATA custody on the original enrolled Runtime connection. The
/// original S/P/route/H sources remain separate prerequisites, not DATA privileges.
/// </summary>
public static partial class PostgresSupportAdministrationContract
{
    public const string OwnerRole = "event_support_administration_owner";
    public const string MigratorRole = "event_support_administration_migrator";
    public const string RuntimeRole = "event_support_administration_runtime";
    public const string SourceTable = "support_administration_source";
    private const string AttemptTable = "support_administration_attempt";
    private const string Prefix = "support_administration_";
    private const string Stamp = "support-administration:v1:";
    private const string Policy = "support_administration_data";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];

    private static readonly (Type Type, string Key, string[] Properties)[] Inventory =
    [
        (typeof(SupportAccessSession), "Id",
            ["Id", "ActorUserId", "TargetTenantId", "TargetTenantUserId", "StatusId", "ModeId",
                "ReasonCode", "ReasonText", "TicketReference", "ApprovedByUserId", "StartedAtUtc",
                "ExpiresAtUtc", "EndedAtUtc", "EndReasonId", "EndReasonText", "CreatedAt", "CreatedBy",
                "UpdatedAt", "UpdatedBy", "ConcurrencyStamp"]),
        (typeof(SupportAccessAuditEvent), "Id",
            ["Id", "SupportAccessSessionId", "OccurredAtUtc", "EventTypeId", "ActorUserId", "TargetTenantId",
                "TargetTenantUserId", "RouteName", "RequestName", "ResourceKind", "ResourceId", "Action",
                "Outcome", "HttpStatusCode", "CorrelationId", "TraceId", "SanitizedMetadataJson"]),
        (typeof(Tenant), "Id",
            ["Id", "FullName", "Slug", "Description", "TenantStatusId", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"]),
        (typeof(TenantUser), "Id",
            ["Id", "TenantId", "UserId", "ActorId", "StatusId", "JoinedAt", "SuspendedAt", "SuspendedBy",
                "BanExpiresAt", "RemovedAt", "RemovedBy", "ModerationNote", "CreatedAt", "CreatedBy",
                "UpdatedAt", "UpdatedBy", "IsDeleted", "DeletedAt", "DeletedBy"])
    ];

    internal sealed record Request(
        string Operation, Guid ActorId, Guid TenantId, Guid? SessionId = null,
        Guid? TargetTenantId = null, Guid? TargetTenantUserId = null, int? ModeId = null,
        int? DurationMinutes = null, string? ReasonCode = null, string? ReasonText = null,
        string? TicketReference = null, string? EndReasonText = null, int Limit = 100, DateTimeOffset? AtUtc = null)
    {
        public override string ToString() => nameof(Request);
    }

    /// <summary>Only independently generated identifiers and the original transition time cross this port.</summary>
    internal sealed record Accepted(DateTimeOffset? TransitionAt = null, Guid? SessionId = null,
        Guid? AuditId = null, Guid? ConcurrencyStamp = null)
    {
        public override string ToString() => nameof(Accepted);
    }

    internal static string FunctionSql(ExploreDbContext context, string name) =>
        new HumanPrincipalSourceSql(context).Name(Prefix + name);

    internal static void RequireInventory(ExploreDbContext context)
    {
        foreach (var source in Inventory)
        {
            var entity = context.Model.FindEntityType(source.Type)
                ?? throw new InvalidOperationException("support_administration_colocated_model_required");
            if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(source.Properties.Order(StringComparer.Ordinal), StringComparer.Ordinal)
                || entity.GetProperties().Any(property => property.IsShadowProperty())
                || entity.FindPrimaryKey() is not { } key
                || !key.Properties.Select(property => property.Name).SequenceEqual([source.Key], StringComparer.Ordinal)
                || entity.GetTableName() is null)
                throw new InvalidOperationException("support_administration_explicit_inventory_drift");
        }
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("support_administration_native_provider_required");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = BuildSql(context, enrollment);
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        RequireInventory(context);
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Support DATA requires the retained open PostgreSQL connection.");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"DO $ready$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $ready$;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static Task<string> ReadAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Request request, CancellationToken token) => InvokeAsync(context, enrollment, "read", request, null, token);

    internal static Task<string> AcquireAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Request request, string originalImage, CancellationToken token) =>
        InvokeAsync(context, enrollment, "acquire", request, originalImage, token);

    internal static Task<string> CompileAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Request request, Accepted accepted, CancellationToken token)
    {
        if (accepted.TransitionAt is { } instant && (instant.Offset != TimeSpan.Zero || instant.Ticks % 10 != 0))
            throw new ArgumentException("Support time must be UTC at microsecond precision.", nameof(accepted));
        var payload = JsonSerializer.SerializeToNode(accepted)!.AsObject();
        if (accepted.TransitionAt is { } transition)
            payload[nameof(Accepted.TransitionAt)] = transition.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        return InvokeAsync(context, enrollment, "compile", request, payload.ToJsonString(), token);
    }

    internal static Task<string> ApplyPlanAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Request request, CancellationToken token) => InvokeAsync(context, enrollment, "apply", request, null, token);

    /// <summary>Last awaited DATA work, before original synchronous deadlines and physical settlement.</summary>
    internal static Task<string> CompleteAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Request request, string disclosedImage, CancellationToken token) =>
        InvokeAsync(context, enrollment, "complete", request, disclosedImage, token);

    private static async Task<string> InvokeAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        string function, Request request, string? argument, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        if (System.Transactions.Transaction.Current is not null
            || function != "read" && context.Database.CurrentTransaction?.GetDbTransaction()
                is not { IsolationLevel: IsolationLevel.Serializable })
            throw new UnauthorizedAccessException("Support DATA requires its original Serializable owner.");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, function)}(@request{(argument is null ? "" : ",@argument")})::text";
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request) });
        if (argument is not null)
            command.Parameters.Add(new NpgsqlParameter("argument", NpgsqlDbType.Jsonb) { Value = argument });
        return (string)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The retained support DATA result is absent."));
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Mutable(Type type) => type == typeof(SupportAccessSession) || type == typeof(SupportAccessAuditEvent);
    private static string Label(Type type) => type == typeof(SupportAccessSession) ? "Sessions"
        : type == typeof(SupportAccessAuditEvent) ? "Audits" : type == typeof(Tenant) ? "Targets" : "Members";
    private static string[] UpdateColumns(Type type) => type == typeof(SupportAccessSession)
        ? ["StatusId", "EndedAtUtc", "EndReasonId", "EndReasonText", "UpdatedAt", "UpdatedBy", "ConcurrencyStamp"] : [];
}
