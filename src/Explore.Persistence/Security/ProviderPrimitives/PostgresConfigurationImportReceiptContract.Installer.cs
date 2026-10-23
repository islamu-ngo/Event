using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportReceiptContract
{
    private const string FingerprintPrefix = "event-configuration-import-receipt:v1:";
    private const string MigratorPlaceholder = "__IMPORT_RECEIPT_MIGRATOR_LOGIN__";
    private const string PolicyName = "configuration_import_receipt";
    private const string BoundaryPolicyName = "configuration_import_receipt_boundary";

    private static readonly string[] OperationColumns =
    [
        "Id", "SessionId", "Kind", "Status", "TargetAuthorityKey", "TargetTenantId", "ActorUserId",
        "SourceOperationId", "ArtifactDigest", "TargetRevisionDigest", "SelectedSectionsDigest",
        "MappingDigest", "ApprovalDigest", "ApplyMode", "SnapshotArtifactHandleId", "SnapshotDigest",
        "SnapshotExpiresAt", "EffectOutboxId", "EffectPayloadDigest", "FidelityVerified", "FidelityDigest", "FailureCode",
        "FailureReason", "StartedAt", "CompletedAt", "_selectedSectionKeys", "_omittedSectionKeys"
    ];
    private static readonly string[] EffectColumns =
    [
        "Id", "AggregateType", "AggregateId", "EventType", "Payload", "Status", "CreatedAt",
        "ProcessedAt", "RetryCount", "LastError", "NextRetryAt", "MaxRetries", "DeadLetteredAt"
    ];
    private static readonly string[] SessionReads =
    [
        "SessionId", "TargetScope", "TargetTenantId", "TargetAuthorityKey", "ArtifactHandleId",
        "ArtifactDigest", "ArtifactByteLength", "ArtifactExpiresAt", "State", "CreatedAt",
        "UpdatedAt", "ExpiresAt", "CancelledAt", "ConsumedAt", "PreviewArtifactDigest",
        "PreviewTargetRevisionDigest", "PreviewSelectedSectionsDigest", "PreviewMappingDigest",
        "PreviewRequiredApprovalDigest", "PreviewApplyMode", "PreviewExpiresAt"
    ];
    private static readonly string[] ArtifactReads =
        ["Id", "Sha256Digest", "ByteLength", "CreatedAt", "ExpiresAt"];

    /// <summary>
    /// Administrative installation only. Does not enroll a Runtime or Global login,
    /// create tables, or enable tenant RLS on instance-only relations.
    /// </summary>
    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql())
            return;
        context.ValidateTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("Receipt installation requires administrative migration authority.");
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
            "configuration_import_receipt_" + operation, Schema(context, typeof(ConfigurationImportOperation)));

    private static string Table(ExploreDbContext context, Type type) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(type)!.GetTableName()!, Schema(context, type));

    private static string ColumnName(ExploreDbContext context, Type type, string property)
    {
        if (property == "xmin")
            return property;
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
        var helper = context.GetService<ISqlGenerationHelper>();
        string schema = helper.DelimitIdentifier(Schema(context, typeof(ConfigurationImportOperation)));
        // xmin is read-only transaction provenance, not application data or a table-wide grant.
        (Type Type, string[] Read, string[] Insert)[] relations =
        [
            (typeof(ConfigurationImportOperation), [.. OperationColumns, "xmin"], OperationColumns),
            (typeof(OutboxMessage), EffectColumns, EffectColumns),
            (typeof(ConfigurationImportSession), [.. SessionReads, "xmin"], []),
            (typeof(ConfigurationImportStoredArtifact), [.. ArtifactReads, "xmin"], [])
        ];
        foreach (var (type, properties) in new[]
                 {
                     (typeof(ConfigurationImportOperation), OperationColumns),
                     (typeof(OutboxMessage), EffectColumns)
                 })
        {
            if (!context.Model.FindEntityType(type)!.GetProperties().Select(property => property.Name)
                    .Order(StringComparer.Ordinal).SequenceEqual(properties.Order(StringComparer.Ordinal)))
                throw new InvalidOperationException("configuration_import_receipt_mapping_contract_changed");
        }
        string schemas = string.Join(", ", relations.Select(relation => Schema(context, relation.Type))
            .Distinct(StringComparer.Ordinal).Select(helper.DelimitIdentifier));
        string expectedColumns = string.Join(",\n", relations.SelectMany(relation =>
            new[] { ("SELECT", relation.Read), ("INSERT", relation.Insert) }
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
        string membershipMissing = installation.MissingMemberships();
        string ownershipInvalid = installation.NonRoutineOwnership();
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
                WHERE attribute.attnum <> 0 AND NOT attribute.attisdropped
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
            OR {installation.ExtraneousGrants()}
            OR NOT pg_catalog.has_schema_privilege('{GlobalRole}', {Text(Schema(context, typeof(ConfigurationImportOperation)))}, 'USAGE')
            OR {string.Join(" OR ", relations.Select(relation => Schema(context, relation.Type)).Distinct(StringComparer.Ordinal)
                .Select(name => $"NOT pg_catalog.has_schema_privilege('{OwnerRole}', {Text(name)}, 'USAGE')"))}
            """;
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        var policies = relations.Select(relation =>
        {
            Type type = relation.Type;
            string table = Table(context, type);
            var tenant = tenantTables.SingleOrDefault(candidate =>
                candidate.TableName == context.Model.FindEntityType(type)!.GetTableName());
            string predicate = PolicyPredicate(context, type);
            return (Table: table, Predicate: predicate, Check: relation.Insert.Length == 0 ? "false" : predicate,
                TenantFingerprint: tenant is null ? null :
                    PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, Schema(context, type)),
                Fingerprint: FingerprintPrefix + Hash(table + ":" + predicate) + ":");
        }).ToArray();
        string baseRlsInvalid = string.Join(" OR ", policies.Select(policy =>
            policy.TenantFingerprint is null
                ? $"""
                    EXISTS (SELECT 1 FROM pg_catalog.pg_class relation WHERE relation.oid = {Text(policy.Table)}::regclass
                        AND (relation.relrowsecurity OR relation.relforcerowsecurity))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                        WHERE p.polrelid = {Text(policy.Table)}::regclass
                            AND EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                                WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}', role_id, 'USAGE')))
                    """
                : $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                        JOIN pg_catalog.pg_policy p ON p.polrelid = relation.oid AND p.polname = 'tenant_isolation'
                        WHERE relation.oid = {Text(policy.Table)}::regclass AND relation.relrowsecurity AND relation.relforcerowsecurity
                            AND p.polpermissive AND p.polcmd = '*' AND p.polroles = ARRAY[0::oid]
                            AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                            AND pg_catalog.obj_description(p.oid, 'pg_policy') =
                                {Text(policy.TenantFingerprint)} || md5(p.polqual::text) || ':' || md5(p.polwithcheck::text))
                    """));
        string policyInvalid = string.Join(" OR ", policies.Where(policy => policy.TenantFingerprint is not null)
            .Select(policy => $"""
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
                """).Prepend("false"));
        (string Name, string Signature, string Arguments, string Returns, string Body)[] functions =
        [
            ("store_operation", "jsonb", "p_value jsonb", "void", StoreOperationBody(context)),
            ("store_effect", "jsonb", "p_value jsonb", "void", StoreEffectBody(context)),
            ("read_effect", "uuid", "p_id uuid",
                "SETOF " + Table(context, typeof(OutboxMessage)), ReadEffectBody(context)),
            ("read_operation", "uuid,text", "p_id uuid, p_authority text",
                "SETOF " + Table(context, typeof(ConfigurationImportOperation)), ReadOperationBody(context, list: false)),
            ("list_operations", "text,integer", "p_authority text, p_maximum integer",
                "SETOF " + Table(context, typeof(ConfigurationImportOperation)), ReadOperationBody(context, list: true))
        ];
        string authoritySignature = FunctionSql(context, "assert_authority") + "()";
        var contracts = functions.Select(function => (
                Signature: FunctionSql(context, function.Name) + "(" + function.Signature + ")",
                Public: true, Set: function.Returns.StartsWith("SETOF ", StringComparison.Ordinal),
                Type: function.Returns.Replace("SETOF ", "", StringComparison.Ordinal)))
            .Append((Signature: authoritySignature, Public: false, Set: false, Type: "void")).ToArray();
        string functionIds = string.Join(", ", contracts.Select(contract =>
            $"pg_catalog.to_regprocedure({Text(contract.Signature)})"));
        string unexpectedFunctions = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_proc function
                WHERE NOT COALESCE(function.oid = ANY (ARRAY[{functionIds}]::oid[]), false)
                    AND (function.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(
                            COALESCE(function.proacl, pg_catalog.acldefault('f', function.proowner))) permission
                            WHERE permission.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))))
            """;
        string functionInvalid = string.Join(" OR ", contracts.Select(contract => $"""
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
            OR {unexpectedFunctions}
            """;
        string structureInvalid = $"""
            (SELECT count(*) FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) <> 3
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ({roles})
                AND (({elevated}) OR pg_catalog.shobj_description(oid, 'pg_authid') IS DISTINCT FROM
                    '{FingerprintPrefix}role:' || rolname))
            OR {membershipInvalid} OR {membershipMissing} OR {ownershipInvalid} OR {privilegeInvalid}
            OR {functionInvalid} OR {policyInvalid} OR {baseRlsInvalid}
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
                    RAISE EXCEPTION 'configuration_import_receipt_authority_not_ready' USING ERRCODE = '42501';
                END IF;
                RETURN;
            END;
            """;
        string grants = string.Join("\n", relations.Select(relation =>
        {
            string Grant(string privilege, string[] properties) => properties.Length == 0 ? "" :
                $"GRANT {privilege} ({string.Join(", ", properties.Select(property => Column(context, relation.Type, property)))}) " +
                $"ON TABLE {Table(context, relation.Type)} TO {OwnerRole};";
            return Grant("SELECT", relation.Read) + "\n" + Grant("INSERT", relation.Insert);
        }));
        string policyDdl = string.Join("\n", policies.Where(policy => policy.TenantFingerprint is not null)
            .SelectMany(policy => new[] { (PolicyName, "PERMISSIVE"), (BoundaryPolicyName, "RESTRICTIVE") }.Select(spec => $"""
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
        string acl = string.Join("\n", contracts.Select(contract => $"""
            REVOKE ALL ON FUNCTION {contract.Signature} FROM PUBLIC, {GlobalRole};
            {(contract.Public ? $"GRANT EXECUTE ON FUNCTION {contract.Signature} TO {GlobalRole};" : "")}
            DO $stamp_function$
            BEGIN
                EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L', function.oid::regprocedure,
                    '{FingerprintPrefix}' || encode(sha256(convert_to(function.prosrc, 'UTF8')), 'hex'))
                    FROM pg_catalog.pg_proc function WHERE function.oid = {Text(contract.Signature)}::regprocedure);
            END; $stamp_function$;
            """));
        return installation.Build(schema,
            schemaGrants: $"GRANT USAGE ON SCHEMA {schemas} TO {OwnerRole};",
            grants, policies: policyDdl, ddl, acl,
            installationInvalid: $"""
                {membershipInvalid.Replace(MigratorPlaceholder, "CURRENT_USER", StringComparison.Ordinal)}
                    OR {ownershipInvalid} OR {unexpectedFunctions} OR {baseRlsInvalid}
                """,
            closedInvalid: structureInvalid.Replace(MigratorPlaceholder, "CURRENT_USER", StringComparison.Ordinal),
            diagnosticPrefix: "configuration_import_receipt");
    }
}
