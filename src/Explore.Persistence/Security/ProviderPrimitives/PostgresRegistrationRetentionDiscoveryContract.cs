using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Global can enumerate bounded active-state tenant UUIDs, not registration rows.
/// Deletion remains the existing transaction under independently selected Runtime authority.
/// </summary>
public static class PostgresRegistrationRetentionDiscoveryContract
{
    public const string OwnerRole = "registration_retention_discovery_owner";
    public const string MigratorRole = "registration_retention_discovery_migrator";
    public const string ExecutorRole = "registration_retention_discovery_executor";

    public static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(typeof(Tenant))!.GetTableName()! + "_registration_retention_ids", Schema(context));

    /// <summary>Migration authority installs this contract; acquisition never repairs it.</summary>
    public static async Task ApplyAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        enrollment.Validate();
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("registration_retention_discovery_requires_postgresql");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("registration_retention_discovery_requires_postgresql");
        var sql = context.GetService<ISqlGenerationHelper>();
        string schemaName = Schema(context);
        string schema = sql.DelimitIdentifier(schemaName);
        string function = FunctionSql(context);
        string signature = function + "(uuid,integer)";
        (Type Type, string[] Properties)[] sources =
        [
            (typeof(Tenant), [nameof(Tenant.Id), nameof(Tenant.TenantStatusId)]),
            (typeof(TenantStatus), [nameof(TenantStatus.Id), nameof(TenantStatus.IsActiveState)])
        ];
        var relations = sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string mappedSchema = context.GetService<IDbContextOptions>()
                .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
            if (mappedSchema != schemaName)
                throw new InvalidOperationException("registration_retention_sources_require_one_schema");
            return new PostgresPrivilegeGuard.Relation(entity.GetTableName()!, OwnerRole, [],
                source.Properties.Select(property => new PostgresPrivilegeGuard.Column(
                    Column(context, source.Type, property), ["SELECT"])).ToArray());
        }).ToArray();
        var contract = new PostgresPrivilegeGuard.Contract(schemaName,
            [OwnerRole, MigratorRole, ExecutorRole],
            [new(context.Model.FindEntityType(typeof(Tenant))!.GetTableName()! + "_registration_retention_ids",
                "uuid,integer", OwnerRole, [ExecutorRole])],
            relations,
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(ExecutorRole, enrollment.GlobalLogin, true, false, false)
            ]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(contract);
        string directoryTables = string.Join(", ", sources.Select(source => Text(Table(context, source.Type)) + "::regclass"));
        Type[] privateSources =
        [
            typeof(RegistrationAnswer), typeof(RegistrationSensitiveAnswerValue),
            typeof(RegistrationOrderPii), typeof(RegistrationParticipantPii)
        ];
        string privateTables = string.Join(", ", privateSources.Select(type => Text(Table(context, type)) + "::regclass"));
        // The directory and status lookup are instance-owned, not tenant rows.
        // Reject unexpected RLS rather than reporting an invisible fleet as empty.
        // Private retention sources retain forced tenant RLS and no Global grants.
        string observationGuard = $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                WHERE role.rolname = {Text(enrollment.GlobalLogin)} AND role.rolcanlogin
                    AND NOT role.rolsuper AND NOT role.rolbypassrls AND NOT role.rolcreatedb
                    AND NOT role.rolcreaterole AND NOT role.rolreplication)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                    WHERE (pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)}, role.oid, 'USAGE')
                        OR pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)}, role.oid, 'SET'))
                        AND (role.rolsuper OR role.rolbypassrls OR role.rolcreatedb
                            OR role.rolcreaterole OR role.rolreplication))
                OR pg_catalog.has_schema_privilege({Text(enrollment.GlobalLogin)}, {Text(schemaName)}, 'CREATE')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                    WHERE relation.oid IN ({directoryTables}) AND (
                        relation.relkind <> 'r' OR relation.relrowsecurity OR relation.relforcerowsecurity))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                    WHERE relation.oid IN ({privateTables}) AND (
                        NOT relation.relrowsecurity OR NOT relation.relforcerowsecurity))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                    WHERE relation.oid IN ({directoryTables}, {privateTables}) AND (
                        pg_catalog.has_table_privilege({Text(enrollment.GlobalLogin)}, relation.oid,
                            'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)}, relation.oid,
                            'SELECT,INSERT,UPDATE,REFERENCES')))
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                    WHERE routine.oid = pg_catalog.to_regprocedure({Text(signature)})
                        AND routine.provolatile = 's') THEN
                RAISE EXCEPTION 'registration_retention_discovery_not_ready' USING ERRCODE = '55000';
            END IF;
            """;
        string grants = string.Join("\n", relations.Select(relation =>
            $"GRANT SELECT ({string.Join(", ", relation.Columns.Select(column => sql.DelimitIdentifier(column.Name)))})" +
            $" ON {sql.DelimitIdentifier(relation.Name, schemaName)} TO {OwnerRole};"));
        string tenantTable = Table(context, typeof(Tenant));
        string statusTable = Table(context, typeof(TenantStatus));
        string tenantId = sql.DelimitIdentifier(Column(context, typeof(Tenant), nameof(Tenant.Id)));
        string tenantStatus = sql.DelimitIdentifier(Column(context, typeof(Tenant), nameof(Tenant.TenantStatusId)));
        string statusId = sql.DelimitIdentifier(Column(context, typeof(TenantStatus), nameof(TenantStatus.Id)));
        string active = sql.DelimitIdentifier(Column(context, typeof(TenantStatus), nameof(TenantStatus.IsActiveState)));
        return $"""
            DO $enrollment$
            DECLARE v_role text;
            BEGIN
                IF session_user::text IS DISTINCT FROM {Text(enrollment.MigratorLogin)}
                    OR current_setting('role') NOT IN ('none', {Text(enrollment.MigratorLogin)})
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = {Text(enrollment.MigratorLogin)} AND rolcanlogin)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                        WHERE rolname = {Text(enrollment.GlobalLogin)} AND rolcanlogin
                            AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                            AND NOT rolcreaterole AND NOT rolreplication) THEN
                    RAISE EXCEPTION 'registration_retention_enrollment_invalid' USING ERRCODE = '55000';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{ExecutorRole}')) THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
                        WHERE namespace.nspname = {Text(schemaName)}
                            AND routine.proname = {Text(contract.Routines[0].Name)}) THEN
                        RAISE EXCEPTION 'registration_retention_partial_bootstrap' USING ERRCODE = '55000';
                    END IF;
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{ExecutorRole}'] LOOP
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END LOOP;
                    -- Creator ADMIN authority must survive until these declared grants exist.
                    GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                    EXECUTE format('GRANT {ExecutorRole} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {Text(enrollment.GlobalLogin)});
                    EXECUTE format('GRANT {MigratorRole} TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', {Text(enrollment.MigratorLogin)});
                    FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{ExecutorRole}'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                            JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                            JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                            WHERE parent.rolname = v_role AND member.rolname = {Text(enrollment.MigratorLogin)}) THEN
                            EXECUTE format('REVOKE %I FROM %I', v_role, {Text(enrollment.MigratorLogin)});
                        END IF;
                    END LOOP;
                    IF ({PostgresPrivilegeGuard.BuildRoleGraphSql(contract)}) IS NOT TRUE THEN
                        RAISE EXCEPTION 'native_privilege_guard_not_ready' USING ERRCODE = '55000';
                    END IF;
                ELSE
                    {guard}
                    {observationGuard}
                END IF;
            END $enrollment$;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {ExecutorRole};
            {grants}
            DO $install$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {function}(p_after_tenant uuid, p_take integer)
                RETURNS TABLE (tenant_id uuid)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $function$
                BEGIN
                    {guard}
                    {PostgresPrivilegeGuard.RestrictedCallerSql}
                    {observationGuard}
                    IF session_user::text IS DISTINCT FROM {Text(enrollment.GlobalLogin)}
                        OR current_setting('role') NOT IN ('none', {Text(enrollment.GlobalLogin)})
                        OR NULLIF(current_setting('app.current_tenant_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'registration_retention_unbound_global_required' USING ERRCODE = '42501';
                    END IF;
                    IF p_take IS NULL OR p_take NOT BETWEEN 1 AND {IRegistrationRetentionTenantDiscoveryRepository.PageSize} THEN
                        RAISE EXCEPTION 'invalid registration retention discovery bounds' USING ERRCODE = '22023';
                    END IF;
                    RETURN QUERY
                        SELECT directory.{tenantId}
                        FROM {tenantTable} AS directory
                        JOIN {statusTable} AS status ON status.{statusId} = directory.{tenantStatus}
                        WHERE status.{active}
                            AND (p_after_tenant IS NULL OR directory.{tenantId} > p_after_tenant)
                        ORDER BY directory.{tenantId}
                        LIMIT p_take;
                END $function$ $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {ExecutorRole};
                    GRANT EXECUTE ON FUNCTION {signature} TO {ExecutorRole};
                $acl$;
                PERFORM set_config('role', v_previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $exit$
            BEGIN
                {guard}
                {observationGuard}
            END $exit$;
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(Tenant))!.GetSchema() ?? "public";

    private static string Table(ExploreDbContext context, Type type) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(type)!.GetTableName()!, Schema(context));

    private static string Column(ExploreDbContext context, Type type, string property)
    {
        var entity = context.Model.FindEntityType(type)!;
        return entity.FindProperty(property)!.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
