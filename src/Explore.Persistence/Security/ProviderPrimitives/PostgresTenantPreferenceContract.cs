using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Closed self-preference data capability; managed original preparation remains the human admission boundary.</summary>
public static class PostgresTenantPreferenceContract
{
    public const string OwnerRole = "event_tenant_preference_owner";
    public const string GlobalRole = "event_tenant_preference_global";
    public const string MigratorRole = "event_tenant_preference_migrator";

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token = default)
    {
        if (!context.Database.IsNpgsql()) return;
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(token) : null;
        var model = Build(context, enrollment);
        string privilegeGuard = Guard(context, enrollment);
        string prior = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            model.Signatures, OwnerRole, GlobalRole, requireComplete: false);
        string current = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(
            model.Signatures, OwnerRole, GlobalRole, requireComplete: true);
        await context.Database.ExecuteSqlRawAsync($"""
            DO $prior$ BEGIN {prior} END; $prior$;
            DO $prior_privileges$ BEGIN
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('{OwnerRole}', '{GlobalRole}', '{MigratorRole}')) THEN
                    {privilegeGuard}
                END IF;
            END $prior_privileges$;
            DO $roles$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{GlobalRole}', '{MigratorRole}'] LOOP
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
                    RAISE EXCEPTION 'Preference execution and migration authority must remain separate';
                END IF;
                IF EXISTS (
                    WITH RECURSIVE inherited_roles(roleid) AS (
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        WHERE edge.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                        UNION
                        SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                        JOIN inherited_roles inherited ON edge.member = inherited.roleid)
                    SELECT 1 FROM inherited_roles
                    WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')) THEN
                    RAISE EXCEPTION 'Preference execution and migration authority must remain separate';
                END IF;
            END $roles$;
            GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            GRANT {MigratorRole} TO {Quote(enrollment.MigratorLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            GRANT {GlobalRole} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE;
            GRANT USAGE, CREATE ON SCHEMA {model.Schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {model.Schema} TO {GlobalRole};
            {model.TableGrants}
            DO $install$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {model.Write}(
                    p_user uuid, p_tenant uuid, p_updated_at timestamp with time zone)
                    RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${model.WriteBody}$body$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {model.Image}(
                    p_user uuid, p_tenant uuid, p_updated_at timestamp with time zone)
                    RETURNS boolean LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                    AS $body${model.ImageBody}$body$ $ddl$;
                REVOKE ALL ON FUNCTION {model.Signatures[0]} FROM PUBLIC, {GlobalRole};
                REVOKE ALL ON FUNCTION {model.Signatures[1]} FROM PUBLIC, {GlobalRole};
                GRANT EXECUTE ON FUNCTION {model.Signatures[0]} TO {GlobalRole};
                GRANT EXECUTE ON FUNCTION {model.Signatures[1]} TO {GlobalRole};
                PERFORM set_config('role', v_previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {model.Schema} FROM {OwnerRole};
            DO $current$ BEGIN {current} END; $current$;
            DO $privileges$ BEGIN {privilegeGuard} END; $privileges$;
            """, token);
        await RequireReadyAsync(context, enrollment, token);
        if (owned is not null) await owned.CommitAsync(token);
    }

    public static string FunctionSql(ExploreDbContext context) => Function(context, "write");
    internal static string ImageFunctionSql(ExploreDbContext context) => Function(context, "image");

    internal static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token)
    {
        var model = Build(context, enrollment);
        await using (var guard = context.Database.GetDbConnection().CreateCommand())
        {
            if (guard.Connection!.State != System.Data.ConnectionState.Open) await guard.Connection.OpenAsync(token);
            guard.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            guard.CommandText = $"DO $guard$ BEGIN {Guard(context, enrollment)} END; $guard$;";
            await guard.ExecuteNonQueryAsync(token);
        }
        foreach (var expected in new[]
        {
            (model.Signatures[0], model.WriteBody, "v"), (model.Signatures[1], model.ImageBody, "s")
        })
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            if (command.Connection!.State != System.Data.ConnectionState.Open)
                await command.Connection.OpenAsync(token);
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                SELECT p.prosrc, p.prosecdef, p.provolatile::text, r.rolname,
                    p.proconfig = ARRAY['search_path=pg_catalog']::text[],
                    p.proargnames = ARRAY['p_user','p_tenant','p_updated_at']::text[]
                    AND p.proargmodes IS NULL AND p.proallargtypes IS NULL
                    AND p.proargdefaults IS NULL AND p.pronargdefaults = 0 AND p.provariadic = 0
                    AND NOT p.proretset AND p.prorettype = 'pg_catalog.bool'::regtype
                    AND p.prokind = 'f' AND p.proparallel = 'u'
                    AND NOT p.proleakproof AND NOT p.proisstrict
                    AND p.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_roles r ON r.oid = p.proowner
                WHERE p.oid = pg_catalog.to_regprocedure(@signature)
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "signature";
            parameter.Value = expected.Item1;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)
                || reader.GetString(0) != expected.Item2 || !reader.GetBoolean(1)
                || reader.GetString(2) != expected.Item3 || reader.GetString(3) != OwnerRole
                || reader.IsDBNull(4) || !reader.GetBoolean(4) || reader.IsDBNull(5) || !reader.GetBoolean(5))
                throw new UnauthorizedAccessException("native_tenant_preference_contract_drift");
        }
    }

    private static Contract Build(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var user = context.Model.FindEntityType(typeof(User))!;
        var member = context.Model.FindEntityType(typeof(TenantUser))!;
        var tenant = context.Model.FindEntityType(typeof(Tenant))!;
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? user.GetSchema() ?? "public";
        string Table(IEntityType entity) => sql.DelimitIdentifier(entity.GetTableName()!, schema);
        string Column(IEntityType entity, string name) => sql.DelimitIdentifier(entity.FindProperty(name)!
            .GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        string uId = Column(user, nameof(User.Id)), deleted = Column(user, nameof(User.IsDeleted));
        string preference = Column(user, nameof(User.LastActiveTenantId));
        string updatedAt = Column(user, nameof(User.UpdatedAt)), updatedBy = Column(user, nameof(User.UpdatedBy));
        string mUser = Column(member, nameof(TenantUser.UserId)), mTenant = Column(member, nameof(TenantUser.TenantId));
        string mDeleted = Column(member, nameof(TenantUser.IsDeleted)), status = Column(member, nameof(TenantUser.StatusId));
        string tId = Column(tenant, nameof(Tenant.Id));
        string write = sql.DelimitIdentifier("human_tenant_preference_write", schema);
        string image = sql.DelimitIdentifier("human_tenant_preference_image", schema);
        string[] signatures = [$"{write}(uuid, uuid, timestamp with time zone)", $"{image}(uuid, uuid, timestamp with time zone)"];
        string acl = PostgresPrivilegeGuard.BuildRoutineAclAssertSql(signatures, OwnerRole, GlobalRole, requireComplete: true)
            + Guard(context, enrollment) + PostgresPrivilegeGuard.RestrictedCallerSql + $"""
            IF session_user <> '{Literal(enrollment.GlobalLogin)}' OR current_user <> '{OwnerRole}' THEN
                RAISE EXCEPTION 'native_tenant_preference_wrong_executor' USING ERRCODE = '55000';
            END IF;
            """;
        string transaction = """
            IF current_setting('transaction_isolation') <> 'serializable' THEN
                RAISE EXCEPTION 'tenant_preference_requires_original_serializable_owner' USING ERRCODE = '55000';
            END IF;
            """;
        string writeBody = $"""
            DECLARE v_written boolean;
            BEGIN
                {acl}
                {transaction}
                WITH written AS (
                    UPDATE {Table(user)} AS u SET {preference} = p_tenant, {updatedAt} = p_updated_at, {updatedBy} = p_user
                    WHERE u.{uId} = p_user AND NOT u.{deleted}
                      AND EXISTS (SELECT 1 FROM {Table(member)} m JOIN {Table(tenant)} t ON t.{tId} = m.{mTenant}
                        WHERE m.{mUser} = p_user AND m.{mTenant} = p_tenant AND NOT m.{mDeleted}
                          AND m.{status} = {(int)TenantUserStatusEnum.Active})
                    RETURNING 1)
                SELECT EXISTS (SELECT 1 FROM written) INTO v_written;
                RETURN v_written;
            END;
            """;
        string imageBody = $"""
            BEGIN
                {acl}
                {transaction}
                RETURN EXISTS (SELECT 1 FROM {Table(user)} u WHERE u.{uId} = p_user AND NOT u.{deleted}
                    AND u.{preference} = p_tenant AND u.{updatedAt} = p_updated_at AND u.{updatedBy} = p_user);
            END;
            """;
        string grants = $"""
            REVOKE ALL ON TABLE {Table(user)}, {Table(member)}, {Table(tenant)} FROM {OwnerRole}, {GlobalRole};
            GRANT SELECT ({uId}, {deleted}, {preference}, {updatedAt}, {updatedBy}) ON {Table(user)} TO {OwnerRole};
            GRANT UPDATE ({preference}, {updatedAt}, {updatedBy}) ON {Table(user)} TO {OwnerRole};
            GRANT SELECT ({mUser}, {mTenant}, {mDeleted}, {status}) ON {Table(member)} TO {OwnerRole};
            GRANT SELECT ({tId}) ON {Table(tenant)} TO {OwnerRole};
            DROP POLICY IF EXISTS tenant_preference_membership ON {Table(member)};
            CREATE POLICY tenant_preference_membership ON {Table(member)} FOR SELECT TO {OwnerRole} USING (true);
            DROP POLICY IF EXISTS tenant_preference_tenant ON {Table(tenant)};
            CREATE POLICY tenant_preference_tenant ON {Table(tenant)} FOR SELECT TO {OwnerRole} USING (true);
            """;
        return new(sql.DelimitIdentifier(schema), write, image, signatures, writeBody, imageBody, grants);
    }

    private sealed record Contract(string Schema, string Write, string Image, string[] Signatures,
        string WriteBody, string ImageBody, string TableGrants);

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(User))!.GetSchema() ?? "public";

    private static string Function(ExploreDbContext context, string operation) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("human_tenant_preference_" + operation, Schema(context));

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string Literal(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        string[] roles = [OwnerRole, GlobalRole, MigratorRole];
        if (new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin, enrollment.MigratorLogin }.Any(roles.Contains))
            throw new InvalidOperationException("tenant_preference_enrollment_collision");
        string Column(IEntityType entity, string name) => entity.FindProperty(name)!
            .GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
        PostgresPrivilegeGuard.Relation Relation(Type type, params (string Name, string[] Privileges)[] columns)
        {
            var entity = context.Model.FindEntityType(type)!;
            return new(entity.GetTableName()!, OwnerRole, [],
                columns.Select(column => new PostgresPrivilegeGuard.Column(Column(entity, column.Name), column.Privileges)).ToArray());
        }
        var contract = new PostgresPrivilegeGuard.Contract(Schema(context), roles,
            new[] { "write", "image" }.Select(operation => new PostgresPrivilegeGuard.Routine(
                "human_tenant_preference_" + operation, "uuid, uuid, timestamp with time zone", OwnerRole, [GlobalRole])).ToArray(),
            [
                Relation(typeof(User), (nameof(User.Id), ["SELECT"]), (nameof(User.IsDeleted), ["SELECT"]),
                    (nameof(User.LastActiveTenantId), ["SELECT", "UPDATE"]),
                    (nameof(User.UpdatedAt), ["SELECT", "UPDATE"]), (nameof(User.UpdatedBy), ["SELECT", "UPDATE"])),
                Relation(typeof(TenantUser), (nameof(TenantUser.UserId), ["SELECT"]), (nameof(TenantUser.TenantId), ["SELECT"]),
                    (nameof(TenantUser.IsDeleted), ["SELECT"]), (nameof(TenantUser.StatusId), ["SELECT"])),
                Relation(typeof(Tenant), (nameof(Tenant.Id), ["SELECT"]))
            ],
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)
            ]);
        string table = context.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(context.Model.FindEntityType(typeof(User))!.GetTableName()!, Schema(context));
        return PostgresPrivilegeGuard.BuildAssertSql(contract) + $"""
            IF pg_catalog.pg_has_role('{Literal(enrollment.RuntimeLogin)}','{OwnerRole}','MEMBER')
                OR pg_catalog.pg_has_role('{Literal(enrollment.RuntimeLogin)}','{MigratorRole}','MEMBER')
                OR pg_catalog.pg_has_role('{Literal(enrollment.RuntimeLogin)}','{GlobalRole}','MEMBER')
                OR pg_catalog.pg_has_role('{Literal(enrollment.GlobalLogin)}','{OwnerRole}','MEMBER')
                OR pg_catalog.pg_has_role('{Literal(enrollment.GlobalLogin)}','{MigratorRole}','MEMBER')
                OR pg_catalog.has_schema_privilege('{Literal(enrollment.RuntimeLogin)}','{Literal(Schema(context))}','CREATE')
                OR pg_catalog.has_schema_privilege('{Literal(enrollment.GlobalLogin)}','{Literal(Schema(context))}','CREATE')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                WHERE rolname IN ('{Literal(enrollment.RuntimeLogin)}','{Literal(enrollment.GlobalLogin)}')
                  AND (NOT rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication))
                OR pg_catalog.has_table_privilege('{Literal(enrollment.RuntimeLogin)}','{Literal(table)}','INSERT,DELETE,TRUNCATE,TRIGGER')
                OR pg_catalog.has_any_column_privilege('{Literal(enrollment.RuntimeLogin)}','{Literal(table)}','UPDATE,INSERT,REFERENCES')
                OR pg_catalog.has_table_privilege('{Literal(enrollment.GlobalLogin)}','{Literal(table)}','DELETE,TRUNCATE,TRIGGER')
                OR pg_catalog.has_any_column_privilege('{Literal(enrollment.GlobalLogin)}','{Literal(table)}','SELECT,UPDATE,INSERT,REFERENCES')
                OR pg_catalog.has_function_privilege('{Literal(enrollment.RuntimeLogin)}',
                    '{Literal(Function(context, "write"))}(uuid,uuid,timestamp with time zone)','EXECUTE')
                OR pg_catalog.has_function_privilege('{Literal(enrollment.RuntimeLogin)}',
                    '{Literal(Function(context, "image"))}(uuid,uuid,timestamp with time zone)','EXECUTE') THEN
                RAISE EXCEPTION 'native_tenant_preference_effective_privilege_drift' USING ERRCODE = '55000';
            END IF;
            """;
    }
}
