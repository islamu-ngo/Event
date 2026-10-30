using Explore.Domain.Learning;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Schema;

/// <summary>
/// Fixed storage rights for C# learning operations. Runtime retains tenant RLS;
/// the existing protected erasure purpose can only select/delete learner roots.
/// </summary>
public static class PostgresLearningPermissions
{
    private const string ErasurePolicy = "learning_subject_erasure";
    private const string GuidancePolicy = "learning_guidance_runtime";
    private static readonly string[] GuidanceColumns =
    [
        nameof(TenantSettingsDocument.Id), nameof(TenantSettingsDocument.TenantId),
        nameof(TenantSettingsDocument.DocumentKey), nameof(TenantSettingsDocument.SchemaVersion),
        nameof(TenantSettingsDocument.DefaultsVersion), nameof(TenantSettingsDocument.PayloadJson),
        nameof(TenantSettingsDocument.ConcurrencyStamp), nameof(TenantSettingsDocument.CreatedAt),
        nameof(TenantSettingsDocument.CreatedBy), nameof(TenantSettingsDocument.UpdatedAt),
        nameof(TenantSettingsDocument.UpdatedBy)
    ];
    private static readonly string[] GuidanceUpdateColumns =
    [
        nameof(TenantSettingsDocument.PayloadJson), nameof(TenantSettingsDocument.ConcurrencyStamp),
        nameof(TenantSettingsDocument.UpdatedAt), nameof(TenantSettingsDocument.UpdatedBy)
    ];

    /// <summary>Pure source-owned Runtime columns for intersecting catalog proofs; no grants or composed admission.</summary>
    internal static bool TryGetGuidanceRuntimeColumns(ExploreDbContext context, Type type,
        out IReadOnlyList<string> read, out IReadOnlyList<string> insert, out IReadOnlyList<string> update)
    {
        read = insert = update = [];
        if (type != typeof(TenantSettingsDocument)) return false;
        var columns = GuidanceColumnNames(context);
        read = insert = Array.AsReadOnly(columns.Values.ToArray());
        update = Array.AsReadOnly(GuidanceUpdateColumns.Select(property => columns[property]).ToArray());
        return true;
    }

    /// <summary>Exact recognition of the source-owned guidance policy using the caller's p alias.</summary>
    internal static string GuidanceRuntimePolicyException(ExploreDbContext context, Type type,
        PostgresNativeQueueEnrollment enrollment)
    {
        if (type != typeof(TenantSettingsDocument)) return "false";
        var storage = GuidanceStorage(context, enrollment);
        string expression = $"""
            '(((' || pg_catalog.quote_ident({Text(storage.Columns[nameof(TenantSettingsDocument.DocumentKey)])})
                || ')::text = ' || pg_catalog.quote_literal({Text(SettingsDocumentKeys.Tenant.LearningGuidance)})
                || '::text) AND (' || pg_catalog.quote_ident({Text(storage.Columns[nameof(TenantSettingsDocument.TenantId)])})
                || ' = (NULLIF(current_setting(''app.current_tenant_id''::text, true), ''''::text))::uuid))'
            """;
        return $"""
            p.polrelid=pg_catalog.to_regclass({Text(storage.Table)}) AND p.polname={Text(GuidancePolicy)}
            AND p.polcmd='*' AND NOT p.polpermissive
            AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.RuntimeLogin)})]
            AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)=({expression})
            AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)=({expression})
            AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(storage.Stamp)}
                || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text)
            AND ({BuiltinPolicyDependencies})
            """;
    }

