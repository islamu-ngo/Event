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

public static partial class PostgresConfigurationImportLegalMutationContract
{
    private const string PolicyName = "configuration_import_legal_mutation";
    private const string BoundaryPolicyName = "configuration_import_legal_mutation_boundary";
    private const string FingerprintPrefix = "event-configuration-import-legal-mutation:v1:";
    private const string MigratorPlaceholder = "__LEGAL_MUTATION_MIGRATOR_LOGIN__";

    private static readonly string[] RootColumns =
    [
        "Id", "Scope", "TenantId", "AuthorityKey", "Kind", "OwnerRole", "State", "CurrentVersion",
        "AccountableIdentityReference", "ConcurrencyStamp", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"
    ];
    private static readonly string[] RootInserts =
    [
        "Id", "Scope", "TenantId", "AuthorityKey", "Kind", "OwnerRole", "State", "CurrentVersion",
        "ConcurrencyStamp", "CreatedAt", "CreatedBy"
    ];
    private static readonly string[] RootUpdates =
        ["State", "CurrentVersion", "AccountableIdentityReference", "ConcurrencyStamp", "UpdatedAt", "UpdatedBy"];
    private static readonly string[] VersionColumns =
    [
        "Id", "LegalDocumentId", "Version", "Audience", "State", "ContentDigest", "SourceOrigin",
        "RequiresFreshAcceptance", "TemplateId", "TemplateVersion", "TemplateSourceKind",
        "TemplateLicenseExpression", "TemplateReviewReference", "ReviewerId", "ReviewEvidenceReference",
        "AccountableIdentityReference", "ApprovedAt", "ProposedEffectiveAt", "PublishedAt", "RetiredAt", "CreatedAt"
    ];
    private static readonly string[] VersionInserts =
    [
        "Id", "LegalDocumentId", "Version", "Audience", "State", "ContentDigest", "SourceOrigin",
        "RequiresFreshAcceptance", "TemplateId", "TemplateVersion", "TemplateSourceKind",
        "TemplateLicenseExpression", "TemplateReviewReference", "CreatedAt"
    ];
    private static readonly string[] SourceColumns =
    [
        "Id", "LegalDocumentVersionId", "LanguageTag", "Title", "Summary", "Markdown",
        "Utf8ByteCount", "LinkCount", "PlaceholderCount"
    ];
    private static readonly string[] PublicationColumns =
    [
        "Id", "LegalDocumentId", "LegalDocumentVersionId", "Version", "LifecycleState", "ContentDigest",
        "AccountableIdentityReference", "ReviewEvidenceReference", "EffectiveAt", "OccurredAt", "RequiresFreshAcceptance"
    ];

