using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresManagedProviderProvisioningContract
{
    /// <summary>
    /// Install after original human source contracts, using the configured migrator.
    /// Reinstallation attests an existing installation; it does not normalize drift.
    /// </summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var contract = Privileges(context, enrollment);
        var sql = new HumanPrincipalSourceSql(context);
        string migrator = sql.Quote(enrollment.MigratorLogin);
        string memberships = string.Join("\n", contract.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {edge.Inherit.ToString().ToUpperInvariant()},
                SET {edge.Set.ToString().ToUpperInvariant()}, ADMIN FALSE',{Text(edge.Parent)},{Text(edge.Member)});
            """));
        string grants = string.Join("\n", contract.Relations.Select(relation =>
            (relation.Privileges.Count == 0 ? "" : $"GRANT {string.Join(",", relation.Privileges)} ON {sql.Name(relation.Name)} TO {OwnerRole};\n")
            + string.Join("\n", new[] { "SELECT", "INSERT", "UPDATE" }.Select(privilege =>
            {
                var columns = relation.Columns.Where(column => column.Privileges.Contains(privilege))
                    .Select(column => sql.Quote(column.Name)).ToArray();
                return columns.Length == 0 ? "" : $"GRANT {privilege} ({string.Join(",", columns)}) ON {sql.Name(relation.Name)} TO {OwnerRole};";
            }))));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string policies = string.Join("\n", Sources.Where(relation => tenantTables.Any(table =>
                table.TableName == context.Model.FindEntityType(relation.Type)!.GetTableName()))
            .Select(relation => $"CREATE POLICY {Policy} ON {sql.Table(relation.Type)} FOR ALL TO {OwnerRole} USING (true) WITH CHECK (true);"));
        string definitions = string.Join("\n", Functions(context, enrollment).Select(function => $"""
            CREATE FUNCTION {FunctionSql(context, function.Name)}({function.Arguments}) RETURNS {function.Result}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${function.Body}$body$;
            REVOKE ALL ON FUNCTION {FunctionSql(context, function.Name)}({function.Signature}) FROM PUBLIC;
            {(function.Public ? $"GRANT EXECUTE ON FUNCTION {FunctionSql(context, function.Name)}({function.Signature}) TO {GlobalRole};"
                : function.Result == "trigger" ? $"GRANT EXECUTE ON FUNCTION {FunctionSql(context, function.Name)}({function.Signature}) TO {migrator};" : "")}
            COMMENT ON FUNCTION {FunctionSql(context, function.Name)}({function.Signature}) IS {Text(Stamp + Hash(function.Body))};
            """));
        string triggers = string.Join("\n", Sources.Select(relation => $"""
            CREATE TRIGGER {Prefix}changed BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON {sql.Table(relation.Type)}
                FOR EACH STATEMENT EXECUTE FUNCTION {FunctionSql(context, "changed")}();
            ALTER TABLE {sql.Table(relation.Type)} ENABLE ALWAYS TRIGGER {Prefix}changed;
            """));
        return $"""
            DO $install$
            DECLARE role_name text; existing integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'human_provisioning_migrator_required' USING ERRCODE = '42501'; END IF;
                {SourceCatalog(context, enrollment)}
                {OriginalSources(context, enrollment)}
                SELECT count(*) INTO existing FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(",", Roles.Select(Text))});
                IF existing <> 0 THEN
                    IF existing <> 3 THEN RAISE EXCEPTION 'human_provisioning_partial_roles' USING ERRCODE = '55000'; END IF;
                    {Guard(context, enrollment)}
                    {ExactBodies(context, enrollment)}
                    RETURN;
                END IF;
                IF pg_catalog.to_regclass({Text(sql.Name(SourceTable))}) IS NOT NULL
                    OR pg_catalog.to_regclass({Text(sql.Name(AttemptTable))}) IS NOT NULL
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                        WHERE n.nspname = {Text(sql.Schema)} AND f.proname LIKE '{Prefix}%')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgname = '{Prefix}changed')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polname = '{Policy}')
                THEN RAISE EXCEPTION 'human_provisioning_partial_installation' USING ERRCODE = '55000'; END IF;
                FOREACH role_name IN ARRAY ARRAY[{string.Join(",", Roles.Select(Text))}] LOOP
                    EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',role_name);
                    EXECUTE pg_catalog.format('COMMENT ON ROLE %I IS %L',role_name,'{Stamp}role:' || role_name);
                END LOOP;
                {memberships}
                FOREACH role_name IN ARRAY ARRAY[{string.Join(",", Roles.Select(Text))}] LOOP
                    IF role_name <> '{MigratorRole}' AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                        WHERE m.roleid = pg_catalog.to_regrole(role_name)::oid AND m.member = pg_catalog.to_regrole(session_user)::oid)
                    THEN EXECUTE pg_catalog.format('REVOKE %I FROM %I',role_name,session_user); END IF;
                END LOOP;
                CREATE TABLE {sql.Name(SourceTable)} (id smallint PRIMARY KEY CHECK (id = 1),revision bigint NOT NULL CHECK (revision >= 0));
                INSERT INTO {sql.Name(SourceTable)} (id,revision) VALUES (1,0);
                CREATE TABLE {sql.Name(AttemptTable)} (
                    attempt bigint NOT NULL,backend integer NOT NULL,request jsonb NOT NULL,preimage jsonb NOT NULL,
                    plan jsonb,expected jsonb,applied boolean NOT NULL,completed boolean NOT NULL,PRIMARY KEY (attempt,backend));
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
            END $install$;
            """;
    }
}