    /// <summary>
    /// Accepts only wholly absent or complete guidance storage. This leaf checks Runtime
    /// alone and never calls tenant creation or another composed catalog predicate.
    /// </summary>
    internal static string OptionalGuidanceRuntimeCataloguePredicate(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var storage = GuidanceStorage(context, enrollment);
        var entity = context.Model.FindEntityType(typeof(TenantSettingsDocument))!;
        string schema = new HumanPrincipalSourceSql(context).Schema;
        var metadata = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model).Single(table =>
            table.TableName == entity.GetTableName() && (table.Schema ?? schema) == schema);
        if (metadata.AllowsNullTenant || metadata.ParentJoin is not null)
            throw new InvalidOperationException("learning_guidance_model_drift");
        TryGetGuidanceRuntimeColumns(context, typeof(TenantSettingsDocument), out var read, out var insert, out var update);
        string expected = string.Join(',', read.Select(column => (column, Privilege: "SELECT"))
            .Concat(insert.Select(column => (column, Privilege: "INSERT")))
            .Concat(update.Select(column => (column, Privilege: "UPDATE")))
            .Select(grant => $"({Text(grant.column)},{Text(grant.Privilege)})"));
        string runtime = $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.RuntimeLogin)})";
        string migrator = $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.MigratorLogin)})";
        string tenantExpression = $"""
            '(' || pg_catalog.quote_ident({Text(storage.Columns[nameof(TenantSettingsDocument.TenantId)])})
                || ' = (NULLIF(current_setting(''app.current_tenant_id''::text, true), ''''::text))::uuid)'
            """;
        string policy = GuidanceRuntimePolicyException(context, typeof(TenantSettingsDocument), enrollment);
        return $"""
            COALESCE((WITH expected(column_name,privilege) AS (VALUES {expected}),
            members AS (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({runtime},r.oid,'MEMBER'))
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid={runtime} AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={runtime})
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid=pg_catalog.to_regclass({Text(storage.Table)})
                    AND t.relowner={migrator} AND t.relkind='r' AND t.relpersistence='p' AND NOT t.relispartition
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=t.oid OR inhparent=t.oid)
                    AND t.relrowsecurity AND t.relforcerowsecurity
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid AND p.polname='tenant_isolation'
                        AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)=({tenantExpression})
                        AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)=({tenantExpression})
                        AND pg_catalog.obj_description(p.oid,'pg_policy')
                            ={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(metadata, schema))}
                                || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text)
                        AND ({BuiltinPolicyDependencies}))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid
                        AND p.polname<>'tenant_isolation' AND NOT ({policy})
                        AND (p.polname={Text(GuidancePolicy)} OR 0::oid=ANY(p.polroles)
                            OR EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                                WHERE pg_catalog.pg_has_role({runtime},role_id,'MEMBER'))))
                    AND NOT EXISTS (SELECT 1 FROM members r WHERE r.oid=t.relowner
                        OR r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR pg_catalog.has_schema_privilege(r.oid,pg_catalog.to_regnamespace({Text(schema)}),'CREATE')
                        OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)}
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                            WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                    OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                        r.oid={runtime} AND a.attnum>0
                                        AND EXISTS (SELECT 1 FROM expected e WHERE e.column_name=a.attname AND e.privilege=permission.privilege)
                                        AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                            WHERE acl.grantee=r.oid AND acl.grantor=t.relowner
                                                AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl
                                            WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege))))))
                    AND ((NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                            WHERE a.attrelid=t.oid AND (acl.grantee=0 OR acl.grantee IN (SELECT oid FROM members)))
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid AND p.polname={Text(GuidancePolicy)}))
                    OR (pg_catalog.has_schema_privilege({runtime},pg_catalog.to_regnamespace({Text(schema)}),'USAGE')
                        AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (
                            SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                            WHERE a.attrelid=t.oid AND a.attname=e.column_name AND a.attnum>0 AND NOT a.attisdropped
                                AND acl.grantee={runtime} AND acl.grantor=t.relowner
                                AND acl.privilege_type=e.privilege AND NOT acl.is_grantable))
                        AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {policy}))))),false)
            """;
    }

    private const string BuiltinPolicyDependencies = """
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend dependency
            WHERE dependency.classid='pg_catalog.pg_policy'::regclass AND dependency.objid=p.oid
                AND ((dependency.refclassid='pg_catalog.pg_proc'::regclass AND NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_proc routine WHERE routine.oid=dependency.refobjid
                            AND routine.pronamespace='pg_catalog'::regnamespace))
                    OR (dependency.refclassid='pg_catalog.pg_operator'::regclass AND NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_operator operation WHERE operation.oid=dependency.refobjid
                            AND operation.oprnamespace='pg_catalog'::regnamespace))))
        """;

    internal static string GlobalPolicyException(ExploreDbContext context, string login)
    {
        var entity = context.Model.FindEntityType(typeof(UserLearningState))
            ?? throw new InvalidOperationException("The learning model is incomplete.");
        string expression = $"pg_has_role(CURRENT_USER, '{PostgresSubjectPrivacyErasureContract.GlobalRole}'::name, 'MEMBER'::text)";
        return $"""
            (session_user={Text(login)} AND current_user=session_user
                AND n.nspname={Text(entity.GetSchema() ?? RelationalModelNamespace.DefaultSchema)}
                AND c.relname={Text(entity.GetTableName()!)} AND p.polname={Text(ErasurePolicy)}
                AND p.polcmd='*' AND p.polpermissive
                AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})]
                AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)={Text(expression)}
                AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)='false')
            """;
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var names = context.GetService<ISqlGenerationHelper>();
        string runtime = names.DelimitIdentifier(enrollment.RuntimeLogin);
        string global = names.DelimitIdentifier(enrollment.GlobalLogin);
        string erasure = names.DelimitIdentifier(PostgresSubjectPrivacyErasureContract.GlobalRole);
        string Table(Type type)
        {
            var entity = context.Model.FindEntityType(type)
                ?? throw new InvalidOperationException("The learning model is incomplete.");
            return names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        }
        string[] tables = new[] { typeof(TenantLearningSpace), typeof(UserLearningState),
            typeof(LearningTourProgress), typeof(LearningTourStepProgress), typeof(LearningPracticeState),
            typeof(LearningRequirementAcknowledgement) }.Select(Table).ToArray();
        string learner = Table(typeof(UserLearningState));
        string policy = names.DelimitIdentifier(ErasurePolicy);
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using (var identity = context.Database.GetDbConnection().CreateCommand())
        {
            identity.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            identity.CommandText = "SELECT session_user = @migrator AND current_user = session_user";
            var parameter = identity.CreateParameter();
            parameter.ParameterName = "migrator";
            parameter.Value = enrollment.MigratorLogin;
            identity.Parameters.Add(parameter);
            if (await identity.ExecuteScalarAsync(cancellationToken) is not true)
                throw new UnauthorizedAccessException("Learning permissions require the declared migrator.");
        }
        await context.Database.ExecuteSqlRawAsync($"""
            GRANT SELECT, INSERT, UPDATE, DELETE ON {string.Join(", ", tables)} TO {runtime};
            GRANT SELECT, DELETE ON {learner} TO {erasure}, {global};
            DROP POLICY IF EXISTS {policy} ON {learner};
            CREATE POLICY {policy} ON {learner} TO {global}
                USING (pg_catalog.pg_has_role(current_user, '{PostgresSubjectPrivacyErasureContract.GlobalRole}'::name, 'MEMBER'::text))
                WITH CHECK (false);
            """, cancellationToken);
        var guidance = GuidanceStorage(context, enrollment);
        string documentColumns = string.Join(", ", guidance.Columns.Values.Select(names.DelimitIdentifier));
        string updateColumns = string.Join(", ", GuidanceUpdateColumns
            .Select(property => names.DelimitIdentifier(guidance.Columns[property])));
        string guidancePolicy = names.DelimitIdentifier(GuidancePolicy);
        await context.Database.ExecuteSqlRawAsync($"""
            GRANT SELECT ({documentColumns}), INSERT ({documentColumns}), UPDATE ({updateColumns})
                ON {guidance.Table} TO {runtime};
            DROP POLICY IF EXISTS {guidancePolicy} ON {guidance.Table};
            CREATE POLICY {guidancePolicy} ON {guidance.Table} AS RESTRICTIVE FOR ALL TO {runtime}
                USING ({guidance.Predicate}) WITH CHECK ({guidance.Predicate});
            DO $guidance$
            DECLARE fingerprint text;
            BEGIN
                SELECT md5(p.polqual::text) || ':' || md5(p.polwithcheck::text) INTO STRICT fingerprint
                    FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(guidance.Table)}::regclass AND p.polname = {Text(GuidancePolicy)};
                EXECUTE pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',
                    {Text(GuidancePolicy)}, {Text(guidance.Table)}, {Text(guidance.Stamp)} || fingerprint);
            END $guidance$;
            """, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Attests fixed storage rights only; the application separately retains human authorization.</summary>
    internal static async Task RequireGuidanceOwnerAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment? enrollment, Guid tenantId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null || tenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Guidance requires its declared Runtime enrollment.");
        enrollment.Validate();
        var storage = GuidanceStorage(context, enrollment);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"""
            SELECT session_user = @runtime AND current_user = session_user
                AND pg_catalog.current_setting('role') IN ('none', session_user::text)
                AND NULLIF(pg_catalog.current_setting('app.current_tenant_id', true), '')::uuid = @tenant
                AND NOT r.rolsuper AND NOT r.rolbypassrls AND NOT r.rolcreatedb AND NOT r.rolcreaterole AND NOT r.rolreplication
                AND pg_catalog.row_security_active(@relation::regclass)
                AND NOT pg_catalog.has_table_privilege(current_user, @relation::regclass,
                    'DELETE,TRUNCATE,REFERENCES,TRIGGER')
                AND {string.Join(" AND ", storage.Columns.Select(pair => $"""
                    pg_catalog.has_column_privilege(current_user, @relation::regclass, {Text(pair.Value)}, 'SELECT')
                    AND pg_catalog.has_column_privilege(current_user, @relation::regclass, {Text(pair.Value)}, 'INSERT')
                    AND pg_catalog.has_column_privilege(current_user, @relation::regclass, {Text(pair.Value)}, 'UPDATE')
                        = {GuidanceUpdateColumns.Contains(pair.Key, StringComparer.Ordinal).ToString().ToLowerInvariant()}
                    """))}
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = @relation::regclass AND p.polname = @policy
                        AND NOT p.polpermissive AND p.polcmd = '*'
                        AND p.polroles = ARRAY[r.oid]
                        AND pg_catalog.obj_description(p.oid, 'pg_policy')
                            = @stamp || md5(p.polqual::text) || ':' || md5(p.polwithcheck::text))
            FROM pg_catalog.pg_roles r WHERE r.rolname = session_user
            """;
        command.Parameters.Add(new NpgsqlParameter("runtime", enrollment.RuntimeLogin));
        command.Parameters.Add(new NpgsqlParameter("tenant", tenantId));
        command.Parameters.Add(new NpgsqlParameter("relation", storage.Table));
        command.Parameters.Add(new NpgsqlParameter("policy", GuidancePolicy));
        command.Parameters.Add(new NpgsqlParameter("stamp", storage.Stamp));
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new UnauthorizedAccessException("The fixed guidance storage permissions or original Runtime owner changed.");
    }

    private static (string Table, Dictionary<string, string> Columns, string Predicate, string Stamp)
        GuidanceStorage(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var entity = context.Model.FindEntityType(typeof(TenantSettingsDocument))
            ?? throw new InvalidOperationException("The guidance document model is absent.");
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = context.GetService<ISqlGenerationHelper>();
        string name = names.DelimitIdentifier(table.Name, table.Schema);
        var columns = GuidanceColumnNames(context);
        string predicate = $"{names.DelimitIdentifier(columns[nameof(TenantSettingsDocument.DocumentKey)])}"
            + $" = {Text(SettingsDocumentKeys.Tenant.LearningGuidance)}"
            + $" AND {names.DelimitIdentifier(columns[nameof(TenantSettingsDocument.TenantId)])}"
            + " = NULLIF(pg_catalog.current_setting('app.current_tenant_id', true), '')::uuid";
        string stamp = "learning-guidance:v1:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{name}|{enrollment.RuntimeLogin}|{predicate}"))) + ":";
        return (name, columns, predicate, stamp);
    }

    private static Dictionary<string, string> GuidanceColumnNames(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(TenantSettingsDocument))
            ?? throw new InvalidOperationException("The guidance document model is absent.");
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        return GuidanceColumns.ToDictionary(property => property,
            property => entity.FindProperty(property)!.GetColumnName(table)!, StringComparer.Ordinal);
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
