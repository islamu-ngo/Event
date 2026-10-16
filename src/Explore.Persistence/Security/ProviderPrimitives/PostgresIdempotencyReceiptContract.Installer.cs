using Explore.Domain;

namespace Explore.Persistence.Security;

public static partial class PostgresIdempotencyReceiptContract
{
    /// <summary>Reinstallation rejects drift before any replacement or grant restoration.</summary>
    internal static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new IdempotencyReceiptSql(context);
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
            string owner = routine.Discovery ? DiscoveryOwnerRole : OwnerRole;
            string executor = routine.Discovery ? GlobalRole : RuntimeRole;
            return $"""
                PERFORM pg_catalog.set_config('role','{owner}',true);
                EXECUTE $ddl$
                    CREATE OR REPLACE FUNCTION {FunctionSql(context, routine.Name)}({routine.Arguments}) RETURNS {routine.Result}
                        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${body}$body$;
                    REVOKE ALL ON FUNCTION {FunctionSql(context, routine.Name)}({routine.Signature}) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION {FunctionSql(context, routine.Name)}({routine.Signature}) TO {executor};
                    COMMENT ON FUNCTION {FunctionSql(context, routine.Name)}({routine.Signature}) IS {Text(Stamp + Hash(body))};
                $ddl$;
                PERFORM pg_catalog.set_config('role',previous_role,true);
                """;
        }));
        string C(string property) => sql.Column(typeof(IdempotencyRecord), property);
        return $"""
            DO $bootstrap$
            DECLARE role_name text; installed integer;
            BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                    OR pg_catalog.current_setting('session_replication_role') <> 'origin'
                THEN RAISE EXCEPTION 'idempotency_receipt_original_migrator' USING ERRCODE = '42501'; END IF;
                SELECT count(*) INTO installed FROM pg_catalog.pg_roles WHERE rolname IN ({roles});
                IF installed = 0 THEN
                    {SourceGuard(context, enrollment, false)}
                    IF pg_catalog.to_regclass({Text(sql.Name(OwnershipTable))}) IS NOT NULL
                        OR pg_catalog.to_regclass({Text(sql.Name("idempotency_receipt_expiry"))}) IS NOT NULL
                        OR pg_catalog.to_regclass({Text(sql.Name("idempotency_receipt_tenant_expiry"))}) IS NOT NULL
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                            WHERE n.nspname = {Text(sql.Schema)} AND f.proname LIKE '{Prefix}%')
                    THEN RAISE EXCEPTION 'idempotency_receipt_partial_installation' USING ERRCODE = '55000'; END IF;
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
                    CREATE TABLE {sql.Name(OwnershipTable)} (
                        receipt_id uuid PRIMARY KEY,tenant_id uuid NOT NULL,nonce_hash bytea NOT NULL,
                        backend integer NOT NULL,original_transaction bigint,
                        CONSTRAINT idempotency_receipt_nonce_length CHECK (pg_catalog.octet_length(nonce_hash) = 32));
                    REVOKE ALL ON {sql.Name(OwnershipTable)} FROM PUBLIC;
                    CREATE INDEX idempotency_receipt_expiry ON {sql.Table(typeof(IdempotencyRecord))}
                        ({C("ExpiresAt")},{C("Id")}) INCLUDE ({C("TenantId")}) WHERE {C("StatusCode")} <> -1;
                    CREATE INDEX idempotency_receipt_tenant_expiry ON {sql.Table(typeof(IdempotencyRecord))}
                        ({C("TenantId")},{C("ExpiresAt")},{C("Id")}) WHERE {C("StatusCode")} <> -1;
                ELSE
                    IF installed <> {Roles.Length} THEN RAISE EXCEPTION 'idempotency_receipt_partial_roles' USING ERRCODE = '55000'; END IF;
                    {guard}
                    {exact}
                END IF;
            END $bootstrap$;
            GRANT USAGE ON SCHEMA {sql.Quote(sql.Schema)} TO {string.Join(",", Roles)};
            {grants}
            GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {OwnerRole},{DiscoveryOwnerRole};
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
            BEGIN
                {definitions}
            END $install$;
            REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {OwnerRole},{DiscoveryOwnerRole};
            DO $installed$ BEGIN {guard} {exact} END $installed$;
            """;
    }
}
