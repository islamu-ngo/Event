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

public static partial class PostgresConfigurationImportScalarSettingContract
{
    private const string FingerprintPrefix = "event-configuration-import-scalar:v1:";
    private const string MigratorPlaceholder = "__SCALAR_MIGRATOR_LOGIN__";
    private const string UpsertSignature = "text,text,integer,text,text,boolean,jsonb,integer,uuid,uuid,text,text";
    private static readonly string[] SelectColumns = ["Id", "SettingKey", "Value"];
    private static readonly string[] InsertColumns =
    [
        "Id", "SettingKey", "Value", "SettingValueTypeId", "IsLocked", "AllowedValues",
        "Description", "Category", "DisplayOrder", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"
    ];
    private static readonly string[] UpdateColumns =
    [
        "Value", "SettingValueTypeId", "IsLocked", "AllowedValues", "Description", "Category",
        "DisplayOrder", "UpdatedAt", "UpdatedBy"
    ];

    /// <summary>
    /// Administrative installation only; concrete Global enrollment belongs to the caller.
    /// Uses literal DDL on the migration transaction and never changes SystemSetting RLS.
    /// </summary>
    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql())
            return;
        context.ValidateTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("Scalar installation requires administrative migration authority.");
        await using IDbContextTransaction? owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(ct) : null;
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = BuildSql(context);
        if (context.Database.GetCommandTimeout() is { } timeout)
            command.CommandTimeout = timeout;
        await command.ExecuteNonQueryAsync(ct);
        if (owned is not null)
            await owned.CommitAsync(ct);
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(SystemSetting))!.GetSchema() ?? "public";

    private static string FunctionSql(ExploreDbContext context, string operation) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            "configuration_import_scalar_" + operation, Schema(context));

    private static string Table(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(typeof(SystemSetting))!.GetTableName()!, Schema(context));

    private static string ColumnName(ExploreDbContext context, string property)
    {
        IEntityType entity = context.Model.FindEntityType(typeof(SystemSetting))!;
        return entity.FindProperty(property)!.GetColumnName(
            StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }

    private static string Column(ExploreDbContext context, string property) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(ColumnName(context, property));

    private static string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string BuildSql(ExploreDbContext context)
    {
        if (!context.Model.FindEntityType(typeof(SystemSetting))!.GetProperties().Select(property => property.Name)
            .Order(StringComparer.Ordinal).SequenceEqual(InsertColumns.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("configuration_import_scalar_mapping_contract_changed");
        string roles = $"'{OwnerRole}', '{MigratorRole}', '{GlobalRole}'";
        const string elevated = "rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication OR rolinherit";
        string schema = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(Schema(context));
        string table = Table(context);
        string expectedColumns = string.Join(",\n",
            new[] { ("SELECT", SelectColumns), ("INSERT", InsertColumns), ("UPDATE", UpdateColumns) }
                .SelectMany(access => access.Item2.Select(property =>
                    $"({Text(table)}::regclass, {Text(ColumnName(context, property))}, {Text(access.Item1)})")));
        string authoritySignature = FunctionSql(context, "assert_authority") + "()";
        string upsertSignature = FunctionSql(context, "upsert") + "(" + UpsertSignature + ")";
        string ids = $"pg_catalog.to_regprocedure({Text(authoritySignature)}), pg_catalog.to_regprocedure({Text(upsertSignature)})";
        string membershipInvalid = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                WHERE member.rolname IN ('{OwnerRole}', '{GlobalRole}')
                    OR member.rolname = '{MigratorRole}' AND parent.rolname <> '{OwnerRole}'
                    OR parent.rolname = '{OwnerRole}' AND member.rolname <> '{MigratorRole}'
                    OR parent.rolname = '{MigratorRole}' AND member.rolname <> {MigratorPlaceholder}
                    OR parent.rolname IN ({roles}) AND edge.admin_option
                    OR parent.rolname IN ('{OwnerRole}', '{MigratorRole}')
                        AND (edge.inherit_option OR NOT edge.set_option)
                    OR parent.rolname = '{GlobalRole}'
                        AND (member.rolsuper OR member.rolbypassrls OR member.rolcreatedb
                            OR member.rolcreaterole OR member.rolreplication))
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
                WHERE member.rolname = {MigratorPlaceholder} AND parent.rolname = '{MigratorRole}'
                    AND edge.set_option AND NOT edge.inherit_option AND NOT edge.admin_option)
            """;
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
            OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                WHERE relation.oid = {Text(table)}::regclass AND relation.relkind IN ('r', 'p')
                    AND pg_catalog.pg_has_role({MigratorPlaceholder}, relation.relowner, 'USAGE')
                    AND pg_catalog.pg_has_role({MigratorPlaceholder}, namespace.nspowner, 'USAGE'))
            """;
        string privilegeInvalid = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                CROSS JOIN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) role
                WHERE namespace.nspname NOT IN ('pg_catalog', 'information_schema')
                    AND namespace.nspname NOT LIKE 'pg_toast%' AND relation.relkind IN ('r','p','v','m','f')
                    AND (pg_catalog.has_table_privilege(role.oid, relation.oid,
                            'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR role.oid <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                            AND pg_catalog.has_any_column_privilege(role.oid, relation.oid, 'SELECT,INSERT,UPDATE,REFERENCES')))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute attribute
                JOIN pg_catalog.pg_class relation ON relation.oid = attribute.attrelid
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) access(privilege)
                WHERE attribute.attnum > 0 AND NOT attribute.attisdropped
                    AND namespace.nspname NOT IN ('pg_catalog', 'information_schema')
                    AND namespace.nspname NOT LIKE 'pg_toast%'
                    AND pg_catalog.has_column_privilege('{OwnerRole}', relation.oid, attribute.attnum, access.privilege)
                    AND NOT EXISTS (SELECT 1 FROM (VALUES {expectedColumns}) expected(relation_id, column_name, privilege)
                        WHERE expected.relation_id = relation.oid AND expected.column_name = attribute.attname
                            AND expected.privilege = access.privilege))
            OR EXISTS (SELECT 1 FROM (VALUES {expectedColumns}) expected(relation_id, column_name, privilege)
                WHERE NOT pg_catalog.has_column_privilege('{OwnerRole}', expected.relation_id,
                    expected.column_name, expected.privilege))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace namespace
                CROSS JOIN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) role
                WHERE pg_catalog.has_schema_privilege(role.oid, namespace.oid, 'CREATE'))
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
            OR NOT pg_catalog.has_schema_privilege('{OwnerRole}', {Text(Schema(context))}, 'USAGE')
            OR NOT pg_catalog.has_schema_privilege('{GlobalRole}', {Text(Schema(context))}, 'USAGE')
            """;
        string existingFunctionInvalid = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE function.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                    AND (function.proowner <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                        OR NOT COALESCE(function.oid = ANY (ARRAY[{ids}]::oid[]), false)))
            """;
        string body = UpsertBody(context);
        string functionInvalid = string.Join(" OR ", new[]
        {
            (Signature: authoritySignature, Public: false, Type: "void"),
            (Signature: upsertSignature, Public: true, Type: "text")
        }.Select(contract => $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE function.oid = pg_catalog.to_regprocedure({Text(contract.Signature)})
                    AND function.proowner = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                    AND function.prosecdef AND function.provolatile = 'v' AND function.proparallel = 'u'
                    AND NOT function.proleakproof AND NOT function.proisstrict AND function.prokind = 'f'
                    AND function.proargdefaults IS NULL AND NOT function.proretset
                    AND function.prorettype = {Text(contract.Type)}::regtype
                    AND function.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND function.proconfig = ARRAY['search_path=pg_catalog']::text[]
                    AND pg_catalog.obj_description(function.oid, 'pg_proc') =
                        '{FingerprintPrefix}' || encode(sha256(convert_to(function.prosrc, 'UTF8')), 'hex')
                    AND pg_catalog.has_function_privilege('{GlobalRole}', function.oid, 'EXECUTE') =
                        {contract.Public.ToString().ToLowerInvariant()}
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(
                        COALESCE(function.proacl, pg_catalog.acldefault('f', function.proowner))) permission
                        WHERE permission.grantee NOT IN (SELECT oid FROM pg_catalog.pg_roles
                            WHERE rolname IN ('{OwnerRole}'{(contract.Public ? $", '{GlobalRole}'" : "")}))
                            OR permission.privilege_type <> 'EXECUTE' OR permission.grantor <> function.proowner
                            OR permission.grantee <> function.proowner AND permission.is_grantable))
            """)) + $"""
            OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE function.oid = pg_catalog.to_regprocedure({Text(upsertSignature)})
                    AND encode(sha256(convert_to(function.prosrc, 'UTF8')), 'hex') = {Text(Hash(body))})
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE NOT COALESCE(function.oid = ANY (ARRAY[{ids}]::oid[]), false)
                    AND (function.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(
                            COALESCE(function.proacl, pg_catalog.acldefault('f', function.proowner))) permission
                            WHERE permission.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            """;
        string structureInvalid = $"""
            (SELECT count(*) FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) <> 3
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ({roles})
                AND (({elevated}) OR pg_catalog.shobj_description(oid, 'pg_authid') IS DISTINCT FROM
                    '{FingerprintPrefix}role:' || rolname))
            OR {membershipInvalid} OR {membershipMissing} OR {ownershipInvalid}
            OR {privilegeInvalid} OR {functionInvalid}
            """;
        string authorityBody = $"""
            BEGIN
                IF CURRENT_USER <> '{OwnerRole}'
                    OR NOT pg_catalog.pg_has_role(SESSION_USER, '{GlobalRole}', 'USAGE')
                    OR pg_catalog.pg_has_role(SESSION_USER, '{OwnerRole}', 'MEMBER')
                    OR pg_catalog.pg_has_role(SESSION_USER, '{MigratorRole}', 'MEMBER')
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                        WHERE (pg_catalog.pg_has_role(SESSION_USER, role.oid, 'USAGE')
                            OR pg_catalog.pg_has_role(SESSION_USER, role.oid, 'SET'))
                            AND (role.rolsuper OR role.rolbypassrls OR role.rolcreatedb
                                OR role.rolcreaterole OR role.rolreplication))
                    OR {structureInvalid} THEN
                    RAISE EXCEPTION 'configuration_import_scalar_authority_not_ready' USING ERRCODE = '42501';
                END IF;
                RETURN;
            END;
            """;
        string Grant(string privilege, string[] columns) =>
            $"GRANT {privilege} ({string.Join(", ", columns.Select(property => Column(context, property)))}) ON TABLE {table} TO {OwnerRole};";
        string FunctionDdl(string operation, string arguments, string returns, string source) => $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, operation)}({arguments}) RETURNS {returns}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${source}$body$;
            """;
        string ddl = FunctionDdl("assert_authority", "", "void", authorityBody) + "\n" +
            FunctionDdl("upsert",
                "p_key text, p_value text, p_type integer, p_description text, p_category text, " +
                "p_locked boolean, p_allowed jsonb, p_order integer, p_id uuid, p_actor uuid, p_created text, p_updated text",
                "text", body);
        string acl = string.Join("\n", new[] { authoritySignature, upsertSignature }.Select(signature => $"""
            REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {GlobalRole};
            {(signature == upsertSignature ? $"GRANT EXECUTE ON FUNCTION {signature} TO {GlobalRole};" : "")}
            DO $stamp$
            BEGIN
                EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L', function.oid::regprocedure,
                    '{FingerprintPrefix}' || encode(sha256(convert_to(function.prosrc, 'UTF8')), 'hex'))
                    FROM pg_catalog.pg_proc function WHERE function.oid = {Text(signature)}::regprocedure);
            END; $stamp$;
            """));
        return $"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                IF CURRENT_USER <> SESSION_USER OR CURRENT_USER IN ({roles}) OR {migrationInGlobal} THEN
                    RAISE EXCEPTION 'configuration_import_scalar_migration_scope' USING ERRCODE = '42501';
                END IF;
                FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                        EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                        EXECUTE pg_catalog.format('COMMENT ON ROLE %I IS %L', role_name, '{FingerprintPrefix}role:' || role_name);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name
                        AND (({elevated}) OR pg_catalog.shobj_description(oid, 'pg_authid') IS DISTINCT FROM
                            '{FingerprintPrefix}role:' || role_name)) THEN
                        RAISE EXCEPTION 'configuration_import_scalar_role_invalid' USING ERRCODE = '42501';
                    END IF;
                END LOOP;
                IF {membershipInvalid.Replace(MigratorPlaceholder, "CURRENT_USER", StringComparison.Ordinal)}
                    OR {ownershipInvalid.Replace(MigratorPlaceholder, "CURRENT_USER", StringComparison.Ordinal)}
                    OR {existingFunctionInvalid} THEN
                    RAISE EXCEPTION 'configuration_import_scalar_migration_authority_invalid' USING ERRCODE = '42501';
                END IF;
            END; $roles$;
            GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE;
            GRANT {MigratorRole} TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            {Grant("SELECT", SelectColumns)}
            {Grant("INSERT", InsertColumns)}
            {Grant("UPDATE", UpdateColumns)}
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
                migrator_login text := CURRENT_USER;
            BEGIN
                PERFORM pg_catalog.set_config('role', '{OwnerRole}', true);
                EXECUTE pg_catalog.replace($ddl${ddl}$ddl$, '{MigratorPlaceholder}', pg_catalog.quote_literal(migrator_login));
                EXECUTE $acl${acl}$acl$;
                PERFORM pg_catalog.set_config('role', previous_role, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $closed$
            BEGIN
                IF {structureInvalid.Replace(MigratorPlaceholder, "CURRENT_USER", StringComparison.Ordinal)} THEN
                    RAISE EXCEPTION 'configuration_import_scalar_authority_not_ready' USING ERRCODE = '42501';
                END IF;
            END; $closed$;
            """;
    }
}
