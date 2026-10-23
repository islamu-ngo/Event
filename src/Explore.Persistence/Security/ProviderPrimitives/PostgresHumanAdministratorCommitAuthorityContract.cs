using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed execute-only human administration fences. Installation is migration-owned;
/// execution borrows the business owner's existing Serializable transaction.
/// </summary>
public static partial class PostgresHumanAdministratorCommitAuthorityContract
{
    public const string OwnerRole = "event_human_admin_fence_owner";
    public const string MigratorRole = "event_human_admin_fence_migrator";
    public const string RuntimeRole = "event_human_admin_fence_runtime";
    public const string GlobalRole = "event_human_admin_fence_global";
    public const string InstanceRole = "event_human_admin_fence_email_instance";
    public const string TenantRole = "event_human_admin_fence_email_tenant";
    private const string InstanceName = "human_administrator_instance_fence";
    private const string TenantName = "human_administrator_tenant_fence";
    private const string SubjectName = "human_administrator_subject_fence";
    private static readonly (string Name, bool Tenant, bool SubjectOnly)[] Functions =
        [(InstanceName, false, false), (TenantName, true, false), (SubjectName, false, true)];
    private const string Signature = "uuid,integer,text,integer,uuid";
    private const string Arguments = "p_actor uuid, p_provider integer, p_account text, p_purpose integer, p_tenant uuid";
    private const string Stamp = "human-administrator-fence:v1:";
    private const string ReadPolicy = "human_administrator_fence_read";
    private const string UpdatePolicy = "human_administrator_fence_update";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole, GlobalRole, InstanceRole, TenantRole];

    private static readonly (Type Type, string[] Columns)[] Sources =
    [
        (typeof(User), ["Id", "IsDeleted"]),
        (typeof(UserExternalLogin), ["Id", "UserId", "AuthenticationProviderId", "ProviderKey"]),
        (typeof(PlatformUserRole), ["Id", "UserId", "RoleId"]),
        (typeof(Role), ["Id", "RoleScopeId", "MasterCode"]),
        (typeof(Tenant), ["Id", "TenantStatusId"]),
        (typeof(TenantStatus), ["Id", "IsActiveState"]),
        (typeof(TenantUser), ["Id", "TenantId", "UserId", "StatusId", "IsDeleted"]),
        (typeof(TenantUserRoleGrant), ["Id", "TenantId", "TenantUserId", "RoleId", "RoleScopeId", "RevokedAt"])
    ];

    public static string InstanceFunctionSql(ExploreDbContext context) => Function(context, InstanceName);
    public static string TenantFunctionSql(ExploreDbContext context) => Function(context, TenantName);
    public static string SubjectFunctionSql(ExploreDbContext context) => Function(context, SubjectName);

    public static async Task ApplyAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = BuildSql(context, enrollment);
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null)
            await owned.CommitAsync(cancellationToken);
    }

    internal static string AdmissionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"DO $admission$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $admission$;";

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(User))!.GetSchema() ?? "public";

    private static string Identifier(ExploreDbContext context, string name) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name);

    private static string Function(ExploreDbContext context, string name) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, Schema(context));

    private static string Table(ExploreDbContext context, Type type) =>
        Function(context, context.Model.FindEntityType(type)!.GetTableName()!);

    private static string ColumnName(ExploreDbContext context, Type type, string property)
    {
        var entity = context.Model.FindEntityType(type)!;
        return entity.FindProperty(property)!.GetColumnName(
            StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }

    private static string Column(ExploreDbContext context, Type type, string property) =>
        Identifier(context, ColumnName(context, type, property));

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool tenant,
        bool subjectOnly = false)
    {
        string T<T>() => Table(context, typeof(T));
        string C<T>(string property) => Column(context, typeof(T), property);
        string principal = $$"""
            IF p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
                OR (p_provider IS NULL) <> (p_account IS NULL) OR p_account = '' THEN
                EXIT sources;
            END IF;
            -- FOR UPDATE, not a no-op UPDATE: FK KEY SHARE must conflict with new contributors.
            PERFORM u.{{C<User>("Id")}} FROM {{T<User>()}} u
                WHERE u.{{C<User>("Id")}} = p_actor FOR UPDATE;
            IF NOT EXISTS (SELECT 1 FROM {{T<User>()}} u
                WHERE u.{{C<User>("Id")}} = p_actor AND NOT u.{{C<User>("IsDeleted")}}) THEN
                EXIT sources;
            END IF;
            IF p_provider IS NOT NULL THEN
                PERFORM l.{{C<UserExternalLogin>("Id")}} FROM {{T<UserExternalLogin>()}} l
                    WHERE l.{{C<UserExternalLogin>("UserId")}} = p_actor
                        AND l.{{C<UserExternalLogin>("AuthenticationProviderId")}} = p_provider
                        AND l.{{C<UserExternalLogin>("ProviderKey")}} = p_account
                    ORDER BY l.{{C<UserExternalLogin>("Id")}} FOR UPDATE;
                IF (SELECT count(*) FROM {{T<UserExternalLogin>()}} l
                    WHERE l.{{C<UserExternalLogin>("UserId")}} = p_actor
                        AND l.{{C<UserExternalLogin>("AuthenticationProviderId")}} = p_provider
                        AND l.{{C<UserExternalLogin>("ProviderKey")}} = p_account) <> 1 THEN
                    EXIT sources;
                END IF;
            END IF;
            """;
        string instance = $$"""
            PERFORM a.{{C<PlatformUserRole>("Id")}} FROM {{T<PlatformUserRole>()}} a
                WHERE a.{{C<PlatformUserRole>("UserId")}} = p_actor
                ORDER BY a.{{C<PlatformUserRole>("Id")}} FOR UPDATE;
            PERFORM r.{{C<Role>("Id")}} FROM {{T<Role>()}} r
                WHERE r.{{C<Role>("Id")}} IN (SELECT a.{{C<PlatformUserRole>("RoleId")}}
                    FROM {{T<PlatformUserRole>()}} a WHERE a.{{C<PlatformUserRole>("UserId")}} = p_actor)
                ORDER BY r.{{C<Role>("Id")}} FOR UPDATE;
            v_allowed := EXISTS (SELECT 1 FROM {{T<PlatformUserRole>()}} a
                JOIN {{T<Role>()}} r ON r.{{C<Role>("Id")}} = a.{{C<PlatformUserRole>("RoleId")}}
                WHERE a.{{C<PlatformUserRole>("UserId")}} = p_actor
                    AND r.{{C<Role>("RoleScopeId")}} = {{(int)RoleScopeEnum.Platform}}
                    AND r.{{C<Role>("MasterCode")}} = 'platform.admin');
            """;
        string tenantFence = $$"""
            PERFORM t.{{C<Tenant>("Id")}} FROM {{T<Tenant>()}} t
                WHERE t.{{C<Tenant>("Id")}} = p_tenant FOR UPDATE;
            PERFORM s.{{C<TenantStatus>("Id")}} FROM {{T<TenantStatus>()}} s
                WHERE s.{{C<TenantStatus>("Id")}} IN (SELECT t.{{C<Tenant>("TenantStatusId")}}
                    FROM {{T<Tenant>()}} t WHERE t.{{C<Tenant>("Id")}} = p_tenant)
                ORDER BY s.{{C<TenantStatus>("Id")}} FOR UPDATE;
            -- Include inactive/deleted candidates: activation must not evade the fenced set.
            PERFORM m.{{C<TenantUser>("Id")}} FROM {{T<TenantUser>()}} m
                WHERE m.{{C<TenantUser>("TenantId")}} = p_tenant AND m.{{C<TenantUser>("UserId")}} = p_actor
                ORDER BY m.{{C<TenantUser>("Id")}} FOR UPDATE;
            PERFORM g.{{C<TenantUserRoleGrant>("Id")}} FROM {{T<TenantUserRoleGrant>()}} g
                WHERE g.{{C<TenantUserRoleGrant>("TenantId")}} = p_tenant
                    AND g.{{C<TenantUserRoleGrant>("TenantUserId")}} IN (
                        SELECT m.{{C<TenantUser>("Id")}} FROM {{T<TenantUser>()}} m
                        WHERE m.{{C<TenantUser>("TenantId")}} = p_tenant AND m.{{C<TenantUser>("UserId")}} = p_actor)
                ORDER BY g.{{C<TenantUserRoleGrant>("Id")}} FOR UPDATE;
            PERFORM r.{{C<Role>("Id")}} FROM {{T<Role>()}} r
                WHERE r.{{C<Role>("Id")}} IN (SELECT g.{{C<TenantUserRoleGrant>("RoleId")}}
                    FROM {{T<TenantUserRoleGrant>()}} g
                    JOIN {{T<TenantUser>()}} m ON m.{{C<TenantUser>("Id")}} = g.{{C<TenantUserRoleGrant>("TenantUserId")}}
                        AND m.{{C<TenantUser>("TenantId")}} = g.{{C<TenantUserRoleGrant>("TenantId")}}
                    WHERE m.{{C<TenantUser>("TenantId")}} = p_tenant AND m.{{C<TenantUser>("UserId")}} = p_actor)
                ORDER BY r.{{C<Role>("Id")}} FOR UPDATE;
            v_allowed := EXISTS (SELECT 1 FROM {{T<TenantUser>()}} m
                JOIN {{T<Tenant>()}} t ON t.{{C<Tenant>("Id")}} = m.{{C<TenantUser>("TenantId")}}
                JOIN {{T<TenantStatus>()}} s ON s.{{C<TenantStatus>("Id")}} = t.{{C<Tenant>("TenantStatusId")}}
                JOIN {{T<TenantUserRoleGrant>()}} g ON g.{{C<TenantUserRoleGrant>("TenantUserId")}} = m.{{C<TenantUser>("Id")}}
                    AND g.{{C<TenantUserRoleGrant>("TenantId")}} = m.{{C<TenantUser>("TenantId")}}
                JOIN {{T<Role>()}} r ON r.{{C<Role>("Id")}} = g.{{C<TenantUserRoleGrant>("RoleId")}}
                WHERE m.{{C<TenantUser>("TenantId")}} = p_tenant AND m.{{C<TenantUser>("UserId")}} = p_actor
                    AND NOT m.{{C<TenantUser>("IsDeleted")}}
                    AND m.{{C<TenantUser>("StatusId")}} = {{(int)TenantUserStatusEnum.Active}}
                    AND s.{{C<TenantStatus>("IsActiveState")}}
                    AND g.{{C<TenantUserRoleGrant>("RevokedAt")}} IS NULL
                    AND g.{{C<TenantUserRoleGrant>("RoleId")}} = {{(int)RoleEnum.TenantAdmin}}
                    AND g.{{C<TenantUserRoleGrant>("RoleScopeId")}} = {{(int)RoleScopeEnum.Tenant}}
                    AND r.{{C<Role>("RoleScopeId")}} = {{(int)RoleScopeEnum.Tenant}});
            """;
        string caller = CallerGuard(enrollment, tenant);
        string guard = Guard(context, enrollment);
        return $"""
            DECLARE v_allowed boolean := false;
            BEGIN
                {guard}
                {caller}
                <<sources>>
                BEGIN
                    {principal}
                    {(subjectOnly ? "v_allowed := true;" : tenant ? tenantFence : instance)}
                END sources;
                {guard}
                {caller}
                RETURN v_allowed;
            END;
            """;
    }

    private static string CallerGuard(PostgresNativeQueueEnrollment enrollment, bool tenant) => $$"""
        {{PostgresPrivilegeGuard.RestrictedCallerSql}}
        IF current_user <> '{{OwnerRole}}'
            OR pg_catalog.current_setting('role') NOT IN ('none', session_user::text)
            OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id', true), '')::uuid IS DISTINCT FROM p_tenant
            OR NOT COALESCE((
                (p_purpose = 1 AND session_user = {{Text(enrollment.RuntimeLogin)}} AND p_tenant IS NOT NULL)
                OR (p_purpose = 4 AND session_user = {{Text(enrollment.EmailTenantMutationLogin)}} AND p_tenant IS NOT NULL)
                {{(tenant ? "" : $"""
                OR (p_purpose IN (2,5) AND session_user = {Text(enrollment.GlobalLogin)} AND p_tenant IS NULL)
                OR (p_purpose = 3 AND session_user = {Text(enrollment.EmailInstanceMutationLogin)} AND p_tenant IS NULL)
                """)}}
            ), false)
            OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user, r.oid, 'USAGE')
                    OR pg_catalog.pg_has_role(session_user, r.oid, 'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN
            RAISE EXCEPTION 'human_administrator_caller_mismatch' USING ERRCODE = '42501';
        END IF;
        """;
}
