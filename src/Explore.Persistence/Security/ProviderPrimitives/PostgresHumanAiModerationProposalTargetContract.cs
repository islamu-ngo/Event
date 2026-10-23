using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only exact-event discovery for a persisted human instance administrator.
/// Reuses the installed assignment writer epoch without changing its selected-tenant routines.
/// The original principal lease owns administrator continuity; all private reads stay on S.
/// </summary>
public static class PostgresHumanAiModerationProposalTargetContract
{
    private const string Owner = "event_ai_moderation_target_owner";
    private const string Runtime = "event_ai_moderation_target_runtime";
    private const string Migrator = "event_ai_moderation_target_migrator";
    private const string Observe = "human_ai_moderation_proposal_target_observe";
    private const string Acquire = "human_ai_moderation_proposal_target_acquire";
    private const string Signature = "uuid,uuid,uuid";
    private const string Arguments = "p_actor uuid,p_selected uuid,p_event uuid";
    private const string ReadPolicy = "human_ai_moderation_proposal_target_read";
    private const string UpdatePolicy = "human_ai_moderation_proposal_target_update";
    private const string Epoch = "human_event_assignment_source_epoch";
    private const string Stamp = "human-ai-moderation-proposal-target:v1:";
    private static readonly string[] Roles = [Owner, Runtime, Migrator];
    private static readonly (Type Type, string[] Columns)[] Sources =
    [
        .. HumanEventAssignmentSourceSql.Sources.Where(source => source.Type != typeof(UserPii)),
        (typeof(PlatformUserRole), ["Id", "UserId", "RoleId"])
    ];

