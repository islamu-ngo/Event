using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportStateContract
{
    private static readonly (string Property, string Type)[] SessionColumns =
    [
        ("SessionId", "uuid"), ("TargetScope", "integer"), ("TargetTenantId", "uuid"),
        ("TargetAuthorityKey", "text"), ("ArtifactHandleId", "uuid"), ("ArtifactDigest", "text"),
        ("ArtifactByteLength", "integer"), ("ArtifactExpiresAt", "timestamp with time zone"),
        ("AccessTokenDigest", "text"), ("State", "integer"), ("CreatedAt", "timestamp with time zone"),
        ("UpdatedAt", "timestamp with time zone"), ("ExpiresAt", "timestamp with time zone"),
        ("CancelledAt", "timestamp with time zone"), ("ConsumedAt", "timestamp with time zone"),
        ("PreviewArtifactDigest", "text"), ("PreviewTargetRevisionDigest", "text"),
        ("PreviewSelectedSectionsDigest", "text"), ("PreviewMappingDigest", "text"),
        ("PreviewRequiredApprovalDigest", "text"), ("PreviewApplyMode", "integer"),
        ("PreviewExpiresAt", "timestamp with time zone"), ("Revision", "bigint")
    ];

    private static readonly (string Property, string Type)[] ArtifactColumns =
    [
        ("Id", "uuid"), ("ProtectedPayload", "bytea"), ("Sha256Digest", "text"),
        ("ByteLength", "integer"), ("CreatedAt", "timestamp with time zone"),
        ("ExpiresAt", "timestamp with time zone")
    ];

    private static readonly string[] SessionUpdates =
    [
        "State", "UpdatedAt", "CancelledAt", "ConsumedAt", "PreviewArtifactDigest",
        "PreviewTargetRevisionDigest", "PreviewSelectedSectionsDigest", "PreviewMappingDigest",
        "PreviewRequiredApprovalDigest", "PreviewApplyMode", "PreviewExpiresAt", "Revision"
    ];

    private static readonly string[] ReceiptReads =
    [
        "TargetAuthorityKey", "TargetTenantId", "Kind", "Status", "SnapshotArtifactHandleId"
    ];

    /// <summary>Install only through migration authority; concrete Global login enrollment is separate.</summary>
    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = BuildSql(context);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string Schema(ExploreDbContext context, Type type) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(type)!.GetSchema() ?? "public";

    private static string FunctionSql(ExploreDbContext context, string operation) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            "configuration_import_state_" + operation, Schema(context, typeof(ConfigurationImportSession)));

    private static string Table<T>(ExploreDbContext context) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context, typeof(T)));

    private static string Column<T>(ExploreDbContext context, string property)
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
    }

    private static string Text(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string BuildSql(ExploreDbContext context)
    {
        string roles = $"'{OwnerRole}', '{MigratorRole}', '{GlobalRole}'";
        string elevated = "rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication OR rolinherit";
        string schemaName = Schema(context, typeof(ConfigurationImportSession));
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(schemaName);
        string[] schemaNames = new[]
        {
            schemaName, Schema(context, typeof(ConfigurationImportStoredArtifact)),
            Schema(context, typeof(ConfigurationImportOperation))
        }.Distinct(StringComparer.Ordinal).ToArray();
        string schemas = string.Join(", ", schemaNames.Select(sql.DelimitIdentifier));
        string unsafeMembership = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                WHERE member.rolname IN ('{OwnerRole}', '{GlobalRole}')
                    OR member.rolname = '{MigratorRole}' AND parent.rolname <> '{OwnerRole}'
                    OR parent.rolname = '{OwnerRole}' AND member.rolname <> '{MigratorRole}'
                    OR parent.rolname = '{MigratorRole}' AND member.rolname <> __STATE_MIGRATOR_LOGIN__
                    OR parent.rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}') AND edge.admin_option)
            """;
        string missingMembership = $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                WHERE member.rolname = '{MigratorRole}' AND parent.rolname = '{OwnerRole}'
                    AND edge.set_option AND NOT edge.inherit_option AND NOT edge.admin_option)
            OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                WHERE member.rolname = __STATE_MIGRATOR_LOGIN__ AND parent.rolname = '{MigratorRole}'
                    AND edge.set_option AND NOT edge.admin_option)
            """;
        string unsafeOwnership = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                WHERE relation.relowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace namespace
                WHERE namespace.nspowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles})))
            """;
        (Type Type, string[] Select, string[] Insert, string[] Update)[] relations =
        [
            (typeof(ConfigurationImportSession), [.. SessionColumns.Select(column => column.Property), "xmin"],
                SessionColumns.Select(column => column.Property).ToArray(), SessionUpdates),
            (typeof(ConfigurationImportStoredArtifact), [.. ArtifactColumns.Select(column => column.Property), "xmin"],
                ArtifactColumns.Select(column => column.Property).ToArray(), []),
            (typeof(ConfigurationImportOperation), ReceiptReads, [], [])
        ];
        string TableFor(Type type) => sql.DelimitIdentifier(
            context.Model.FindEntityType(type)!.GetTableName()!, Schema(context, type));
        string ColumnName(Type type, string property)
        {
            if (property == "xmin")
                return property;
            var entity = context.Model.FindEntityType(type)!;
            return entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
        }
        // Enumerate the exact column/privilege set, not a generic table-level grant.
        string expectedColumns = string.Join(",\n", relations.SelectMany(relation =>
            new[] { ("SELECT", relation.Select), ("INSERT", relation.Insert), ("UPDATE", relation.Update) }
                .SelectMany(access => access.Item2.Select(property =>
                    $"({Text(TableFor(relation.Type))}::regclass, {Text(ColumnName(relation.Type, property))}, {Text(access.Item1)})"))));
        string unsafePrivileges = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class relation
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                WHERE namespace.nspname NOT IN ('pg_catalog', 'information_schema')
                    AND namespace.nspname NOT LIKE 'pg_toast%'
                    AND relation.relkind IN ('r', 'p', 'v', 'm', 'f')
                    AND (pg_catalog.has_table_privilege('{GlobalRole}', relation.oid,
                            'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_catalog.has_any_column_privilege('{GlobalRole}', relation.oid,
                            'SELECT,INSERT,UPDATE,REFERENCES')
                        OR pg_catalog.has_table_privilege('{OwnerRole}', relation.oid,
                            'SELECT,INSERT,UPDATE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_catalog.has_table_privilege('{OwnerRole}', relation.oid, 'DELETE')
                            AND relation.oid <> {Text(Table<ConfigurationImportStoredArtifact>(context))}::regclass))
            OR NOT pg_catalog.has_table_privilege('{OwnerRole}',
                {Text(Table<ConfigurationImportStoredArtifact>(context))}::regclass, 'DELETE')
            OR EXISTS (
                SELECT 1 FROM pg_catalog.pg_attribute attribute
                JOIN pg_catalog.pg_class relation ON relation.oid = attribute.attrelid
                JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                CROSS JOIN (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('REFERENCES')) access(privilege)
                WHERE attribute.attnum <> 0 AND NOT attribute.attisdropped
                    AND namespace.nspname NOT IN ('pg_catalog', 'information_schema')
                    AND namespace.nspname NOT LIKE 'pg_toast%'
                    AND relation.relkind IN ('r', 'p', 'v', 'm', 'f')
                    AND pg_catalog.has_column_privilege('{OwnerRole}', relation.oid, attribute.attnum, access.privilege)
                    AND NOT EXISTS (SELECT 1 FROM (VALUES {expectedColumns}) expected(relation_id, column_name, privilege)
                        WHERE expected.relation_id = relation.oid AND expected.column_name = attribute.attname
                            AND expected.privilege = access.privilege))
            OR EXISTS (
                SELECT 1 FROM (VALUES {expectedColumns}) expected(relation_id, column_name, privilege)
                WHERE NOT pg_catalog.has_column_privilege('{OwnerRole}', expected.relation_id,
                    expected.column_name, expected.privilege))
            """;
        (string Name, string Signature)[] functions =
        [
            ("create_session", "uuid,uuid,text,integer,timestamp with time zone,text,timestamp with time zone,timestamp with time zone"),
            ("read_session", "uuid"),
            ("write_session", "uuid,bigint,integer,uuid,text,uuid,text,integer,timestamp with time zone,text,integer,timestamp with time zone,timestamp with time zone,timestamp with time zone,timestamp with time zone,timestamp with time zone,text,text,text,text,text,integer,timestamp with time zone,bigint"),
            ("read_artifact", "uuid"),
            ("delete_artifact", "uuid"),
            ("store_artifact", "uuid,bytea,text,integer,timestamp with time zone,timestamp with time zone")
        ];
        string functionIds = string.Join(", ", functions.Select(function =>
            $"({Text(FunctionSql(context, function.Name) + "(" + function.Signature + ")")}::regprocedure)"));
        string functionInvalid = $"""
            EXISTS (SELECT 1 FROM (VALUES {functionIds}) expected(function_id)
                LEFT JOIN pg_catalog.pg_proc procedure ON procedure.oid = expected.function_id
                WHERE procedure.oid IS NULL
                    OR procedure.proowner <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                    OR NOT procedure.prosecdef OR procedure.provolatile <> 'v'
                    OR procedure.proconfig IS DISTINCT FROM ARRAY['search_path=pg_catalog']::text[]
                    OR NOT pg_catalog.has_function_privilege('{GlobalRole}', procedure.oid, 'EXECUTE')
                    OR EXISTS (
                        SELECT 1 FROM pg_catalog.aclexplode(COALESCE(procedure.proacl,
                            pg_catalog.acldefault('f', procedure.proowner))) permission
                        WHERE permission.grantee NOT IN (
                            SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}', '{GlobalRole}'))
                            OR permission.privilege_type <> 'EXECUTE'
                            OR permission.grantor <> procedure.proowner
                            OR permission.grantee = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}')
                                AND permission.is_grantable))
            """;
        string unsafeSchema = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_namespace namespace
                WHERE pg_catalog.has_schema_privilege('{OwnerRole}', namespace.oid, 'CREATE')
                    OR pg_catalog.has_schema_privilege('{GlobalRole}', namespace.oid, 'CREATE'))
            """;
        string unexpectedFunctions = $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_proc procedure
                WHERE NOT EXISTS (SELECT 1 FROM (VALUES {functionIds}) expected(function_id)
                        WHERE expected.function_id = procedure.oid)
                    AND (procedure.proowner IN (
                            SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({roles}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(COALESCE(procedure.proacl,
                            pg_catalog.acldefault('f', procedure.proowner))) permission
                            WHERE permission.grantee = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{GlobalRole}'))))
            """;
        string authorityGuard = $"""
            IF CURRENT_USER <> '{OwnerRole}'
                OR NOT pg_catalog.pg_has_role(SESSION_USER, '{GlobalRole}', 'USAGE')
                OR pg_catalog.pg_has_role(SESSION_USER, '{OwnerRole}', 'MEMBER')
                OR pg_catalog.pg_has_role(SESSION_USER, '{MigratorRole}', 'MEMBER')
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = SESSION_USER
                    AND (rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication))
                OR (SELECT count(*) FROM pg_catalog.pg_roles WHERE rolname IN ({roles})) <> 3
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ({roles}) AND ({elevated}))
                OR {unsafeMembership} OR {missingMembership} OR {unsafeOwnership} OR {unsafePrivileges}
                OR {unsafeSchema} OR {functionInvalid} OR {unexpectedFunctions} THEN
                RAISE EXCEPTION 'configuration_import_state_authority_not_ready' USING ERRCODE = '42501';
            END IF;
            """;
        string grants = string.Join("\n", relations.Select(relation =>
        {
            string allColumns = string.Join(", ", context.Model.FindEntityType(relation.Type)!.GetProperties()
                .Select(property => sql.DelimitIdentifier(ColumnName(relation.Type, property.Name)))
                .Append(sql.DelimitIdentifier("xmin")).Distinct());
            string Grants(string privilege, string[] properties) => properties.Length == 0 ? "" :
                $"GRANT {privilege} ({string.Join(", ", properties.Select(property => sql.DelimitIdentifier(ColumnName(relation.Type, property))))}) " +
                $"ON TABLE {TableFor(relation.Type)} TO {OwnerRole};";
            return $"""
                REVOKE ALL ON TABLE {TableFor(relation.Type)} FROM {OwnerRole}, {GlobalRole};
                REVOKE ALL ({allColumns}) ON TABLE {TableFor(relation.Type)} FROM {OwnerRole}, {GlobalRole};
                {Grants("SELECT", relation.Select)}
                {Grants("INSERT", relation.Insert)}
                {Grants("UPDATE", relation.Update)}
                {(relation.Type == typeof(ConfigurationImportStoredArtifact)
                    ? $"GRANT DELETE ON TABLE {TableFor(relation.Type)} TO {OwnerRole};" : "")}
                """;
        }));
        string acl = string.Join("\n", functions.Select(function => $"""
            REVOKE ALL ON FUNCTION {FunctionSql(context, function.Name)}({function.Signature}) FROM PUBLIC, {GlobalRole};
            GRANT EXECUTE ON FUNCTION {FunctionSql(context, function.Name)}({function.Signature}) TO {GlobalRole};
            """));
        return $"""
            DO $roles$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY[{roles}] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role) THEN
                        EXECUTE pg_catalog.format(
                            'CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = v_role AND ({elevated})) THEN
                        RAISE EXCEPTION 'configuration_import_state_role_invalid' USING ERRCODE = '42501';
                    END IF;
                END LOOP;
                IF {unsafeMembership.Replace("__STATE_MIGRATOR_LOGIN__", "CURRENT_USER", StringComparison.Ordinal)}
                    OR {unsafeOwnership}
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
                    RAISE EXCEPTION 'configuration_import_state_migration_authority_invalid' USING ERRCODE = '42501';
                END IF;
            END; $roles$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE ON SCHEMA {schemas} TO {OwnerRole};
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            {grants}
            DO $install$
            DECLARE previous_role text := pg_catalog.current_setting('role');
                migrator_login text := CURRENT_USER;
            BEGIN
                PERFORM pg_catalog.set_config('role', '{OwnerRole}', true);
                EXECUTE pg_catalog.replace($ddl$
                    {BuildFunctions(context, authorityGuard)}
                    $ddl$, '__STATE_MIGRATOR_LOGIN__', pg_catalog.quote_literal(migrator_login));
                EXECUTE $acl$ {acl} $acl$;
                PERFORM pg_catalog.set_config('role', previous_role, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            DO $closed$
            BEGIN
                IF {unsafeSchema} OR {unsafePrivileges} OR {functionInvalid} OR {unexpectedFunctions}
                    OR {missingMembership.Replace("__STATE_MIGRATOR_LOGIN__", "CURRENT_USER", StringComparison.Ordinal)} THEN
                    RAISE EXCEPTION 'configuration_import_state_authority_not_ready' USING ERRCODE = '42501';
                END IF;
            END; $closed$;
            """;
    }
}
