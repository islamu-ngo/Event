using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only closed private Event read operations. Runtime never receives source-table
/// grants, owner-role switching, an arbitrary query, or an installation path.
/// </summary>
public static partial class PostgresPrivateEventManagementContract
{
    public const string OwnerRole = "event_private_management_owner";
    public const string MigratorRole = "event_private_management_migrator";
    public const string RuntimeRole = "event_private_management_runtime";
    private const string Routine = "private_event_management_read";
    private const string Signature = "uuid,uuid,integer,uuid,uuid,integer,integer,integer";
    private const string Policy = "private_event_management_read";
    private const string Stamp = "private-event-management:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];

    internal static RepositoryOperations? For(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new(context) : null;

    internal sealed class RepositoryOperations(ExploreDbContext context)
    {
        internal Task<PrivateEventManagementGraph> ReadAsync(PostgresNativeQueueEnrollment enrollment,
            Guid principal, PrivateEventManagementRead request, CancellationToken token) =>
            PostgresPrivateEventManagementContract.ReadAsync(context, enrollment, principal, request, token);

        internal Task<IReadOnlyList<Event>> ReadAuthorizationTargetsAsync(
            Func<IPrivateEventManagementReader> resolveReader, IReadOnlyCollection<Guid> ids, CancellationToken token) =>
            resolveReader().ReadAuthorizationTargetsAsync(ids, token);

        internal Task<EventAuthoritySnapshot> ReadAuthorityAsync(
            Func<IPrivateEventManagementReader> resolveReader, Guid tenant, Guid user,
            IReadOnlyCollection<Guid> ids, CancellationToken token) =>
            resolveReader().ReadAuthorityAsync(tenant, user, ids, token);
    }

    public static string FunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Routine);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) throw new NotSupportedException("private_management_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    internal static async Task<PrivateEventManagementGraph> ReadAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, Guid principal, PrivateEventManagementRead request, CancellationToken token)
    {
        context.ValidateTenantLease();
        if (context.IsTenantFilterBypassed || context.TenantFilterTenantId is not { } tenant || tenant == Guid.Empty
            || principal == Guid.Empty || context.Database.GetDbConnection().State != ConnectionState.Open
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new UnauthorizedAccessException("private_management_original_serializable_owner_required");
        var definition = Define(context, enrollment);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"""
            DO $admit$ BEGIN
                {definition.Guard}
                {ExactBody(context, definition.Body)}
                {Caller(enrollment, "session_user", Text(tenant.ToString()) + "::uuid")}
            END $admit$;
            SELECT {FunctionSql(context)}(@tenant,@principal,@kind,@event,@session,@page,@size,@type)::text;
            """;
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenant });
        command.Parameters.Add(new NpgsqlParameter("principal", NpgsqlDbType.Uuid) { Value = principal });
        command.Parameters.Add(new NpgsqlParameter("kind", NpgsqlDbType.Integer) { Value = (int)request.Kind });
        command.Parameters.Add(new NpgsqlParameter("event", NpgsqlDbType.Uuid) { Value = (object?)request.EventId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("session", NpgsqlDbType.Uuid) { Value = (object?)request.SessionId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("page", NpgsqlDbType.Integer) { Value = request.PageNumber });
        command.Parameters.Add(new NpgsqlParameter("size", NpgsqlDbType.Integer) { Value = request.PageSize });
        command.Parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Integer) { Value = (object?)request.EventTypeId ?? DBNull.Value });
        string payload = await command.ExecuteScalarAsync(token) as string
            ?? throw new InvalidOperationException("private_management_payload_missing");
        using var document = JsonDocument.Parse(payload);
        return Materialize(context, document.RootElement);
    }

    private sealed record Definition(GraphSql Sql, PostgresPrivilegeGuard.Contract Privileges,
        IReadOnlyDictionary<Type, string> Policies, string Guard, string Body);

    private static Definition Define(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin, enrollment.MigratorLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("private_management_enrollment_collision");
        var sql = new GraphSql(context);
        string cases = string.Join("\n", Enum.GetValues<PrivateEventManagementReadKind>().Select(kind =>
            $"WHEN {(int)kind} THEN RETURN {Projection(sql, kind)};"));
        string parent = $"{sql.Live<Event>("parent")} AND {sql.Column<Event>("parent", "Id")} = p_event";
        string session = $"{sql.Live<EventSession>("session")} AND {sql.Column<EventSession>("session", "Id")} = p_session AND {sql.Column<EventSession>("session", "EventId")} = p_event";
        var policies = Policies(context, sql);
        var privileges = new PostgresPrivilegeGuard.Contract(sql.Names.Schema, Roles,
            [new(Routine, Signature, OwnerRole, [RuntimeRole])],
            sql.Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Key)!.GetTableName()!, OwnerRole, [],
                source.Value.Order(StringComparer.Ordinal).Select(property =>
                    new PostgresPrivilegeGuard.Column(sql.Names.ColumnName(source.Key, property), ["SELECT"])).ToArray())).ToArray(),
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(privileges) + SourceGuard(context, sql, enrollment)
            + PolicyGuard(sql, policies) + CallerPrivileges(context, sql, enrollment);
        string body = $"""
            BEGIN
                {guard}
                {Caller(enrollment, Text(OwnerRole), "p_tenant")}
                IF p_principal IS NULL OR p_principal = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_kind IS NULL OR p_kind NOT BETWEEN 0 AND {(int)PrivateEventManagementReadKind.SessionSyncContext}
                    OR p_page IS NULL OR p_page < 1 OR p_size IS NULL OR p_size NOT BETWEEN 1 AND 100
                THEN RAISE EXCEPTION 'private_management_selector_invalid' USING ERRCODE = '22023'; END IF;
                IF p_kind NOT IN ({(int)PrivateEventManagementReadKind.MyEvents},{(int)PrivateEventManagementReadKind.Templates},{(int)PrivateEventManagementReadKind.SessionTemplates},{(int)PrivateEventManagementReadKind.SessionAuthorizationTarget})
                    AND (p_event IS NULL OR NOT EXISTS (SELECT 1 FROM {sql.Table<Event>()} parent WHERE {parent}))
                THEN RETURN pg_catalog.jsonb_build_object(); END IF;
                IF p_kind = {(int)PrivateEventManagementReadKind.SessionSyncHistory}
                    AND (p_session IS NULL OR NOT EXISTS (SELECT 1 FROM {sql.Table<EventSession>()} session WHERE {session}))
                THEN RAISE EXCEPTION 'private_management_session_owner_mismatch' USING ERRCODE = '42501'; END IF;
                IF p_kind = {(int)PrivateEventManagementReadKind.SessionSyncContext}
                    AND (p_session IS NULL OR NOT EXISTS (SELECT 1 FROM {sql.Table<EventSession>()} session WHERE {session}))
                THEN RETURN pg_catalog.jsonb_build_object(); END IF;
                CASE p_kind
                    {cases}
                    ELSE RAISE EXCEPTION 'private_management_unknown_operation' USING ERRCODE = '22023';
                END CASE;
            END;
            """;
        return new(sql, privileges, policies, guard, body);
    }

    private static string Caller(PostgresNativeQueueEnrollment enrollment, string owner, string tenant) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> {owner} OR session_user <> {Text(enrollment.RuntimeLogin)}
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            OR {tenant} IS NULL OR {tenant} = '00000000-0000-0000-0000-000000000000'::uuid
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM {tenant}
        THEN RAISE EXCEPTION 'private_management_caller_mismatch' USING ERRCODE = '42501'; END IF;
        """;

    private static string ExactBody(ExploreDbContext context, string body) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
            WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context) + "(" + Signature + ")")})
                AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                AND f.prorettype = 'jsonb'::regtype AND NOT f.proretset AND f.proargdefaults IS NULL
                AND f.provariadic = 0 AND f.proallargtypes IS NULL AND f.proargmodes IS NULL
                AND f.proargnames = ARRAY['p_tenant','p_principal','p_kind','p_event','p_session','p_page','p_size','p_type']::text[]
                AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(body))})
        THEN RAISE EXCEPTION 'private_management_body_drift' USING ERRCODE = '55000'; END IF;
        """;

    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
