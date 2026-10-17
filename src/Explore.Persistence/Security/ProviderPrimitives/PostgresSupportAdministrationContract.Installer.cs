using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresSupportAdministrationContract
{
    /// <summary>
    /// Parent composition installs this after the original S/P/route/H contracts.
    /// It does not alter those contracts, enroll Global, or repair partial installation.
    /// </summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var contract = Privileges(context, enrollment);
        var sql = new HumanPrincipalSourceSql(context);
        string roles = string.Join(",", Roles.Select(Text));
        string migrator = sql.Quote(enrollment.MigratorLogin);
        string relations = string.Join(",", Inventory.Select(source => Text(sql.Table(source.Type)) + "::regclass"));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
            """));
        string grants = string.Join("\n", contract.Relations.Select(relation =>
        {
            string columns = string.Join("\n", new[] { "SELECT", "INSERT", "UPDATE" }.Select(privilege =>
            {
                string[] selected = relation.Columns.Where(column => column.Privileges.Contains(privilege))
                    .Select(column => sql.Quote(column.Name)).ToArray();
                return selected.Length == 0 ? "" : $"GRANT {privilege} ({string.Join(",", selected)}) ON {sql.Name(relation.Name)} TO {OwnerRole};";
            }));
            return relation.Privileges.Count == 0 ? columns
                : columns + $"\nGRANT {string.Join(",", relation.Privileges)} ON {sql.Name(relation.Name)} TO {OwnerRole};";
        }));
        string definitions = string.Join("\n", Functions(context, enrollment).Select(function => $"""
            CREATE FUNCTION {FunctionSql(context, function.Name)}({function.Arguments}) RETURNS {function.Result}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${function.Body}$body$;
            REVOKE ALL ON FUNCTION {FunctionSql(context, function.Name)}({function.Signature}) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {FunctionSql(context, function.Name)}({function.Signature})
                TO {(function.Result == "trigger" ? migrator : RuntimeRole)};
            COMMENT ON FUNCTION {FunctionSql(context, function.Name)}({function.Signature}) IS {Text(Stamp + Hash(function.Body))};
            """));
        string triggers = string.Join("\n", Inventory.Select(source => $"""
            CREATE TRIGGER {Prefix}changed BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON {sql.Table(source.Type)}
                FOR EACH STATEMENT EXECUTE FUNCTION {FunctionSql(context, "changed")}();
            ALTER TABLE {sql.Table(source.Type)} ENABLE ALWAYS TRIGGER {Prefix}changed;
            """));
        string policies = string.Join("\n", Inventory.Where(source => tenantTables.Any(table =>
                table.TableName == context.Model.FindEntityType(source.Type)!.GetTableName())).Select(source =>
            $"CREATE POLICY {Policy} ON {sql.Table(source.Type)} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);"));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; existing integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'support_administration_migrator_required' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, enrollment)}
                {OriginalSources(context, enrollment)}
                SELECT count(*) INTO existing FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF existing <> 0 THEN
                    IF existing <> 3 THEN RAISE EXCEPTION 'support_administration_partial_roles' USING ERRCODE = '55000'; END IF;
                    {Guard(context, enrollment)}
                    {ExactBodies(context, enrollment)}
                    RETURN;
                END IF;
                IF pg_catalog.to_regclass({Text(sql.Name(SourceTable))}) IS NOT NULL
                    OR pg_catalog.to_regclass({Text(sql.Name(AttemptTable))}) IS NOT NULL
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                        WHERE n.nspname = {Text(sql.Schema)} AND f.proname LIKE '{Prefix}%')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgname IN ('{Prefix}changed','{Prefix}settlement')
                        AND t.tgrelid IN ({relations}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polname = '{Policy}' AND p.polrelid IN ({relations}))
                THEN RAISE EXCEPTION 'support_administration_partial_installation' USING ERRCODE = '55000'; END IF;
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
                CREATE TABLE {sql.Name(SourceTable)} (id smallint PRIMARY KEY CHECK (id = 1), revision bigint NOT NULL CHECK (revision >= 0));
                INSERT INTO {sql.Name(SourceTable)} (id,revision) VALUES (1,0);
                CREATE TABLE {sql.Name(AttemptTable)} (
                    attempt bigint NOT NULL,backend integer NOT NULL,request jsonb NOT NULL,preimage jsonb NOT NULL,
                    plan jsonb,postimage jsonb,result text,applied boolean NOT NULL,completed boolean NOT NULL,
                    PRIMARY KEY (attempt,backend));
                {policies}
                GRANT USAGE ON SCHEMA {sql.Quote(sql.Schema)} TO {string.Join(",", Roles)};
                GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {OwnerRole};
                {grants}
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $definitions${definitions}$definitions$;
                PERFORM pg_catalog.set_config('role','none',true);
                REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {OwnerRole};
                {triggers}
                CREATE CONSTRAINT TRIGGER {Prefix}settlement AFTER INSERT ON {sql.Name(AttemptTable)}
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION {FunctionSql(context, "settlement")}();
                ALTER TABLE {sql.Name(AttemptTable)} ENABLE ALWAYS TRIGGER {Prefix}settlement;
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                REVOKE EXECUTE ON FUNCTION {FunctionSql(context, "changed")}() FROM {migrator};
                REVOKE EXECUTE ON FUNCTION {FunctionSql(context, "settlement")}() FROM {migrator};
                PERFORM pg_catalog.set_config('role','none',true);
                {Guard(context, enrollment)}
                {ExactBodies(context, enrollment)}
            END $bootstrap$;
            """;
    }
}
