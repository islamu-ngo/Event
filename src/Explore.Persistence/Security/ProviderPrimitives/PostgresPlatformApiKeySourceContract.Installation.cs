using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresPlatformApiKeySourceContract
{
    private static (Type Type, string[] Columns, string[] Locks)[] Sources() =>
        HumanPrincipalSourceSql.Sources.Concat(HumanPrincipalSourceSql.LocalSources)
            .Concat(HumanSupportAccessSourceSql.Sources)
            .Select(source => (source.Type, source.Columns, Locks: new[] { source.Columns[0] }))
            .Concat(HumanAuthorizationRouteSql.Sources.Select(source =>
                (source.Type, source.Columns, Locks: Array.Empty<string>())))
            .GroupBy(source => source.Type)
            .Select(group => (group.Key, group.SelectMany(source => source.Columns).Distinct(StringComparer.Ordinal).ToArray(),
                group.SelectMany(source => source.Locks).Distinct(StringComparer.Ordinal).ToArray())).ToArray();

    private static PostgresPrivilegeGuard.Contract Privileges(ExploreDbContext context, PlatformApiKeySourceEnrollment enrollment)
    {
        enrollment.Validate();
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, [.. Roles, .. Executors],
            [new(Observe, Signature, OwnerRole, Executors), new(Acquire, Signature, OwnerRole, Executors)],
            [.. Sources().Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Type)!.GetTableName()!, OwnerRole, [],
                source.Columns.Select(property => new PostgresPrivilegeGuard.Column(sql.ColumnName(source.Type, property),
                    source.Locks.Contains(property, StringComparer.Ordinal) ? ["SELECT", "UPDATE"] : ["SELECT"])).ToArray())),
                .. Anchors.Select(anchor => new PostgresPrivilegeGuard.Relation(anchor, OwnerRole, [],
                    [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])]))],
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.Original.MigratorLogin, false, true, false),
                new(Executors[0], enrollment.ReadLogin, true, false, false),
                new(Executors[1], enrollment.WriteLogin, true, false, false),
                new(Executors[2], enrollment.ReportLogin, true, false, false)]);
    }

    private static string Guard(ExploreDbContext context, PlatformApiKeySourceEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        string routineIds = string.Join(",", new[] { Observe, Acquire }.Select(name =>
            $"pg_catalog.to_regprocedure({Text(sql.Name(name) + "(" + Signature + ")")})"));
        string routines = string.Join("\n", new[] { Observe, Acquire }.Select(name => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(sql.Name(name) + "(" + Signature + ")")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof
                    AND NOT f.proisstrict AND NOT f.proretset AND f.proargdefaults IS NULL
                    AND f.prorettype = 'pg_catalog.jsonb'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.obj_description(f.oid,'pg_proc') =
                        '{Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
            THEN RAISE EXCEPTION 'platform_key_source_routine_drift' USING ERRCODE = '55000'; END IF;
            """));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string policies = string.Join("\n", Sources().Select(source =>
        {
            string table = sql.Table(source.Type);
            bool tenant = tenantTables.Any(candidate => candidate.TableName == context.Model.FindEntityType(source.Type)!.GetTableName());
            string locks = source.Locks.Length == 0 ? "" : $"""
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = pg_catalog.to_regclass({Text(table)}) AND p.polname = '{LockPolicy}'
                        AND p.polpermissive AND p.polcmd = 'w' AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true'
                        AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'true')
                """;
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = pg_catalog.to_regclass({Text(table)}) AND p.polname = '{ReadPolicy}'
                        AND p.polpermissive AND p.polcmd = 'r' AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true' AND p.polwithcheck IS NULL)
                    {locks}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = pg_catalog.to_regclass({Text(table)})
                            AND (0::oid = ANY(p.polroles) OR pg_catalog.to_regrole('{OwnerRole}')::oid = ANY(p.polroles))
                            AND p.polname NOT IN ('{ReadPolicy}'{(source.Locks.Length == 0 ? "" : $",'{LockPolicy}'")}
                                {(tenant ? $",'{PostgresTenantRowLevelSecurityModel.PolicyName}'" : "")}))
                THEN RAISE EXCEPTION 'platform_key_source_policy_drift' USING ERRCODE = '55000'; END IF;
                """;
        }));
        return $"""
            {OriginalSources(context, enrollment)}
            {PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment))}
            {routines}
            {policies}
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM '{Stamp}role:' || r.rolname)
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                    AND NOT pg_catalog.has_schema_privilege(r.oid,{Text(sql.Schema)},'USAGE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                    CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({roles})
                        AND n.nspname <> {Text(sql.Schema)} AND n.nspname NOT IN ('pg_catalog','information_schema')
                        AND n.nspname NOT LIKE 'pg_toast%' AND t.relkind IN ('r','p','v','m','f')
                        AND (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND t.relkind = 'S' AND pg_catalog.has_sequence_privilege(r.oid,t.oid,'USAGE,SELECT,UPDATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_roles r
                    WHERE r.rolname IN ({roles}) AND pg_catalog.has_database_privilege(r.oid,d.oid,'CREATE'))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass
                    AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                    AND d.deptype = 'o' AND (d.classid <> 'pg_catalog.pg_proc'::regclass
                        OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())
                        OR d.objid NOT IN ({routineIds})))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({routineIds})
                    AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            THEN RAISE EXCEPTION 'platform_key_source_authority_drift' USING ERRCODE = '55000'; END IF;
            """;
    }

    /// <summary>Install after original S/P/route/H and key capabilities. Existing drift is never repaired.</summary>
    public static string BuildSql(ExploreDbContext context, PlatformApiKeySourceEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string edges = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
            """));
        string grants = string.Join("\n", Sources().Select(source => $"""
            GRANT SELECT ({string.Join(",", source.Columns.Select(property => sql.Column(source.Type, property)))})
                ON {sql.Table(source.Type)} TO {OwnerRole};
            CREATE POLICY {ReadPolicy} ON {sql.Table(source.Type)} FOR SELECT TO {OwnerRole} USING (true);
            {(source.Locks.Length == 0 ? "" : $"""
            GRANT UPDATE ({string.Join(",", source.Locks.Select(property => sql.Column(source.Type, property)))})
                ON {sql.Table(source.Type)} TO {OwnerRole};
            CREATE POLICY {LockPolicy} ON {sql.Table(source.Type)} FOR UPDATE TO {OwnerRole} USING (true) WITH CHECK (true);
            """)}
            """));
        string anchors = string.Join("\n", Anchors.Select(anchor =>
            $"GRANT SELECT (id,revision), UPDATE (revision) ON {sql.Name(anchor)} TO {OwnerRole};"));
        string definitions = string.Join("\n", Functions(context, enrollment, Executors[0]).Select(function => $"""
            CREATE FUNCTION {sql.Name(function.Name)}({Arguments}) RETURNS jsonb
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${function.Source}$body$;
            REVOKE ALL ON FUNCTION {sql.Name(function.Name)}({Signature}) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {sql.Name(function.Name)}({Signature}) TO {string.Join(",", Executors)};
            COMMENT ON FUNCTION {sql.Name(function.Name)}({Signature}) IS {Text(Stamp + Hash(function.Source))};
            """));
        return $"""
            DO $key_source_install$
            DECLARE role_name text; role_count integer; previous_role text := pg_catalog.current_setting('role');
            BEGIN
                IF session_user <> {Text(enrollment.Original.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'platform_key_source_migrator_mismatch' USING ERRCODE = '42501'; END IF;
                {OriginalSources(context, enrollment)}
                {AnchorRows(context)}
                SELECT count(*) INTO role_count FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF role_count = {Roles.Length} THEN
                    {Guard(context, enrollment)}
                    {ExactBodies(context, enrollment)}
                    RETURN;
                ELSIF role_count <> 0 THEN RAISE EXCEPTION 'platform_key_source_partial_roles' USING ERRCODE = '55000'; END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.proname LIKE 'platform_key_source_%')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polname IN ('{ReadPolicy}','{LockPolicy}'))
                THEN RAISE EXCEPTION 'platform_key_source_bootstrap_artifacts' USING ERRCODE = '55000'; END IF;
                FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                    EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',role_name);
                    EXECUTE pg_catalog.format('COMMENT ON ROLE %I IS %L',role_name,'{Stamp}role:' || role_name);
                END LOOP;
                {edges}
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m WHERE m.roleid = pg_catalog.to_regrole('{OwnerRole}')::oid
                    AND m.member = pg_catalog.to_regrole(session_user)::oid)
                THEN EXECUTE pg_catalog.format('REVOKE %I FROM %I','{OwnerRole}',session_user); END IF;
                GRANT USAGE ON SCHEMA {sql.Quote(sql.Schema)} TO {string.Join(",", Roles)};
                {grants}
                {anchors}
                GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {OwnerRole};
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $key_source_ddl${definitions}$key_source_ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
                REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {OwnerRole};
                {Guard(context, enrollment)}
                {ExactBodies(context, enrollment)}
            END $key_source_install$;
            """;
    }
}
