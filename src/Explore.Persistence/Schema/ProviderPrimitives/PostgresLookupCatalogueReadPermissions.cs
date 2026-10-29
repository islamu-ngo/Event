using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>Installs fixed reference-column reads for Runtime; catalogue selection and materialization belong to EF.</summary>
public static class PostgresLookupCatalogueReadPermissions
{
    internal static IReadOnlyList<string> RuntimeRoleReadProperties { get; } = Array.AsReadOnly<string>(
        ["Id", "MasterCode", "FullName", "Description", "RoleScopeId", "IsSystem"]);

    private static readonly (Type Type, string[] Properties)[] Lookups =
    [
        (typeof(EventType), ["Id", "MasterCode", "FullName", "Description", "TenantId"]),
        (typeof(EventFormat), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(EventStatus), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(AudienceAge), ["Id", "MasterCode", "FullName", "Description", "MinAge", "MaxAge"]),
        (typeof(AudienceGender), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(Madhab), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(Language), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(VisibilityType), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(ApprovalStatus), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(RegistrationMode), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(Role), RuntimeRoleReadProperties.ToArray()),
        (typeof(OrganizationPosition), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(ActorType), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(DidCustodyType), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(FileType), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(TagType), ["Id", "MasterCode", "FullName", "Description"]),
        (typeof(OwnerType), ["Id", "MasterCode", "FullName", "Description"])
    ];

