using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportBrandingContract
{
    private const string FingerprintPrefix = "event-configuration-import-branding:v1:";
    private const string MigratorPlaceholder = "__BRANDING_MIGRATOR_LOGIN__";
    private const string PolicyName = "configuration_import_branding_owner";
    private const string RestrictivePolicyName = "configuration_import_branding_boundary";

    /// <summary>
    /// Installs with migration authority, using its existing transaction when present.
    /// The caller separately enrolls the concrete Global login; Runtime is never enrolled.
    /// </summary>
    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql())
            return;
        ValidateCatalog();
        context.ValidateTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("Branding installation requires migration authority.");
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

    private static string Schema(ExploreDbContext context, Type type) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(type)!.GetSchema() ?? "public";

    private static string FunctionSql(ExploreDbContext context, string operation) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            "configuration_import_branding_" + operation, Schema(context, typeof(TenantSettingsDocument)));

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

    private static string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string BuildSql(ExploreDbContext context)
    {
        var installation = new PostgresConfigurationImportInstallationSql(
            OwnerRole, MigratorRole, GlobalRole, FingerprintPrefix, MigratorPlaceholder);
        string roles = $"'{OwnerRole}', '{MigratorRole}', '{GlobalRole}'";
        const string elevated = "rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication OR rolinherit";
        string schema = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(Schema(context, typeof(TenantSettingsDocument)));
        (Type Type, string[] Select, string[] Insert, string[] Update, string Target)[] relations =
        [
            (typeof(TenantSettingsDocument),
                ["Id", "TenantId", "DocumentKey", "SchemaVersion", "DefaultsVersion", "ConcurrencyStamp"],
                ["Id", "TenantId", "DocumentKey", "SchemaVersion", "DefaultsVersion", "PayloadJson",
                    "ConcurrencyStamp", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"],
                ["SchemaVersion", "DefaultsVersion", "PayloadJson", "ConcurrencyStamp", "UpdatedAt", "UpdatedBy"], "TenantId")
        ];
        string Predicate(Type type, string target) =>
            Column(context, type, target) + " = NULLIF(pg_catalog.current_setting(" +
            "'app.configuration_import_branding_tenant', true), '')::uuid AND " +
            Column(context, type, "DocumentKey") + " = " + Text(SettingsDocumentKeys.Tenant.Branding) + " AND " +
            Column(context, type, "SchemaVersion") + " = " + TenantBrandingSettingsDocumentDefaults.SchemaVersion + " AND " +
            Column(context, type, "DefaultsVersion") + " = " + Text(TenantBrandingSettingsDocumentDefaults.DefaultsVersion);
        string expectedColumns = string.Join(",\n", relations.SelectMany(relation =>
            new[] { ("SELECT", relation.Select), ("INSERT", relation.Insert), ("UPDATE", relation.Update) }
                .SelectMany(access => access.Item2.Select(property =>
                    $"({Text(Table(context, relation.Type))}::regclass, " +
                    $"{Text(ColumnName(context, relation.Type, property))}, {Text(access.Item1)})"))));
        (string Name, string Signature, string Arguments, string Returns, string Body)[] functions =
        [
            ("write", "uuid,uuid,uuid,uuid,text,integer,text,jsonb,uuid,timestamp with time zone",
                "p_tenant uuid, p_id uuid, p_expected uuid, p_stamp uuid, p_key text, p_schema integer, " +
                "p_defaults text, p_payload jsonb, p_actor uuid, p_at timestamp with time zone", "boolean", WriteBody(context))
        ];
        string authoritySignature = FunctionSql(context, "assert_authority") + "()";
        string Signature(string name, string arguments) => FunctionSql(context, name) + "(" + arguments + ")";
        string ids = string.Join(", ", functions.Select(function =>
            $"pg_catalog.to_regprocedure({Text(Signature(function.Name, function.Signature))})")
            .Append($"pg_catalog.to_regprocedure({Text(authoritySignature)})"));
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
        string membershipMissing = installation.MissingMemberships();
        string ownershipInvalid = installation.NonRoutineOwnership();
        ownershipInvalid += " OR " + string.Join(" OR ", relations.Select(relation => $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                WHERE relation.oid = {Text(Table(context, relation.Type))}::regclass AND relation.relkind IN ('r','p')
                    AND pg_catalog.pg_has_role({MigratorPlaceholder}, relation.relowner, 'USAGE')
                    AND pg_catalog.pg_has_role({MigratorPlaceholder}, namespace.nspowner, 'USAGE'))
            """));
        string privilegeInvalid = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                CROSS JOIN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) role
                WHERE namespace.nspname NOT IN ('pg_catalog','information_schema')
                    AND namespace.nspname NOT LIKE 'pg_toast%' AND relation.relkind IN ('r','p','v','m','f')
                    AND (pg_catalog.has_table_privilege(role.oid, relation.oid,
                            'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR role.oid <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                            AND pg_catalog.has_any_column_privilege(role.oid, relation.oid, 'SELECT,INSERT,UPDATE,REFERENCES')))
            OR {installation.ColumnPrivilegeDrift(expectedColumns)}
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace namespace
                CROSS JOIN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) role
                WHERE pg_catalog.has_schema_privilege(role.oid, namespace.oid, 'CREATE'))
            OR {installation.ExtraneousGrants()}
            OR NOT pg_catalog.has_schema_privilege('{GlobalRole}', {Text(Schema(context, typeof(TenantSettingsDocument)))}, 'USAGE')
            """;
        privilegeInvalid += " OR " + string.Join(" OR ", relations.Select(relation =>
            $"NOT pg_catalog.has_schema_privilege('{OwnerRole}', {Text(Schema(context, relation.Type))}, 'USAGE')"));
        string existingFunctionInvalid = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE function.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                    AND (function.proowner <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                        OR NOT COALESCE(function.oid = ANY (ARRAY[{ids}]::oid[]), false)))
            """;
        string functionInvalid = string.Join(" OR ", functions.Select(function =>
            (Signature: Signature(function.Name, function.Signature), Public: true, Type: function.Returns))
            .Append((Signature: authoritySignature, Public: false, Type: "void"))
            .Select(contract => $"""
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
                """));
        functionInvalid += " OR " + string.Join(" OR ", functions.Select(function => $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE function.oid = pg_catalog.to_regprocedure({Text(Signature(function.Name, function.Signature))})
                    AND encode(sha256(convert_to(function.prosrc, 'UTF8')), 'hex') = {Text(Hash(function.Body))})
            """));
        functionInvalid += $"""
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE NOT COALESCE(function.oid = ANY (ARRAY[{ids}]::oid[]), false)
                    AND (function.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(
                            COALESCE(function.proacl, pg_catalog.acldefault('f', function.proowner))) permission
                            WHERE permission.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            """;
        var tenantRelations = relations;
        string rlsInvalid = string.Join(" OR ", tenantRelations.Select(relation => $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class
                WHERE oid = {Text(Table(context, relation.Type))}::regclass AND relrowsecurity AND relforcerowsecurity)
            """));
        string policyInvalid = string.Join(" OR ", tenantRelations.SelectMany(relation =>
            new[] { (PolicyName, true), (RestrictivePolicyName, false) }.Select(policy => $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                    WHERE policy.polrelid = {Text(Table(context, relation.Type))}::regclass
                        AND policy.polname = '{policy.Item1}' AND policy.polpermissive = {policy.Item2.ToString().ToLowerInvariant()}
                        AND policy.polcmd = '*'
                        AND policy.polroles = ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')]
                        AND pg_catalog.obj_description(policy.oid, 'pg_policy') =
                            {Text(FingerprintPrefix + Hash(Predicate(relation.Type, relation.Target)) + ":")} ||
                            encode(sha256(convert_to(policy.polqual::text, 'UTF8')), 'hex') || ':' ||
                            encode(sha256(convert_to(policy.polwithcheck::text, 'UTF8')), 'hex'))
                """)));
        string structureInvalid = $"""
            (SELECT count(*) FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) <> 3
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ({roles})
                AND (({elevated}) OR pg_catalog.shobj_description(oid, 'pg_authid') IS DISTINCT FROM
                    '{FingerprintPrefix}role:' || rolname))
            OR {membershipInvalid} OR {membershipMissing} OR {ownershipInvalid}
            OR {privilegeInvalid} OR {functionInvalid} OR {rlsInvalid} OR {policyInvalid}
            """;
        string authorityBody = $"""
            BEGIN
                IF CURRENT_USER <> '{OwnerRole}'
                    OR NOT pg_catalog.pg_has_role(SESSION_USER, '{GlobalRole}', 'USAGE')
                    OR pg_catalog.pg_has_role(SESSION_USER, '{OwnerRole}', 'MEMBER')
                    OR pg_catalog.pg_has_role(SESSION_USER, '{MigratorRole}', 'MEMBER')
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id', true), '') IS NOT NULL
                    OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
                        WHERE (pg_catalog.pg_has_role(SESSION_USER, role.oid, 'USAGE')
                            OR pg_catalog.pg_has_role(SESSION_USER, role.oid, 'SET'))
                            AND (role.rolsuper OR role.rolbypassrls OR role.rolcreatedb
                                OR role.rolcreaterole OR role.rolreplication))
                    OR {structureInvalid} THEN
                    RAISE EXCEPTION 'configuration_import_branding_authority_not_ready' USING ERRCODE = '42501';
                END IF;
                RETURN;
            END;
            """;
        string grants = string.Join("\n", relations.SelectMany(relation =>
            new[] { ("SELECT", relation.Select), ("INSERT", relation.Insert), ("UPDATE", relation.Update) }
                .Where(access => access.Item2.Length > 0).Select(access =>
                    $"GRANT {access.Item1} ({string.Join(", ", access.Item2.Select(property => Column(context, relation.Type, property)))}) " +
                    $"ON TABLE {Table(context, relation.Type)} TO {OwnerRole};")));
        string policies = string.Join("\n", tenantRelations.SelectMany(relation =>
            new[] { (PolicyName, "PERMISSIVE"), (RestrictivePolicyName, "RESTRICTIVE") }.Select(policy => $"""
                DROP POLICY IF EXISTS "{policy.Item1}" ON {Table(context, relation.Type)};
                CREATE POLICY "{policy.Item1}" ON {Table(context, relation.Type)} AS {policy.Item2}
                    FOR ALL TO {OwnerRole} USING ({Predicate(relation.Type, relation.Target)})
                    WITH CHECK ({Predicate(relation.Type, relation.Target)});
                DO $stamp_policy$
                BEGIN
                    EXECUTE (SELECT pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',
                        policy.polname, {Text(Table(context, relation.Type))},
                        {Text(FingerprintPrefix + Hash(Predicate(relation.Type, relation.Target)) + ":")} ||
                        encode(sha256(convert_to(policy.polqual::text, 'UTF8')), 'hex') || ':' ||
                        encode(sha256(convert_to(policy.polwithcheck::text, 'UTF8')), 'hex'))
                        FROM pg_catalog.pg_policy policy
                        WHERE policy.polrelid = {Text(Table(context, relation.Type))}::regclass AND policy.polname = '{policy.Item1}');
                END; $stamp_policy$;
                """)));
        string FunctionDdl(string operation, string arguments, string returns, string source) => $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, operation)}({arguments}) RETURNS {returns}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${source}$body$;
            """;
        string ddl = FunctionDdl("assert_authority", "", "void", authorityBody) + "\n" +
            string.Join("\n", functions.Select(function =>
                FunctionDdl(function.Name, function.Arguments, function.Returns, function.Body)));
        string acl = string.Join("\n", functions.Select(function => Signature(function.Name, function.Signature))
            .Append(authoritySignature).Select(signature => $"""
                REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {GlobalRole};
                {(signature != authoritySignature ? $"GRANT EXECUTE ON FUNCTION {signature} TO {GlobalRole};" : "")}
                DO $stamp$
                BEGIN
                    EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L', function.oid::regprocedure,
                        '{FingerprintPrefix}' || encode(sha256(convert_to(function.prosrc, 'UTF8')), 'hex'))
                        FROM pg_catalog.pg_proc function WHERE function.oid = {Text(signature)}::regprocedure);
                END; $stamp$;
                """));
        string schemaGrants = string.Join("\n", relations.Select(relation => Schema(context, relation.Type))
            .Distinct(StringComparer.Ordinal).Select(name =>
                $"GRANT USAGE ON SCHEMA {context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name)} TO {OwnerRole};"));
        return installation.Build(schema, schemaGrants, grants, policies, ddl, acl,
            installationInvalid: $"""
                {membershipInvalid.Replace(MigratorPlaceholder, "CURRENT_USER", StringComparison.Ordinal)}
                    OR {ownershipInvalid.Replace(MigratorPlaceholder, "CURRENT_USER", StringComparison.Ordinal)}
                    OR {existingFunctionInvalid} OR {rlsInvalid}
                """,
            closedInvalid: structureInvalid.Replace(MigratorPlaceholder, "CURRENT_USER", StringComparison.Ordinal),
            diagnosticPrefix: "configuration_import_branding");
    }
}
