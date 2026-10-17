using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Security;

/// <summary>Execute-only physical orphan admission and leased custody, with no ordinary storage mutation rights.</summary>
public static class PostgresLocalStorageQuarantineContract
{
    public const string OwnerRole = "event_local_storage_quarantine_owner";
    public const string MigratorRole = "event_local_storage_quarantine_migrator";
    public const string GlobalRole = "event_local_storage_quarantine_global";
    private const string PolicyName = "local_storage_quarantine";
    private const string Fingerprint = "event-local-storage-quarantine:v1:";

    public static string FunctionSql(ExploreDbContext context, string operation) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("local_storage_quarantine_" + operation, Schema(context));

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = BuildSql(context);
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        context.ValidateTenantLease();
        if (context.TenantFilterTenantId is not null)
            throw new InvalidOperationException("local_storage_quarantine_global_lease_required");
        if (context.Database.GetDbConnection().State != ConnectionState.Open)
            await context.Database.OpenConnectionAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = 5;
        var functions = Functions(context);
        var reads = ReadColumns();
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string quarantine = Table(context, typeof(LocalStorageQuarantine));
        var predicates = new List<string>
        {
            $"""
            pg_has_role(current_user, '{GlobalRole}', 'USAGE')
            AND NOT pg_has_role(current_user, '{OwnerRole}', 'MEMBER')
            AND NOT pg_has_role(current_user, '{MigratorRole}', 'MEMBER')
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_has_role(session_user, r.oid, 'USAGE') OR pg_has_role(session_user, r.oid, 'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication))
            AND (SELECT count(*) = 3 FROM pg_catalog.pg_roles
                WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')
                AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
                AND NOT rolcreaterole AND NOT rolcreatedb AND NOT rolreplication AND NOT rolinherit)
            AND NOT has_schema_privilege('{OwnerRole}', {Text(Schema(context))}, 'CREATE')
            AND NOT has_schema_privilege('{GlobalRole}', {Text(Schema(context))}, 'CREATE')
            AND NOT has_schema_privilege(current_user, {Text(Schema(context))}, 'CREATE')
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members
                WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{MigratorRole}')
                    AND roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                    AND NOT inherit_option AND set_option AND NOT admin_option)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members
                WHERE member IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}', '{GlobalRole}'))
                    OR member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{MigratorRole}')
                        AND (roleid <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                            OR inherit_option OR NOT set_option OR admin_option)
                    OR roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                        AND (member <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{MigratorRole}')
                            OR inherit_option OR NOT set_option OR admin_option))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class r
                JOIN pg_catalog.pg_namespace n ON n.oid = r.relnamespace
                WHERE n.nspname = {Text(Schema(context))} AND r.relkind IN ('r','p','v','m','f')
                    AND (has_table_privilege(current_user, r.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR has_any_column_privilege(current_user, r.oid, 'SELECT,INSERT,UPDATE,REFERENCES')
                        OR has_table_privilege('{GlobalRole}', r.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR has_any_column_privilege('{GlobalRole}', r.oid, 'SELECT,INSERT,UPDATE,REFERENCES')
                        OR has_table_privilege('{OwnerRole}', r.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR r.oid NOT IN ({string.Join(", ", reads.Select(read => Text(Table(context, read.Type)) + "::regclass").Append(Text(quarantine) + "::regclass"))})
                            AND has_any_column_privilege('{OwnerRole}', r.oid, 'SELECT,INSERT,UPDATE,REFERENCES')))
            """
        };
        for (int index = 0; index < functions.Count; index++)
        {
            var function = functions[index];
            predicates.Add($"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_proc p
                    JOIN pg_catalog.pg_roles owner ON owner.oid = p.proowner
                    WHERE p.oid = to_regprocedure(@signature{index}) AND owner.rolname = '{OwnerRole}'
                    AND p.prosecdef AND p.provolatile = '{function.Volatility}' AND p.proparallel = 'u'
                    AND p.proconfig IS NOT DISTINCT FROM ARRAY['search_path=pg_catalog']::text[]
                    AND p.prosrc = @body{index}
                    AND has_function_privilege(current_user, p.oid, 'EXECUTE') = {(function.Public ? "true" : "false")}
                    AND has_function_privilege('{GlobalRole}', p.oid, 'EXECUTE') = {(function.Public ? "true" : "false")}
                    AND NOT EXISTS (SELECT 1 FROM aclexplode(coalesce(p.proacl, acldefault('f', p.proowner))) acl
                        WHERE acl.grantee NOT IN (
                            SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}'
                                {(function.Public ? $"OR rolname = '{GlobalRole}'" : "")})
                            OR acl.privilege_type <> 'EXECUTE'
                            OR acl.is_grantable AND acl.grantee <> p.proowner))
                """);
            command.Parameters.Add(new NpgsqlParameter($"signature{index}", FunctionSql(context, function.Name) + "(" + function.Types + ")"));
            command.Parameters.Add(new NpgsqlParameter($"body{index}", function.Body));
        }
        foreach (var read in reads)
        {
            string table = Table(context, read.Type);
            string allowed = string.Join(", ", read.Columns.Select(column => Text(ColumnName(context, read.Type, column))));
            predicates.Add($"""
                NOT has_table_privilege(current_user, {Text(table)}, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES')
                AND NOT has_any_column_privilege(current_user, {Text(table)}, 'SELECT,INSERT,UPDATE,REFERENCES')
                AND NOT has_table_privilege('{OwnerRole}', {Text(table)}, 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES')
                AND NOT has_any_column_privilege('{OwnerRole}', {Text(table)}, 'INSERT,UPDATE,REFERENCES')
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid = to_regclass({Text(table)}) AND a.attnum > 0 AND NOT a.attisdropped
                    AND has_column_privilege('{OwnerRole}', a.attrelid, a.attnum, 'SELECT')
                        IS DISTINCT FROM (a.attname IN ({allowed})))
                """);
            var tenantTable = tenantTables.SingleOrDefault(candidate =>
                candidate.TableName == context.Model.FindEntityType(read.Type)!.GetTableName());
            if (tenantTable is not null)
                predicates.Add($"""
                    EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = to_regclass({Text(table)}) AND p.polname = '{PolicyName}_read'
                        AND p.polcmd = 'r' AND p.polpermissive AND p.polroles = ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')]
                        AND pg_catalog.pg_get_expr(p.polqual, p.polrelid) = 'true' AND p.polwithcheck IS NULL
                        AND pg_catalog.obj_description(p.oid, 'pg_policy') =
                            '{Fingerprint}' || md5(p.polqual::text) || ':')
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid = to_regclass({Text(table)})
                        AND c.relrowsecurity AND c.relforcerowsecurity)
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = to_regclass({Text(table)}) AND p.polname = 'tenant_isolation'
                            AND p.polcmd = '*' AND p.polpermissive AND p.polroles = ARRAY[0::oid]
                            AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                            AND pg_catalog.obj_description(p.oid, 'pg_policy') =
                                {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenantTable, Schema(context)))}
                                || md5(p.polqual::text) || ':' || md5(p.polwithcheck::text))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = to_regclass({Text(table)}) AND p.polpermissive
                            AND p.polname NOT IN ('{PolicyName}_read', 'tenant_isolation')
                            AND EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                                WHERE role_id = 0 OR pg_has_role('{OwnerRole}', role_id, 'USAGE')))
                    """);
        }
        string[] mutable = MutableColumns.Select(column => ColumnName(context, typeof(LocalStorageQuarantine), column)).ToArray();
        predicates.Add($"""
            NOT has_table_privilege(current_user, {Text(quarantine)}, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES')
            AND NOT has_any_column_privilege(current_user, {Text(quarantine)}, 'SELECT,INSERT,UPDATE,REFERENCES')
            AND NOT has_table_privilege('{OwnerRole}', {Text(quarantine)}, 'DELETE,TRUNCATE,REFERENCES')
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid = to_regclass({Text(quarantine)})
                AND c.relrowsecurity AND c.relforcerowsecurity)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                WHERE a.attrelid = to_regclass({Text(quarantine)}) AND a.attnum > 0 AND NOT a.attisdropped
                AND (NOT has_column_privilege('{OwnerRole}', a.attrelid, a.attnum, 'SELECT')
                    OR NOT has_column_privilege('{OwnerRole}', a.attrelid, a.attnum, 'INSERT')
                    OR has_column_privilege('{OwnerRole}', a.attrelid, a.attnum, 'UPDATE')
                        IS DISTINCT FROM (a.attname IN ({string.Join(", ", mutable.Select(Text))}))
                    OR has_column_privilege('{OwnerRole}', a.attrelid, a.attnum, 'REFERENCES')))
            AND NOT EXISTS (SELECT 1 FROM (VALUES ('{PolicyName}_read','r'), ('{PolicyName}_insert','a'),
                    ('{PolicyName}_update','w')) expected(name, command)
                LEFT JOIN pg_catalog.pg_policy p ON p.polrelid = to_regclass({Text(quarantine)}) AND p.polname = expected.name
                WHERE p.oid IS NULL OR p.polcmd::text <> expected.command OR NOT p.polpermissive
                    OR p.polroles <> ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')]
                    OR (expected.command IN ('r','w') AND pg_get_expr(p.polqual,p.polrelid) IS DISTINCT FROM 'true')
                    OR (expected.command = 'a' AND p.polqual IS NOT NULL)
                    OR (expected.command IN ('a','w') AND pg_get_expr(p.polwithcheck,p.polrelid) IS DISTINCT FROM 'true')
                    OR (expected.command = 'r' AND p.polwithcheck IS NOT NULL)
                    OR pg_catalog.obj_description(p.oid, 'pg_policy') IS DISTINCT FROM
                        '{Fingerprint}' || COALESCE(md5(p.polqual::text), '') || ':' || COALESCE(md5(p.polwithcheck::text), ''))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                WHERE p.polrelid = to_regclass({Text(quarantine)}) AND p.polpermissive
                    AND p.polname NOT IN ('{PolicyName}_read','{PolicyName}_insert','{PolicyName}_update')
                    AND EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                        WHERE role_id = 0 OR pg_has_role('{OwnerRole}', role_id, 'USAGE')))
            """);
        command.CommandText = "SELECT " + string.Join("\nAND ", predicates.Select(predicate => "(" + predicate + ")"));
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("local_storage_quarantine_authority_not_ready");
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context));
        string quarantine = Table(context, typeof(LocalStorageQuarantine));
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string[] quarantineColumns = context.Model.FindEntityType(typeof(LocalStorageQuarantine))!
            .GetProperties().Select(property => Column(context, typeof(LocalStorageQuarantine), property.Name)).ToArray();
        string reads = string.Join("\n", ReadColumns().Select(read =>
        {
            string table = Table(context, read.Type);
            string policy = tenantTables.Any(candidate =>
                candidate.TableName == context.Model.FindEntityType(read.Type)!.GetTableName()) ? $"""
                DROP POLICY IF EXISTS "{PolicyName}_read" ON {table};
                CREATE POLICY "{PolicyName}_read" ON {table} FOR SELECT TO {OwnerRole} USING (true);
                {StampPolicySql(table, PolicyName + "_read")}
                """ : "";
            return $"""
                REVOKE ALL ON TABLE {table} FROM {OwnerRole}, {GlobalRole};
                GRANT SELECT ({string.Join(", ", read.Columns.Select(column => Column(context, read.Type, column)))})
                    ON {table} TO {OwnerRole};
                {policy}
                """;
        }));
        var functions = Functions(context);
        string definitions = string.Join("\n", functions.Select(function => $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, function.Name)}({function.Arguments})
            RETURNS {function.Returns} LANGUAGE plpgsql {(function.Volatility == 's' ? "STABLE" : "VOLATILE")}
            SECURITY DEFINER SET search_path = pg_catalog AS $body${function.Body}$body$;
            """));
        string signatures = string.Join(", ", functions.Select(function => FunctionSql(context, function.Name) + "(" + function.Types + ")"));
        string exposed = string.Join(", ", functions.Where(function => function.Public)
            .Select(function => FunctionSql(context, function.Name) + "(" + function.Types + ")"));
        return $"""
            DO $roles$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}','{MigratorRole}','{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS',v_role);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role
                        AND (rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication OR rolinherit)) THEN
                        RAISE EXCEPTION 'local_storage_quarantine_role_invalid';
                    END IF;
                END LOOP;
                IF pg_has_role('{GlobalRole}','{OwnerRole}','MEMBER')
                    OR pg_has_role('{GlobalRole}','{MigratorRole}','MEMBER')
                    OR pg_has_role('{OwnerRole}','{GlobalRole}','MEMBER')
                    OR pg_has_role('{MigratorRole}','{GlobalRole}','MEMBER')
                    OR EXISTS (
                        WITH RECURSIVE memberships(roleid) AS (
                            SELECT roleid FROM pg_catalog.pg_auth_members
                            WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = current_user)
                            UNION SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                            JOIN memberships parent ON edge.member = parent.roleid)
                        SELECT 1 FROM memberships WHERE roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')) THEN
                    RAISE EXCEPTION 'local_storage_quarantine_authorities_must_remain_separate';
                END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members
                    WHERE member IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}', '{GlobalRole}'))
                        OR member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{MigratorRole}')
                            AND (roleid <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                                OR inherit_option OR NOT set_option OR admin_option)
                        OR roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                            AND (member <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{MigratorRole}')
                                OR inherit_option OR NOT set_option OR admin_option)) THEN
                    RAISE EXCEPTION 'local_storage_quarantine_membership_invalid';
                END IF;
            END; $roles$;
            GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE;
            GRANT {MigratorRole} TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;
            GRANT USAGE,CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            {reads}
            REVOKE ALL ON TABLE {quarantine} FROM PUBLIC, {OwnerRole}, {GlobalRole};
            GRANT SELECT ({string.Join(", ", quarantineColumns)}), INSERT ({string.Join(", ", quarantineColumns)}),
                UPDATE ({string.Join(", ", MutableColumns.Select(column => Column(context, typeof(LocalStorageQuarantine), column)))})
                ON {quarantine} TO {OwnerRole};
            ALTER TABLE {quarantine} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {quarantine} FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS "{PolicyName}_read" ON {quarantine};
            DROP POLICY IF EXISTS "{PolicyName}_insert" ON {quarantine};
            DROP POLICY IF EXISTS "{PolicyName}_update" ON {quarantine};
            CREATE POLICY "{PolicyName}_read" ON {quarantine} FOR SELECT TO {OwnerRole} USING (true);
            CREATE POLICY "{PolicyName}_insert" ON {quarantine} FOR INSERT TO {OwnerRole} WITH CHECK (true);
            CREATE POLICY "{PolicyName}_update" ON {quarantine} FOR UPDATE TO {OwnerRole} USING (true) WITH CHECK (true);
            {StampPolicySql(quarantine, PolicyName + "_read")}
            {StampPolicySql(quarantine, PolicyName + "_insert")}
            {StampPolicySql(quarantine, PolicyName + "_update")}
            DO $install$
            DECLARE v_previous text := current_setting('role');
            BEGIN
                PERFORM set_config('role','{OwnerRole}',true);
                EXECUTE $ddl${definitions}$ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {signatures} FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {exposed} TO {GlobalRole};
                $acl$;
                PERFORM set_config('role',v_previous,true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            """;
    }

    private static IReadOnlyList<FunctionDefinition> Functions(ExploreDbContext context)
    {
        string Q(string property) => Column(context, typeof(LocalStorageQuarantine), property);
        string B(string property) => Column(context, typeof(StorageProviderBinding), property);
        string quarantine = Table(context, typeof(LocalStorageQuarantine));
        string bindings = Table(context, typeof(StorageProviderBinding));
        string path = FunctionSql(context, "path"), occupied = FunctionSql(context, "occupied");
        string union = LocalStorageCustodyQuery.BuildUnion(context, "v_binding", "v_key");
        string rootMode = OperatingSystem.IsWindows() ? """
            p_root := replace(p_root,chr(92),'/');
            IF left(lower(p_root COLLATE "C"),8) = '//?/unc/' THEN
                p_root := '//' || substr(p_root,9);
            ELSIF p_root ~ '^//[?]/[A-Za-z]:/' THEN
                p_root := substr(p_root,5);
            ELSIF left(p_root,4) IN ('//./','//?/') THEN
                RETURN NULL;
            END IF;
            IF p_root ~ '^[A-Za-z]:/' THEN
                v_prefix := left(p_root,2) || '/';
                p_root := substr(p_root,4);
            ELSIF p_root ~ '^//[^/]+/[^/]+(/|$)' THEN
                v_prefix := '//' || split_part(p_root,'/',3) || '/' || split_part(p_root,'/',4) || '/';
                p_root := substr(p_root,length(rtrim(v_prefix,'/'))+1);
            ELSE
                RAISE EXCEPTION 'local_quarantine_inventory_identity_invalid' USING ERRCODE = '22023';
            END IF;
            """ : """
            IF left(p_root,1) <> '/' THEN
                RAISE EXCEPTION 'local_quarantine_inventory_identity_invalid' USING ERRCODE = '22023';
            END IF;
            v_prefix := '/';
            """;
        string caseMode = OperatingSystem.IsLinux() ? "" : """
            IF v_segment COLLATE "C" ~ '[^ -~]' THEN RETURN NULL; END IF;
            v_segment := lower(v_segment COLLATE "C");
            """;
        if (OperatingSystem.IsWindows())
            caseMode += """

                IF EXISTS (SELECT 1 FROM unnest(string_to_array(v_segment,'/')) segment
                    WHERE right(segment,1) IN ('.',' ') OR strpos(segment,'~') > 0
                        OR split_part(segment,'.',1) ~ '^(con|prn|aux|nul|com[1-9]|lpt[1-9])$') THEN RETURN NULL; END IF;
                """;
        string reservedSegment = OperatingSystem.IsLinux() ? "segment = '.quarantine'"
            : "lower(segment COLLATE \"C\") = '.quarantine'";
        string transactionGuard = """
            IF current_setting('transaction_isolation') <> 'serializable' THEN
                RAISE EXCEPTION 'local_quarantine_serializable_transaction_required' USING ERRCODE = '25000';
            END IF;
            """;
        return
        [
            new("path", "p_root text,p_key text", "text,text", "text", 's', false, $$"""
                DECLARE v_segments text[] := ARRAY[]::text[]; v_segment text; v_prefix text;
                BEGIN
                    p_key := replace(p_key,chr(92),'/');
                    IF p_root IS NULL OR p_root ~ '[[:cntrl:]]'
                        OR p_key IS NULL OR length(p_key) NOT BETWEEN 1 AND 1024 OR btrim(p_key) = ''
                        OR p_key ~ '[[:cntrl:]]' OR strpos(p_key,':') > 0
                        OR EXISTS (SELECT 1 FROM unnest(string_to_array(p_key,'/')) segment WHERE segment IN ('','.','..'))
                        OR EXISTS (SELECT 1 FROM unnest(string_to_array(p_key,'/')) segment WHERE {{reservedSegment}}) THEN
                        RAISE EXCEPTION 'local_quarantine_inventory_identity_invalid' USING ERRCODE = '22023';
                    END IF;
                    {{rootMode}}
                    FOREACH v_segment IN ARRAY string_to_array(p_root,'/') LOOP
                        IF v_segment IN ('','.') THEN CONTINUE; END IF;
                        IF v_segment = '..' THEN
                            v_segments := v_segments[1:greatest(cardinality(v_segments)-1,0)];
                        ELSE
                            v_segments := array_append(v_segments,v_segment);
                        END IF;
                    END LOOP;
                    v_segment := v_prefix || CASE WHEN cardinality(v_segments) > 0
                        THEN array_to_string(v_segments,'/') || '/' ELSE '' END || p_key;
                    IF length(v_segment) > 4096 THEN
                        RAISE EXCEPTION 'local_quarantine_inventory_identity_invalid' USING ERRCODE = '22023';
                    END IF;
                    {{caseMode}}
                    RETURN v_segment;
                END;
                """),
            new("occupied", "p_path text,p_excluding uuid", "text,uuid", "boolean", 's', false, $$"""
                DECLARE v_ref record; v_root text; v_existing record; v_hash text; v_binding uuid; v_key text;
                BEGIN
                    v_hash := encode(sha256(convert_to(p_path,'UTF8')),'hex');
                    FOR v_existing IN SELECT q.* FROM {{quarantine}} q
                        WHERE q.{{Q(nameof(LocalStorageQuarantine.PhysicalPathHash))}} = v_hash
                            AND (p_excluding IS NULL OR q.{{Q(nameof(LocalStorageQuarantine.Id))}} <> p_excluding) LOOP
                        IF v_existing.{{Q(nameof(LocalStorageQuarantine.CanonicalPhysicalPath))}} IS DISTINCT FROM p_path THEN
                            RAISE EXCEPTION 'local_quarantine_physical_identity_conflict' USING ERRCODE = '23505';
                        END IF;
                        RETURN true;
                    END LOOP;
                    FOR v_ref IN SELECT b.{{B(nameof(StorageProviderBinding.Id))}} AS binding_id,
                        b.{{B(nameof(StorageProviderBinding.LocalRootPath))}} AS root_path FROM {{bindings}} b
                        WHERE b.{{B(nameof(StorageProviderBinding.Provider))}} = '{{StorageProviders.Local}}'
                        ORDER BY b.{{B(nameof(StorageProviderBinding.Id))}} LOOP
                        BEGIN
                            v_root := {{path}}(v_ref.root_path,'__custody_probe__');
                            IF v_root IS NULL THEN RETURN true; END IF;
                            v_root := left(v_root,length(v_root)-length('__custody_probe__'));
                        EXCEPTION WHEN invalid_parameter_value THEN RETURN true;
                        END;
                        IF left(p_path,length(v_root)) IS DISTINCT FROM v_root THEN CONTINUE; END IF;
                        v_binding := v_ref.binding_id;
                        v_key := substr(p_path,length(v_root)+1);
                        IF EXISTS ({{union}}) THEN RETURN true; END IF;
                    END LOOP;
                    RETURN false;
                END;
                """),
            new("inspect", "p_binding uuid,p_key text", "uuid,text", "boolean", 's', true, $$"""
                DECLARE v_root text; v_path text;
                BEGIN
                    SELECT b.{{B(nameof(StorageProviderBinding.LocalRootPath))}} INTO v_root FROM {{bindings}} b
                        WHERE b.{{B(nameof(StorageProviderBinding.Id))}} = p_binding
                            AND b.{{B(nameof(StorageProviderBinding.Provider))}} = '{{StorageProviders.Local}}';
                    v_path := {{path}}(v_root,p_key);
                    IF v_path IS NULL THEN RETURN false; END IF;
                    RETURN NOT {{occupied}}(v_path,NULL);
                END;
                """),
            new("admit", "p_binding uuid,p_key text,p_size bigint,p_modified bigint,p_cutoff bigint,p_reason text,p_id uuid,p_stamp uuid",
                "uuid,text,bigint,bigint,bigint,text,uuid,uuid", "SETOF " + quarantine, 'v', true, $$"""
                DECLARE v_root text; v_path text; v_hash text; v_existing {{quarantine}}%ROWTYPE;
                    v_now bigint := (extract(epoch FROM clock_timestamp()) * 10000000)::bigint + 621355968000000000;
                BEGIN
                    {{transactionGuard}}
                    IF p_size IS NULL OR p_size < 0 OR p_modified IS NULL OR p_modified NOT BETWEEN 1 AND 3155378975999999999
                        OR p_cutoff IS NULL OR p_cutoff NOT BETWEEN 1 AND 3155378975999999999 OR p_modified > p_cutoff OR p_cutoff > v_now
                        OR p_reason IS NULL OR length(p_reason) NOT BETWEEN 1 AND 128 OR btrim(p_reason) = '' OR p_reason ~ '[[:cntrl:]]'
                        OR p_id IS NULL OR p_id = '00000000-0000-0000-0000-000000000000'::uuid OR substr(p_id::text,15,1) <> '7'
                        OR p_stamp IS NULL OR p_stamp = '00000000-0000-0000-0000-000000000000'::uuid THEN
                        RAISE EXCEPTION 'local_quarantine_admission_invalid' USING ERRCODE = '22023';
                    END IF;
                    SELECT b.{{B(nameof(StorageProviderBinding.LocalRootPath))}} INTO v_root FROM {{bindings}} b
                        WHERE b.{{B(nameof(StorageProviderBinding.Id))}} = p_binding
                            AND b.{{B(nameof(StorageProviderBinding.Provider))}} = '{{StorageProviders.Local}}';
                    v_path := {{path}}(v_root,p_key);
                    IF v_path IS NULL THEN RETURN; END IF;
                    v_hash := encode(sha256(convert_to(v_path,'UTF8')),'hex');
                    PERFORM pg_advisory_xact_lock(hashtextextended('local-storage-quarantine:' || v_hash,0));
                    SELECT q.* INTO v_existing FROM {{quarantine}} q
                        WHERE q.{{Q(nameof(LocalStorageQuarantine.PhysicalPathHash))}} = v_hash;
                    IF FOUND THEN
                        IF v_existing.{{Q(nameof(LocalStorageQuarantine.CanonicalPhysicalPath))}} IS DISTINCT FROM v_path THEN
                            RAISE EXCEPTION 'local_quarantine_physical_identity_conflict' USING ERRCODE = '23505';
                        END IF;
                        RETURN NEXT v_existing; RETURN;
                    END IF;
                    IF {{occupied}}(v_path,NULL) THEN RETURN; END IF;
                    RETURN QUERY INSERT INTO {{quarantine}} (
                        {{Q(nameof(LocalStorageQuarantine.Id))}},{{Q(nameof(LocalStorageQuarantine.ProviderBindingId))}},
                        {{Q(nameof(LocalStorageQuarantine.ObjectKey))}},{{Q(nameof(LocalStorageQuarantine.CanonicalPhysicalPath))}},
                        {{Q(nameof(LocalStorageQuarantine.PhysicalPathHash))}},{{Q(nameof(LocalStorageQuarantine.ObservedSize))}},
                        {{Q(nameof(LocalStorageQuarantine.ObservedLastModifiedUtc))}},{{Q(nameof(LocalStorageQuarantine.ModifiedBeforeUtc))}},
                        {{Q(nameof(LocalStorageQuarantine.AdmissionReason))}},{{Q(nameof(LocalStorageQuarantine.AdmittedAtUtc))}},
                        {{Q(nameof(LocalStorageQuarantine.State))}},{{Q(nameof(LocalStorageQuarantine.ConcurrencyStamp))}},
                        {{Q(nameof(LocalStorageQuarantine.NextAttemptAtUtc))}},{{Q(nameof(LocalStorageQuarantine.LeaseExpiresAtUtc))}},
                        {{Q(nameof(LocalStorageQuarantine.Outcome))}},{{Q(nameof(LocalStorageQuarantine.SettledAtUtc))}})
                        VALUES (p_id,p_binding,p_key,v_path,v_hash,p_size,p_modified,p_cutoff,p_reason,v_now,1,p_stamp,v_now,NULL,NULL,NULL)
                        ON CONFLICT ({{Q(nameof(LocalStorageQuarantine.PhysicalPathHash))}}) DO NOTHING
                        RETURNING *;
                END;
                """),
            new("claim", "p_limit integer,p_lease_seconds double precision,p_stamp uuid", "integer,double precision,uuid",
                "SETOF " + quarantine, 'v', true, $$"""
                DECLARE v_work {{quarantine}}%ROWTYPE; v_root text; v_path text;
                    v_now bigint := (extract(epoch FROM clock_timestamp()) * 10000000)::bigint + 621355968000000000;
                BEGIN
                    {{transactionGuard}}
                    IF p_limit IS NULL OR p_limit NOT BETWEEN 1 AND {{ILocalStorageQuarantineOwner.MaximumClaims}}
                        OR p_lease_seconds IS NULL OR NOT (p_lease_seconds > 0 AND p_lease_seconds <= 3600)
                        OR p_stamp IS NULL OR p_stamp = '00000000-0000-0000-0000-000000000000'::uuid THEN
                        RAISE EXCEPTION 'local_quarantine_claim_invalid' USING ERRCODE = '22023';
                    END IF;
                    FOR v_work IN SELECT q.* FROM {{quarantine}} q
                        WHERE q.{{Q(nameof(LocalStorageQuarantine.State))}} = 1 AND q.{{Q(nameof(LocalStorageQuarantine.NextAttemptAtUtc))}} <= v_now
                            OR q.{{Q(nameof(LocalStorageQuarantine.State))}} = 2 AND q.{{Q(nameof(LocalStorageQuarantine.LeaseExpiresAtUtc))}} <= v_now
                        ORDER BY q.{{Q(nameof(LocalStorageQuarantine.PhysicalPathHash))}},q.{{Q(nameof(LocalStorageQuarantine.Id))}}
                        LIMIT p_limit FOR UPDATE SKIP LOCKED LOOP
                        PERFORM pg_advisory_xact_lock(hashtextextended('local-storage-quarantine:' ||
                            v_work.{{Q(nameof(LocalStorageQuarantine.PhysicalPathHash))}},0));
                        SELECT b.{{B(nameof(StorageProviderBinding.LocalRootPath))}} INTO v_root FROM {{bindings}} b
                            WHERE b.{{B(nameof(StorageProviderBinding.Id))}} = v_work.{{Q(nameof(LocalStorageQuarantine.ProviderBindingId))}}
                                AND b.{{B(nameof(StorageProviderBinding.Provider))}} = '{{StorageProviders.Local}}';
                        v_path := {{path}}(v_root,v_work.{{Q(nameof(LocalStorageQuarantine.ObjectKey))}});
                        IF v_path IS NULL THEN
                            UPDATE {{quarantine}} SET {{Q(nameof(LocalStorageQuarantine.State))}} = 4,
                                {{Q(nameof(LocalStorageQuarantine.Outcome))}} = 4,{{Q(nameof(LocalStorageQuarantine.ConcurrencyStamp))}} = p_stamp,
                                {{Q(nameof(LocalStorageQuarantine.SettledAtUtc))}} = v_now,
                                {{Q(nameof(LocalStorageQuarantine.NextAttemptAtUtc))}} = NULL,{{Q(nameof(LocalStorageQuarantine.LeaseExpiresAtUtc))}} = NULL
                                WHERE {{Q(nameof(LocalStorageQuarantine.Id))}} = v_work.{{Q(nameof(LocalStorageQuarantine.Id))}};
                            CONTINUE;
                        END IF;
                        IF v_path IS DISTINCT FROM v_work.{{Q(nameof(LocalStorageQuarantine.CanonicalPhysicalPath))}}
                            OR encode(sha256(convert_to(v_path,'UTF8')),'hex') IS DISTINCT FROM v_work.{{Q(nameof(LocalStorageQuarantine.PhysicalPathHash))}} THEN
                            RAISE EXCEPTION 'local_quarantine_physical_identity_conflict' USING ERRCODE = '23505';
                        END IF;
                        IF p_stamp = v_work.{{Q(nameof(LocalStorageQuarantine.ConcurrencyStamp))}} THEN
                            RAISE EXCEPTION 'local_quarantine_claim_stamp_reused' USING ERRCODE = '22023';
                        END IF;
                        IF {{occupied}}(v_path,v_work.{{Q(nameof(LocalStorageQuarantine.Id))}}) THEN
                            UPDATE {{quarantine}} SET {{Q(nameof(LocalStorageQuarantine.State))}} = 4,
                                {{Q(nameof(LocalStorageQuarantine.Outcome))}} = 4,{{Q(nameof(LocalStorageQuarantine.ConcurrencyStamp))}} = p_stamp,
                                {{Q(nameof(LocalStorageQuarantine.SettledAtUtc))}} = v_now,
                                {{Q(nameof(LocalStorageQuarantine.NextAttemptAtUtc))}} = NULL,{{Q(nameof(LocalStorageQuarantine.LeaseExpiresAtUtc))}} = NULL
                                WHERE {{Q(nameof(LocalStorageQuarantine.Id))}} = v_work.{{Q(nameof(LocalStorageQuarantine.Id))}};
                        ELSE
                            RETURN QUERY UPDATE {{quarantine}} SET {{Q(nameof(LocalStorageQuarantine.State))}} = 2,
                                {{Q(nameof(LocalStorageQuarantine.ConcurrencyStamp))}} = p_stamp,{{Q(nameof(LocalStorageQuarantine.NextAttemptAtUtc))}} = NULL,
                                {{Q(nameof(LocalStorageQuarantine.SettledAtUtc))}} = NULL,
                                {{Q(nameof(LocalStorageQuarantine.LeaseExpiresAtUtc))}} = v_now + (p_lease_seconds * 10000000)::bigint
                                WHERE {{Q(nameof(LocalStorageQuarantine.Id))}} = v_work.{{Q(nameof(LocalStorageQuarantine.Id))}}
                                RETURNING *;
                        END IF;
                    END LOOP;
                END;
                """),
            new("validate_claim", "p_id uuid,p_claim uuid,p_binding uuid,p_key text,p_size bigint,p_modified_ticks bigint,p_expiry_ticks bigint,p_path text,p_hash text",
                "uuid,uuid,uuid,text,bigint,bigint,bigint,text,text", "boolean", 'v', true, $$"""
                DECLARE v_work {{quarantine}}%ROWTYPE; v_root text; v_path text; v_now bigint;
                BEGIN
                    {{transactionGuard}}
                    IF p_id IS NULL OR p_id = '00000000-0000-0000-0000-000000000000'::uuid
                        OR p_claim IS NULL OR p_claim = '00000000-0000-0000-0000-000000000000'::uuid
                        OR p_binding IS NULL OR p_binding = '00000000-0000-0000-0000-000000000000'::uuid
                        OR p_key IS NULL OR length(p_key) = 0
                        OR p_size IS NULL OR p_size < 0
                        OR p_modified_ticks IS NULL OR p_modified_ticks NOT BETWEEN 1 AND 3155378975999999999
                        OR p_expiry_ticks IS NULL OR p_expiry_ticks NOT BETWEEN 1 AND 3155378975999999999
                        OR p_path IS NULL OR length(p_path) = 0
                        OR p_hash IS NULL OR p_hash COLLATE "C" !~ '^[0-9a-f]{64}$' THEN
                        RETURN false;
                    END IF;
                    SELECT q.* INTO v_work FROM {{quarantine}} q
                        WHERE q.{{Q(nameof(LocalStorageQuarantine.Id))}} = p_id FOR SHARE;
                    IF NOT FOUND THEN RETURN false; END IF;
                    v_now := (extract(epoch FROM clock_timestamp()) * 10000000)::bigint + 621355968000000000;
                    IF v_work.{{Q(nameof(LocalStorageQuarantine.State))}} <> 2
                        OR v_work.{{Q(nameof(LocalStorageQuarantine.ConcurrencyStamp))}} IS DISTINCT FROM p_claim
                        OR v_work.{{Q(nameof(LocalStorageQuarantine.ProviderBindingId))}} IS DISTINCT FROM p_binding
                        OR v_work.{{Q(nameof(LocalStorageQuarantine.ObjectKey))}} COLLATE "C" IS DISTINCT FROM p_key COLLATE "C"
                        OR v_work.{{Q(nameof(LocalStorageQuarantine.ObservedSize))}} IS DISTINCT FROM p_size
                        OR v_work.{{Q(nameof(LocalStorageQuarantine.ObservedLastModifiedUtc))}} IS DISTINCT FROM p_modified_ticks
                        OR v_work.{{Q(nameof(LocalStorageQuarantine.LeaseExpiresAtUtc))}} IS DISTINCT FROM p_expiry_ticks
                        OR p_expiry_ticks <= v_now
                        OR v_work.{{Q(nameof(LocalStorageQuarantine.CanonicalPhysicalPath))}} COLLATE "C" IS DISTINCT FROM p_path COLLATE "C"
                        OR v_work.{{Q(nameof(LocalStorageQuarantine.PhysicalPathHash))}} COLLATE "C" IS DISTINCT FROM p_hash COLLATE "C" THEN
                        RETURN false;
                    END IF;
                    PERFORM pg_advisory_xact_lock(hashtextextended('local-storage-quarantine:' || p_hash,0));
                    SELECT b.{{B(nameof(StorageProviderBinding.LocalRootPath))}} INTO v_root FROM {{bindings}} b
                        WHERE b.{{B(nameof(StorageProviderBinding.Id))}} = p_binding
                            AND b.{{B(nameof(StorageProviderBinding.Provider))}} = '{{StorageProviders.Local}}';
                    IF NOT FOUND THEN RETURN false; END IF;
                    v_path := {{path}}(v_root,p_key);
                    IF v_path IS NULL THEN RETURN false; END IF;
                    IF v_path COLLATE "C" IS DISTINCT FROM p_path COLLATE "C"
                        OR encode(sha256(convert_to(v_path,'UTF8')),'hex') COLLATE "C" IS DISTINCT FROM p_hash COLLATE "C" THEN
                        RAISE EXCEPTION 'local_quarantine_physical_identity_conflict' USING ERRCODE = '23505';
                    END IF;
                    IF {{occupied}}(v_path,p_id) THEN RETURN false; END IF;
                    v_now := (extract(epoch FROM clock_timestamp()) * 10000000)::bigint + 621355968000000000;
                    RETURN p_expiry_ticks > v_now AND EXISTS (SELECT 1 FROM {{quarantine}} q
                        WHERE q.{{Q(nameof(LocalStorageQuarantine.Id))}} = p_id
                            AND q.{{Q(nameof(LocalStorageQuarantine.State))}} = 2
                            AND q.{{Q(nameof(LocalStorageQuarantine.ConcurrencyStamp))}} = p_claim
                            AND q.{{Q(nameof(LocalStorageQuarantine.ProviderBindingId))}} = p_binding
                            AND q.{{Q(nameof(LocalStorageQuarantine.ObjectKey))}} COLLATE "C" = p_key COLLATE "C"
                            AND q.{{Q(nameof(LocalStorageQuarantine.ObservedSize))}} = p_size
                            AND q.{{Q(nameof(LocalStorageQuarantine.ObservedLastModifiedUtc))}} = p_modified_ticks
                            AND q.{{Q(nameof(LocalStorageQuarantine.LeaseExpiresAtUtc))}} = p_expiry_ticks
                            AND q.{{Q(nameof(LocalStorageQuarantine.CanonicalPhysicalPath))}} COLLATE "C" = p_path COLLATE "C"
                            AND q.{{Q(nameof(LocalStorageQuarantine.PhysicalPathHash))}} COLLATE "C" = p_hash COLLATE "C");
                END;
                """),
            new("settle", "p_id uuid,p_claim uuid,p_outcome smallint,p_retry bigint,p_successor uuid",
                "uuid,uuid,smallint,bigint,uuid", "boolean", 'v', true, $$"""
                DECLARE v_now bigint := (extract(epoch FROM clock_timestamp()) * 10000000)::bigint + 621355968000000000;
                    v_changed integer;
                BEGIN
                    {{transactionGuard}}
                    IF p_id IS NULL OR p_id = '00000000-0000-0000-0000-000000000000'::uuid
                        OR p_claim IS NULL OR p_claim = '00000000-0000-0000-0000-000000000000'::uuid
                        OR p_successor IS NULL OR p_successor = '00000000-0000-0000-0000-000000000000'::uuid OR p_successor = p_claim
                        OR (p_outcome IS NULL) = (p_retry IS NULL)
                        OR p_outcome IS NOT NULL AND p_outcome NOT IN (1,2,3,4)
                        OR p_retry IS NOT NULL AND (p_retry NOT BETWEEN 1 AND 3155378975999999999 OR p_retry <= v_now) THEN
                        RAISE EXCEPTION 'local_quarantine_settlement_invalid' USING ERRCODE = '22023';
                    END IF;
                    UPDATE {{quarantine}} SET {{Q(nameof(LocalStorageQuarantine.State))}} =
                            CASE WHEN p_retry IS NOT NULL THEN 1 WHEN p_outcome = 4 THEN 4 ELSE 3 END,
                        {{Q(nameof(LocalStorageQuarantine.Outcome))}} = p_outcome,{{Q(nameof(LocalStorageQuarantine.ConcurrencyStamp))}} = p_successor,
                        {{Q(nameof(LocalStorageQuarantine.SettledAtUtc))}} = CASE WHEN p_retry IS NULL THEN v_now ELSE NULL END,
                        {{Q(nameof(LocalStorageQuarantine.NextAttemptAtUtc))}} = p_retry,{{Q(nameof(LocalStorageQuarantine.LeaseExpiresAtUtc))}} = NULL
                        WHERE {{Q(nameof(LocalStorageQuarantine.Id))}} = p_id AND {{Q(nameof(LocalStorageQuarantine.ConcurrencyStamp))}} = p_claim
                            AND {{Q(nameof(LocalStorageQuarantine.State))}} = 2 AND {{Q(nameof(LocalStorageQuarantine.LeaseExpiresAtUtc))}} > v_now;
                    GET DIAGNOSTICS v_changed = ROW_COUNT;
                    RETURN v_changed = 1;
                END;
                """)
        ];
    }

    private static readonly string[] MutableColumns =
    [
        nameof(LocalStorageQuarantine.State), nameof(LocalStorageQuarantine.ConcurrencyStamp),
        nameof(LocalStorageQuarantine.NextAttemptAtUtc), nameof(LocalStorageQuarantine.LeaseExpiresAtUtc),
        nameof(LocalStorageQuarantine.Outcome), nameof(LocalStorageQuarantine.SettledAtUtc)
    ];

    private static string StampPolicySql(string table, string name) => $"""
        DO $stamp$
        BEGIN
            EXECUTE (SELECT format('COMMENT ON POLICY %I ON %s IS %L', p.polname, {Text(table)},
                '{Fingerprint}' || COALESCE(md5(p.polqual::text), '') || ':' || COALESCE(md5(p.polwithcheck::text), ''))
                FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(table)}::regclass AND p.polname = {Text(name)});
        END $stamp$;
        """;

    private static (Type Type, string[] Columns)[] ReadColumns() =>
    [
        (typeof(StorageProviderBinding), [nameof(StorageProviderBinding.Id), nameof(StorageProviderBinding.Provider), nameof(StorageProviderBinding.LocalRootPath)]),
        (typeof(StorageObject), [nameof(StorageObject.Provider), nameof(StorageObject.StorageProviderBindingId), nameof(StorageObject.ObjectKey)]),
        (typeof(StorageUploadSession), [nameof(StorageUploadSession.Provider), nameof(StorageUploadSession.StorageProviderBindingId), nameof(StorageUploadSession.ObjectKey)]),
        (typeof(StorageProducerOperation), [nameof(StorageProducerOperation.Provider), nameof(StorageProducerOperation.ProviderBindingId), nameof(StorageProducerOperation.ObjectKey)]),
        (typeof(StorageObjectDeletionTombstone), [nameof(StorageObjectDeletionTombstone.Provider), nameof(StorageObjectDeletionTombstone.ProviderBindingId), nameof(StorageObjectDeletionTombstone.ObjectKey)])
    ];

    private static string Table(ExploreDbContext context, Type type) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Model.FindEntityType(type)!.GetTableName()!, Schema(context));

    private static string Column(ExploreDbContext context, Type type, string property) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(ColumnName(context, type, property));

    private static string ColumnName(ExploreDbContext context, Type type, string property)
    {
        var entity = context.Model.FindEntityType(type)!;
        return entity.FindProperty(property)!.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(LocalStorageQuarantine))!.GetSchema() ?? "public";

    private static string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private sealed record FunctionDefinition(string Name, string Arguments, string Types,
        string Returns, char Volatility, bool Public, string Body);
}
