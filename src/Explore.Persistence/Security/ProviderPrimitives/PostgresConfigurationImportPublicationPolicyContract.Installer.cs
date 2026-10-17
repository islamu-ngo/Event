using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportPublicationPolicyContract
{
    private const string PolicyName = "configuration_import_publication_owner";
    private const string RestrictivePolicyName = "configuration_import_publication_owner_boundary";
    private const string FingerprintPrefix = "event-configuration-import-publication:v1:";

    /// <summary>
    /// Migration-owned installation on the migration caller's existing transaction.
    /// Global login enrollment and parent-owned ordinary table policies are separate.
    /// No runtime role is enrolled or given private-table privileges.
    /// </summary>
    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql())
            return;
        context.ValidateTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("Publication installation requires migration authority, not an import owner.");
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(ct) : null;
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = (context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Publication installation requires the migration transaction."))
            .GetDbTransaction();
        command.CommandText = BuildSql(context);
        await command.ExecuteNonQueryAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
    }

    private static string Schema(ExploreDbContext context, Type type) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(type)!.GetSchema() ?? "public";

    private static string FunctionSql(ExploreDbContext context, string operation) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            "configuration_import_publication_" + operation, Schema(context, typeof(SystemSetting)));

    private static string Table(ExploreDbContext context, Type type) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(type)!.GetTableName()!, Schema(context, type));

    private static string ColumnName(ExploreDbContext context, Type type, string property)
    {
        IEntityType entity = context.Model.FindEntityType(type)!;
        return entity.FindProperty(property)!.GetColumnName(
            StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }

    private static string Column(ExploreDbContext context, Type type, string property) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(ColumnName(context, type, property));

    private static string Text(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Hash(string source) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

    private static string BuildSql(ExploreDbContext context) => BuildSql(context, out _);

    private static string BuildSql(ExploreDbContext context,
        out (string Name, string Signature, string Body)[] originalFunctions)
    {
        string roles = $"'{OwnerRole}', '{MigratorRole}', '{GlobalRole}'";
        string elevated = "rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication OR rolinherit";
        string schema = context.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(Schema(context, typeof(SystemSetting)));
        string schemas = string.Join(", ", new[]
        {
            Schema(context, typeof(SystemSetting)), Schema(context, typeof(TenantSetting))
        }.Distinct(StringComparer.Ordinal).Select(name =>
            context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name)));
        (Type Type, string[] Select, string[] Insert, string[] Update)[] relations =
        [
            (typeof(SystemSetting),
                ["Id", "SettingKey", "Value", "SettingValueTypeId", "IsLocked", "AllowedValues",
                    "Description", "Category", "DisplayOrder", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"],
                ["Id", "SettingKey", "Value", "SettingValueTypeId", "IsLocked", "AllowedValues",
                    "Description", "Category", "DisplayOrder", "CreatedAt", "CreatedBy"],
                ["Value", "SettingValueTypeId", "IsLocked", "AllowedValues",
                    "Description", "Category", "DisplayOrder", "UpdatedAt", "UpdatedBy"]),
            (typeof(TenantSetting),
                ["Id", "TenantId", "SettingKey", "Value", "IsLocked", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"],
                ["Id", "TenantId", "SettingKey", "Value", "IsLocked", "CreatedAt", "CreatedBy"],
                ["Value", "UpdatedAt", "UpdatedBy"])
        ];
        string expectedColumns = string.Join(",\n", relations.SelectMany(relation =>
            new[] { ("SELECT", relation.Select), ("INSERT", relation.Insert), ("UPDATE", relation.Update) }
                .SelectMany(access => access.Item2.Select(property =>
                    $"({Text(Table(context, relation.Type))}::regclass, " +
                    $"{Text(ColumnName(context, relation.Type, property))}, {Text(access.Item1)})"))));
        string relationIds = string.Join(", ", relations.Select(relation => Text(Table(context, relation.Type)) + "::regclass"));
        string requiredDeletes = string.Join(" OR ", relations.Select(relation =>
            $"NOT pg_catalog.has_table_privilege('{OwnerRole}', {Text(Table(context, relation.Type))}::regclass, 'DELETE')"));
        string requiredSchemaUsage = string.Join(" OR ", new[]
        {
            Schema(context, typeof(SystemSetting)), Schema(context, typeof(TenantSetting))
        }.Distinct(StringComparer.Ordinal).Select(name =>
            $"NOT pg_catalog.has_schema_privilege('{OwnerRole}', {Text(name)}, 'USAGE')"))
            + $" OR NOT pg_catalog.has_schema_privilege('{GlobalRole}', {Text(Schema(context, typeof(SystemSetting)))}, 'USAGE')";
        string membershipInvalid = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                WHERE member.rolname IN ('{OwnerRole}', '{GlobalRole}')
                    OR member.rolname = '{MigratorRole}' AND parent.rolname <> '{OwnerRole}'
                    OR parent.rolname = '{OwnerRole}' AND member.rolname <> '{MigratorRole}'
                    OR parent.rolname = '{MigratorRole}' AND member.rolname <> __STATE_MIGRATOR_LOGIN__
                    OR parent.rolname IN ({roles}) AND edge.admin_option
                    OR parent.rolname IN ('{OwnerRole}', '{MigratorRole}')
                        AND (edge.inherit_option OR NOT edge.set_option))
            """;
        string membershipMissing = $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                WHERE member.rolname = '{MigratorRole}' AND parent.rolname = '{OwnerRole}'
                    AND edge.set_option AND NOT edge.inherit_option AND NOT edge.admin_option)
            OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                WHERE member.rolname = __STATE_MIGRATOR_LOGIN__ AND parent.rolname = '{MigratorRole}'
                    AND edge.set_option AND NOT edge.inherit_option AND NOT edge.admin_option)
            """;
        // Follow actual edges. A superuser's implicit membership is not an enrollment.
        string migrationInGlobal = $"""
            EXISTS (WITH RECURSIVE memberships(roleid) AS (
                SELECT roleid FROM pg_catalog.pg_auth_members
                    WHERE member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                UNION SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                    JOIN memberships parent ON parent.roleid = edge.member)
                SELECT 1 FROM memberships JOIN pg_catalog.pg_roles role ON role.oid = memberships.roleid
                    WHERE role.rolname = '{GlobalRole}')
            """;
        string ownershipInvalid = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend dependency
                WHERE dependency.refclassid = 'pg_catalog.pg_authid'::regclass
                    AND dependency.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                    AND dependency.deptype = 'o' AND (dependency.classid <> 'pg_catalog.pg_proc'::regclass
                        OR dependency.dbid <> (SELECT oid FROM pg_catalog.pg_database
                            WHERE datname = pg_catalog.current_database())))
            """;
        string privilegeInvalid = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                WHERE namespace.nspname NOT IN ('pg_catalog','information_schema')
                    AND namespace.nspname NOT LIKE 'pg_toast%'
                    AND relation.relkind IN ('r','p','v','m','f')
                    AND (pg_catalog.has_table_privilege('{OwnerRole}', relation.oid,
                            'SELECT,INSERT,UPDATE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_catalog.has_table_privilege('{OwnerRole}', relation.oid, 'DELETE')
                            AND relation.oid NOT IN ({relationIds})
                        OR pg_catalog.has_table_privilege('{GlobalRole}', relation.oid,
                            'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_catalog.has_any_column_privilege('{GlobalRole}', relation.oid,
                            'SELECT,INSERT,UPDATE,REFERENCES')
                        OR pg_catalog.has_table_privilege('{MigratorRole}', relation.oid,
                            'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_catalog.has_any_column_privilege('{MigratorRole}', relation.oid,
                            'SELECT,INSERT,UPDATE,REFERENCES')))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute attribute
                JOIN pg_catalog.pg_class relation ON relation.oid = attribute.attrelid
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) access(privilege)
                WHERE attribute.attnum > 0 AND NOT attribute.attisdropped
                    AND namespace.nspname NOT IN ('pg_catalog','information_schema')
                    AND namespace.nspname NOT LIKE 'pg_toast%'
                    AND pg_catalog.has_column_privilege('{OwnerRole}', relation.oid, attribute.attnum, access.privilege)
                    AND NOT EXISTS (SELECT 1 FROM (VALUES {expectedColumns}) expected(relation_id, column_name, privilege)
                        WHERE expected.relation_id = relation.oid AND expected.column_name = attribute.attname
                            AND expected.privilege = access.privilege))
            OR EXISTS (SELECT 1 FROM (VALUES {expectedColumns}) expected(relation_id, column_name, privilege)
                WHERE NOT pg_catalog.has_column_privilege('{OwnerRole}', expected.relation_id,
                    expected.column_name, expected.privilege))
            OR {requiredDeletes} OR {requiredSchemaUsage}
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace
                WHERE pg_catalog.has_schema_privilege('{OwnerRole}', oid, 'CREATE')
                    OR pg_catalog.has_schema_privilege('{GlobalRole}', oid, 'CREATE')
                    OR pg_catalog.has_schema_privilege('{MigratorRole}', oid, 'CREATE'))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_class sequence
                CROSS JOIN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) role
                WHERE sequence.relkind = 'S'
                    AND pg_catalog.has_sequence_privilege(role.oid, sequence.oid, 'USAGE,SELECT,UPDATE'))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_database database
                CROSS JOIN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) role
                WHERE pg_catalog.has_database_privilege(role.oid, database.oid, 'CREATE'))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                CROSS JOIN LATERAL pg_catalog.aclexplode(relation.relacl) permission
                WHERE permission.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                    AND permission.is_grantable)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute attribute
                CROSS JOIN LATERAL pg_catalog.aclexplode(attribute.attacl) permission
                WHERE permission.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                    AND permission.is_grantable)
            """;
        string read = ReadBody(context);
        string write = WriteBody(context);
        (string Name, string Signature, string Arguments, string Body)[] functions =
        [
            ("read", "uuid,uuid,integer", "p_tenant uuid, p_after uuid, p_limit integer", read),
            ("write", "uuid,jsonb,uuid,timestamp with time zone",
                "p_tenant uuid, p_mutations jsonb, p_actor uuid, p_at timestamp with time zone", write),
            ("source", "uuid", "p_tenant uuid", OriginalSourceBody(context)),
            ("write_original", "uuid,uuid,jsonb,uuid,timestamp with time zone,jsonb,jsonb",
                "p_source_tenant uuid, p_tenant uuid, p_mutations jsonb, p_actor uuid, p_at timestamp with time zone, p_before jsonb, p_after jsonb",
                OriginalWriteBody(context))
        ];
        string functionIds = string.Join(", ", functions.Select(function =>
            Text(FunctionSql(context, function.Name) + "(" + function.Signature + ")") + "::regprocedure")
            .Append(Text(FunctionSql(context, "assert_authority") + "()") + "::regprocedure"));
        string optionalFunctionIds = string.Join(", ", functions.Select(function =>
            $"(pg_catalog.to_regprocedure({Text(FunctionSql(context, function.Name) + "(" + function.Signature + ")")}))")
            .Append($"(pg_catalog.to_regprocedure({Text(FunctionSql(context, "assert_authority") + "()")}))"));
        string existingFunctionInvalid = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE function.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                    AND (function.proowner <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                        OR NOT EXISTS (SELECT 1 FROM (VALUES {optionalFunctionIds}) expected(function_id)
                            WHERE expected.function_id = function.oid)))
            """;
        string functionInvalid = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_proc function WHERE function.oid IN ({functionIds})
                AND (function.proowner <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                    OR NOT function.prosecdef OR function.provolatile <> 'v'
                    OR function.proparallel <> 'u' OR function.proleakproof OR function.proisstrict
                    OR function.proretset OR function.prokind <> 'f' OR function.proargdefaults IS NOT NULL
                    OR function.prolang <> (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    OR function.prorettype <> CASE WHEN function.oid =
                        {Text(FunctionSql(context, "assert_authority") + "()")}::regprocedure
                        THEN 'pg_catalog.void'::regtype ELSE 'pg_catalog.jsonb'::regtype END
                    OR function.proconfig IS DISTINCT FROM ARRAY['search_path=pg_catalog']::text[]
                    OR pg_catalog.obj_description(function.oid, 'pg_proc') IS DISTINCT FROM
                        '{FingerprintPrefix}' || pg_catalog.encode(
                            pg_catalog.sha256(pg_catalog.convert_to(function.prosrc, 'UTF8')), 'hex')
                    OR pg_catalog.has_function_privilege('{GlobalRole}', function.oid, 'EXECUTE')
                        <> (function.oid <> {Text(FunctionSql(context, "write") + "(uuid,jsonb,uuid,timestamp with time zone)")}::regprocedure)
                    OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(
                        COALESCE(function.proacl, pg_catalog.acldefault('f', function.proowner))) permission
                        WHERE permission.grantee NOT IN (SELECT oid FROM pg_catalog.pg_roles
                            WHERE rolname IN ('{OwnerRole}','{GlobalRole}'))
                            OR permission.privilege_type <> 'EXECUTE' OR permission.grantor <> function.proowner
                            OR permission.grantee = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')
                                AND permission.is_grantable)))
            OR EXISTS (SELECT 1 FROM (VALUES {string.Join(", ", functions.Select(function =>
                $"({Text(FunctionSql(context, function.Name) + "(" + function.Signature + ")")}::regprocedure, {Text(Hash(function.Body))})"))})
                    expected(function_id, hash)
                JOIN pg_catalog.pg_proc function ON function.oid = expected.function_id
                WHERE pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(function.prosrc, 'UTF8')), 'hex') <> expected.hash)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc function WHERE function.oid NOT IN ({functionIds})
                AND (function.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(
                        COALESCE(function.proacl, pg_catalog.acldefault('f', function.proowner))) permission
                        WHERE permission.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            """;
        string policyInvalid = string.Join(" OR ", relations.Where(relation => relation.Type == typeof(TenantSetting)).Select(relation =>
        {
            string table = Table(context, relation.Type);
            string predicate = Column(context, relation.Type, "SettingKey") + " = ANY (" + FixedKeysSql() + ")";
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                    WHERE policy.polrelid = {Text(table)}::regclass AND policy.polname = '{PolicyName}'
                        AND policy.polpermissive AND policy.polcmd = '*'
                        AND policy.polroles = ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')]
                        AND pg_catalog.obj_description(policy.oid, 'pg_policy') =
                            {Text(FingerprintPrefix + Hash(predicate) + ":")} ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(policy.polqual::text, 'UTF8')), 'hex') || ':' ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(policy.polwithcheck::text, 'UTF8')), 'hex'))
                OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                    WHERE policy.polrelid = {Text(table)}::regclass AND policy.polname = '{RestrictivePolicyName}'
                        AND NOT policy.polpermissive AND policy.polcmd = '*'
                        AND policy.polroles = ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')]
                        AND pg_catalog.obj_description(policy.oid, 'pg_policy') =
                            {Text(FingerprintPrefix + Hash(predicate) + ":")} ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(policy.polqual::text, 'UTF8')), 'hex') || ':' ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(policy.polwithcheck::text, 'UTF8')), 'hex'))
                """;
        }));
        string tableRlsInvalid = string.Join(" OR ", relations.Where(relation => relation.Type == typeof(TenantSetting)).Select(relation => $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class WHERE oid = {Text(Table(context, relation.Type))}::regclass
                AND relrowsecurity AND relforcerowsecurity)
            """));
        string authority = $"""
            BEGIN
                IF CURRENT_USER <> '{OwnerRole}'
                    OR NOT pg_catalog.pg_has_role(SESSION_USER, '{GlobalRole}', 'USAGE')
                    OR pg_catalog.pg_has_role(SESSION_USER, '{OwnerRole}', 'MEMBER')
                    OR pg_catalog.pg_has_role(SESSION_USER, '{MigratorRole}', 'MEMBER')
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = SESSION_USER
                        AND (rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication))
                    OR (SELECT count(*) FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) <> 3
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ({roles})
                        AND (({elevated}) OR pg_catalog.shobj_description(oid, 'pg_authid') IS DISTINCT FROM
                            '{FingerprintPrefix}role:' || rolname))
                    OR {membershipInvalid} OR {membershipMissing} OR {ownershipInvalid}
                    OR {privilegeInvalid} OR {functionInvalid} OR {policyInvalid} OR {tableRlsInvalid} THEN
                    RAISE EXCEPTION 'configuration_import_publication_authority_not_ready' USING ERRCODE = '42501';
                END IF;
                {LockSql()}
                RETURN;
            END;
            """;
        string grants = string.Join("\n", relations.Select(relation =>
        {
            string Grant(string access, string[] properties) => $"GRANT {access} (" +
                string.Join(", ", properties.Select(property => Column(context, relation.Type, property))) +
                $") ON TABLE {Table(context, relation.Type)} TO {OwnerRole};";
            return $"""
                {Grant("SELECT", relation.Select)}
                {Grant("INSERT", relation.Insert)}
                {Grant("UPDATE", relation.Update)}
                GRANT DELETE ON TABLE {Table(context, relation.Type)} TO {OwnerRole};
                """;
        }));
        string policies = string.Join("\n", relations.Where(relation => relation.Type == typeof(TenantSetting)).Select(relation =>
        {
            string table = Table(context, relation.Type);
            string predicate = Column(context, relation.Type, "SettingKey") + " = ANY (" + FixedKeysSql() + ")";
            return string.Join("\n", new[] { (PolicyName, "PERMISSIVE"), (RestrictivePolicyName, "RESTRICTIVE") }
                .Select(policy => $"""
                    DROP POLICY IF EXISTS "{policy.Item1}" ON {table};
                    CREATE POLICY "{policy.Item1}" ON {table} AS {policy.Item2}
                        FOR ALL TO {OwnerRole} USING ({predicate}) WITH CHECK ({predicate});
                    DO $stamp_policy$
                    BEGIN
                        EXECUTE (SELECT pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',
                            p.polname, {Text(table)}, {Text(FingerprintPrefix + Hash(predicate) + ":")} ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(p.polqual::text, 'UTF8')), 'hex') || ':' ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(p.polwithcheck::text, 'UTF8')), 'hex'))
                            FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(table)}::regclass
                                AND p.polname = '{policy.Item1}');
                    END; $stamp_policy$;
                    """));
        }));
        string FunctionDdl(string name, string arguments, string returns, string body) => $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, name)}({arguments}) RETURNS {returns}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${body}$body$;
            """;
        string ddl = FunctionDdl("assert_authority", "", "void", authority) + "\n" +
            string.Join("\n", functions.Select(function => FunctionDdl(function.Name, function.Arguments, "jsonb", function.Body)));
        string acl = string.Join("\n", functions.Select(function => (function.Name, function.Signature))
            .Append(("assert_authority", "")).Select(function => $"""
                REVOKE ALL ON FUNCTION {FunctionSql(context, function.Item1)}({function.Item2}) FROM PUBLIC, {GlobalRole};
                {(function.Item1 == "write" ? "" : $"GRANT EXECUTE ON FUNCTION {FunctionSql(context, function.Item1)}({function.Item2}) TO {GlobalRole};")}
                DO $stamp_function$
                BEGIN
                    EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L',
                        function.oid::regprocedure, '{FingerprintPrefix}' || pg_catalog.encode(
                            pg_catalog.sha256(pg_catalog.convert_to(function.prosrc, 'UTF8')), 'hex'))
                        FROM pg_catalog.pg_proc function
                        WHERE function.oid = {Text(FunctionSql(context, function.Item1) + "(" + function.Item2 + ")")}::regprocedure);
                END; $stamp_function$;
                """));
        originalFunctions = functions.Select(function => (function.Name, function.Signature, function.Body))
            .Append(("assert_authority", "", authority)).ToArray();
        // Tenant overrides require forced RLS. Instance settings have no tenant
        // column; their existing table grants remain unchanged. The Global group
        // receives only fixed-function execution; the owner uses the exact columns above.
        return $"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                IF CURRENT_USER <> SESSION_USER OR CURRENT_USER IN ({roles}) OR {migrationInGlobal} THEN
                    RAISE EXCEPTION 'configuration_import_publication_migration_scope' USING ERRCODE = '42501';
                END IF;
                FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                        EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                        EXECUTE pg_catalog.format('COMMENT ON ROLE %I IS %L', role_name,
                            '{FingerprintPrefix}role:' || role_name);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name
                        AND (({elevated}) OR pg_catalog.shobj_description(oid, 'pg_authid') IS DISTINCT FROM
                            '{FingerprintPrefix}role:' || role_name)) THEN
                        RAISE EXCEPTION 'configuration_import_publication_role_invalid' USING ERRCODE = '42501';
                    END IF;
                END LOOP;
                IF {membershipInvalid.Replace("__STATE_MIGRATOR_LOGIN__", "CURRENT_USER", StringComparison.Ordinal)}
                    OR {ownershipInvalid} OR {existingFunctionInvalid} OR {tableRlsInvalid} THEN
                    RAISE EXCEPTION 'configuration_import_publication_migration_authority_invalid' USING ERRCODE = '42501';
                END IF;
            END; $roles$;
            GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE;
            GRANT {MigratorRole} TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;
            GRANT USAGE ON SCHEMA {schemas} TO {OwnerRole};
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            {grants}
            {policies}
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
                migrator_login text := CURRENT_USER;
            BEGIN
                PERFORM pg_catalog.set_config('role', '{OwnerRole}', true);
                EXECUTE pg_catalog.replace($ddl${ddl}$ddl$, '__STATE_MIGRATOR_LOGIN__', pg_catalog.quote_literal(migrator_login));
                EXECUTE $acl${acl}$acl$;
                PERFORM pg_catalog.set_config('role', previous_role, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $closed$
            BEGIN
                IF {membershipMissing.Replace("__STATE_MIGRATOR_LOGIN__", "CURRENT_USER", StringComparison.Ordinal)}
                    OR {privilegeInvalid} OR {functionInvalid} OR {policyInvalid} THEN
                    RAISE EXCEPTION 'configuration_import_publication_authority_not_ready' USING ERRCODE = '42501';
                END IF;
            END; $closed$;
            """;
    }
}
