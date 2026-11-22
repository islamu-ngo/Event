using System.Security.Cryptography;
using System.Text;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Security;

/// <summary>
/// Shared SQL attestation builders for native PostgreSQL security contracts.
/// Keeps catalogue, privilege, routine-body, and role-hygiene assertions parameterized without relaxing any check.
/// </summary>
internal static class PostgresContractAttestationSql
{
    internal static string Boolean(bool value) => value ? "true" : "false";

    internal static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    internal static string RoutineSignatureIdsSql<T>(ExploreDbContext context, IEnumerable<T> routines,
        Func<T, (string Name, string Signature)> selector, Func<ExploreDbContext, string, string> functionSqlResolver) =>
        string.Join(",", routines.Select(routine =>
        {
            var (name, signature) = selector(routine);
            return "pg_catalog.to_regprocedure(" + Text(functionSqlResolver(context, name) + "(" + signature + ")") + ")";
        }));

    internal static string BuildGuardInvocationSql(string witnessSql, string functionSql) => $"""
        {witnessSql}
        PERFORM {functionSql}();
        """;

    internal static string BuildCatalogueGuardBodySql(string guardSql) => $"""
        BEGIN
            {guardSql}
        END;
        """;

    internal static string BuildCatalogueGuardWitnessSql(string functionSql, string ownerRole,
        string fingerprint, string stamp, string driftError) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
            WHERE f.oid = pg_catalog.to_regprocedure({Text(functionSql + "()")})
                AND f.proowner = pg_catalog.to_regrole('{ownerRole}')::oid
                AND f.prokind = 'f' AND f.prosecdef AND f.provolatile = 'v' AND f.proparallel = 'u'
                AND NOT f.proleakproof AND NOT f.proisstrict AND NOT f.proretset
                AND f.prorettype = 'pg_catalog.void'::regtype AND f.pronargs = 0
                AND f.proargtypes = ''::pg_catalog.oidvector AND f.pronargdefaults = 0
                AND f.proargdefaults IS NULL AND f.provariadic = 0 AND f.prosupport = 0
                AND f.proallargtypes IS NULL AND f.proargmodes IS NULL AND f.proargnames IS NULL
                AND f.proconfig = ARRAY['search_path=pg_catalog']::text[]
                AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(fingerprint)}
                AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(stamp + fingerprint)}
                AND pg_catalog.has_function_privilege(f.proowner,f.oid,'EXECUTE')
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(
                    COALESCE(f.proacl,pg_catalog.acldefault('f',f.proowner))) a
                    WHERE a.privilege_type <> 'EXECUTE' OR a.grantee <> f.proowner))
        THEN RAISE EXCEPTION '{driftError}' USING ERRCODE = '55000'; END IF;
        """;

    internal static string BuildExactBodiesSql<T>(ExploreDbContext context, IEnumerable<T> functions,
        Func<T, (string Name, string Signature, string Body)> selector,
        Func<ExploreDbContext, string, string> functionSqlResolver, string driftError) =>
        string.Join("\n", functions.Select(item =>
        {
            var (name, signature, body) = selector(item);
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                    WHERE f.oid = pg_catalog.to_regprocedure({Text(functionSqlResolver(context, name) + "(" + signature + ")")})
                        AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(body))})
                THEN RAISE EXCEPTION '{driftError}' USING ERRCODE = '55000'; END IF;
                """;
        }));

    internal static string BuildRoutineMetadataChecksSql<T>(ExploreDbContext context, IEnumerable<T> routines,
        Func<T, (string Name, string Signature, string Result)> selector,
        Func<ExploreDbContext, string, string> functionSqlResolver, string stamp, string driftError) =>
        string.Join("\n", routines.Select(item =>
        {
            var (name, signature, result) = selector(item);
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                    WHERE f.oid = pg_catalog.to_regprocedure({Text(functionSqlResolver(context, name) + "(" + signature + ")")})
                        AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                        AND NOT f.proretset AND f.proargdefaults IS NULL AND f.prorettype = 'pg_catalog.{result}'::regtype
                        AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                        AND pg_catalog.obj_description(f.oid,'pg_proc') =
                            '{stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex'))
                THEN RAISE EXCEPTION '{driftError}' USING ERRCODE = '55000'; END IF;
                """;
        }));

    internal static string BuildAuthorityRoleAndNamespaceHygieneConditions(string schema,
        string? migratorLoginForNamespaceOwner, string rolesSql, string stamp, string routineIdsSql, bool checkNamespaceOwner)
    {
        string prefix = checkNamespaceOwner && migratorLoginForNamespaceOwner is not null
            ? $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(schema)}
                    AND n.nspowner = pg_catalog.to_regrole({Text(migratorLoginForNamespaceOwner)})::oid)
                OR
            """ + " "
            : string.Empty;
        return $"""
            {prefix}EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({rolesSql})
                AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM '{stamp}role:' || r.rolname)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN pg_catalog.pg_roles r
                WHERE r.rolname IN ({rolesSql}) AND pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE'))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({rolesSql})
                AND NOT pg_catalog.has_schema_privilege(r.oid,{Text(schema)},'USAGE'))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r
                WHERE r.rolname IN ({rolesSql}) AND t.relkind = 'S' AND pg_catalog.has_sequence_privilege(r.oid,t.oid,'USAGE,SELECT,UPDATE'))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_roles r
                WHERE r.rolname IN ({rolesSql}) AND pg_catalog.has_database_privilege(r.oid,d.oid,'CREATE'))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid = t.relnamespace
                CROSS JOIN pg_catalog.pg_roles r WHERE r.rolname IN ({rolesSql}) AND n.nspname <> {Text(schema)}
                    AND n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
                    AND t.relkind IN ('r','p','v','m','f')
                    AND (pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d WHERE d.refclassid = 'pg_catalog.pg_authid'::regclass
                AND d.refobjid IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({rolesSql})) AND d.deptype = 'o'
                AND (d.classid <> 'pg_catalog.pg_proc'::regclass OR d.dbid <> (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())
                    OR d.objid NOT IN ({routineIdsSql})))
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid NOT IN ({routineIdsSql})
                AND (f.proowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({rolesSql}))
                    OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a WHERE a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ({rolesSql})))))
            """;
    }

    internal static PostgresPrivilegeGuard.Contract BuildAdministrationContractPrivileges(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, string[] roles,
        IEnumerable<(string Name, string Signature, string Result)> routines, string routinePrefix,
        IEnumerable<(Type Type, PostgresPrivilegeGuard.Column[] Columns)> relationSpecs,
        string ownerRole, string migratorRole, string runtimeRole, string sourceTable, string attemptTable,
        string collisionError)
    {
        enrollment.Validate();
        if (new[]
        {
            enrollment.RuntimeLogin, enrollment.GlobalLogin, enrollment.MigratorLogin,
            enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin
        }.Any(roles.Contains))
            throw new InvalidOperationException(collisionError);
        var sql = new HumanPrincipalSourceSql(context);
        return new(sql.Schema, roles,
            routines.Select(routine => new PostgresPrivilegeGuard.Routine(routinePrefix + routine.Name,
                routine.Signature, ownerRole, routine.Result is "trigger" or "void" ? [ownerRole] : [runtimeRole])).ToArray(),
            [
                .. relationSpecs.Select(spec => new PostgresPrivilegeGuard.Relation(
                    context.Model.FindEntityType(spec.Type)!.GetTableName()!, ownerRole, [], spec.Columns)),
                new(sourceTable, ownerRole, [], [new("id", ["SELECT"]), new("revision", ["SELECT", "UPDATE"])]),
                new(attemptTable, ownerRole, ["SELECT", "INSERT", "UPDATE"], [])
            ],
            [
                new(ownerRole, migratorRole, false, true, false),
                new(migratorRole, enrollment.MigratorLogin, false, true, false),
                new(runtimeRole, enrollment.RuntimeLogin, true, false, false)
            ]);
    }

    internal static string OriginalAdministrationSourcesSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresPolicySourceSetContract.OriginalSourceAssertionSql(context, enrollment) + "\n"
        + PostgresHumanPrincipalSourceContract.OriginalSourceAssertionSql(context, enrollment) + "\n"
        + PostgresHumanAuthorizationRouteContract.OriginalSourceAssertionSql(context, enrollment) + "\n"
        + PostgresHumanSupportAccessSourceContract.OriginalSourceAssertionSql(context, enrollment);

    internal static string BuildRelationSourceCatalogSql(ExploreDbContext context, HumanPrincipalSourceSql sql,
        Type sourceType, IEnumerable<string> mappedProperties, PostgresNativeQueueEnrollment enrollment,
        bool isTenantScoped, Func<string, string> defaultExpressionPredicate,
        Func<Type, string?, string> keyFilterPredicate, string keyDriftError, string fkDriftError,
        string catalogDriftError, string? extraCheckCountPredicate = null, string? trailingGuards = null)
    {
        var entity = context.Model.FindEntityType(sourceType)!;
        string table = Text(sql.Table(sourceType));
        string columns = string.Join(" OR ", mappedProperties.Select(name =>
        {
            var property = entity.FindProperty(name)!;
            string collation = property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true
                ? "C" : "default";
            string defaultCheck = defaultExpressionPredicate(name);
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                    AND a.attname = {Text(sql.ColumnName(sourceType, name))} AND a.attnum > 0 AND NOT a.attisdropped
                    AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                    AND a.attnotnull = {Boolean(!property.IsNullable)}
                    AND a.attgenerated = '' AND a.attidentity = '' AND {defaultCheck}
                    AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation({Text("pg_catalog." + sql.Quote(collation))})))
                """;
        }));
        string KeyColumns(IReadOnlyList<IProperty> properties, Type type) =>
            "ARRAY[" + string.Join(",", properties.Select(property => Text(sql.ColumnName(type, property.Name)))) + "]::name[]";
        string keys = string.Join("\n", entity.GetKeys().Select(key => (key.Properties, Filter: (string?)null))
            .Concat(entity.GetIndexes().Where(index => index.IsUnique).Select(index => (index.Properties, Filter: index.GetFilter())))
            .Select(index => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table})
                    AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate AND i.indexprs IS NULL
                    AND i.indnkeyatts = {index.Properties.Count} AND i.indnatts = {index.Properties.Count}
                    AND NOT EXISTS (SELECT 1 FROM unnest(i.indoption::smallint[]) o(flag) WHERE flag <> 0)
                    AND NOT EXISTS (SELECT 1 FROM unnest(i.indclass::oid[]) operator_id
                        JOIN pg_catalog.pg_opclass o ON o.oid = operator_id JOIN pg_catalog.pg_am am ON am.oid = o.opcmethod
                        WHERE NOT o.opcdefault OR am.amname <> 'btree')
                    AND NOT EXISTS (SELECT 1 FROM unnest(i.indcollation::oid[]) WITH ORDINALITY col(value,ord)
                        JOIN unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ord) ON k.ord = col.ord
                        JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
                        WHERE col.value <> a.attcollation)
                    AND {(index.Filter is null ? "i.indpred IS NULL" : "pg_catalog.pg_get_expr(i.indpred,i.indrelid) = " + Text(keyFilterPredicate(sourceType, index.Filter)))}
                    AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                        JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id ORDER BY k.ord)
                        = {KeyColumns(index.Properties, sourceType)})
                THEN RAISE EXCEPTION '{keyDriftError}' USING ERRCODE = '55000'; END IF;
                """));
        string foreignKeys = string.Join("\n", entity.GetForeignKeys().Select(key =>
        {
            var principal = key.PrincipalEntityType;
            string principalTable = sql.Name(principal.GetTableName()!);
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint fk
                    WHERE fk.contype = 'f' AND fk.convalidated AND NOT fk.condeferrable AND NOT fk.condeferred
                        AND fk.confmatchtype = 's' AND fk.confupdtype = 'a'
                        AND fk.confdeltype = '{(key.DeleteBehavior == DeleteBehavior.Cascade ? "c"
                            : key.DeleteBehavior == DeleteBehavior.SetNull ? "n" : key.DeleteBehavior == DeleteBehavior.Restrict ? "r" : "a")}'
                        AND fk.conrelid = pg_catalog.to_regclass({table}) AND fk.confrelid = pg_catalog.to_regclass({Text(principalTable)})
                        AND ARRAY(SELECT a.attname FROM unnest(fk.conkey) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = fk.conrelid AND a.attnum = k.id ORDER BY k.ord)
                            = {KeyColumns(key.Properties, sourceType)}
                        AND ARRAY(SELECT a.attname FROM unnest(fk.confkey) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = fk.confrelid AND a.attnum = k.id ORDER BY k.ord)
                            = {KeyColumns(key.PrincipalKey.Properties, principal.ClrType)}
                        AND EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgconstraint = fk.oid)
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                            WHERE t.tgconstraint = fk.oid AND t.tgenabled NOT IN ('O','A')))
                THEN RAISE EXCEPTION '{fkDriftError}' USING ERRCODE = '55000'; END IF;
                """;
        }));
        string checkCountClause = extraCheckCountPredicate is null ? string.Empty : $"\n    OR {extraCheckCountPredicate}";
        string trailing = string.IsNullOrEmpty(trailingGuards) ? string.Empty : $"\n{trailingGuards}";
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                AND t.relkind = 'r' AND t.relpersistence = 'p'
                AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                AND t.relrowsecurity = {Boolean(isTenantScoped)} AND t.relforcerowsecurity = {Boolean(isTenantScoped)})
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = pg_catalog.to_regclass({table}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i
                    WHERE i.inhrelid = pg_catalog.to_regclass({table}) OR i.inhparent = pg_catalog.to_regclass({table}))
                OR (SELECT count(*) FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass({table})
                    AND a.attnum > 0 AND NOT a.attisdropped) <> {mappedProperties.Count()}
                OR (SELECT count(*) FROM pg_catalog.pg_index i WHERE i.indrelid = pg_catalog.to_regclass({table})
                    AND i.indisunique) <> {entity.GetKeys().Count() + entity.GetIndexes().Count(index => index.IsUnique)}
                OR (SELECT count(*) FROM pg_catalog.pg_constraint c WHERE c.conrelid = pg_catalog.to_regclass({table})
                    AND c.contype = 'f') <> {entity.GetForeignKeys().Count()}{checkCountClause}
                OR {columns}
            THEN RAISE EXCEPTION '{catalogDriftError}' USING ERRCODE = '55000'; END IF;
            {keys}
            {foreignKeys}{trailing}
            """;
    }

    internal static string BuildSourcePolicyGuardsSql(ExploreDbContext context, HumanPrincipalSourceSql sql,
        IEnumerable<Type> sourceTypes, string policyName, string ownerRole,
        string missingError, string widenedError)
    {
        var tenantTables = context.GetTenantTableInventory();
        return string.Join("\n", sourceTypes.Select(type =>
        {
            string table = Text(sql.Table(type));
            var tenant = tenantTables.SingleOrDefault(candidate =>
                candidate.TableName == context.Model.FindEntityType(type)!.GetTableName());
            string expected = tenant is null ? string.Empty : $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                    AND p.polname = '{policyName}' AND p.polcmd = '*' AND p.polpermissive
                    AND p.polroles = ARRAY[pg_catalog.to_regrole('{ownerRole}')::oid]
                    AND pg_catalog.pg_get_expr(p.polqual,p.polrelid) = 'true'
                    AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid) = 'true')
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                        AND p.polname = '{PostgresTenantRowLevelSecurityModel.PolicyName}' AND p.polpermissive
                        AND p.polcmd = '*' AND p.polroles = ARRAY[0::oid]
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                THEN RAISE EXCEPTION '{missingError}' USING ERRCODE = '55000'; END IF;
                """;
            return expected + $"""
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                    {(tenant is null ? string.Empty : $"AND p.polname NOT IN ('{policyName}','{PostgresTenantRowLevelSecurityModel.PolicyName}')")}
                    AND (0::oid = ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                        WHERE pg_catalog.pg_has_role('{ownerRole}',role_id,'USAGE'))))
                THEN RAISE EXCEPTION '{widenedError}' USING ERRCODE = '55000'; END IF;
                """;
        }));
    }

    internal static string BuildChangedTriggerGuardsSql(HumanPrincipalSourceSql sql, IEnumerable<Type> sourceTypes,
        string triggerName, string functionSignatureSql, string driftError) =>
        string.Join("\n", sourceTypes.Select(type => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                WHERE t.tgrelid = {Text(sql.Table(type))}::regclass AND t.tgname = '{triggerName}'
                    AND t.tgfoid = pg_catalog.to_regprocedure({Text(functionSignatureSql)})
                    AND t.tgtype = 62 AND t.tgenabled = 'A' AND NOT t.tgisinternal AND t.tgqual IS NULL
                    AND t.tgnargs = 0 AND t.tgargs = ''::bytea AND t.tgattr = ''::int2vector AND t.tgconstraint = 0
                    AND NOT t.tgdeferrable AND NOT t.tginitdeferred AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
            THEN RAISE EXCEPTION '{driftError}' USING ERRCODE = '55000'; END IF;
            """));
}