    internal static void ValidateModel(ExploreDbContext context, Type type)
    {
        var properties = Lookups.SingleOrDefault(value => value.Type == type).Properties
            ?? throw new InvalidOperationException("global_lookup_type_not_declared");
        var entity = context.Model.FindEntityType(type)
            ?? throw new InvalidOperationException("global_lookup_model_missing");
        if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(properties.Order(StringComparer.Ordinal))
            || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id"
            || entity.BaseType is not null || entity.GetDerivedTypes().Any()
            || entity.GetComplexProperties().Any()
            || entity.GetNavigations().Any(navigation => navigation.IsEagerLoaded)
            || entity.GetSkipNavigations().Any()
            || entity.GetProperties().Any(property => property.ClrType != (property.Name switch
            {
                "Id" or "RoleScopeId" => typeof(int),
                "MinAge" or "MaxAge" => typeof(int?),
                "TenantId" => typeof(Guid?),
                "IsSystem" => typeof(bool),
                _ => typeof(string)
            })))
            throw new InvalidOperationException("global_lookup_model_shape_drift");
        var tenantTable = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .SingleOrDefault(table => table.TableName == entity.GetTableName());
        if (type == typeof(EventType)
            ? tenantTable is not { AllowsNullTenant: true, ParentJoin: null, TenantIdColumn: not null }
            : tenantTable is not null)
            throw new InvalidOperationException("global_lookup_non_global_source");
    }

    /// <summary>Describes fixed Runtime reference columns without inspecting or requiring database grants.</summary>
    internal static bool TryGetRuntimeReadColumns(ExploreDbContext context, Type type,
        out IReadOnlyList<string> columns)
    {
        var properties = Lookups.SingleOrDefault(lookup => lookup.Type == type).Properties;
        if (properties is null)
        {
            columns = Array.Empty<string>();
            return false;
        }
        ValidateModel(context, type);
        columns = Array.AsReadOnly(Mapping(context, type, properties).Columns);
        return true;
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var sources = Sources(context);
        var names = context.GetService<ISqlGenerationHelper>();
        string runtime = Text(enrollment.RuntimeLogin);
        string complete = PermissionPredicate(context, enrollment, sources, requireComplete: true);
        string allowed = PermissionPredicate(context, enrollment, sources, requireComplete: false);
        string values = SourceValues(sources);
        string grants = string.Join("\n", sources.Select(source => $"""
            GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(enrollment.RuntimeLogin)};
            GRANT SELECT ({string.Join(",", source.Columns.Select(names.DelimitIdentifier))})
                ON TABLE {source.Table} TO {names.DelimitIdentifier(enrollment.RuntimeLogin)};
            """));
        string sql = $"""
            DO $lookup_permissions$ BEGIN
                IF session_user <> {Text(enrollment.MigratorLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none', session_user::text) THEN
                    RAISE EXCEPTION 'global_lookup_installer_wrong_identity' USING ERRCODE = '42501';
                END IF;
                IF NOT ({NoLegacyArtifacts(context)}) THEN
                    RAISE EXCEPTION 'global_lookup_requires_fresh_development_database' USING ERRCODE = '55000';
                END IF;
                IF NOT ({allowed}) OR (EXISTS (SELECT 1 FROM ({values}) source(table_name, allowed_columns)
                    WHERE pg_catalog.has_any_column_privilege({runtime}, source.table_name, 'SELECT'))
                    AND NOT ({complete})) THEN
                    RAISE EXCEPTION 'global_lookup_read_permissions_drift' USING ERRCODE = '55000';
                END IF;
            END $lookup_permissions$;
            {grants}
            DO $lookup_permissions$ BEGIN
                IF NOT ({complete}) THEN
                    RAISE EXCEPTION 'global_lookup_read_permissions_drift' USING ERRCODE = '55000';
                END IF;
            END $lookup_permissions$;
            """;
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("global_lookup_enrollment_required");
        enrollment.Validate();
        string sql = $"""
            SELECT session_user = {Text(enrollment.RuntimeLogin)} AND current_user = session_user
                AND pg_catalog.current_setting('role') IN ('none', session_user::text)
                AND pg_catalog.current_setting('session_replication_role') = 'origin'
                AND NULLIF(pg_catalog.current_setting('app.current_tenant_id', true), '') IS NULL
                AND ({NoLegacyArtifacts(context)})
                AND ({PermissionPredicate(context, enrollment, Sources(context), requireComplete: true)}) AS "Value"
            """;
        if (!await context.Database.SqlQueryRaw<bool>(sql).SingleAsync(cancellationToken))
            throw new UnauthorizedAccessException("global_lookup_read_permissions_not_ready");
        // Recheck the existing tenant policy contract even if this connection predates schema drift.
        await using var readiness = PostgresTenantRuntimeReadiness.CreateCommand(context.Database.GetDbConnection(), context);
        readiness.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        PostgresTenantRuntimeReadiness.RequireReady(await readiness.ExecuteScalarAsync(cancellationToken));
    }

    private static string PermissionPredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        (string Schema, string Table, string[] Columns)[] sources, bool requireComplete)
    {
        string runtime = Text(enrollment.RuntimeLogin);
        string values = SourceValues(sources);
        string schemas = "ARRAY[" + string.Join(",", sources.Select(source => source.Schema).Distinct().Select(Text)) + "]::text[]";
        string protectedTables = "ARRAY[" + string.Join(",", new[] { typeof(RolePermission), typeof(UserPii) }
            .Select(type => Text(Mapping(context, type, []).Table))) + "]::text[]";
        // Email source projections belong to their own contract; Runtime must remain unable to borrow that authority.
        string otherLogins = "ARRAY[" + string.Join(",", new[] { enrollment.GlobalLogin,
            enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Select(Text)) + "]::text[]";
        // Recognize only the creator's fixed read columns on this owner's actual
        // reference sources. The optional package must be wholly absent or complete.
        var creatorReads = Lookups.SelectMany(lookup =>
        {
            if (!PostgresTenantCreationPermissions.TryGetGlobalColumns(context, lookup.Type, out var columns, out _))
                return Array.Empty<string>();
            var source = Mapping(context, lookup.Type, lookup.Properties);
            return columns.Where(column => source.Columns.Contains(column, StringComparer.Ordinal))
                .Select(column => $"(source.table_name={Text(source.Table)} AND a.attname={Text(column)})");
        }).ToArray();
        string creatorColumns = creatorReads.Length == 0 ? "false" : string.Join(" OR ", creatorReads);
        string creatorProof = creatorReads.Length == 0 ? "true"
            : PostgresTenantCreationPermissions.OptionalGlobalCataloguePredicate(context, enrollment);
        string complete = requireComplete ? $"""
            AND NOT EXISTS (SELECT 1 FROM ({values}) source(table_name, allowed_columns)
                CROSS JOIN LATERAL unnest(source.allowed_columns) expected(name)
                WHERE NOT pg_catalog.has_column_privilege({runtime}, source.table_name, expected.name, 'SELECT')
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                        CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                        WHERE a.attrelid = pg_catalog.to_regclass(source.table_name) AND a.attname = expected.name
                            AND acl.grantee = pg_catalog.to_regrole({runtime})::oid
                            AND acl.privilege_type = 'SELECT' AND NOT acl.is_grantable))
            """ : "";
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = {runtime} AND rolcanlogin)
            AND NOT EXISTS (SELECT 1 FROM ({values}) source(table_name, allowed_columns)
                LEFT JOIN pg_catalog.pg_class c ON c.oid = pg_catalog.to_regclass(source.table_name)
                WHERE c.oid IS NULL OR c.relkind <> 'r' OR c.relpersistence <> 'p'
                    OR c.relowner <> pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE pg_catalog.pg_has_role({runtime}, r.oid, 'MEMBER')
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR EXISTS (SELECT 1 FROM unnest({schemas}) schema_name
                            WHERE pg_catalog.has_schema_privilege(r.oid, schema_name, 'CREATE'))
                        OR EXISTS (SELECT 1 FROM ({values}) source(table_name, allowed_columns)
                            WHERE (pg_catalog.has_table_privilege(r.oid, source.table_name,
                                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "source.table_name", false)})
                                OR pg_catalog.has_any_column_privilege(r.oid, source.table_name, 'INSERT,UPDATE,REFERENCES')
                                OR (r.rolname <> {runtime} AND pg_catalog.has_any_column_privilege(r.oid, source.table_name, 'SELECT'))
                                OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                    WHERE a.attrelid = pg_catalog.to_regclass(source.table_name) AND a.attnum <> 0 AND NOT a.attisdropped
                                        AND (pg_catalog.has_column_privilege(r.oid, a.attrelid, a.attnum, 'SELECT WITH GRANT OPTION')
                                            OR (NOT (a.attname = ANY(source.allowed_columns))
                                                AND pg_catalog.has_column_privilege(r.oid, a.attrelid, a.attnum, 'SELECT')))))
                        OR EXISTS (SELECT 1 FROM unnest({protectedTables}) table_name
                            WHERE (pg_catalog.has_table_privilege(r.oid, table_name,
                                    'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "table_name", false)})
                                OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                    WHERE a.attrelid = pg_catalog.to_regclass(table_name) AND a.attnum <> 0 AND NOT a.attisdropped
                                        AND pg_catalog.has_column_privilege(r.oid, a.attrelid, a.attnum,
                                            'SELECT,INSERT,UPDATE,REFERENCES')))))
            AND NOT EXISTS (SELECT 1 FROM unnest({otherLogins}) login
                WHERE pg_catalog.pg_has_role(login, {runtime}, 'MEMBER')
                    OR pg_catalog.pg_has_role({runtime}, login, 'MEMBER'))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)}, r.oid, 'MEMBER')
                    AND EXISTS (SELECT 1 FROM ({values}) source(table_name, allowed_columns)
                        WHERE (pg_catalog.has_table_privilege(r.oid, source.table_name,
                                'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "source.table_name", false)})
                            OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                WHERE a.attrelid = pg_catalog.to_regclass(source.table_name) AND a.attnum <> 0 AND NOT a.attisdropped
                                    AND (pg_catalog.has_column_privilege(r.oid, a.attrelid, a.attnum, 'INSERT,UPDATE,REFERENCES')
                                        OR pg_catalog.has_column_privilege(r.oid, a.attrelid, a.attnum, 'SELECT WITH GRANT OPTION')
                                        OR (pg_catalog.has_column_privilege(r.oid, a.attrelid, a.attnum, 'SELECT')
                                            AND NOT (a.attnum > 0 AND r.rolname={Text(enrollment.GlobalLogin)}
                                                AND ({creatorColumns})
                                                AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                                    JOIN pg_catalog.pg_class owner ON owner.oid=a.attrelid
                                                    WHERE acl.grantee=r.oid AND acl.grantor=owner.relowner
                                                        AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                                                AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                                    WHERE acl.grantee=0 AND acl.privilege_type='SELECT')))))))
            AND ({creatorProof}) IS TRUE
            {complete}
            """;
    }

    private static string NoLegacyArtifacts(ExploreDbContext context)
    {
        var source = Mapping(context, typeof(EventType), []);
        return $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = {Text(source.Schema)} AND p.proname = 'global_lookup_catalogue')
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                WHERE rolname IN ('event_global_lookup_owner', 'event_global_lookup_migrator', 'event_global_lookup_runtime'))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy
                WHERE polrelid = pg_catalog.to_regclass({Text(source.Table)})
                    AND polname IN ('global_lookup_catalogue_read', 'global_lookup_catalogue_read_allow'))
            """;
    }

    private static (string Schema, string Table, string[] Columns)[] Sources(ExploreDbContext context) =>
        Lookups.Select(lookup =>
        {
            ValidateModel(context, lookup.Type);
            return Mapping(context, lookup.Type, lookup.Properties);
        }).ToArray();

    private static (string Schema, string Table, string[] Columns) Mapping(ExploreDbContext context, Type type, string[] properties)
    {
        var entity = context.Model.FindEntityType(type)!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(mapping.Name, schema);
        return (schema, table, properties.Select(property => entity.FindProperty(property)!.GetColumnName(mapping)!).ToArray());
    }

    private static string SourceValues((string Schema, string Table, string[] Columns)[] sources) =>
        "VALUES " + string.Join(",", sources.Select(source =>
            $"({Text(source.Table)}, ARRAY[{string.Join(",", source.Columns.Select(Text))}]::text[])"));

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
