namespace Explore.Persistence.Security;

public static partial class PostgresHumanEmailSourceContract
{
    /// <summary>
    /// Install after the original S/P/route/H contracts. Existing objects are only attested,
    /// never repaired. No additional sentinel, writer trigger, credential or broad membership.
    /// </summary>
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
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
        string definitions = string.Join("\n", new[] { (Observe, false), (Acquire, true) }.Select(function => $"""
            CREATE FUNCTION {sql.Name(function.Item1)}({Arguments}) RETURNS jsonb
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${Body(context, enrollment, function.Item2)}$body$;
            REVOKE ALL ON FUNCTION {sql.Name(function.Item1)}({Signature}) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {sql.Name(function.Item1)}({Signature}) TO {InstanceRole},{TenantRole};
            COMMENT ON FUNCTION {sql.Name(function.Item1)}({Signature}) IS {Text(Stamp + Hash(Body(context, enrollment, function.Item2)))};
            """));
        return $"""
            DO $email_install$
            DECLARE role_name text; role_count integer; previous_role text := pg_catalog.current_setting('role');
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'human_email_migrator_mismatch' USING ERRCODE = '42501'; END IF;
                {OriginalSources(context, enrollment)}
                {AnchorRows(context)}
                SELECT count(*) INTO role_count FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF role_count = {Roles.Length} THEN
                    {Guard(context, enrollment)}
                    {ExactBodies(context, enrollment)}
                    RETURN;
                ELSIF role_count <> 0 THEN
                    RAISE EXCEPTION 'human_email_partial_roles' USING ERRCODE = '55000';
                END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                    WHERE f.proname LIKE 'human_email_source_%')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polname IN ('{ReadPolicy}','{LockPolicy}'))
                THEN RAISE EXCEPTION 'human_email_bootstrap_artifacts' USING ERRCODE = '55000'; END IF;
                FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                    EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',role_name);
                    EXECUTE pg_catalog.format('COMMENT ON ROLE %I IS %L',role_name,'{Stamp}role:' || role_name);
                END LOOP;
                {edges}
                FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                    IF role_name <> '{MigratorRole}' AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                        WHERE m.roleid = pg_catalog.to_regrole(role_name)::oid AND m.member = pg_catalog.to_regrole(session_user)::oid)
                    THEN EXECUTE pg_catalog.format('REVOKE %I FROM %I',role_name,session_user); END IF;
                END LOOP;
                GRANT USAGE ON SCHEMA {sql.Quote(sql.Schema)} TO {string.Join(",", Roles)};
                {grants}
                {anchors}
                GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {OwnerRole};
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $email_ddl${definitions}$email_ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
                REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {OwnerRole};
                {Guard(context, enrollment)}
                {ExactBodies(context, enrollment)}
            END $email_install$;
            """;
    }
}