    public static string ObserveFunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Observe);
    internal static string AcquireFunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Acquire);
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string AdministratorPredicate(ExploreDbContext context, string actor)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string U(string property) => "u." + sql.Column(typeof(User), property);
        string G(string property) => "g." + sql.Column(typeof(PlatformUserRole), property);
        string R(string property) => "r." + sql.Column(typeof(Role), property);
        return $"""
            EXISTS (SELECT 1 FROM {sql.Table(typeof(User))} u
                JOIN {sql.Table(typeof(PlatformUserRole))} g ON {G("UserId")} = {U("Id")}
                JOIN {sql.Table(typeof(Role))} r ON {R("Id")} = {G("RoleId")}
                WHERE {U("Id")} = {actor} AND {U("IsDeleted")} = {(context.Database.IsNpgsql() ? "false" : "0")}
                    AND {R("RoleScopeId")} = {(int)RoleScopeEnum.Platform}
                    AND {R("MasterCode")} = 'platform.admin')
            """;
    }

    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, Roles,
            [new(Observe, Signature, Owner, [Runtime]), new(Acquire, Signature, Owner, [Runtime])],
            [.. Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Type)!.GetTableName()!, Owner, [],
                source.Columns.Select((column, index) => new PostgresPrivilegeGuard.Column(
                    sql.ColumnName(source.Type, column), index == 0 ? ["SELECT", "UPDATE"] : ["SELECT"])).ToArray())),
                new(Epoch, Owner, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])])],
            [new(Owner, Migrator, false, true, false), new(Migrator, enrollment.MigratorLogin, false, true, false),
                new(Runtime, enrollment.RuntimeLogin, true, false, false)]);
    }

    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string routines = string.Join(",", new[] { Observe, Acquire }.Select(name =>
            $"pg_catalog.to_regprocedure({Text(sql.Name(name) + "(" + Signature + ")")})"));
        string sourceChecks = string.Join("\n", Sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            var declared = context.GetService<IDesignTimeModel>().Model.FindEntityType(source.Type)!;
            string relation = $"pg_catalog.to_regclass({Text(sql.Table(source.Type))})";
            string columns = string.Join(" OR ", source.Columns.Select(column =>
            {
                var property = entity.FindProperty(column)!;
                string identity = NpgsqlPropertyExtensions.GetValueGenerationStrategy(declared.FindProperty(column)!) switch
                {
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn => "d",
                    NpgsqlValueGenerationStrategy.IdentityAlwaysColumn => "a",
                    _ => ""
                };
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = {relation}
                        AND a.attname = {Text(sql.ColumnName(source.Type, column))} AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {(property.IsNullable ? "false" : "true")}
                        AND a.attgenerated = '' AND a.attidentity = {Text(identity)}
                        AND (a.attcollation = 0 OR a.attcollation = 'pg_catalog."default"'::regcollation))
                    """;
            }));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = {relation}
                    AND t.relkind = 'r' AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = {relation})
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = {relation} OR i.inhparent = {relation})
                    OR {columns}
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {relation}
                        AND p.polname = '{ReadPolicy}' AND p.polcmd = 'r' AND p.polpermissive
                        AND p.polroles = ARRAY[pg_catalog.to_regrole('{Owner}')::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true' AND p.polwithcheck IS NULL)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {relation}
                        AND p.polname = '{UpdatePolicy}' AND p.polcmd = 'w' AND p.polpermissive
                        AND p.polroles = ARRAY[pg_catalog.to_regrole('{Owner}')::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true'
                        AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'true')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {relation}
                        AND (0::oid = ANY(p.polroles) OR pg_catalog.to_regrole('{Owner}')::oid = ANY(p.polroles))
                        AND p.polname NOT IN ('{ReadPolicy}','{UpdatePolicy}','{PostgresTenantRowLevelSecurityModel.PolicyName}'))
                THEN RAISE EXCEPTION 'ai_moderation_target_source_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        return $"""
            {PostgresHumanEventAssignmentSourceContract.AssignmentSourceAttestationSql(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {sourceChecks}
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid IN ({routines})
                AND (f.provolatile <> 'v' OR f.proparallel <> 'u' OR f.proleakproof OR f.proisstrict OR f.proretset
                    OR f.proargdefaults IS NOT NULL OR f.pronargdefaults <> 0 OR f.pronargs <> 3 OR f.provariadic <> 0
                    OR f.proargnames IS DISTINCT FROM ARRAY['p_actor','p_selected','p_event']::text[]
                    OR f.proargmodes IS NOT NULL OR f.proallargtypes IS NOT NULL OR f.prorettype <> 'pg_catalog.jsonb'::regtype
                    OR f.prolang <> (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    OR pg_catalog.obj_description(f.oid,'pg_proc') IS DISTINCT FROM
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))})
                    AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d
                    WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass AND d.deptype = 'o'
                        AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(",", Roles.Select(Text))}))
                        AND (d.classid <> 'pg_catalog.pg_proc'::regclass
                            OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())
                            OR d.objid NOT IN ({routines})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))})
                        AND pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))})
                        AND n.nspname <> {Text(sql.Schema)} AND n.nspname NOT IN ('pg_catalog','information_schema')
                        AND n.nspname NOT LIKE 'pg_toast%' AND t.relkind IN ('r','p','v','m','f')
                        AND (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))}) AND t.relkind = 'S'
                        AND pg_catalog.has_sequence_privilege(r.oid,t.oid,'USAGE,SELECT,UPDATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({string.Join(",", Roles.Select(Text))}) AND pg_catalog.has_database_privilege(r.oid,d.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({routines})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(",", Roles.Select(Text))}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(",", Roles.Select(Text))})))))
            THEN RAISE EXCEPTION 'ai_moderation_target_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool acquire)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string Native(string query) => query.Replace("@actor", "p_actor", StringComparison.Ordinal)
            .Replace("@tenant", "v_tenant", StringComparison.Ordinal).Replace("@events", "ARRAY[p_event]", StringComparison.Ordinal);
        var queries = new HumanEventAssignmentSourceSql(context, 1).Queries;
        string caller = $"""
            {PostgresPrivilegeGuard.RestrictedCallerSql}
            IF current_user <> '{Owner}' OR session_user <> {Text(enrollment.RuntimeLogin)}
                OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                OR pg_catalog.current_setting('session_replication_role') <> 'origin'
                OR p_selected IS NULL OR p_selected = '00000000-0000-0000-0000-000000000000'::uuid
                OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_selected
                OR p_actor IS NULL OR p_actor = '00000000-0000-0000-0000-000000000000'::uuid
                OR p_event IS NULL OR p_event = '00000000-0000-0000-0000-000000000000'::uuid
                {(acquire ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                        AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
                OR NOT {AdministratorPredicate(context, "p_actor")}
            THEN RAISE EXCEPTION 'ai_moderation_target_original_admin_required' USING ERRCODE = '42501'; END IF;
            """;
        return $"""
            DECLARE v_tenant uuid; result jsonb := pg_catalog.jsonb_build_object();
            BEGIN
                {Guard(context, enrollment)}
                {caller}
                {(acquire ? $"UPDATE {sql.Name(Epoch)} SET revision = NOT revision WHERE id = 1; IF NOT FOUND THEN RAISE EXCEPTION 'ai_moderation_target_epoch_missing' USING ERRCODE = '55000'; END IF;" : "")}
                SELECT e.{sql.Column(typeof(Explore.Domain.Event), "TenantId")} INTO v_tenant
                    FROM {sql.Table(typeof(Explore.Domain.Event))} e
                    WHERE e.{sql.Column(typeof(Explore.Domain.Event), "Id")} = p_event
                        AND NOT e.{sql.Column(typeof(Explore.Domain.Event), "IsDeleted")}
                    {(acquire ? "FOR UPDATE" : "")};
                IF v_tenant IS NULL OR v_tenant = p_selected THEN RETURN NULL; END IF;
                {(acquire ? string.Join("\n", queries.Select(query => $"PERFORM 1 FROM ({Native(query)} FOR UPDATE) held;")) : "")}
                result := pg_catalog.jsonb_build_object('tenant',v_tenant);
                {string.Join("\n", queries.Select((query, index) => $"""
                    result := result || pg_catalog.jsonb_build_object('q{index}',
                        (SELECT COALESCE(pg_catalog.jsonb_agg(pg_catalog.to_jsonb(facts)), '[]'::jsonb) FROM ({Native(query)}) facts));
                    """))}
                {caller}
                {Guard(context, enrollment)}
                RETURN result;
            END;
            """;
    }

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", new[] { (Name: Observe, Acquire: false), (Name: Acquire, Acquire: true) }.Select(routine => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid = pg_catalog.to_regprocedure(
                {Text(new HumanPrincipalSourceSql(context).Name(routine.Name) + "(" + Signature + ")")})
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(Body(context, enrollment, routine.Acquire)))})
            THEN RAISE EXCEPTION 'ai_moderation_target_body_drift' USING ERRCODE = '55000'; END IF;
            """));

    internal static string AdmissionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        $"DO $admission$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $admission$;";

    internal static async Task RequireCurrentInstallationAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken token)
    {
        var original = (NpgsqlConnection)context.Database.GetDbConnection();
        var settings = new NpgsqlConnectionStringBuilder(original.ConnectionString) { Pooling = false, Enlist = false };
        await using var verification = await original.CloneWithAsync(settings.ConnectionString, token);
        await verification.OpenAsync(token);
        await using var command = verification.CreateCommand();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        // Only pg_catalog metadata, never target/principal/private rows, crosses this connection.
        command.CommandText = $"""
            DO $runtime$ BEGIN
                IF session_user <> {Text(enrollment.RuntimeLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'ai_moderation_target_catalog_runtime_required' USING ERRCODE = '42501'; END IF;
            END $runtime$;
            {AdmissionSql(context, enrollment)}
            """;
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken token)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var privileges = Privileges(context, enrollment);
        string definitions = string.Join("\n", new[] { (Name: Observe, Acquire: false), (Name: Acquire, Acquire: true) }
            .Select(routine => $"""
                CREATE FUNCTION {sql.Name(routine.Name)}({Arguments}) RETURNS jsonb LANGUAGE plpgsql VOLATILE
                    SECURITY DEFINER SET search_path = pg_catalog AS $body${Body(context, enrollment, routine.Acquire)}$body$;
                REVOKE ALL ON FUNCTION {sql.Name(routine.Name)}({Signature}) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION {sql.Name(routine.Name)}({Signature}) TO {Runtime};
                COMMENT ON FUNCTION {sql.Name(routine.Name)}({Signature}) IS {Text(Stamp + Hash(Body(context, enrollment, routine.Acquire)))};
                """));
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"""
            DO $install$
            DECLARE role_name text; previous_role text;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'ai_moderation_target_migrator_required' USING ERRCODE = '42501'; END IF;
                {PostgresHumanEventAssignmentSourceContract.AssignmentSourceAttestationSql(context, enrollment)}
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(",", Roles.Select(Text))})) THEN
                    {Guard(context, enrollment)} {ExactBodies(context, enrollment)}
                    RETURN;
                END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                    WHERE n.nspname = {Text(sql.Schema)} AND f.proname LIKE 'human_ai_moderation_proposal_target_%')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy WHERE polname IN ('{ReadPolicy}','{UpdatePolicy}'))
                THEN RAISE EXCEPTION 'ai_moderation_target_bootstrap_drift' USING ERRCODE = '55000'; END IF;
                FOREACH role_name IN ARRAY ARRAY[{string.Join(",", Roles.Select(Text))}] LOOP
                    EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',role_name);
                    EXECUTE pg_catalog.format('COMMENT ON ROLE %I IS %L',role_name,'{Stamp}role:' || role_name);
                END LOOP;
                {string.Join("\n", privileges.Memberships.Select(edge => $"""
                    EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                        SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
                    """))}
                FOREACH role_name IN ARRAY ARRAY[{string.Join(",", Roles.Select(Text))}] LOOP
                    IF role_name <> '{Migrator}' AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                        WHERE m.roleid = pg_catalog.to_regrole(role_name)::oid AND m.member = pg_catalog.to_regrole(session_user)::oid)
                    THEN EXECUTE pg_catalog.format('REVOKE %I FROM %I',role_name,session_user); END IF;
                END LOOP;
                GRANT USAGE ON SCHEMA {sql.Quote(sql.Schema)} TO {string.Join(",", Roles)};
                GRANT SELECT (id,revision), UPDATE (revision) ON {sql.Name(Epoch)} TO {Owner};
                {string.Join("\n", Sources.Select(source => $"""
                    GRANT SELECT ({string.Join(",", source.Columns.Select(column => sql.Column(source.Type, column)))})
                        ON {sql.Table(source.Type)} TO {Owner};
                    GRANT UPDATE ({sql.Column(source.Type, source.Columns[0])}) ON {sql.Table(source.Type)} TO {Owner};
                    CREATE POLICY {ReadPolicy} ON {sql.Table(source.Type)} FOR SELECT TO {Owner} USING (true);
                    CREATE POLICY {UpdatePolicy} ON {sql.Table(source.Type)} FOR UPDATE TO {Owner} USING (true) WITH CHECK (true);
                    """))}
                GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {Owner};
                previous_role := pg_catalog.current_setting('role');
                PERFORM pg_catalog.set_config('role','{Owner}',true);
                EXECUTE $ddl${definitions}$ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
                REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {Owner};
                {Guard(context, enrollment)} {ExactBodies(context, enrollment)}
            END $install$;
            """;
        await command.ExecuteNonQueryAsync(token);
    }
}