    /// <summary>
    /// Administrative installation only. Ordinary forced legal RLS must already be
    /// installed. Enrollment of a concrete Global login is the caller's responsibility.
    /// The installer uses literal DbCommand text, including JSON and regex braces.
    /// </summary>
    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql())
            return;
        context.ValidateTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("Legal mutation installation requires administrative migration authority.");
        if (context.Database.CurrentTransaction is not null)
        {
            await InstallAsync(context, ct);
            return;
        }
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(ct);
        await InstallAsync(context, ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task InstallAsync(ExploreDbContext context, CancellationToken ct)
    {
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = BuildSql(context);
        if (context.Database.GetCommandTimeout() is { } timeout)
            command.CommandTimeout = timeout;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Schema(ExploreDbContext context, Type type) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(type)!.GetSchema() ?? "public";

    private static string FunctionSql(ExploreDbContext context, string operation) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            "configuration_import_legal_mutation_" + operation, Schema(context, typeof(LegalDocument)));

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
        string roles = $"'{OwnerRole}', '{MigratorRole}', '{GlobalRole}'";
        const string elevated = "rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication OR rolinherit";
        var helper = context.GetService<ISqlGenerationHelper>();
        string schema = helper.DelimitIdentifier(Schema(context, typeof(LegalDocument)));
        (Type Type, string[] Read, string[] Insert, string[] Update)[] relations =
        [
            (typeof(LegalDocument), RootColumns, RootInserts, RootUpdates),
            (typeof(LegalDocumentVersion), VersionColumns, VersionInserts, []),
            (typeof(LegalDocumentLocalizedSource), SourceColumns, SourceColumns, []),
            (typeof(LegalDocumentPublication), PublicationColumns, [], [])
        ];
        // A newly mapped scalar is a contract change, not an invitation to grant it
        // implicitly or to return an aggregate missing that field.
        foreach (var relation in relations)
        {
            if (!context.Model.FindEntityType(relation.Type)!.GetProperties().Select(property => property.Name)
                    .Order(StringComparer.Ordinal).SequenceEqual(relation.Read.Order(StringComparer.Ordinal)))
                throw new InvalidOperationException("configuration_import_legal_mapping_contract_changed");
        }
        string schemas = string.Join(", ", relations.Select(relation => Schema(context, relation.Type))
            .Distinct(StringComparer.Ordinal).Select(helper.DelimitIdentifier));
        string expectedColumns = string.Join(",\n", relations.SelectMany(relation =>
            new[] { ("SELECT", relation.Read), ("INSERT", relation.Insert), ("UPDATE", relation.Update) }
                .SelectMany(access => access.Item2.Select(property =>
                    $"({Text(Table(context, relation.Type))}::regclass, " +
                    $"{Text(ColumnName(context, relation.Type, property))}, {Text(access.Item1)})"))));
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
        // Use actual membership edges: a fixture DBA's implicit superuser
        // pg_has_role(..., MEMBER) result is not Global enrollment.
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
            OR NOT pg_catalog.has_schema_privilege('{GlobalRole}', {Text(Schema(context, typeof(LegalDocument)))}, 'USAGE')
            OR {string.Join(" OR ", relations.Select(relation => Schema(context, relation.Type)).Distinct(StringComparer.Ordinal)
                .Select(name => $"NOT pg_catalog.has_schema_privilege('{OwnerRole}', {Text(name)}, 'USAGE')"))}
            """;
        var policies = relations.Select(relation =>
        {
            const string sourceOriginPattern = "^sha256:[0-9a-f]{64}$";
            Type type = relation.Type;
            string table = Table(context, type);
            string documents = Table(context, typeof(LegalDocument));
            string versions = Table(context, typeof(LegalDocumentVersion));
            string D(string name) => Column(context, typeof(LegalDocument), name);
            string V(string name) => Column(context, typeof(LegalDocumentVersion), name);
            string predicate;
            string check;
            if (type == typeof(LegalDocument))
            {
                predicate = TargetPredicate(context, "");
                check = $"({predicate}) AND {D("State")} = 2 AND {D("AccountableIdentityReference")} IS NULL";
            }
            else if (type == typeof(LegalDocumentVersion))
            {
                string ancestry = $"""
                    SELECT 1 FROM {documents} d WHERE d.{D("Id")} = {table}.{V("LegalDocumentId")}
                        AND {TargetPredicate(context, "d.")}
                    """;
                predicate = $"EXISTS ({ancestry})";
                check = $"""
                    {V("State")} = 2 AND {V("SourceOrigin")} ~ {Text(sourceOriginPattern)}
                    AND EXISTS ({ancestry} AND d.{D("CurrentVersion")} = {table}.{V("Version")} AND d.{D("State")} = 2)
                    """;
            }
            else if (type == typeof(LegalDocumentLocalizedSource))
            {
                string ancestry = $"""
                    SELECT 1 FROM {versions} v JOIN {documents} d ON d.{D("Id")} = v.{V("LegalDocumentId")}
                    WHERE v.{V("Id")} = {table}.{Column(context, type, "LegalDocumentVersionId")}
                        AND {TargetPredicate(context, "d.")}
                    """;
                predicate = $"EXISTS ({ancestry})";
                check = $"""
                    EXISTS ({ancestry} AND d.{D("CurrentVersion")} = v.{V("Version")}
                        AND d.{D("State")} = 2 AND v.{V("State")} = 2
                        AND v.{V("SourceOrigin")} ~ {Text(sourceOriginPattern)})
                    """;
            }
            else
            {
                predicate = $"""
                    EXISTS (SELECT 1 FROM {documents} d
                        WHERE d.{D("Id")} = {table}.{Column(context, type, "LegalDocumentId")}
                            AND {TargetPredicate(context, "d.")})
                    """;
                check = "false";
            }
            var tenantTable = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
                .SingleOrDefault(candidate => candidate.TableName == context.Model.FindEntityType(type)!.GetTableName())
                ?? throw new InvalidOperationException("configuration_import_legal_tenant_rls_model_required");
            string tenantFingerprint = PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(
                tenantTable, Schema(context, type));
            return (Table: table, Predicate: predicate, Check: check, TenantFingerprint: tenantFingerprint,
                Fingerprint: FingerprintPrefix + Hash(table + ":" + predicate + ":" + check) + ":");
        }).ToArray();
        string tenantRlsInvalid = string.Join(" OR ", policies.Select(policy => $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_policy p ON p.polrelid = relation.oid AND p.polname = 'tenant_isolation'
                WHERE relation.oid = {Text(policy.Table)}::regclass AND relation.relrowsecurity AND relation.relforcerowsecurity
                    AND p.polpermissive AND p.polcmd = '*' AND p.polroles = ARRAY[0::oid]
                    AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                    AND pg_catalog.obj_description(p.oid, 'pg_policy') =
                        {Text(policy.TenantFingerprint)} || md5(p.polqual::text) || ':' || md5(p.polwithcheck::text))
            """));
        string policyInvalid = string.Join(" OR ", policies.Select(policy => $"""
            {string.Join(" OR ", new[] { (PolicyName, true), (BoundaryPolicyName, false) }.Select(spec => $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(policy.Table)}::regclass AND p.polname = '{spec.Item1}'
                        AND p.polpermissive = {spec.Item2.ToString().ToLowerInvariant()} AND p.polcmd = '*'
                        AND p.polroles = ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')]
                        AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                        AND pg_catalog.obj_description(p.oid, 'pg_policy') =
                            {Text(policy.Fingerprint)} || md5(p.polqual::text) || ':' || md5(p.polwithcheck::text))
                """))}
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy other
                WHERE other.polrelid = {Text(policy.Table)}::regclass
                    AND other.polname NOT IN ('{PolicyName}', '{BoundaryPolicyName}', 'tenant_isolation')
                    AND EXISTS (SELECT 1 FROM unnest(other.polroles) role_id
                        WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}', role_id, 'USAGE')))
            """));
        (string Name, string Signature, string Arguments, string Returns, string Body, bool Public)[] functions =
        [
            ("set_target", "integer,uuid,integer", "p_scope integer, p_tenant uuid, p_kind integer",
                "void", TargetBody(context), false),
            ("read_root", "integer,uuid,integer", "p_scope integer, p_tenant uuid, p_kind integer",
                "SETOF " + Table(context, typeof(LegalDocument)), ReadBody(context, typeof(LegalDocument)), true),
            ("read_versions", "integer,uuid,integer", "p_scope integer, p_tenant uuid, p_kind integer",
                "SETOF " + Table(context, typeof(LegalDocumentVersion)), ReadBody(context, typeof(LegalDocumentVersion)), true),
            ("read_sources", "integer,uuid,integer", "p_scope integer, p_tenant uuid, p_kind integer",
                "SETOF " + Table(context, typeof(LegalDocumentLocalizedSource)), ReadBody(context, typeof(LegalDocumentLocalizedSource)), true),
            ("read_publications", "integer,uuid,integer", "p_scope integer, p_tenant uuid, p_kind integer",
                "SETOF " + Table(context, typeof(LegalDocumentPublication)), ReadBody(context, typeof(LegalDocumentPublication)), true),
            ("write", "integer,uuid,integer,jsonb,jsonb,jsonb,uuid,integer,integer",
                "p_scope integer, p_tenant uuid, p_kind integer, p_document jsonb, p_version jsonb, p_sources jsonb, " +
                "p_expected_stamp uuid, p_expected_version integer, p_expected_state integer",
                "void", WriteBody(context), true)
        ];
        string authoritySignature = FunctionSql(context, "assert_authority") + "()";
        string functionIds = string.Join(", ", functions.Select(function =>
                $"pg_catalog.to_regprocedure({Text(FunctionSql(context, function.Name) + "(" + function.Signature + ")")})")
            .Append($"pg_catalog.to_regprocedure({Text(authoritySignature)})"));
        string existingFunctionInvalid = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE function.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                    AND (function.proowner <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                        OR NOT COALESCE(function.oid = ANY (ARRAY[{functionIds}]::oid[]), false)))
            """;
        var functionContracts = functions.Select(function =>
            (Signature: FunctionSql(context, function.Name) + "(" + function.Signature + ")",
                function.Public, Set: function.Returns.StartsWith("SETOF ", StringComparison.Ordinal),
                Type: function.Returns.Replace("SETOF ", "", StringComparison.Ordinal)))
            .Append((Signature: authoritySignature, Public: false, Set: false, Type: "void")).ToArray();
        string functionInvalid = string.Join(" OR ", functionContracts.Select(contract => $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE function.oid = pg_catalog.to_regprocedure({Text(contract.Signature)})
                    AND function.proowner = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                    AND function.prosecdef AND function.provolatile = 'v' AND function.proparallel = 'u'
                    AND NOT function.proleakproof AND NOT function.proisstrict AND function.prokind = 'f'
                    AND function.proargdefaults IS NULL AND function.proretset = {contract.Set.ToString().ToLowerInvariant()}
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
            OR EXISTS (SELECT 1 FROM (VALUES {string.Join(", ", functions.Select(function =>
                $"(pg_catalog.to_regprocedure({Text(FunctionSql(context, function.Name) + "(" + function.Signature + ")")}), {Text(Hash(function.Body))})"))})
                    expected(function_id, hash)
                JOIN pg_catalog.pg_proc function ON function.oid = expected.function_id
                WHERE encode(sha256(convert_to(function.prosrc, 'UTF8')), 'hex') <> expected.hash)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE NOT COALESCE(function.oid = ANY (ARRAY[{functionIds}]::oid[]), false)
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
            OR {membershipInvalid} OR {membershipMissing} OR {ownershipInvalid} OR {privilegeInvalid}
            OR {functionInvalid} OR {policyInvalid} OR {tenantRlsInvalid}
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
                            AND (role.rolsuper OR role.rolbypassrls OR role.rolcreatedb OR role.rolcreaterole OR role.rolreplication))
                    OR {structureInvalid} THEN
                    RAISE EXCEPTION 'configuration_import_legal_mutation_authority_not_ready' USING ERRCODE = '42501';
                END IF;
                RETURN;
            END;
            """;
        string grants = string.Join("\n", relations.Select(relation =>
        {
            string Grant(string privilege, string[] properties) => properties.Length == 0 ? "" :
                $"GRANT {privilege} ({string.Join(", ", properties.Select(property => Column(context, relation.Type, property)))}) " +
                $"ON TABLE {Table(context, relation.Type)} TO {OwnerRole};";
            return Grant("SELECT", relation.Read) + "\n" + Grant("INSERT", relation.Insert) + "\n" + Grant("UPDATE", relation.Update);
        }));
        string policyDdl = string.Join("\n", policies.SelectMany(policy =>
            new[] { (PolicyName, "PERMISSIVE"), (BoundaryPolicyName, "RESTRICTIVE") }.Select(spec => $"""
                DROP POLICY IF EXISTS "{spec.Item1}" ON {policy.Table};
                CREATE POLICY "{spec.Item1}" ON {policy.Table} AS {spec.Item2} FOR ALL TO {OwnerRole}
                    USING ({policy.Predicate}) WITH CHECK ({policy.Check});
                DO $stamp_policy$
                BEGIN
                    EXECUTE (SELECT pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',
                        p.polname, {Text(policy.Table)}, {Text(policy.Fingerprint)} ||
                            md5(p.polqual::text) || ':' || md5(p.polwithcheck::text))
                        FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(policy.Table)}::regclass
                            AND p.polname = '{spec.Item1}');
                END; $stamp_policy$;
                """)));
        string FunctionDdl(string name, string arguments, string returns, string body) => $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, name)}({arguments}) RETURNS {returns}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${body}$body$;
            """;
        string ddl = FunctionDdl("assert_authority", "", "void", authorityBody) + "\n" +
            string.Join("\n", functions.Select(function =>
                FunctionDdl(function.Name, function.Arguments, function.Returns, function.Body)));
        string acl = string.Join("\n", functionContracts.Select(contract => $"""
            REVOKE ALL ON FUNCTION {contract.Signature} FROM PUBLIC, {GlobalRole};
            {(contract.Public ? $"GRANT EXECUTE ON FUNCTION {contract.Signature} TO {GlobalRole};" : "")}
            DO $stamp_function$
            BEGIN
                EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L', function.oid::regprocedure,
                    '{FingerprintPrefix}' || encode(sha256(convert_to(function.prosrc, 'UTF8')), 'hex'))
                    FROM pg_catalog.pg_proc function WHERE function.oid = {Text(contract.Signature)}::regprocedure);
            END; $stamp_function$;
            """));
        return $"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                IF CURRENT_USER <> SESSION_USER OR CURRENT_USER IN ({roles}) OR {migrationInGlobal} THEN
                    RAISE EXCEPTION 'configuration_import_legal_mutation_migration_scope' USING ERRCODE = '42501';
                END IF;
                FOREACH role_name IN ARRAY ARRAY[{roles}] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name) THEN
                        EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                        EXECUTE pg_catalog.format('COMMENT ON ROLE %I IS %L', role_name, '{FingerprintPrefix}role:' || role_name);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = role_name
                        AND (({elevated}) OR pg_catalog.shobj_description(oid, 'pg_authid') IS DISTINCT FROM
                            '{FingerprintPrefix}role:' || role_name)) THEN
                        RAISE EXCEPTION 'configuration_import_legal_mutation_role_invalid' USING ERRCODE = '42501';
                    END IF;
                END LOOP;
                IF {membershipInvalid.Replace(MigratorPlaceholder, "CURRENT_USER", StringComparison.Ordinal)}
                    OR {ownershipInvalid} OR {existingFunctionInvalid} OR {tenantRlsInvalid} THEN
                    RAISE EXCEPTION 'configuration_import_legal_mutation_migration_authority_invalid' USING ERRCODE = '42501';
                END IF;
            END; $roles$;
            GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE;
            GRANT {MigratorRole} TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;
            GRANT USAGE ON SCHEMA {schemas} TO {OwnerRole};
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            {grants}
            {policyDdl}
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
                    RAISE EXCEPTION 'configuration_import_legal_mutation_authority_not_ready' USING ERRCODE = '42501';
                END IF;
            END; $closed$;
            """;
    }
}
