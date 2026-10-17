using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Returns one global user's complete active membership graph in a single statement.</summary>
public static class PostgresUserTenantMembershipContract
{
    public const string OwnerRole = "event_user_membership_owner";
    public const string MigratorRole = "event_user_membership_migrator";
    public const string GlobalRole = "event_user_membership_global";

    public static string FunctionSql(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(TenantUser))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.GetTableName()! + "_active_for_user", Schema(context, entity));
    }

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
        var membership = context.Model.FindEntityType(typeof(TenantUser))!;
        var tenant = context.Model.FindEntityType(typeof(Tenant))!;
        var status = context.Model.FindEntityType(typeof(TenantStatus))!;
        var sql = context.GetService<ISqlGenerationHelper>();
        string Table(IEntityType entity) => sql.DelimitIdentifier(entity.GetTableName()!, Schema(context, entity));
        string Column(IEntityType entity, string property) => sql.DelimitIdentifier(
            entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        string Columns(IEntityType entity) => string.Join(", ", entity.GetProperties()
            .Select(property => Column(entity, property.Name)));
        var membershipMapping = StoreObjectIdentifier.Table(membership.GetTableName()!, membership.GetSchema());
        (IEntityType Entity, string Alias, string Property, string Output)[] fields =
        [
            .. membership.GetProperties().Select(property =>
                (membership, "m", property.Name, property.GetColumnName(membershipMapping)!)),
            (tenant, "t", nameof(Tenant.FullName), "tenant_full_name"),
            (tenant, "t", nameof(Tenant.Slug), "tenant_slug"),
            (tenant, "t", nameof(Tenant.Description), "tenant_description"),
            (tenant, "t", nameof(Tenant.TenantStatusId), "tenant_status_id"),
            (tenant, "t", nameof(Tenant.CreatedAt), "tenant_created_at"),
            (tenant, "t", nameof(Tenant.CreatedBy), "tenant_created_by"),
            (tenant, "t", nameof(Tenant.UpdatedAt), "tenant_updated_at"),
            (tenant, "t", nameof(Tenant.UpdatedBy), "tenant_updated_by"),
            (status, "s", nameof(TenantStatus.MasterCode), "tenant_status_master_code"),
            (status, "s", nameof(TenantStatus.FullName), "tenant_status_full_name"),
            (status, "s", nameof(TenantStatus.Description), "tenant_status_description"),
            (status, "s", nameof(TenantStatus.IsActiveState), "tenant_status_is_active_state")
        ];
        string result = string.Join(", ", fields.Select(field =>
            $"{sql.DelimitIdentifier(field.Output)} {field.Entity.FindProperty(field.Property)!.GetColumnType()}"));
        string projection = string.Join(", ", fields.Select(field =>
            $"{field.Alias}.{Column(field.Entity, field.Property)} AS {sql.DelimitIdentifier(field.Output)}"));
        string schema = sql.DelimitIdentifier(Schema(context, membership));
        string members = Table(membership);
        string tenants = Table(tenant);
        string statuses = Table(status);
        string userId = Column(membership, nameof(TenantUser.UserId));
        string tenantId = Column(membership, nameof(TenantUser.TenantId));
        string memberStatus = Column(membership, nameof(TenantUser.StatusId));
        string deleted = Column(membership, nameof(TenantUser.IsDeleted));
        string tenantKey = Column(tenant, nameof(Tenant.Id));
        string tenantStatus = Column(tenant, nameof(Tenant.TenantStatusId));
        string statusKey = Column(status, nameof(TenantStatus.Id));
        string function = FunctionSql(context);
        string priorAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [$"{function}(uuid)"], OwnerRole, GlobalRole, requireComplete: false);
        string currentAcl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            [$"{function}(uuid)"], OwnerRole, GlobalRole, requireComplete: true);
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
                    RAISE EXCEPTION 'user membership execution and migration authority must remain separate';
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
                    RAISE EXCEPTION 'user membership execution and migration logins must remain separate';
                END IF;
            END $contract$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            REVOKE ALL ON TABLE {members}, {tenants}, {statuses} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({Columns(membership)}) ON {members} TO {OwnerRole};
            GRANT SELECT ({Columns(tenant)}) ON {tenants} TO {OwnerRole};
            GRANT SELECT ({Columns(status)}) ON {statuses} TO {OwnerRole};
            DROP POLICY IF EXISTS user_membership_authentication_read ON {members};
            CREATE POLICY user_membership_authentication_read ON {members} FOR SELECT TO {OwnerRole}
                USING (NOT {deleted} AND {memberStatus} = {(int)TenantUserStatusEnum.Active});
            DO $bootstrap$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(p_user_id uuid)
                RETURNS TABLE ({result})
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {currentAcl}
                    RETURN QUERY SELECT {projection}
                    FROM {members} AS m
                    INNER JOIN {tenants} AS t ON t.{tenantKey} = m.{tenantId}
                    INNER JOIN {statuses} AS s ON s.{statusKey} = t.{tenantStatus}
                    WHERE m.{userId} = p_user_id
                      AND NOT m.{deleted}
                      AND m.{memberStatus} = {(int)TenantUserStatusEnum.Active}
                      AND t.{tenantStatus} = {(int)TenantStatusEnum.Active};
                END;
                $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {function}(uuid) FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {function}(uuid) TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $bootstrap$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $current_acl$ BEGIN {currentAcl} END; $current_acl$;
            """;
    }

    private static string Schema(ExploreDbContext context, IEntityType entity) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? entity.GetSchema() ?? "public";
}
