using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresStorageConfigurationContract
{
    /// <summary>Migration-owned installation; existing artifacts are attested before any replacement.</summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        var contract = Privileges(context, enrollment);
        string roles = string.Join(",", Roles.Select(Text));
        string anchor = sql.Name(SourceTable);
        string migrator = sql.Quote(enrollment.MigratorLogin);
        string sources = string.Join(",", Sources.Select(type => $"{Text(sql.Table(type))}::regclass"));
        string guard = Guard(context, enrollment);
        string exact = ExactBodies(context, enrollment);
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
            """));
        string grants = string.Join("\n", contract.Relations.SelectMany(relation =>
            new[] { "SELECT", "INSERT", "UPDATE" }.Select(privilege =>
            {
                var columns = relation.Columns.Where(column => column.Privileges.Contains(privilege))
                    .Select(column => sql.Quote(column.Name)).ToArray();
                return columns.Length == 0 ? "" : $"GRANT {privilege} ({string.Join(",", columns)}) ON {sql.Name(relation.Name)} TO {OwnerRole};";
            })));
        string definitions = string.Join("\n", Functions(context, enrollment).Select(function => $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, function.Name)}({function.Arguments}) RETURNS {function.Result}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${function.Body}$body$;
            REVOKE ALL ON FUNCTION {FunctionSql(context, function.Name)}({function.Signature}) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {FunctionSql(context, function.Name)}({function.Signature})
                TO {(function.Result == "trigger" ? migrator : GlobalRole + "," + RuntimeRole)};
            COMMENT ON FUNCTION {FunctionSql(context, function.Name)}({function.Signature}) IS {Text(Stamp + Hash(function.Body))};
            """));
        string triggers = string.Join("\n", Sources.Select(type => $"""
            DROP TRIGGER IF EXISTS {Prefix}changed ON {sql.Table(type)};
            CREATE TRIGGER {Prefix}changed BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON {sql.Table(type)}
                FOR EACH STATEMENT EXECUTE FUNCTION {FunctionSql(context, "changed")}();
            ALTER TABLE {sql.Table(type)} ENABLE ALWAYS TRIGGER {Prefix}changed;
            """));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; existing integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'storage_configuration_migrator' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, enrollment)}
                SELECT count(*) INTO existing FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF existing = 0 THEN
                    IF pg_catalog.to_regclass({Text(anchor)}) IS NOT NULL
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                            WHERE n.nspname = {Text(sql.Schema)} AND f.proname LIKE '{Prefix}%')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgrelid IN ({sources})
                            AND t.tgname = '{Prefix}changed')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                            WHERE p.polrelid = {Text(sql.Table(typeof(TenantSetting)))}::regclass AND p.polname = '{TenantPolicy}')
                    THEN RAISE EXCEPTION 'storage_configuration_partial_installation' USING ERRCODE = '55000'; END IF;
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
                    CREATE TABLE {anchor} (id smallint PRIMARY KEY CHECK (id = 1), revision bigint NOT NULL CHECK (revision >= 0));
                    INSERT INTO {anchor} (id,revision) VALUES (1,0);
                    CREATE POLICY {TenantPolicy} ON {sql.Table(typeof(TenantSetting))}
                        FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);
                ELSE
                    IF existing <> 4 THEN RAISE EXCEPTION 'storage_configuration_partial_roles' USING ERRCODE = '55000'; END IF;
                    {guard}
                    {exact}
                    IF (SELECT count(*) FROM {anchor} WHERE id = 1) <> 1
                    THEN RAISE EXCEPTION 'storage_configuration_anchor_missing' USING ERRCODE = '55000'; END IF;
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
            {triggers}
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
