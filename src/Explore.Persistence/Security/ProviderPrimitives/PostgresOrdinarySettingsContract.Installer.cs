using Explore.Domain;

namespace Explore.Persistence.Security;

public static partial class PostgresOrdinarySettingsContract
{
    /// <summary>
    /// Installs only through the independently declared migrator. Reinstallation attests
    /// before replacing anything; it never normalizes an unknown partial contract.
    /// </summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string guard = Guard(context, enrollment);
        string exact = ExactBodies(context, enrollment);
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
            """));
        string grants = string.Join("\n", contract.Relations.Select(relation =>
            (relation.Privileges.Count == 0 ? "" : $"GRANT {string.Join(",", relation.Privileges)} ON {sql.Name(relation.Name)} TO {relation.Role};")
            + "\n" + string.Join("\n", new[] { "SELECT", "INSERT", "UPDATE" }.Select(privilege =>
            {
                string[] columns = relation.Columns.Where(column => column.Privileges.Contains(privilege, StringComparer.Ordinal))
                    .Select(column => sql.Quote(column.Name)).ToArray();
                return columns.Length == 0 ? "" : $"GRANT {privilege} ({string.Join(",", columns)}) ON {sql.Name(relation.Name)} TO {relation.Role};";
            }))));
        string definitions = string.Join("\n", Routines.Select(routine =>
        {
            string body = Body(context, enrollment, routine.Name);
            return $"""
                CREATE OR REPLACE FUNCTION {FunctionSql(context, routine.Name)}({routine.Arguments}) RETURNS {routine.Result}
                    LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${body}$body$;
                REVOKE ALL ON FUNCTION {FunctionSql(context, routine.Name)}({routine.Signature}) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION {FunctionSql(context, routine.Name)}({routine.Signature})
                    TO {(routine.Result == "trigger" ? sql.Quote(enrollment.MigratorLogin) : RuntimeRole + "," + GlobalRole)};
                COMMENT ON FUNCTION {FunctionSql(context, routine.Name)}({routine.Signature}) IS {Text(Stamp + Hash(body))};
                """;
        }));
        string triggers = string.Join("\n", Sources.Select(type => $"""
            DROP TRIGGER IF EXISTS {Prefix}changed ON {sql.Table(type)};
            CREATE TRIGGER {Prefix}changed BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON {sql.Table(type)}
                FOR EACH STATEMENT EXECUTE FUNCTION {FunctionSql(context, "changed")}();
            ALTER TABLE {sql.Table(type)} ENABLE ALWAYS TRIGGER {Prefix}changed;
            DROP TRIGGER IF EXISTS {Prefix}row ON {sql.Table(type)};
            CREATE TRIGGER {Prefix}row AFTER INSERT OR UPDATE OR DELETE ON {sql.Table(type)}
                FOR EACH ROW EXECUTE FUNCTION {FunctionSql(context, "row")}();
            ALTER TABLE {sql.Table(type)} ENABLE ALWAYS TRIGGER {Prefix}row;
            """));
        string policies = string.Join("\n", Sources.Where(type => type != typeof(SystemSetting)).Select(type => $"""
            DROP POLICY IF EXISTS {Policy} ON {sql.Table(type)};
            CREATE POLICY {Policy} ON {sql.Table(type)} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
            """));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; installed integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'ordinary_settings_original_migrator' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, enrollment)}
                SELECT count(*) INTO installed FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF installed = 0 THEN
                    IF pg_catalog.to_regclass({Text(sql.Name(SourceTable))}) IS NOT NULL
                        OR pg_catalog.to_regclass({Text(sql.Name(AttemptTable))}) IS NOT NULL
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                            WHERE n.nspname = {Text(sql.Schema)} AND f.proname LIKE '{Prefix}%')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                            WHERE t.tgrelid IN ({string.Join(",", Sources.Select(type => Text(sql.Table(type)) + "::regclass"))})
                                AND t.tgname IN ('{Prefix}changed','{Prefix}row'))
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                            WHERE t.tgrelid = {Text(sql.Table(typeof(Tenant)))}::regclass
                                AND t.tgname IN ('{Prefix}tenant_changed','{Prefix}tenant_truncated'))
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                            WHERE p.polrelid IN ({string.Join(",", Sources.Select(type => Text(sql.Table(type)) + "::regclass"))})
                                AND p.polname = '{Policy}')
                    THEN RAISE EXCEPTION 'ordinary_settings_partial_installation' USING ERRCODE = '55000'; END IF;
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
                    CREATE TABLE {sql.Name(SourceTable)}
                        (id smallint PRIMARY KEY CHECK (id = 1),revision bigint NOT NULL,tenant_revision bigint NOT NULL);
                    INSERT INTO {sql.Name(SourceTable)} (id,revision,tenant_revision) VALUES (1,0,0);
                    CREATE TABLE {sql.Name(AttemptTable)} (
                        attempt bigint NOT NULL,backend integer NOT NULL,tenant uuid,user_id uuid,
                        request jsonb NOT NULL,image jsonb NOT NULL,completed jsonb NOT NULL,pending jsonb,
                        observed boolean NOT NULL,finished boolean NOT NULL,disclosure_tenants uuid[] NOT NULL,
                        PRIMARY KEY (attempt,backend));
                    REVOKE ALL ON {sql.Name(SourceTable)},{sql.Name(AttemptTable)} FROM PUBLIC;
                ELSE
                    IF installed <> {Roles.Length} THEN RAISE EXCEPTION 'ordinary_settings_partial_roles' USING ERRCODE = '55000'; END IF;
                    {guard}
                    {exact}
                    IF NOT EXISTS (SELECT 1 FROM {sql.Name(SourceTable)} WHERE id = 1)
                    THEN RAISE EXCEPTION 'ordinary_settings_anchor_missing' USING ERRCODE = '55000'; END IF;
                END IF;
            END $bootstrap$;
            GRANT USAGE ON SCHEMA {sql.Quote(sql.Schema)} TO {string.Join(",", Roles)};
            {grants}
            DO $disclosure_grants$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{PostgresDiscoveryDisclosureContract.OwnerRole}',true);
                EXECUTE 'GRANT EXECUTE ON FUNCTION
                    {PostgresDiscoveryDisclosureContract.FunctionSql(context, "dependencies")}(text,uuid[]),
                    {PostgresDiscoveryDisclosureContract.FunctionSql(context, "fence_tenants")}(uuid[]),
                    {PostgresDiscoveryDisclosureContract.FunctionSql(context, "finalize")}(uuid[],uuid[],boolean[],bigint[],bigint[])
                    TO {OwnerRole}';
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $disclosure_grants$;
            GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {OwnerRole};
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl${definitions}$ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {OwnerRole};
            {policies}
            {triggers}
            DROP TRIGGER IF EXISTS {Prefix}tenant_changed ON {sql.Table(typeof(Tenant))};
            CREATE TRIGGER {Prefix}tenant_changed AFTER INSERT OR UPDATE OR DELETE ON {sql.Table(typeof(Tenant))}
                FOR EACH ROW EXECUTE FUNCTION {FunctionSql(context, "tenant_changed")}();
            ALTER TABLE {sql.Table(typeof(Tenant))} ENABLE ALWAYS TRIGGER {Prefix}tenant_changed;
            DROP TRIGGER IF EXISTS {Prefix}tenant_truncated ON {sql.Table(typeof(Tenant))};
            CREATE TRIGGER {Prefix}tenant_truncated AFTER TRUNCATE ON {sql.Table(typeof(Tenant))}
                FOR EACH STATEMENT EXECUTE FUNCTION {FunctionSql(context, "tenant_truncated")}();
            ALTER TABLE {sql.Table(typeof(Tenant))} ENABLE ALWAYS TRIGGER {Prefix}tenant_truncated;
            DROP TRIGGER IF EXISTS {Prefix}settlement ON {sql.Name(AttemptTable)};
            CREATE CONSTRAINT TRIGGER {Prefix}settlement AFTER INSERT OR UPDATE ON {sql.Name(AttemptTable)}
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION {FunctionSql(context, "settlement")}();
            ALTER TABLE {sql.Name(AttemptTable)} ENABLE ALWAYS TRIGGER {Prefix}settlement;
            DO $close$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                REVOKE EXECUTE ON FUNCTION {FunctionSql(context, "changed")}(),{FunctionSql(context, "row")}(),
                    {FunctionSql(context, "tenant_changed")}(),{FunctionSql(context, "tenant_truncated")}(),
                    {FunctionSql(context, "settlement")}() FROM {sql.Quote(enrollment.MigratorLogin)};
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $close$;
            DO $installed$ BEGIN {guard} {exact} END $installed$;
            """;
    }
}
