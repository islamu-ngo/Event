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

/// <summary>Execute-only own publisher memberships, including unavailable options, on the original Runtime owner.</summary>
public static partial class PostgresEventCreationPublisherReadContract
{
    public const string OwnerRole = "event_creation_publisher_owner";
    public const string MigratorRole = "event_creation_publisher_migrator";
    public const string RuntimeRole = "event_creation_publisher_runtime";
    private const string Routine = "event_creation_publisher_read";
    private const string Policy = "event_creation_publisher_read";
    private const string Stamp = "event-creation-publisher:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];

    public static string FunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Routine);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("event_creation_publisher_requires_postgresql");
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
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Publisher reads require the original open PostgreSQL connection.");
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

    internal static async Task<(IReadOnlyList<OrganizationMember> Organizations, IReadOnlyList<GroupMember> Groups,
        IReadOnlyList<RolePermission> Permissions)> ReadAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, Guid tenantId, Guid principalId, CancellationToken cancellationToken)
    {
        if (context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
            || tenantId == Guid.Empty || principalId == Guid.Empty
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new UnauthorizedAccessException("Publisher reads require their original Runtime tenant transaction.");
        await RequireReadyAsync(context, enrollment, cancellationToken);
        await using var command = Command(context);
        command.CommandText = $"SELECT * FROM {FunctionSql(context)}(@tenant,@principal)";
        ((NpgsqlCommand)command).Parameters.AddWithValue("tenant", NpgsqlDbType.Uuid, tenantId);
        ((NpgsqlCommand)command).Parameters.AddWithValue("principal", NpgsqlDbType.Uuid, principalId);
        var organizations = new List<OrganizationMember>();
        var groups = new List<GroupMember>();
        var permissions = new Dictionary<(int Role, int Permission), RolePermission>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Guid member = reader.GetGuid(1);
            Guid placement = reader.GetGuid(2);
            Guid publisher = reader.GetGuid(3);
            int role = reader.GetInt32(4);
            string name = reader.GetString(5);
            if (reader.GetBoolean(0))
                organizations.Add(new OrganizationMember
                {
                    Id = member,
                    TenantId = tenantId,
                    Tenant = null!,
                    UserId = principalId,
                    User = null!,
                    RoleId = role,
                    Role = null!,
                    OrganizationTenantId = placement,
                    OrganizationTenant = new OrganizationTenant
                    {
                        Id = placement,
                        TenantId = tenantId,
                        Tenant = null!,
                        ApprovalStatus = null!,
                        OrganizationId = publisher,
                        Organization = new Organization
                        {
                            Id = publisher,
                            Pii = new OrganizationPii { OrganizationId = publisher, FullName = name }
                        }
                    }
                });
            else
                groups.Add(new GroupMember
                {
                    Id = member,
                    TenantId = tenantId,
                    Tenant = null!,
                    UserId = principalId,
                    User = null!,
                    RoleId = role,
                    Role = null!,
                    GroupTenantId = placement,
                    GroupTenant = new GroupTenant
                    {
                        Id = placement,
                        TenantId = tenantId,
                        Tenant = null!,
                        ApprovalStatus = null!,
                        GroupId = publisher,
                        Group = new Group { Id = publisher, FullName = name }
                    }
                });
            if (!reader.IsDBNull(6))
            {
                int permission = reader.GetInt32(6);
                permissions.TryAdd((role, permission), new RolePermission
                {
                    RoleId = role,
                    Role = null!,
                    PermissionId = permission,
                    Permission = new Permission
                    {
                        Id = permission,
                        MasterCode = PermissionCodes.EventCreate,
                        IsActive = true,
                        ResourceKind = null!,
                        Action = null!,
                        FullName = null!,
                        GroupName = null!
                    }
                });
            }
        }
        return (organizations.AsReadOnly(), groups.AsReadOnly(), permissions.Values.ToList().AsReadOnly());
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
        THEN RAISE EXCEPTION 'event_creation_publisher_caller_mismatch' USING ERRCODE = '42501'; END IF;
        """;

    private sealed record Definition(SourceSql Sql, PostgresPrivilegeGuard.Contract Privileges,
        IReadOnlyDictionary<Type, string> Policies, string Guard, string Body);

    private static Definition Define(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.RuntimeLogin, enrollment.GlobalLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("event_creation_publisher_enrollment_collision");
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
            + PolicyGuard(sql, policies) + CallerPrivilegeGuard(context, sql, enrollment);
        string body = $"""
            BEGIN
                {guard}
                {Caller(enrollment, Text(OwnerRole), "p_tenant")}
                IF p_principal IS NULL OR p_principal = '00000000-0000-0000-0000-000000000000'::uuid
                THEN RAISE EXCEPTION 'event_creation_publisher_principal_required' USING ERRCODE = '42501'; END IF;
                RETURN QUERY {query};
            END;
            """;
        return new(sql, privileges, policies, guard, body);
    }

    private static string Query(SourceSql s)
    {
        string Memberships(bool organization)
        {
            string M(string property) => organization ? s.C<OrganizationMember>("m", property) : s.C<GroupMember>("m", property);
            string P(string property) => organization ? s.C<OrganizationTenant>("pt", property) : s.C<GroupTenant>("pt", property);
            string O(string property) => organization ? s.C<Organization>("o", property) : s.C<Group>("o", property);
            string member = organization ? s.T<OrganizationMember>() : s.T<GroupMember>();
            string placement = organization ? s.T<OrganizationTenant>() : s.T<GroupTenant>();
            string owner = organization ? s.T<Organization>() : s.T<Group>();
            string name = organization ? s.C<OrganizationPii>("pii", "FullName") : O("FullName");
            return $"""
                SELECT {Boolean(organization)}, {M("Id")}, {P("Id")}, {O("Id")}, {M("RoleId")}, {name}::text,
                    (SELECT {s.C<Permission>("p", "Id")} FROM {s.T<RolePermission>()} rp
                        JOIN {s.T<Permission>()} p ON {s.C<Permission>("p", "Id")} = {s.C<RolePermission>("rp", "PermissionId")}
                        WHERE {s.C<RolePermission>("rp", "RoleId")} = {M("RoleId")}
                            AND {s.C<Permission>("p", "IsActive")}
                            AND {s.C<Permission>("p", "MasterCode")} = {s.Literal<Permission>("MasterCode", PermissionCodes.EventCreate)})
                FROM {member} m
                JOIN {placement} pt ON {P("Id")} = {M(organization ? "OrganizationTenantId" : "GroupTenantId")}
                JOIN {owner} o ON {O("Id")} = {P(organization ? "OrganizationId" : "GroupId")}
                {(organization ? $"LEFT JOIN {s.T<OrganizationPii>()} pii ON {s.C<OrganizationPii>("pii", "OrganizationId")} = {O("Id")}" : "")}
                WHERE {M("TenantId")} = p_tenant AND {M("UserId")} = p_principal AND NOT {M("IsDeleted")}
                    AND {P("TenantId")} = p_tenant AND NOT {P("IsDeleted")} AND NOT {O("IsDeleted")}
                    AND EXISTS (SELECT 1 FROM {s.T<User>()} u WHERE {s.C<User>("u", "Id")} = p_principal AND NOT {s.C<User>("u", "IsDeleted")})
                    AND EXISTS (SELECT 1 FROM {s.T<TenantUser>()} tu
                        JOIN {s.T<Tenant>()} t ON {s.C<Tenant>("t", "Id")} = {s.C<TenantUser>("tu", "TenantId")}
                        JOIN {s.T<TenantStatus>()} ts ON {s.C<TenantStatus>("ts", "Id")} = {s.C<Tenant>("t", "TenantStatusId")}
                        WHERE {s.C<TenantUser>("tu", "TenantId")} = p_tenant AND {s.C<TenantUser>("tu", "UserId")} = p_principal
                            AND NOT {s.C<TenantUser>("tu", "IsDeleted")} AND {s.C<TenantUser>("tu", "StatusId")} = {(int)TenantUserStatusEnum.Active}
                            AND {s.C<Tenant>("t", "TenantStatusId")} = {(int)TenantStatusEnum.Active} AND {s.C<TenantStatus>("ts", "IsActiveState")})
                """;
        }
        return Memberships(true) + "\nUNION ALL\n" + Memberships(false) + "\nORDER BY 1 DESC, 2";
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
                throw new InvalidOperationException($"event_creation_publisher_unmapped_source:{typeof(T).Name}.{property}");
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
                    throw new InvalidOperationException("event_creation_publisher_source_tenancy_changed");
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
