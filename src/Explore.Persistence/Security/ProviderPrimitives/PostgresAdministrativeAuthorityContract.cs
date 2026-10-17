using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Execute-only discovery of primitive administrative scopes for an exact internal user.</summary>
public static class PostgresAdministrativeAuthorityContract
{
    public const string OwnerRole = "event_admin_authority_owner";
    public const string MigratorRole = "event_admin_authority_migrator";
    public const string GlobalRole = "event_admin_authority_global";

    public static string InstanceFunctionSql(ExploreDbContext context) =>
        Function(context, typeof(PlatformUserRole), "_is_admin_for_user");
    public static string TenantFunctionSql(ExploreDbContext context) =>
        Function(context, typeof(TenantUserRoleGrant), "_admin_tenants_for_user");
    public static string OrganizationFunctionSql(ExploreDbContext context) =>
        Function(context, typeof(OrganizationMember), "_admin_organizations_for_user");
    public static string GroupFunctionSql(ExploreDbContext context) =>
        Function(context, typeof(GroupMember), "_admin_groups_for_user");

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        IEntityType Entity(Type type) => context.Model.FindEntityType(type)!;
        string Table(Type type)
        {
            var entity = Entity(type);
            return sql.DelimitIdentifier(entity.GetTableName()!, Schema(context, entity));
        }
        string Column(Type type, string property)
        {
            var entity = Entity(type);
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }
        string C<T>(string property) => Column(typeof(T), property);
        (Type Type, string[] Columns)[] grants =
        [
            (typeof(PlatformUserRole), [nameof(PlatformUserRole.UserId), nameof(PlatformUserRole.RoleId)]),
            (typeof(Role), [nameof(Role.Id), nameof(Role.RoleScopeId), nameof(Role.MasterCode)]),
            (typeof(TenantUserRoleGrant), [nameof(TenantUserRoleGrant.TenantId), nameof(TenantUserRoleGrant.TenantUserId),
                nameof(TenantUserRoleGrant.RoleId), nameof(TenantUserRoleGrant.RevokedAt)]),
            (typeof(TenantUser), [nameof(TenantUser.Id), nameof(TenantUser.TenantId), nameof(TenantUser.UserId), nameof(TenantUser.StatusId), nameof(TenantUser.IsDeleted)]),
            (typeof(OrganizationMember), [nameof(OrganizationMember.UserId), nameof(OrganizationMember.TenantId),
                nameof(OrganizationMember.RoleId), nameof(OrganizationMember.OrganizationTenantId), nameof(OrganizationMember.IsDeleted)]),
            (typeof(OrganizationTenant), [nameof(OrganizationTenant.Id), nameof(OrganizationTenant.TenantId), nameof(OrganizationTenant.OrganizationId), nameof(OrganizationTenant.IsDeleted)]),
            (typeof(Organization), [nameof(Organization.Id), nameof(Organization.IsDeleted)]),
            (typeof(GroupMember), [nameof(GroupMember.UserId), nameof(GroupMember.TenantId),
                nameof(GroupMember.RoleId), nameof(GroupMember.GroupTenantId), nameof(GroupMember.IsDeleted)]),
            (typeof(GroupTenant), [nameof(GroupTenant.Id), nameof(GroupTenant.TenantId), nameof(GroupTenant.GroupId), nameof(GroupTenant.IsDeleted)]),
            (typeof(Group), [nameof(Group.Id), nameof(Group.IsDeleted)])
        ];
        string tableGrants = string.Join("\n", grants.Select(grant => $"""
            REVOKE ALL ON TABLE {Table(grant.Type)} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({string.Join(", ", grant.Columns.Select(column => Column(grant.Type, column)))})
                ON {Table(grant.Type)} TO {OwnerRole};
            """));
        Type[] tenantTables = [typeof(TenantUserRoleGrant), typeof(TenantUser), typeof(OrganizationMember),
            typeof(OrganizationTenant), typeof(GroupMember), typeof(GroupTenant)];
        string policies = string.Join("\n", tenantTables.Select(type => $"""
            DROP POLICY IF EXISTS administrative_authority_read ON {Table(type)};
            CREATE POLICY administrative_authority_read ON {Table(type)} FOR SELECT TO {OwnerRole} USING (true);
            """));
        string schema = sql.DelimitIdentifier(Schema(context, Entity(typeof(PlatformUserRole))));
        string instance = InstanceFunctionSql(context);
        string tenants = TenantFunctionSql(context);
        string organizations = OrganizationFunctionSql(context);
        string groups = GroupFunctionSql(context);
        string[] signatures = [$"{instance}(uuid)", $"{tenants}(uuid)", $"{organizations}(uuid, uuid)", $"{groups}(uuid, uuid)"];
        string priorAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            signatures, OwnerRole, GlobalRole, requireComplete: false);
        string currentAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            signatures, OwnerRole, GlobalRole, requireComplete: true);
        return $"""
            DO $prior_acl$ BEGIN {priorAcl} END; $prior_acl$;
            DO $contract$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    ELSE
                        EXECUTE format('ALTER ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                END LOOP;
                IF pg_has_role('{GlobalRole}', '{OwnerRole}', 'MEMBER')
                   OR pg_has_role('{GlobalRole}', '{MigratorRole}', 'MEMBER')
                   OR pg_has_role('{OwnerRole}', '{GlobalRole}', 'MEMBER')
                   OR pg_has_role('{MigratorRole}', '{GlobalRole}', 'MEMBER') THEN
                    RAISE EXCEPTION 'administrative discovery execution and migration authority must remain separate';
                END IF;
                IF EXISTS (
                    WITH RECURSIVE inherited_roles(roleid) AS (
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members AS edge
                        WHERE edge.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members AS edge
                        INNER JOIN inherited_roles AS inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM inherited_roles
                    WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')) THEN
                    RAISE EXCEPTION 'administrative discovery execution and migration logins must remain separate';
                END IF;
            END $contract$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            {tableGrants}
            {policies}
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {instance}(p_user_id uuid)
                RETURNS boolean LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {currentAcl}
                    RETURN EXISTS (
                        SELECT 1 FROM {Table(typeof(PlatformUserRole))} AS a
                        INNER JOIN {Table(typeof(Role))} AS r ON r.{C<Role>(nameof(Role.Id))} = a.{C<PlatformUserRole>(nameof(PlatformUserRole.RoleId))}
                        WHERE a.{C<PlatformUserRole>(nameof(PlatformUserRole.UserId))} = p_user_id
                          AND r.{C<Role>(nameof(Role.RoleScopeId))} = {(int)RoleScopeEnum.Platform}
                          AND r.{C<Role>(nameof(Role.MasterCode))} = 'platform.admin');
                END;
                $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {tenants}(p_user_id uuid)
                RETURNS TABLE ("Value" uuid) LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {currentAcl}
                    RETURN QUERY SELECT DISTINCT g.{C<TenantUserRoleGrant>(nameof(TenantUserRoleGrant.TenantId))}
                    FROM {Table(typeof(TenantUserRoleGrant))} AS g
                    INNER JOIN {Table(typeof(TenantUser))} AS u ON u.{C<TenantUser>(nameof(TenantUser.Id))} = g.{C<TenantUserRoleGrant>(nameof(TenantUserRoleGrant.TenantUserId))}
                        AND u.{C<TenantUser>(nameof(TenantUser.TenantId))} = g.{C<TenantUserRoleGrant>(nameof(TenantUserRoleGrant.TenantId))}
                    WHERE u.{C<TenantUser>(nameof(TenantUser.UserId))} = p_user_id
                      AND g.{C<TenantUserRoleGrant>(nameof(TenantUserRoleGrant.RoleId))} = {(int)RoleEnum.TenantAdmin}
                      AND g.{C<TenantUserRoleGrant>(nameof(TenantUserRoleGrant.RevokedAt))} IS NULL
                      AND u.{C<TenantUser>(nameof(TenantUser.StatusId))} = {(int)TenantUserStatusEnum.Active}
                      AND NOT u.{C<TenantUser>(nameof(TenantUser.IsDeleted))};
                END;
                $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {organizations}(p_user_id uuid, p_tenant_id uuid)
                RETURNS TABLE ("Value" uuid) LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {currentAcl}
                    RETURN QUERY SELECT DISTINCT p.{C<OrganizationTenant>(nameof(OrganizationTenant.OrganizationId))}
                    FROM {Table(typeof(OrganizationMember))} AS m
                    INNER JOIN {Table(typeof(OrganizationTenant))} AS p ON p.{C<OrganizationTenant>(nameof(OrganizationTenant.Id))} = m.{C<OrganizationMember>(nameof(OrganizationMember.OrganizationTenantId))}
                        AND p.{C<OrganizationTenant>(nameof(OrganizationTenant.TenantId))} = m.{C<OrganizationMember>(nameof(OrganizationMember.TenantId))}
                    INNER JOIN {Table(typeof(Organization))} AS o ON o.{C<Organization>(nameof(Organization.Id))} = p.{C<OrganizationTenant>(nameof(OrganizationTenant.OrganizationId))}
                    WHERE m.{C<OrganizationMember>(nameof(OrganizationMember.UserId))} = p_user_id
                      AND m.{C<OrganizationMember>(nameof(OrganizationMember.RoleId))} = {(int)RoleEnum.OrgAdmin}
                      AND (p_tenant_id IS NULL OR m.{C<OrganizationMember>(nameof(OrganizationMember.TenantId))} = p_tenant_id)
                      AND NOT m.{C<OrganizationMember>(nameof(OrganizationMember.IsDeleted))}
                      AND NOT p.{C<OrganizationTenant>(nameof(OrganizationTenant.IsDeleted))}
                      AND NOT o.{C<Organization>(nameof(Organization.IsDeleted))};
                END;
                $function$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {groups}(p_user_id uuid, p_tenant_id uuid)
                RETURNS TABLE ("Value" uuid) LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {currentAcl}
                    RETURN QUERY SELECT DISTINCT p.{C<GroupTenant>(nameof(GroupTenant.GroupId))}
                    FROM {Table(typeof(GroupMember))} AS m
                    INNER JOIN {Table(typeof(GroupTenant))} AS p ON p.{C<GroupTenant>(nameof(GroupTenant.Id))} = m.{C<GroupMember>(nameof(GroupMember.GroupTenantId))}
                        AND p.{C<GroupTenant>(nameof(GroupTenant.TenantId))} = m.{C<GroupMember>(nameof(GroupMember.TenantId))}
                    INNER JOIN {Table(typeof(Group))} AS g ON g.{C<Group>(nameof(Group.Id))} = p.{C<GroupTenant>(nameof(GroupTenant.GroupId))}
                    WHERE m.{C<GroupMember>(nameof(GroupMember.UserId))} = p_user_id
                      AND m.{C<GroupMember>(nameof(GroupMember.RoleId))} = {(int)RoleEnum.GroupAdmin}
                      AND (p_tenant_id IS NULL OR m.{C<GroupMember>(nameof(GroupMember.TenantId))} = p_tenant_id)
                      AND NOT m.{C<GroupMember>(nameof(GroupMember.IsDeleted))}
                      AND NOT p.{C<GroupTenant>(nameof(GroupTenant.IsDeleted))}
                      AND NOT g.{C<Group>(nameof(Group.IsDeleted))};
                END;
                $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {instance}(uuid), {tenants}(uuid), {organizations}(uuid, uuid), {groups}(uuid, uuid) FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {instance}(uuid), {tenants}(uuid), {organizations}(uuid, uuid), {groups}(uuid, uuid) TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $current_acl$ BEGIN {currentAcl} END; $current_acl$;
            """;
    }

    private static string Function(ExploreDbContext context, Type type, string suffix)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + suffix, Schema(context, entity));
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
