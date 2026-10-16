using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only, six-key projection for already enrolled selected credentials. The NOLOGIN owner
/// can SELECT only route source columns; no IAM source, DML grant, authority decision, or new login.
/// Exact source-generated bodies are attested separately from self-reported catalogue comments.
/// </summary>
public static class PostgresHumanAuthorizationRouteContract
{
    public const string OwnerRole = "event_human_route_source_owner";
    public const string MigratorRole = "event_human_route_source_migrator";
    public const string RuntimeRole = "event_human_route_source_runtime";
    public const string GlobalRole = "event_human_route_source_global";
    private const string Observe = "human_authorization_route_observe";
    private const string Acquire = "human_authorization_route_acquire";
    private const string Signature = "integer,uuid";
    private const string ReadPolicy = "human_authorization_route_read";
    private const string Stamp = "human-authorization-route:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole, GlobalRole];

    public static string ObserveFunctionSql(ExploreDbContext context) => new HumanAuthorizationRouteSql(context).Name(Observe);
    public static string AcquireFunctionSql(ExploreDbContext context) => new HumanAuthorizationRouteSql(context).Name(Acquire);

    /// <summary>Deployment-owned installation only; runtime never calls this or repairs drift.</summary>
    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql()) return;
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = new HumanAuthorizationRouteSql(context);
        return new(sql.Schema, Roles,
            [new(Observe, Signature, OwnerRole, [RuntimeRole, GlobalRole]),
                new(Acquire, Signature, OwnerRole, [RuntimeRole, GlobalRole])],
            HumanAuthorizationRouteSql.Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Type)!.GetTableName()!, OwnerRole, [],
                source.Columns.Select(property => new PostgresPrivilegeGuard.Column(
                    sql.ColumnName(source.Type, property), ["SELECT"])).ToArray())).ToArray(),
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)]);
    }

    private static string SourceGuard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanAuthorizationRouteSql(context);
        string tables = string.Join("\n", HumanAuthorizationRouteSql.Sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            string table = sql.Table(source.Type);
            string columns = string.Join(" OR ", source.Columns.Select(property =>
            {
                var mapped = entity.FindProperty(property)!;
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        WHERE a.attrelid = pg_catalog.to_regclass({Text(table)})
                            AND a.attname = {Text(sql.ColumnName(source.Type, property))}
                            AND a.attnum > 0 AND NOT a.attisdropped AND a.attgenerated = '' AND a.attidentity = ''
                            AND a.atttypid = pg_catalog.to_regtype({Text(mapped.GetRelationalTypeMapping().StoreType)})
                            AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(mapped.GetRelationalTypeMapping().StoreType)}
                            AND a.attnotnull = {(mapped.IsNullable ? "false" : "true")}
                            AND (a.attcollation = 0 OR a.attcollation = 'pg_catalog."default"'::regcollation))
                    """;
            }));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                    WHERE t.oid = pg_catalog.to_regclass({Text(table)}) AND t.relkind = 'r' AND t.relpersistence = 'p'
                        AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({Text(table)}))
                    OR {columns}
                THEN RAISE EXCEPTION 'human_route_source_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n
                WHERE n.nspname = {Text(sql.Schema)}
                    AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'human_route_schema_drift' USING ERRCODE = '55000'; END IF;
            {tables}
            """;
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanAuthorizationRouteSql(context);
        string routines = string.Join("\n", new[] { Observe, Acquire }.Select(name => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(sql.Name(name) + "(" + Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof
                    AND NOT f.proisstrict AND NOT f.proretset AND f.proargdefaults IS NULL
                    AND f.prorettype = 'pg_catalog.jsonb'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql'))
            THEN RAISE EXCEPTION 'human_route_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string policies = string.Join("\n", HumanAuthorizationRouteSql.Sources.Select(source =>
        {
            string table = sql.Table(source.Type);
            var tenant = tenantTables.SingleOrDefault(candidate =>
                candidate.TableName == context.Model.FindEntityType(source.Type)!.GetTableName());
            string tenantGuard = tenant is null ? "" : $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                    WHERE t.oid = pg_catalog.to_regclass({Text(table)}) AND t.relrowsecurity AND t.relforcerowsecurity)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)})
                            AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}'
                            AND p.polpermissive AND p.polcmd = '*' AND p.polroles = ARRAY[0::oid]
                            AND pg_catalog.obj_description(p.oid,'pg_policy') =
                                {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Schema))}
                                || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                THEN RAISE EXCEPTION 'human_route_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                """;
            return $"""
                {tenantGuard}
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = pg_catalog.to_regclass({Text(table)}) AND p.polname = '{ReadPolicy}'
                        AND p.polpermissive AND p.polcmd = 'r'
                        AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true' AND p.polwithcheck IS NULL)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)})
                            AND (0::oid = ANY(p.polroles) OR pg_catalog.to_regrole('{OwnerRole}')::oid = ANY(p.polroles))
                            AND p.polname NOT IN ('{ReadPolicy}'
                                {(tenant is null ? "" : $",'{PostgresTenantRowLevelSecurityModel.PolicyName}'")}))
                THEN RAISE EXCEPTION 'human_route_rls_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        return $"""
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {SourceGuard(context, enrollment)}
            {routines}
            {policies}
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))})
                    AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
            THEN RAISE EXCEPTION 'human_route_role_drift' USING ERRCODE = '55000'; END IF;
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                WHERE n.nspname = {Text(sql.Schema)} AND f.oid NOT IN (
                    pg_catalog.to_regprocedure({Text(sql.Name(Observe) + "(" + Signature + ")")}),
                    pg_catalog.to_regprocedure({Text(sql.Name(Acquire) + "(" + Signature + ")")}))
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(",", Roles.Select(Text))}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(",", Roles.Select(Text))})))))
            THEN RAISE EXCEPTION 'human_route_extra_routine_authority' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool acquire)
    {
        var sql = new HumanAuthorizationRouteSql(context);
        string NativeQuery(bool tenant)
        {
            string query = sql.Query(tenant).Replace("@tenant", "p_tenant", StringComparison.Ordinal);
            for (int index = 0; index < HumanAuthorizationRouteFacts.Keys.Length; index++)
                query = query.Replace($"@key{index}", Text(HumanAuthorizationRouteFacts.Keys[index]), StringComparison.Ordinal);
            return query;
        }
        string Aggregate(bool tenant) =>
            $"(SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(r)), '[]'::jsonb) FROM ({NativeQuery(tenant)}) r)";
        return $"""
            DECLARE result jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {PostgresPrivilegeGuard.RestrictedCallerSql}
                IF current_user <> '{OwnerRole}'
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                    OR pg_catalog.current_setting('session_replication_role') <> 'origin'
                    {(acquire ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
                    OR NOT COALESCE((p_purpose = 1 AND session_user = {Text(enrollment.RuntimeLogin)} AND p_tenant IS NOT NULL
                        OR p_purpose IN (2,3) AND session_user = {Text(enrollment.GlobalLogin)} AND p_tenant IS NULL),false)
                    OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                        WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                            AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
                THEN RAISE EXCEPTION 'human_route_caller_mismatch' USING ERRCODE = '42501'; END IF;
                result := pg_catalog.jsonb_build_object('System',{Aggregate(false)},'Tenant',
                    CASE WHEN p_tenant IS NULL THEN '[]'::jsonb ELSE {Aggregate(true)} END);
                {Guard(context, enrollment)}
                RETURN result;
            END;
            """;
    }

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanAuthorizationRouteSql(context);
        return string.Join("\n", new[] { (Observe, false), (Acquire, true) }.Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(sql.Name(function.Item1) + "(" + Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') =
                        {Text(Hash(Body(context, enrollment, function.Item2)))}
                    AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(Stamp + Hash(Body(context, enrollment, function.Item2)))})
            THEN RAISE EXCEPTION 'human_route_exact_body_drift' USING ERRCODE = '55000'; END IF;
            """));
    }

    internal static string AdmissionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"DO $admission$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $admission$;";

    internal static string OriginalSourceAssertionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        Guard(context, enrollment) + "\n" + ExactBodies(context, enrollment);

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanAuthorizationRouteSql(context);
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
            """));
        string grants = string.Join("\n", HumanAuthorizationRouteSql.Sources.Select(source => $"""
            GRANT SELECT ({string.Join(",", source.Columns.Select(property => sql.Column(source.Type, property)))})
                ON {sql.Table(source.Type)} TO {OwnerRole};
            CREATE POLICY {ReadPolicy} ON {sql.Table(source.Type)} FOR SELECT TO {OwnerRole} USING (true);
            """));
        string definitions = string.Join("\n", new[] { (Observe, false), (Acquire, true) }.Select(function => $"""
            CREATE FUNCTION {sql.Name(function.Item1)}(p_purpose integer,p_tenant uuid) RETURNS jsonb
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body${Body(context, enrollment, function.Item2)}$body$;
            REVOKE ALL ON FUNCTION {sql.Name(function.Item1)}({Signature}) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {sql.Name(function.Item1)}({Signature}) TO {RuntimeRole},{GlobalRole};
            COMMENT ON FUNCTION {sql.Name(function.Item1)}({Signature}) IS {Text(Stamp + Hash(Body(context, enrollment, function.Item2)))};
            """));
        // Installation is idempotent only for an already exact contract; it never normalizes drift.
        return $"""
            DO $install$
            DECLARE role_name text; count_roles integer; previous_role text := pg_catalog.current_setting('role');
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'human_route_migrator_mismatch' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, enrollment)}
                SELECT count(*) INTO count_roles FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF count_roles = {Roles.Length} THEN
                    {Guard(context, enrollment)}
                    {ExactBodies(context, enrollment)}
                    RETURN;
                ELSIF count_roles <> 0 THEN
                    RAISE EXCEPTION 'human_route_partial_roles' USING ERRCODE = '55000';
                END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                    WHERE n.nspname = {Text(sql.Schema)} AND f.proname IN ('{Observe}','{Acquire}'))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polname = '{ReadPolicy}')
                THEN RAISE EXCEPTION 'human_route_bootstrap_artifacts' USING ERRCODE = '55000'; END IF;
                FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                    EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',role_name);
                    EXECUTE pg_catalog.format('COMMENT ON ROLE %I IS %L',role_name,'{Stamp}role:' || role_name);
                END LOOP;
                {memberships}
                FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                    IF role_name <> '{MigratorRole}' AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                        WHERE m.roleid = pg_catalog.to_regrole(role_name)::oid AND m.member = pg_catalog.to_regrole(session_user)::oid)
                    THEN EXECUTE pg_catalog.format('REVOKE %I FROM %I',role_name,session_user); END IF;
                END LOOP;
                EXECUTE $ddl$
                    GRANT USAGE ON SCHEMA {sql.Quote(sql.Schema)} TO {string.Join(",", Roles)};
                    GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {OwnerRole};
                    {grants}
                $ddl$;
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl${definitions}$ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
                EXECUTE 'REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {OwnerRole}';
                {Guard(context, enrollment)}
                {ExactBodies(context, enrollment)}
            END $install$;
            """;
    }
}
