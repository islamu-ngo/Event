using Explore.Domain;

namespace Explore.Persistence.Security;

public static partial class PostgresAiConversationOwnerFenceContract
{
    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var definition = Define(context, enrollment);
        var sql = definition.Sql;
        var names = sql.Names;
        string roles = string.Join(",", Roles.Select(Text));
        string sources = string.Join(",", sql.Sources.Keys.Select(type => Text(names.Table(type)) + "::regclass"));
        string memberships = string.Join("\n", definition.Privileges.Memberships.Select(edge => $"""
            EXECUTE pg_catalog.format('GRANT %I TO %I WITH INHERIT {Boolean(edge.Inherit)}, SET {Boolean(edge.Set)}, ADMIN FALSE',
                {Text(edge.Parent)},{Text(edge.Member)});
            """));
        string policies = string.Join("\n", definition.Policies.SelectMany(pair => PolicyModes.Select(permissive =>
        {
            string name = permissive ? Policy + "_allow" : Policy;
            return $"""
                CREATE POLICY {name} ON {names.Table(pair.Key)} AS {(permissive ? "PERMISSIVE" : "RESTRICTIVE")}
                    FOR ALL TO {OwnerRole} USING ({pair.Value}) WITH CHECK (false);
                EXECUTE (SELECT pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',p.polname,
                    {Text(names.Table(pair.Key))},{Text(Stamp + Hash(pair.Value) + ":")} || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                    FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(names.Table(pair.Key))}::regclass AND p.polname = '{name}');
                """;
        })));
        string grants = string.Join("\n", sql.Sources.Select(source =>
            $"GRANT SELECT ({string.Join(",", source.Value.Order(StringComparer.Ordinal).Select(property => names.Column(source.Key, property)))}) ON {names.Table(source.Key)} TO {OwnerRole};"
            + (source.Key == typeof(PrivacyErasureSaga) ? "" : $" GRANT UPDATE ({names.Column(source.Key, sql.LockProperty(source.Key))}) ON {names.Table(source.Key)} TO {OwnerRole};")));
        return $"""
            DO $bootstrap$
            DECLARE role_name text; existing integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                THEN RAISE EXCEPTION 'ai_conversation_owner_migrator_required' USING ERRCODE = '42501'; END IF;
                {SourceGuard(context, sql, enrollment)}
                SELECT count(*) INTO existing FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF existing = 0 THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                        WHERE n.nspname = {Text(names.Schema)} AND p.proname LIKE 'ai_conversation_owner_%')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid IN ({sources})
                            AND p.polname IN ('{Policy}','{Policy}_allow'))
                    THEN RAISE EXCEPTION 'ai_conversation_owner_partial_installation' USING ERRCODE = '55000'; END IF;
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',role_name);
                    END LOOP;
                    {memberships}
                    FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                        IF role_name <> '{MigratorRole}' AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members m
                            WHERE m.roleid = pg_catalog.to_regrole(role_name)::oid AND m.member = pg_catalog.to_regrole(session_user)::oid)
                        THEN EXECUTE pg_catalog.format('REVOKE %I FROM %I',role_name,session_user); END IF;
                    END LOOP;
                    {policies}
                ELSE
                    IF existing <> 3 THEN RAISE EXCEPTION 'ai_conversation_owner_partial_roles' USING ERRCODE = '55000'; END IF;
                    {definition.Guard} {ExactBodies(context, definition)}
                END IF;
            END $bootstrap$;
            GRANT USAGE ON SCHEMA {names.Quote(names.Schema)} TO {string.Join(",", Roles)};
            GRANT CREATE ON SCHEMA {names.Quote(names.Schema)} TO {OwnerRole};
            {grants}
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                PERFORM pg_catalog.set_config('role','{OwnerRole}',true);
                EXECUTE $ddl$
                    CREATE OR REPLACE FUNCTION {FunctionSql(context)}(p_tenant uuid,p_principal uuid,p_actor uuid,p_admitted uuid,p_privacy boolean,p_organizer boolean)
                        RETURNS boolean
                        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${definition.Body}$body$;
                    REVOKE ALL ON FUNCTION {FunctionSql(context)}({Arguments}) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION {FunctionSql(context)}({Arguments}) TO {RuntimeRole};
                    CREATE OR REPLACE FUNCTION {PrivacyFunctionSql(context)}(p_tenant uuid,p_principal uuid)
                        RETURNS boolean
                        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${definition.PrivacyBody}$body$;
                    REVOKE ALL ON FUNCTION {PrivacyFunctionSql(context)}(uuid,uuid) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION {PrivacyFunctionSql(context)}(uuid,uuid) TO {RuntimeRole};
                $ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
            END $install$;
            REVOKE CREATE ON SCHEMA {names.Quote(names.Schema)} FROM {OwnerRole};
            DO $installed$ BEGIN {definition.Guard} {ExactBodies(context, definition)} END $installed$;
            """;
    }
}
