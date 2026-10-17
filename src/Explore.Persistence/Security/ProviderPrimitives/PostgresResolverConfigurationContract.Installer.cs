using Explore.Domain;

namespace Explore.Persistence.Security;

public static partial class PostgresResolverConfigurationContract
{
    /// <summary>
    /// Source-defined native installation through the declared Migrator only. All existing
    /// artifacts are attested before replacement; partial installation and drift are fatal.
    /// </summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string guard = Guard(context, enrollment);
        string exact = ExactBodies(context, enrollment);
        string anchor = sql.Name(Sentinel);
        string table = sql.Table(typeof(SystemSetting));
        string migrator = sql.Quote(enrollment.MigratorLogin);
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
            """));
        string grants = string.Join("\n", contract.Relations.SelectMany(relation =>
            new[] { "SELECT", "INSERT", "UPDATE" }.Select(privilege =>
            {
                var columns = relation.Columns.Where(column => column.Privileges.Contains(privilege, StringComparer.Ordinal))
                    .Select(column => sql.Quote(column.Name)).ToArray();
                return columns.Length == 0 ? "" : $"GRANT {privilege} ({string.Join(",", columns)}) ON {sql.Name(relation.Name)} TO {OwnerRole};";
            })));
        string definitions = string.Join("\n", Functions(context, enrollment).Select(function => $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, function.Operation)}({function.Arguments}) RETURNS {function.Result}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${function.Body}$body$;
            REVOKE ALL ON FUNCTION {FunctionSql(context, function.Operation)}({function.Signature}) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {FunctionSql(context, function.Operation)}({function.Signature})
                TO {(function.Result == "trigger" ? migrator : GlobalRole)};
            COMMENT ON FUNCTION {FunctionSql(context, function.Operation)}({function.Signature}) IS {Text(Stamp + Hash(function.Body))};
            """));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; existing integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'resolver_configuration_migrator' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, enrollment)}
                SELECT count(*) INTO existing FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF existing = 0 THEN
                    IF pg_catalog.to_regclass({Text(anchor)}) IS NOT NULL
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                            WHERE n.nspname = {Text(sql.Schema)} AND f.proname LIKE '{Prefix}%')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({Text(table)})
                            AND t.tgname IN ('{Prefix}changed','{Prefix}truncate'))
                    THEN RAISE EXCEPTION 'resolver_configuration_partial_installation' USING ERRCODE = '55000'; END IF;
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
                    CREATE TABLE {anchor} (id smallint PRIMARY KEY CHECK (id = 1), revision boolean NOT NULL);
                    INSERT INTO {anchor} (id,revision) VALUES (1,false);
                ELSE
                    IF existing <> 3 THEN RAISE EXCEPTION 'resolver_configuration_partial_roles' USING ERRCODE = '55000'; END IF;
                    {guard}
                    {exact}
                    IF NOT EXISTS (SELECT 1 FROM {anchor} WHERE id = 1)
                    THEN RAISE EXCEPTION 'resolver_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
                END IF;
            END $bootstrap$;
            GRANT USAGE ON SCHEMA {sql.Quote(sql.Schema)} TO {string.Join(",", Roles)};
            GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {OwnerRole};
            {grants}
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl${definitions}$ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {OwnerRole};
            DROP TRIGGER IF EXISTS {Prefix}changed ON {table};
            CREATE TRIGGER {Prefix}changed AFTER INSERT OR UPDATE OR DELETE ON {table}
                FOR EACH ROW EXECUTE FUNCTION {FunctionSql(context, "changed")}();
            ALTER TABLE {table} ENABLE ALWAYS TRIGGER {Prefix}changed;
            DROP TRIGGER IF EXISTS {Prefix}truncate ON {table};
            CREATE TRIGGER {Prefix}truncate BEFORE TRUNCATE ON {table}
                FOR EACH STATEMENT EXECUTE FUNCTION {FunctionSql(context, "changed")}();
            ALTER TABLE {table} ENABLE ALWAYS TRIGGER {Prefix}truncate;
            DO $close_trigger$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                REVOKE EXECUTE ON FUNCTION {FunctionSql(context, "changed")}() FROM {migrator};
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $close_trigger$;
            DO $installed$ BEGIN {guard} {exact} END $installed$;
            """;
    }
}
