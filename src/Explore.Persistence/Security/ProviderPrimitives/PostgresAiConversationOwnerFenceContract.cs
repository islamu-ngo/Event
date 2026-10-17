using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Holds AI conversation owner eligibility sources on the caller's original transaction.
/// This is a data fence shared by human mutations and machine settlement, not a human admission.
/// It returns no private source data and grants Runtime only EXECUTE.
/// </summary>
public static partial class PostgresAiConversationOwnerFenceContract
{
    public const string OwnerRole = "event_ai_conversation_owner";
    public const string MigratorRole = "event_ai_conversation_migrator";
    public const string RuntimeRole = "event_ai_conversation_runtime";
    private const string Routine = "ai_conversation_owner_fence";
    private const string PrivacyRoutine = "ai_conversation_owner_privacy_fence";
    private const string Policy = "ai_conversation_owner_fence";
    private const string Stamp = "ai-conversation-owner:v1:";
    private const string Arguments = "uuid,uuid,uuid,uuid,boolean,boolean";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];
    private static readonly bool[] PolicyModes = [false, true];

    public static string FunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Routine);
    public static string PrivacyFunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(PrivacyRoutine);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("ai_conversation_owner_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = Command(context);
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    internal static async Task<bool> FenceAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid tenantId, Guid userId, Guid? actorId, Guid? admittedActorId, bool privacy, CancellationToken cancellationToken,
        bool requireOrganizer = false) =>
        await ReadAsync(context, enrollment, tenantId, userId, actorId, admittedActorId, privacy,
            requireOrganizer, false, cancellationToken);

    internal static Task<bool> IsPrivacyFencedAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        ReadAsync(context, enrollment, tenantId, userId, null, null, false, false, true, cancellationToken);

    private static async Task<bool> ReadAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Guid tenantId, Guid userId, Guid? actorId, Guid? admittedActorId, bool privacy,
        bool requireOrganizer, bool privacyFence, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var transaction = context.Database.CurrentTransaction;
        var contextId = context.ContextId;
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || connection.State != ConnectionState.Open
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
            || tenantId == Guid.Empty || userId == Guid.Empty
            || transaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || !ReferenceEquals(transaction.GetDbTransaction().Connection, connection))
            throw new UnauthorizedAccessException("AI owner fencing requires its original Runtime tenant Serializable transaction.");
        var definition = Define(context, enrollment);
        if (privacyFence)
            await RequireCurrentInstallationAsync((NpgsqlConnection)connection, context, enrollment, definition, cancellationToken);
        await using var command = Command(context);
        command.CommandText = $"""
            DO $ready$ BEGIN
                {definition.Guard} {ExactBodies(context, definition)}
                {Caller(enrollment, "session_user", Text(tenantId.ToString()) + "::uuid")}
            END $ready$;
            SELECT {(privacyFence ? PrivacyFunctionSql(context) + "(@tenant,@principal)"
                : FunctionSql(context) + "(@tenant,@principal,@actor,@admitted,@privacy,@organizer)")};
            """;
        var parameters = ((NpgsqlCommand)command).Parameters;
        parameters.AddWithValue("tenant", NpgsqlDbType.Uuid, tenantId);
        parameters.AddWithValue("principal", NpgsqlDbType.Uuid, userId);
        if (!privacyFence)
        {
            parameters.AddWithValue("actor", NpgsqlDbType.Uuid, (object?)actorId ?? DBNull.Value);
            parameters.AddWithValue("admitted", NpgsqlDbType.Uuid, (object?)admittedActorId ?? DBNull.Value);
            parameters.AddWithValue("privacy", NpgsqlDbType.Boolean, privacy);
            parameters.AddWithValue("organizer", NpgsqlDbType.Boolean, requireOrganizer);
        }
        bool eligible = await command.ExecuteScalarAsync(cancellationToken) is true;
        if (privacyFence)
            await RequireCurrentInstallationAsync((NpgsqlConnection)connection, context, enrollment, definition, cancellationToken);
        context.ValidateTenantLease();
        if (context.ContextId != contextId || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
            || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
            || !ReferenceEquals(context.Database.GetDbConnection(), connection)
            || !ReferenceEquals(transaction.GetDbTransaction().Connection, connection))
            throw new UnauthorizedAccessException("AI owner fencing cannot replace its original transaction.");
        return eligible;
    }

    private static async Task RequireCurrentInstallationAsync(NpgsqlConnection original,
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, Definition definition,
        CancellationToken cancellationToken)
    {
        // Catalog rows in the graph owner's Serializable snapshot can predate DDL.
        // Check current metadata separately; all private data stays on that owner.
        var settings = new NpgsqlConnectionStringBuilder(original.ConnectionString) { Pooling = false, Enlist = false };
        await using var verification = await original.CloneWithAsync(settings.ConnectionString, cancellationToken);
        await verification.OpenAsync(cancellationToken);
        await using var command = verification.CreateCommand();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"""
            DO $current$ BEGIN
                IF session_user <> {Text(enrollment.RuntimeLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'ai_conversation_owner_runtime_required' USING ERRCODE = '42501'; END IF;
                {definition.Guard} {ExactBodies(context, definition)}
            END $current$;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand Command(ExploreDbContext context)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        return command;
    }

    private static string Caller(PostgresNativeQueueEnrollment enrollment, string owner, string tenant) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> {owner} OR session_user <> {Text(enrollment.RuntimeLogin)}
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
            OR {tenant} IS NULL OR {tenant} = '00000000-0000-0000-0000-000000000000'::uuid
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM {tenant}
        THEN RAISE EXCEPTION 'ai_conversation_owner_caller_mismatch' USING ERRCODE = '42501'; END IF;
        """;

    private sealed record Definition(SourceSql Sql, PostgresPrivilegeGuard.Contract Privileges,
        IReadOnlyDictionary<Type, string> Policies, string Guard, string Body, string PrivacyBody);

    private static Definition Define(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.RuntimeLogin, enrollment.GlobalLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("ai_conversation_owner_enrollment_collision");
        var sql = new SourceSql(context);
        string work = FenceSql(sql);
        string privacyWork = $"""
            RETURN EXISTS (SELECT 1 FROM {sql.T<PrivacyErasureSaga>()} s
                WHERE {sql.C<PrivacyErasureSaga>("s", nameof(PrivacyErasureSaga.SubjectId))} = p_principal);
            """;
        var policies = sql.Policies(context);
        var privileges = new PostgresPrivilegeGuard.Contract(sql.Names.Schema, Roles,
            [new(Routine, Arguments, OwnerRole, [RuntimeRole]),
                new(PrivacyRoutine, "uuid,uuid", OwnerRole, [RuntimeRole])],
            sql.Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Key)!.GetTableName()!, OwnerRole, [],
                source.Value.Order(StringComparer.Ordinal).Select(property => new PostgresPrivilegeGuard.Column(
                    sql.Names.ColumnName(source.Key, property), source.Key != typeof(PrivacyErasureSaga)
                        && property == sql.LockProperty(source.Key)
                        ? ["SELECT", "UPDATE"] : ["SELECT"])).ToArray())).ToArray(),
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(privileges) + SourceGuard(context, sql, enrollment)
            + PolicyGuard(sql, policies) + CallerPrivilegeGuard(sql, enrollment);
        string body = $"""
            BEGIN
                {guard}
                {Caller(enrollment, Text(OwnerRole), "p_tenant")}
                IF p_principal IS NULL OR p_principal = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_privacy IS NULL OR p_organizer IS NULL OR p_privacy AND p_organizer
                    OR p_organizer AND p_admitted IS NULL
                THEN RAISE EXCEPTION 'ai_conversation_owner_required' USING ERRCODE = '42501'; END IF;
                {work}
            END;
            """;
        string privacyBody = $"""
            BEGIN
                {guard}
                {Caller(enrollment, Text(OwnerRole), "p_tenant")}
                IF p_principal IS NULL OR p_principal = '00000000-0000-0000-0000-000000000000'::uuid
                THEN RAISE EXCEPTION 'ai_conversation_owner_required' USING ERRCODE = '42501'; END IF;
                {privacyWork}
            END;
            """;
        return new(sql, privileges, policies, guard, body, privacyBody);
    }

    private sealed class SourceSql(ExploreDbContext context)
    {
        internal HumanPrincipalSourceSql Names { get; } = new(context);
        internal Dictionary<Type, HashSet<string>> Sources { get; } = [];
        internal string Q(string value) => Names.Quote(value);
        internal string T<T>() => Names.Table(typeof(T));
        internal string C<T>(string alias, string property)
        {
            if (context.Model.FindEntityType(typeof(T))?.FindProperty(property) is null)
                throw new InvalidOperationException($"ai_conversation_owner_unmapped_source:{typeof(T).Name}.{property}");
            if (!Sources.TryGetValue(typeof(T), out var fields)) Sources.Add(typeof(T), fields = []);
            fields.Add(property);
            return Q(alias) + "." + Names.Column(typeof(T), property);
        }
        internal string LockProperty(Type type) => context.Model.FindEntityType(type)!.FindPrimaryKey()!.Properties[0].Name;
        internal string Literal<T>(string property, object value) => context.Model.FindEntityType(typeof(T))!
            .FindProperty(property)!.GetRelationalTypeMapping().GenerateSqlLiteral(value);

        internal IReadOnlyDictionary<Type, string> Policies(ExploreDbContext owner)
        {
            var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(owner.Model);
            return Sources.Keys.ToDictionary(type => type, type =>
            {
                var entity = owner.Model.FindEntityType(type)!;
                var tenant = tenantTables.SingleOrDefault(table => table.TableName == entity.GetTableName());
                if (tenant?.ParentJoin is not null || tenant?.AllowsNullTenant is true)
                    throw new InvalidOperationException("ai_conversation_owner_source_tenancy_changed");
                return tenant is null ? "true"
                    : $"{Q(tenant.TenantIdColumn!)} = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid";
            });
        }
    }

    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
