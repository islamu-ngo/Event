using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only actor selection on the original Runtime Serializable owner. Installation
/// belongs to the migrator; reads never enroll, repair, reconnect, or retry.
/// </summary>
public static partial class PostgresAiActorContextReadContract
{
    public const string OwnerRole = "event_ai_actor_context_owner";
    public const string MigratorRole = "event_ai_actor_context_migrator";
    public const string RuntimeRole = "event_ai_actor_context_runtime";
    internal const int MaximumActors = 1000;
    private const string Routine = "ai_actor_context_read";
    private const string Policy = "ai_actor_context_read";
    private const string Stamp = "ai-actor-context:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];

    public static string FunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Routine);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("ai_actor_context_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = Command(context);
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        RequireConnection(context);
        var definition = Define(context, enrollment);
        string tenant = context.TenantFilterTenantId is { } id ? Text(id.ToString()) + "::uuid" : "NULL::uuid";
        await using var command = Command(context);
        command.CommandText = $"""
            DO $ready$ BEGIN
                {definition.Guard} {ExactBody(context, definition.Body)}
                {Caller(enrollment, "session_user", tenant)}
            END $ready$;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<IReadOnlyList<Actor>> ReadAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, Guid tenantId, Guid principalId, CancellationToken cancellationToken)
    {
        if (context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
            || tenantId == Guid.Empty || principalId == Guid.Empty
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new UnauthorizedAccessException("Actor selection requires its original Runtime tenant transaction.");
        await RequireReadyAsync(context, enrollment, cancellationToken);
        await using var command = Command(context);
        command.CommandText = $"SELECT * FROM {FunctionSql(context)}(@tenant,@principal)";
        ((NpgsqlCommand)command).Parameters.AddWithValue("tenant", NpgsqlDbType.Uuid, tenantId);
        ((NpgsqlCommand)command).Parameters.AddWithValue("principal", NpgsqlDbType.Uuid, principalId);
        var actors = new List<Actor>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (actors.Count == MaximumActors)
                    throw new InvalidOperationException("ai_actor_context_overflow");
                actors.Add(new Actor
                {
                    Id = reader.GetGuid(0),
                    ActorTypeId = reader.GetInt32(1),
                    ActorType = null!,
                    UserId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    OrganizationId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    GroupId = reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    Pii = new ActorPii { ActorId = reader.GetGuid(0), DisplayName = reader.GetString(5) },
                    Organization = reader.IsDBNull(3) ? null : new Organization
                    {
                        Id = reader.GetGuid(3),
                        Pii = new OrganizationPii { OrganizationId = reader.GetGuid(3), FullName = reader.GetString(6) }
                    },
                    Group = reader.IsDBNull(4) ? null : new Group { Id = reader.GetGuid(4), FullName = reader.GetString(7) }
                });
            }
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.ProgramLimitExceeded
            && error.MessageText == "ai_actor_context_overflow")
        {
            throw new InvalidOperationException("ai_actor_context_overflow", error);
        }
        return actors.AsReadOnly();
    }

    private static void RequireConnection(ExploreDbContext context)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Actor selection requires the original open PostgreSQL connection.");
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
        THEN RAISE EXCEPTION 'ai_actor_context_caller_mismatch' USING ERRCODE = '42501'; END IF;
        """;

    private sealed record Definition(SourceSql Sql, PostgresPrivilegeGuard.Contract Privileges,
        IReadOnlyDictionary<Type, string> Policies, string Guard, string Body);

    private static Definition Define(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.RuntimeLogin, enrollment.GlobalLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("ai_actor_context_enrollment_collision");
        var sql = new SourceSql(context);
        string query = Query(sql);
        var policies = sql.Policies(context);
        var privileges = new PostgresPrivilegeGuard.Contract(sql.Names.Schema, Roles,
            [new(Routine, "uuid,uuid", OwnerRole, [RuntimeRole])],
            sql.Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Key)!.GetTableName()!, OwnerRole, [],
                source.Value.Order(StringComparer.Ordinal).Select(property => new PostgresPrivilegeGuard.Column(
                    sql.Names.ColumnName(source.Key, property), ["SELECT"])).ToArray())).ToArray(),
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(privileges) + SourceGuard(context, sql, enrollment)
            + PolicyGuard(sql, policies) + CallerPrivilegeGuard(sql, enrollment);
        string body = $"""
            DECLARE v_count integer := 0;
            BEGIN
                {guard}
                {Caller(enrollment, Text(OwnerRole), "p_tenant")}
                IF p_principal IS NULL OR p_principal = '00000000-0000-0000-0000-000000000000'::uuid
                THEN RAISE EXCEPTION 'ai_actor_context_principal_required' USING ERRCODE = '42501'; END IF;
                FOR actor_id, actor_type_id, user_id, organization_id, group_id, display_name, organization_name, group_name IN {query} LOOP
                    v_count := v_count + 1;
                    IF v_count > {MaximumActors}
                    THEN RAISE EXCEPTION 'ai_actor_context_overflow' USING ERRCODE = '54000'; END IF;
                    RETURN NEXT;
                END LOOP;
                RETURN;
            END;
            """;
        return new(sql, privileges, policies, guard, body);
    }

    private static string Query(SourceSql s)
    {
        string Grant(string memberAlias, string placementAlias, bool organization)
        {
            string member = organization ? s.T<OrganizationMember>() : s.T<GroupMember>();
            string placement = organization ? s.T<OrganizationTenant>() : s.T<GroupTenant>();
            string M(string property) => organization ? s.C<OrganizationMember>(memberAlias, property) : s.C<GroupMember>(memberAlias, property);
            string P(string property) => organization ? s.C<OrganizationTenant>(placementAlias, property) : s.C<GroupTenant>(placementAlias, property);
            return $"""
                EXISTS (SELECT 1 FROM {member} {s.Q(memberAlias)}
                    JOIN {placement} {s.Q(placementAlias)} ON {P("Id")} = {M(organization ? "OrganizationTenantId" : "GroupTenantId")}
                    JOIN {s.T<Role>()} r ON {s.C<Role>("r", "Id")} = {M("RoleId")}
                    JOIN {s.T<RolePermission>()} rp ON {s.C<RolePermission>("rp", "RoleId")} = {s.C<Role>("r", "Id")}
                    JOIN {s.T<Permission>()} p ON {s.C<Permission>("p", "Id")} = {s.C<RolePermission>("rp", "PermissionId")}
                    WHERE {M("TenantId")} = p_tenant AND {M("UserId")} = p_principal AND NOT {M("IsDeleted")}
                        AND {P("TenantId")} = p_tenant AND NOT {P("IsDeleted")} AND NOT {P("IsSuspended")}
                        AND {P("ApprovalStatusId")} = {(int)ApprovalStatusEnum.Approved}
                        AND {P(organization ? "OrganizationId" : "GroupId")} = {s.C<Actor>("a", organization ? "OrganizationId" : "GroupId")}
                        AND {s.C<Role>("r", "RoleScopeId")} = {(int)(organization ? RoleScopeEnum.Organization : RoleScopeEnum.Group)}
                        AND {s.C<Permission>("p", "IsActive")}
                        AND {s.C<Permission>("p", "MasterCode")} = {s.Literal<Permission>("MasterCode", PermissionCodes.EventCreate)})
                """;
        }
        return $"""
            SELECT {s.C<Actor>("a", "Id")}, {s.C<Actor>("a", "ActorTypeId")},
                {s.C<Actor>("a", "UserId")}, {s.C<Actor>("a", "OrganizationId")}, {s.C<Actor>("a", "GroupId")},
                {s.C<ActorPii>("ap", "DisplayName")}, {s.C<OrganizationPii>("op", "FullName")}, {s.C<Group>("g", "FullName")}
            FROM {s.T<Actor>()} a
            JOIN {s.T<ActorPii>()} ap ON {s.C<ActorPii>("ap", "ActorId")} = {s.C<Actor>("a", "Id")}
            LEFT JOIN {s.T<Organization>()} o ON {s.C<Organization>("o", "Id")} = {s.C<Actor>("a", "OrganizationId")}
            LEFT JOIN {s.T<OrganizationPii>()} op ON {s.C<OrganizationPii>("op", "OrganizationId")} = {s.C<Organization>("o", "Id")}
            LEFT JOIN {s.T<Group>()} g ON {s.C<Group>("g", "Id")} = {s.C<Actor>("a", "GroupId")}
            WHERE NOT {s.C<Actor>("a", "IsDeleted")} AND NOT {s.C<Actor>("a", "IsSuspended")}
                AND {s.C<Actor>("a", "ExternalActorSubjectId")} IS NULL AND {s.C<Actor>("a", "ServicePrincipalId")} IS NULL
                AND EXISTS (SELECT 1 FROM {s.T<User>()} u WHERE {s.C<User>("u", "Id")} = p_principal AND NOT {s.C<User>("u", "IsDeleted")})
                AND EXISTS (SELECT 1 FROM {s.T<TenantUser>()} tu
                    JOIN {s.T<Tenant>()} t ON {s.C<Tenant>("t", "Id")} = {s.C<TenantUser>("tu", "TenantId")}
                    JOIN {s.T<TenantStatus>()} ts ON {s.C<TenantStatus>("ts", "Id")} = {s.C<Tenant>("t", "TenantStatusId")}
                    WHERE {s.C<TenantUser>("tu", "TenantId")} = p_tenant AND {s.C<TenantUser>("tu", "UserId")} = p_principal
                        AND NOT {s.C<TenantUser>("tu", "IsDeleted")} AND {s.C<TenantUser>("tu", "StatusId")} = {(int)TenantUserStatusEnum.Active}
                        AND {s.C<Tenant>("t", "TenantStatusId")} = {(int)TenantStatusEnum.Active} AND {s.C<TenantStatus>("ts", "IsActiveState")})
                AND (({s.C<Actor>("a", "ActorTypeId")} = {(int)ActorTypeEnum.User} AND {s.C<Actor>("a", "UserId")} = p_principal
                        AND {s.C<Actor>("a", "OrganizationId")} IS NULL AND {s.C<Actor>("a", "GroupId")} IS NULL)
                    OR ({s.C<Actor>("a", "ActorTypeId")} = {(int)ActorTypeEnum.Organization}
                        AND {s.C<Actor>("a", "UserId")} IS NULL AND {s.C<Actor>("a", "GroupId")} IS NULL
                        AND NOT {s.C<Organization>("o", "IsDeleted")} AND {s.C<OrganizationPii>("op", "FullName")} IS NOT NULL AND {Grant("om", "ot", true)})
                    OR ({s.C<Actor>("a", "ActorTypeId")} = {(int)ActorTypeEnum.Group}
                        AND {s.C<Actor>("a", "UserId")} IS NULL AND {s.C<Actor>("a", "OrganizationId")} IS NULL
                        AND NOT {s.C<Group>("g", "IsDeleted")} AND {Grant("gm", "gt", false)}))
            ORDER BY {s.C<Actor>("a", "Id")} LIMIT {MaximumActors + 1}
            """;
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
                throw new InvalidOperationException($"ai_actor_context_unmapped_source:{typeof(T).Name}.{property}");
            if (!Sources.TryGetValue(typeof(T), out var fields)) Sources.Add(typeof(T), fields = []);
            fields.Add(property);
            return Q(alias) + "." + Names.Column(typeof(T), property);
        }
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
                    throw new InvalidOperationException("ai_actor_context_source_tenancy_changed");
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
