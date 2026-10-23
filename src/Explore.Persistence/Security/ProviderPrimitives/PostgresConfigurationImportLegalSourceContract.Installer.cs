using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportLegalSourceContract
{
    /// <summary>
    /// Migration-owned installation. Parent-owned tenant RLS must already protect all three legal source tables.
    /// Does not enroll any Global or Runtime login.
    /// </summary>
    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql())
            return;
        if (context.Database.CurrentTransaction is not null)
        {
            await context.Database.ExecuteSqlRawAsync(BuildSql(context), ct);
            return;
        }
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), ct);
        await transaction.CommitAsync(ct);
    }

    private static string Schema(ExploreDbContext context, Type type) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(type)!.GetSchema() ?? "public";

    private static string FunctionSql(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(FunctionName, Schema(context, typeof(LegalDocument)));

    private static string Table(ExploreDbContext context, Type type) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(type)!.GetTableName()!, Schema(context, type));

    private static string Column(ExploreDbContext context, Type type, string property)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
    }

    private static string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Kinds(LegalDocumentScope scope) =>
        "ARRAY[" + string.Join(", ", LegalDocumentKindCatalog.Entries.Values
            .Where(entry => entry.Scope == scope).Select(entry => (int)entry.Kind).Order()) + "]::integer[]";

    private static (Type Type, string[] Columns, string Predicate)[] Relations(ExploreDbContext context)
    {
        string D(string property) => Column(context, typeof(LegalDocument), property);
        string V(string property) => Column(context, typeof(LegalDocumentVersion), property);
        string L(string property) => Column(context, typeof(LegalDocumentLocalizedSource), property);
        string documents = Table(context, typeof(LegalDocument));
        string versions = Table(context, typeof(LegalDocumentVersion));
        string documentMatch = $"""
            ((d.{D("Scope")} = {(int)LegalDocumentScope.Instance} AND d.{D("TenantId")} IS NULL
                AND d.{D("AuthorityKey")} = 'instance' AND d.{D("Kind")} = ANY ({Kinds(LegalDocumentScope.Instance)}))
             OR (d.{D("Scope")} = {(int)LegalDocumentScope.Tenant} AND d.{D("TenantId")} IS NOT NULL
                AND d.{D("TenantId")} <> '00000000-0000-0000-0000-000000000000'::uuid
                AND d.{D("AuthorityKey")} = 'tenant:' || replace(d.{D("TenantId")}::text, '-', '')
                AND d.{D("Kind")} = ANY ({Kinds(LegalDocumentScope.Tenant)})))
            """;
        return
        [
            (typeof(LegalDocument),
                ["Id", "Scope", "TenantId", "AuthorityKey", "Kind", "CurrentVersion"],
                documentMatch.Replace("d.", "", StringComparison.Ordinal)),
            (typeof(LegalDocumentVersion),
                ["Id", "LegalDocumentId", "Version", "Audience", "RequiresFreshAcceptance",
                    "TemplateId", "TemplateVersion", "TemplateSourceKind",
                    "TemplateLicenseExpression", "TemplateReviewReference"],
                $"""
                EXISTS (SELECT 1 FROM {documents} d
                    WHERE d.{D("Id")} = {Table(context, typeof(LegalDocumentVersion))}.{V("LegalDocumentId")}
                      AND d.{D("CurrentVersion")} = {Table(context, typeof(LegalDocumentVersion))}.{V("Version")}
                      AND {documentMatch})
                """),
            (typeof(LegalDocumentLocalizedSource),
                ["LegalDocumentVersionId", "LanguageTag", "Title", "Summary", "Markdown"],
                $"""
                EXISTS (SELECT 1 FROM {versions} v JOIN {documents} d ON d.{D("Id")} = v.{V("LegalDocumentId")}
                    WHERE v.{V("Id")} = {Table(context, typeof(LegalDocumentLocalizedSource))}.{L("LegalDocumentVersionId")}
                      AND d.{D("CurrentVersion")} = v.{V("Version")} AND {documentMatch})
                """)
        ];
    }

    private static string PolicyFingerprint(ExploreDbContext context, Type type, string predicate) =>
        "event-configuration-import-legal-source:v1:" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{Table(context, type)}:{predicate}"))) + ":";

    private static string TenantFingerprint(ExploreDbContext context, Type type)
    {
        string tableName = context.Model.FindEntityType(type)!.GetTableName()!;
        var table = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .SingleOrDefault(candidate => candidate.TableName == tableName)
            ?? throw new InvalidOperationException("configuration_import_legal_source_tenant_rls_model_required");
        return PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(table, Schema(context, type));
    }

    private static string RoleSafetySql(
        ExploreDbContext context, string migrationLogin = "__LEGAL_SOURCE_MIGRATOR_LOGIN__")
    {
        var relations = Relations(context);
        string roleNames = $"'{OwnerRole}', '{MigratorRole}', '{GlobalRole}'";
        string protectedTables = string.Join(", ", relations.Select(relation =>
            Text(Table(context, relation.Type)) + "::regclass"));
        string? targetSchema = context.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema;
        string schemaNames = string.Join(", ", context.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null)
            .Select(entity => targetSchema ?? entity.GetSchema() ?? "public")
            .Distinct(StringComparer.Ordinal).Select(Text));
        string excludedColumns = string.Join(" OR ", relations.SelectMany(relation =>
        {
            var entity = context.Model.FindEntityType(relation.Type)!;
            return entity.GetProperties().Where(property => !relation.Columns.Contains(property.Name, StringComparer.Ordinal))
                .Select(property =>
                    $"pg_catalog.has_column_privilege('{OwnerRole}', {Text(Table(context, relation.Type))}::regclass, " +
                    $"{Text(property.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!)}, 'SELECT')");
        }));
        return $"""
            (SELECT count(*) FROM pg_catalog.pg_roles WHERE rolname IN ({roleNames})) = 3
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ({roleNames})
                AND (rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication OR rolinherit))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                WHERE member.rolname IN ('{OwnerRole}', '{GlobalRole}')
                   OR member.rolname = '{MigratorRole}' AND parent.rolname <> '{OwnerRole}'
                   OR parent.rolname = '{OwnerRole}' AND member.rolname <> '{MigratorRole}'
                   OR parent.rolname = '{MigratorRole}'
                      AND (member.rolname <> {migrationLogin} OR edge.admin_option OR NOT edge.set_option)
                   OR parent.rolname = '{GlobalRole}'
                      AND (member.rolsuper OR member.rolbypassrls OR member.rolcreatedb
                           OR member.rolcreaterole OR member.rolreplication))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                WHERE namespace.nspname IN ({schemaNames}) AND relation.relkind IN ('r', 'p', 'v', 'm', 'f')
                  AND (relation.relowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roleNames}))
                    OR pg_catalog.has_table_privilege('{GlobalRole}', relation.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER')
                    OR pg_catalog.has_any_column_privilege('{GlobalRole}', relation.oid, 'SELECT, INSERT, UPDATE, REFERENCES')
                    OR pg_catalog.has_table_privilege('{MigratorRole}', relation.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER')
                    OR pg_catalog.has_any_column_privilege('{MigratorRole}', relation.oid, 'SELECT, INSERT, UPDATE, REFERENCES')
                    OR pg_catalog.has_table_privilege('{OwnerRole}', relation.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER')
                    OR pg_catalog.has_any_column_privilege('{OwnerRole}', relation.oid, 'INSERT, UPDATE, REFERENCES')
                    OR relation.oid NOT IN ({protectedTables}) AND pg_catalog.has_any_column_privilege('{OwnerRole}', relation.oid, 'SELECT')))
            AND NOT ({excludedColumns})
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace namespace
                WHERE namespace.nspname IN ({schemaNames})
                  AND (pg_catalog.has_schema_privilege('{OwnerRole}', namespace.oid, 'CREATE')
                    OR pg_catalog.has_schema_privilege('{GlobalRole}', namespace.oid, 'CREATE')))
            """;
    }

    private static string AuthorityReadySql(ExploreDbContext context) => $"""
        ({StructureReadySql(context)}
        AND pg_catalog.pg_has_role(session_user, '{GlobalRole}', 'USAGE')
        AND NOT pg_catalog.pg_has_role(session_user, '{OwnerRole}', 'MEMBER')
        AND NOT pg_catalog.pg_has_role(session_user, '{MigratorRole}', 'MEMBER')
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles role
            WHERE (pg_catalog.pg_has_role(session_user, role.oid, 'USAGE')
                OR pg_catalog.pg_has_role(session_user, role.oid, 'SET'))
              AND (role.rolsuper OR role.rolbypassrls OR role.rolcreatedb OR role.rolcreaterole OR role.rolreplication)))
        """;

    private static string StructureReadySql(
        ExploreDbContext context, string migrationLogin = "__LEGAL_SOURCE_MIGRATOR_LOGIN__")
    {
        var relations = Relations(context);
        string expected = string.Join(", ", relations.Select(relation =>
            $"({Text(Table(context, relation.Type))}::regclass, " +
            $"{Text(PolicyFingerprint(context, relation.Type, relation.Predicate))}, " +
            $"{Text(TenantFingerprint(context, relation.Type))})"));
        string signature = Text(FunctionSql(context) + "(integer,uuid)");
        return $"""
            (
            {RoleSafetySql(context, migrationLogin)}
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE function.oid = pg_catalog.to_regprocedure({signature})
                  AND function.proowner = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                  AND function.prosecdef AND function.provolatile = 's'
                  AND function.proconfig = ARRAY['search_path=pg_catalog']::text[]
                  AND pg_catalog.obj_description(function.oid, 'pg_proc') =
                      'event-configuration-import-legal-source-function:v1:' || pg_catalog.md5(function.prosrc)
                  AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(
                      COALESCE(function.proacl, pg_catalog.acldefault('f', function.proowner))) permission
                      WHERE permission.grantee NOT IN (
                          SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}', '{GlobalRole}'))
                         OR permission.privilege_type <> 'EXECUTE'
                         OR permission.grantee = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')
                            AND permission.is_grantable)
                  AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(function.proacl) permission
                      WHERE permission.grantee = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')
                        AND permission.privilege_type = 'EXECUTE' AND NOT permission.is_grantable))
            AND NOT EXISTS (SELECT 1 FROM (VALUES {expected}) expected(relation_id, fingerprint, tenant_fingerprint)
                LEFT JOIN pg_catalog.pg_class relation ON relation.oid = expected.relation_id
                LEFT JOIN pg_catalog.pg_policy policy ON policy.polrelid = relation.oid AND policy.polname = '{PolicyName}'
                LEFT JOIN pg_catalog.pg_policy tenant_policy ON tenant_policy.polrelid = relation.oid
                    AND tenant_policy.polname = 'tenant_isolation'
                WHERE relation.oid IS NULL OR NOT relation.relrowsecurity OR NOT relation.relforcerowsecurity
                   OR policy.oid IS NULL OR policy.polcmd <> 'r' OR NOT policy.polpermissive
                   OR policy.polwithcheck IS NOT NULL OR policy.polqual IS NULL
                   OR policy.polroles <> ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')]
                   OR pg_catalog.obj_description(policy.oid, 'pg_policy') IS DISTINCT FROM
                      expected.fingerprint || pg_catalog.md5(policy.polqual::text)
                   OR tenant_policy.oid IS NULL OR tenant_policy.polcmd <> '*' OR NOT tenant_policy.polpermissive
                   OR tenant_policy.polroles <> ARRAY[0::oid] OR tenant_policy.polqual IS NULL
                   OR tenant_policy.polwithcheck IS NULL
                   OR pg_catalog.obj_description(tenant_policy.oid, 'pg_policy') IS DISTINCT FROM
                      expected.tenant_fingerprint || pg_catalog.md5(tenant_policy.polqual::text)
                          || ':' || pg_catalog.md5(tenant_policy.polwithcheck::text)
                   OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy other
                       WHERE other.polrelid = relation.oid AND other.polname NOT IN ('{PolicyName}', 'tenant_isolation')
                         AND other.polpermissive
                         AND EXISTS (SELECT 1 FROM unnest(other.polroles) role_id
                             WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}', role_id, 'USAGE'))))
            )
            """;
    }

    private static string BuildSql(ExploreDbContext context)
    {
        var relations = Relations(context);
        string D(string property) => Column(context, typeof(LegalDocument), property);
        string V(string property) => Column(context, typeof(LegalDocumentVersion), property);
        string L(string property) => Column(context, typeof(LegalDocumentLocalizedSource), property);
        string documents = Table(context, typeof(LegalDocument));
        string versions = Table(context, typeof(LegalDocumentVersion));
        string locales = Table(context, typeof(LegalDocumentLocalizedSource));
        string function = FunctionSql(context);
        string signature = function + "(integer, uuid)";
        string schema = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(Schema(context, typeof(LegalDocument)));
        string schemas = string.Join(", ", relations.Select(relation => Schema(context, relation.Type))
            .Distinct(StringComparer.Ordinal).Select(context.GetService<ISqlGenerationHelper>().DelimitIdentifier));
        string projection = $"""
            d.{D("Id")}, d.{D("Kind")}::integer, v.{V("Audience")}::integer,
            v.{V("RequiresFreshAcceptance")}, v.{V("TemplateId")}::text, v.{V("TemplateVersion")}::text,
            v.{V("TemplateSourceKind")}::integer, v.{V("TemplateLicenseExpression")}::text,
            v.{V("TemplateReviewReference")}::text, l.{L("LanguageTag")}::text,
            l.{L("Title")}::text, l.{L("Summary")}::text, l.{L("Markdown")}::text
            """;
        string target = $"""
            d.{D("Scope")} = p_scope AND d.{D("TenantId")} IS NOT DISTINCT FROM p_tenant
            AND d.{D("AuthorityKey")} = CASE WHEN p_scope = {(int)LegalDocumentScope.Instance}
                THEN 'instance' ELSE 'tenant:' || replace(p_tenant::text, '-', '') END
            AND d.{D("Kind")} = ANY (CASE WHEN p_scope = {(int)LegalDocumentScope.Instance}
                THEN {Kinds(LegalDocumentScope.Instance)} ELSE {Kinds(LegalDocumentScope.Tenant)} END)
            """;
        string join = $"""
            FROM {documents} d
            LEFT JOIN {versions} v ON v.{V("LegalDocumentId")} = d.{D("Id")} AND v.{V("Version")} = d.{D("CurrentVersion")}
            LEFT JOIN {locales} l ON l.{L("LegalDocumentVersionId")} = v.{V("Id")}
            WHERE {target}
            """;
        string grants = string.Join("\n", relations.Select(relation =>
        {
            var entity = context.Model.FindEntityType(relation.Type)!;
            string all = string.Join(", ", entity.GetProperties().Select(property => Column(context, relation.Type, property.Name)).Distinct());
            string selected = string.Join(", ", relation.Columns.Select(property => Column(context, relation.Type, property)));
            return $"""
                REVOKE ALL ON TABLE {Table(context, relation.Type)} FROM {OwnerRole}, {GlobalRole};
                REVOKE ALL ({all}) ON TABLE {Table(context, relation.Type)} FROM {OwnerRole}, {GlobalRole};
                GRANT SELECT ({selected}) ON TABLE {Table(context, relation.Type)} TO {OwnerRole};
                DROP POLICY IF EXISTS "{PolicyName}" ON {Table(context, relation.Type)};
                CREATE POLICY "{PolicyName}" ON {Table(context, relation.Type)}
                    FOR SELECT TO {OwnerRole} USING ({relation.Predicate});
                DO $stamp$
                BEGIN
                    EXECUTE (SELECT pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L', policy.polname,
                        {Text(Table(context, relation.Type))},
                        {Text(PolicyFingerprint(context, relation.Type, relation.Predicate))} || pg_catalog.md5(policy.polqual::text))
                        FROM pg_catalog.pg_policy policy
                        WHERE policy.polrelid = {Text(Table(context, relation.Type))}::regclass AND policy.polname = '{PolicyName}');
                END; $stamp$;
                """;
        }));
        string prerequisites = string.Join(" OR ", relations.Select(relation => $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                WHERE relation.oid = {Text(Table(context, relation.Type))}::regclass
                  AND relation.relrowsecurity AND relation.relforcerowsecurity)
            """));
        int maximumDocuments = LegalMarkdownContentLimits.MaximumDocumentsPerScope;
        int maximumLocales = LegalMarkdownContentLimits.MaximumLocalesPerDocument;
        return $"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                IF {prerequisites} THEN
                    RAISE EXCEPTION 'configuration_import_legal_source_tenant_rls_required' USING ERRCODE = '55000';
                END IF;
                FOREACH role_name IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                        EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                    END IF;
                END LOOP;
                IF NOT ({RoleSafetySql(context, "CURRENT_USER")})
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                        JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                        JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                        WHERE parent.rolname = '{MigratorRole}' AND member.rolname <> CURRENT_USER)
                    OR EXISTS (
                        WITH RECURSIVE memberships(roleid) AS (
                            SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                            WHERE edge.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = CURRENT_USER)
                            UNION
                            SELECT edge.roleid FROM pg_catalog.pg_auth_members edge
                            JOIN memberships parent ON parent.roleid = edge.member)
                        SELECT 1 FROM memberships
                        JOIN pg_catalog.pg_roles role ON role.oid = memberships.roleid
                        WHERE role.rolname = '{GlobalRole}') THEN
                    RAISE EXCEPTION 'configuration_import_legal_source_authority_not_ready' USING ERRCODE = '55000';
                END IF;
            END; $roles$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE ON SCHEMA {schemas} TO {OwnerRole};
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            {grants}
            DO $install$
            DECLARE previous_role text := current_setting('role');
                migrator_login text := CURRENT_USER;
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE pg_catalog.replace($ddl$
                CREATE OR REPLACE FUNCTION {function}(p_scope integer, p_tenant uuid)
                RETURNS TABLE (document_id uuid, kind integer, audience integer, requires_fresh_acceptance boolean,
                    template_id text, template_version text, template_source_kind integer,
                    template_license_expression text, template_review_reference text,
                    language_tag text, title text, summary text, markdown text)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $body$
                BEGIN
                    IF p_scope IS NULL OR p_scope NOT IN ({(int)LegalDocumentScope.Instance}, {(int)LegalDocumentScope.Tenant})
                        OR p_scope = {(int)LegalDocumentScope.Instance} AND p_tenant IS NOT NULL
                        OR p_scope = {(int)LegalDocumentScope.Tenant} AND (p_tenant IS NULL
                            OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid) THEN
                        RAISE EXCEPTION 'configuration_import_legal_source_target_invalid' USING ERRCODE = '22023';
                    END IF;
                    IF CURRENT_USER <> '{OwnerRole}' OR NOT ({AuthorityReadySql(context)}) THEN
                        RAISE EXCEPTION 'configuration_import_legal_source_authority_not_ready' USING ERRCODE = '55000';
                    END IF;
                    IF (SELECT count(*) FROM (SELECT 1 FROM {documents} d WHERE {target} LIMIT {maximumDocuments + 1}) bounded) > {maximumDocuments}
                        OR EXISTS (SELECT d.{D("Kind")} FROM {documents} d WHERE {target}
                            GROUP BY d.{D("Kind")} HAVING count(*) > 1)
                        OR EXISTS (SELECT 1 {join} AND (
                            v.{V("Id")} IS NULL OR l.{L("LanguageTag")} IS NULL
                            OR length(l.{L("LanguageTag")}) NOT BETWEEN 1 AND {LegalMarkdownContentLimits.MaximumLanguageTagLength}
                            OR length(l.{L("Title")}) NOT BETWEEN 1 AND {LegalMarkdownContentLimits.MaximumTitleLength}
                            OR length(l.{L("Summary")}) NOT BETWEEN 1 AND {LegalMarkdownContentLimits.MaximumSummaryLength}
                            OR octet_length(l.{L("Markdown")}) NOT BETWEEN 1 AND {LegalMarkdownContentLimits.MaximumMarkdownUtf8BytesPerLocale}))
                        OR EXISTS (SELECT d.{D("Id")} {join}
                            GROUP BY d.{D("Id")} HAVING count(*) > {maximumLocales}) THEN
                        RAISE EXCEPTION 'configuration_import_legal_source_invalid' USING ERRCODE = '55000';
                    END IF;
                    RETURN QUERY SELECT {projection} {join}
                        ORDER BY d.{D("Kind")}, d.{D("Id")}, l.{L("LanguageTag")} COLLATE "C"
                        LIMIT {maximumDocuments * maximumLocales + 1};
                END; $body$;
                $ddl$, '__LEGAL_SOURCE_MIGRATOR_LOGIN__', pg_catalog.quote_literal(migrator_login));
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {signature} FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {signature} TO {GlobalRole};
                $acl$;
                EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L', {Text(signature)},
                    'event-configuration-import-legal-source-function:v1:' || pg_catalog.md5(function.prosrc))
                    FROM pg_catalog.pg_proc function
                    WHERE function.oid = {Text(signature)}::regprocedure);
                PERFORM set_config('role', previous_role, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $closed$
            BEGIN
                IF NOT ({StructureReadySql(context, "CURRENT_USER")}) THEN
                    RAISE EXCEPTION 'configuration_import_legal_source_authority_not_ready' USING ERRCODE = '55000';
                END IF;
            END; $closed$;
            """;
    }
}
