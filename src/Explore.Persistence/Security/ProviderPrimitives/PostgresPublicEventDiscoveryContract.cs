using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only, ordinary Runtime public candidates. The caller retains its Serializable owner;
/// this contract neither opens another connector nor acquires snapshot or disclosure authority.
/// </summary>
public static partial class PostgresPublicEventDiscoveryContract
{
    public const string OwnerRole = "event_public_discovery_owner";
    public const string MigratorRole = "event_public_discovery_migrator";
    public const string RuntimeRole = "event_public_discovery_runtime";
    private const string Prefix = "public_event_discovery_";
    private const string Policy = "public_event_discovery_read";
    private const string Stamp = "public-event-discovery:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];
    private static readonly string[] Operations = ["seek_local", "page_local", "members_local", "seek_remote", "members_remote", "visible_remote_records"];

    public static string FunctionSql(ExploreDbContext context, string operation)
    {
        if (!Operations.Contains(operation, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(operation));
        return new HumanPrincipalSourceSql(context).Name(Prefix + operation);
    }

    /// <summary>Parent-owned migrator/enrollment hook; never called by a public reader.</summary>
    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("public_discovery_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = Command(context);
        command.CommandText = BuildSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Public discovery readiness requires the original open PostgreSQL connection.");
        await using var command = Command(context);
        command.CommandText = AdmissionSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static string AdmissionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var definition = Define(context, enrollment);
        string tenant = context.TenantFilterTenantId is { } selected ? Text(selected.ToString()) + "::uuid" : "NULL::uuid";
        return $"""
            DO $ready$ BEGIN
                {definition.Guard} {ExactBodies(context, definition.Bodies)}
                {PostgresPrivilegeGuard.RestrictedCallerSql}
                IF session_user <> {Text(enrollment.RuntimeLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                    OR {tenant} IS NULL OR {tenant} = '00000000-0000-0000-0000-000000000000'::uuid
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM {tenant}
                THEN RAISE EXCEPTION 'public_discovery_readiness_caller' USING ERRCODE = '42501'; END IF;
            END $ready$;
            """;
    }

    private static DbCommand Command(ExploreDbContext context)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        return command;
    }

    private sealed record Definition(SourceSql Sql, PostgresPrivilegeGuard.Contract Privileges,
        IReadOnlyDictionary<Type, string> Policies, string Guard, IReadOnlyDictionary<string, string> Bodies);

    private static Definition Define(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.RuntimeLogin, enrollment.GlobalLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("public_discovery_enrollment_collision");
        var sql = new SourceSql(context);
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["seek_local"] = LocalQuery(sql, false),
            ["page_local"] = LocalQuery(sql, false, paged: true),
            ["members_local"] = LocalQuery(sql, true),
            ["seek_remote"] = RemoteQuery(sql, false, false),
            ["members_remote"] = RemoteQuery(sql, true, false),
            ["visible_remote_records"] = RemoteQuery(sql, false, true)
        };
        var policies = sql.Policies();
        var privileges = new PostgresPrivilegeGuard.Contract(sql.Names.Schema, Roles,
            Operations.Select(operation => new PostgresPrivilegeGuard.Routine(
                Prefix + operation, "uuid,jsonb", OwnerRole, [RuntimeRole])).ToArray(),
            sql.Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Key)!.GetTableName()!, OwnerRole, [],
                source.Value.Order(StringComparer.Ordinal).Select(property => new PostgresPrivilegeGuard.Column(
                    sql.Names.ColumnName(source.Key, property), ["SELECT"])).ToArray())).ToArray(),
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(privileges)
            + SourceGuard(context, sql, enrollment) + PolicyGuard(sql, policies)
            + CallerPrivilegeGuard(sql, enrollment);
        var bodies = queries.ToDictionary(pair => pair.Key, pair => $"""
            BEGIN
                {guard}
                {PostgresPrivilegeGuard.RestrictedCallerSql}
                IF current_user <> '{OwnerRole}' OR session_user <> {Text(enrollment.RuntimeLogin)}
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                    OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
                    OR pg_catalog.current_setting('session_replication_role') <> 'origin'
                    OR p_tenant IS NULL OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
                THEN RAISE EXCEPTION 'public_discovery_caller_mismatch' USING ERRCODE = '42501'; END IF;
                {ValidateRequest(pair.Key)}
                RETURN QUERY {pair.Value};
            END;
            """, StringComparer.Ordinal);
        return new(sql, privileges, policies, guard, bodies);
    }

    private static string ExactBodies(ExploreDbContext context, IReadOnlyDictionary<string, string> bodies) =>
        string.Join("\n", bodies.Select(pair => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, pair.Key) + "(uuid,jsonb)")})
                    AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                    AND f.prorettype = 'jsonb'::regtype AND f.proretset
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(pair.Value))})
            THEN RAISE EXCEPTION 'public_discovery_body_drift' USING ERRCODE = '55000'; END IF;
            """));

    private static string CallerPrivilegeGuard(SourceSql sql, PostgresNativeQueueEnrollment enrollment)
    {
        string sources = string.Join(",", sql.Sources.Keys.Select(type => Text(sql.Names.Table(type)) + "::regclass"));
        return $"""
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'USAGE')
                    OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r WHERE t.oid IN ({sources})
                    AND (pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'USAGE')
                        OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'SET'))
                    AND (r.oid = t.relowner
                        OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE (pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'USAGE')
                        OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'SET'))
                        AND pg_catalog.has_schema_privilege(r.oid,pg_catalog.to_regnamespace({Text(sql.Names.Schema)}),'CREATE'))
            THEN RAISE EXCEPTION 'public_discovery_runtime_source_privilege' USING ERRCODE = '42501'; END IF;
            """;
    }

    private static string PolicyGuard(SourceSql sql, IReadOnlyDictionary<Type, string> policies) =>
        string.Join("\n", policies.SelectMany(pair => new[] { false, true }.Select(permissive =>
        {
            string name = permissive ? Policy + "_allow" : Policy;
            string expected = Stamp + Hash(pair.Value) + ":";
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass AND p.polname = {Text(name)}
                        AND p.polcmd = 'r' AND p.polpermissive = {Boolean(permissive)}
                        AND p.polroles = ARRAY[pg_catalog.to_regrole('{OwnerRole}')::oid]
                        AND p.polqual IS NOT NULL AND p.polwithcheck IS NULL
                        AND pg_catalog.obj_description(p.oid,'pg_policy') = {Text(expected)} || pg_catalog.md5(p.polqual::text))
                THEN RAISE EXCEPTION 'public_discovery_policy_drift' USING ERRCODE = '55000'; END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid = {Text(sql.Names.Table(pair.Key))}::regclass
                        AND p.polname NOT IN ('tenant_isolation','{Policy}','{Policy}_allow')
                        AND EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                            WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}',role_id,'USAGE')))
                THEN RAISE EXCEPTION 'public_discovery_unexpected_policy' USING ERRCODE = '55000'; END IF;
                """;
        })));

    private static string SourceGuard(ExploreDbContext context, SourceSql sql, PostgresNativeQueueEnrollment enrollment)
    {
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n WHERE n.nspname = {Text(sql.Names.Schema)}
                AND n.nspowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid)
            THEN RAISE EXCEPTION 'public_discovery_namespace_drift' USING ERRCODE = '55000'; END IF;
            """ + string.Join("\n", sql.Sources.Select(source =>
        {
            var entity = context.Model.FindEntityType(source.Key)!;
            var declared = context.GetService<IDesignTimeModel>().Model.FindEntityType(source.Key)!;
            var tenant = tenantTables.SingleOrDefault(table => table.TableName == entity.GetTableName());
            string table = Text(sql.Names.Table(source.Key));
            string columns = string.Join(" OR ", source.Value.Select(name =>
            {
                var property = entity.FindProperty(name)!;
                string collation = property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true
                    || property.FindAnnotation(LocationUnicodePropertyExtensions.AnnotationName)?.Value is true ? "C" : "default";
                string identity = NpgsqlPropertyExtensions.GetValueGenerationStrategy(declared.FindProperty(name)!) switch
                {
                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn => "d",
                    NpgsqlValueGenerationStrategy.IdentityAlwaysColumn => "a",
                    _ => ""
                };
                return $"""
                    NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid = {table}::regclass
                        AND a.attname = {Text(sql.Names.ColumnName(source.Key, name))} AND a.attnum > 0 AND NOT a.attisdropped
                        AND pg_catalog.format_type(a.atttypid,a.atttypmod) = {Text(property.GetRelationalTypeMapping().StoreType)}
                        AND a.attnotnull = {Boolean(!property.IsNullable)} AND a.attgenerated = '' AND a.attidentity = {Text(identity)}
                        AND (a.attcollation = 0 OR a.attcollation = pg_catalog.to_regcollation({Text("pg_catalog." + sql.Names.Quote(collation))})))
                    """;
            }));
            string primary = string.Join(",", entity.FindPrimaryKey()!.Properties.Select(property =>
                Text(sql.Names.ColumnName(source.Key, property.Name))));
            return $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t WHERE t.oid = pg_catalog.to_regclass({table})
                    AND t.relkind = 'r' AND t.relpersistence = 'p' AND t.relowner = pg_catalog.to_regrole({Text(enrollment.MigratorLogin)})::oid
                    AND t.relrowsecurity = {Boolean(tenant is not null)} AND t.relforcerowsecurity = {Boolean(tenant is not null)})
                    OR {columns}
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class = {table}::regclass)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid = {table}::regclass OR i.inhparent = {table}::regclass)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid = {table}::regclass
                        AND i.indisprimary AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate
                        AND i.indexprs IS NULL AND i.indpred IS NULL
                        AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                            JOIN pg_catalog.pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.id ORDER BY k.ord)
                            = ARRAY[{primary}]::name[])
                THEN RAISE EXCEPTION 'public_discovery_source_catalog_drift' USING ERRCODE = '55000'; END IF;
                {(tenant is null ? "" : $"""
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {table}::regclass
                        AND p.polname = 'tenant_isolation' AND p.polcmd = '*' AND p.polpermissive
                        AND p.polroles = ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                        AND pg_catalog.obj_description(p.oid,'pg_policy') =
                            {Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, sql.Names.Schema))}
                            || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
                    THEN RAISE EXCEPTION 'public_discovery_tenant_policy_drift' USING ERRCODE = '55000'; END IF;
                    """)}
                """;
        }));
    }

    private static string ValidateRequest(string operation)
    {
        bool local = operation.EndsWith("_local", StringComparison.Ordinal);
        bool paged = operation == "page_local";
        bool seek = operation.StartsWith("seek_", StringComparison.Ordinal) || paged;
        bool visible = operation == "visible_remote_records";
        string[] requestKeys = local
            ? ["Clauses", "Occurrence", "Sort", "Descending", "After", seek ? "Take" : "Members", .. paged ? new[] { "Offset" } : Array.Empty<string>()]
            : visible ? ["Members", "Sort", "Descending", "After"]
            : ["Take", "Sort", "Descending", "After", "SearchPattern", "DateFrom", "DateTo", "Modes", "Temporal", "Now", "Members"];
        string supported = string.Join(" OR ", new (string Group, string[] Kinds)[]
        {
            ("event", Enum.GetNames<EventFilterType>()), ("islamic", Enum.GetNames<IslamicAspectFilterType>()),
            ("tech", Enum.GetNames<TechAspectFilterType>()), ("presence", Enum.GetNames<AspectPresenceFilterType>()),
            ("subquery", Enum.GetNames<EventSubqueryFilterType>().Where(name => name != "Occurrence").ToArray()),
            ("property", Enum.GetNames<EventCustomPropertyProjectionFilterType>())
        }.Select(group => $"c->>'Group' = {Text(group.Group)} AND c->>'Kind' IN ({string.Join(",", group.Kinds.Select(Text))})"));
        return $"""
            IF p_request IS NULL OR pg_catalog.jsonb_typeof(p_request) <> 'object' OR octet_length(p_request::text) > 262144
                OR (p_request->>'Sort' IN ('Date','Title','Views','CreatedAt')) IS NOT TRUE
                OR pg_catalog.jsonb_typeof(p_request->'Descending') IS DISTINCT FROM 'boolean'
                OR NOT p_request ? 'After'
                OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_object_keys(p_request) key
                    WHERE key NOT IN ({string.Join(",", requestKeys.Select(Text))}))
                {(seek ? """
                    OR pg_catalog.jsonb_typeof(p_request->'Take') IS DISTINCT FROM 'number'
                    OR ((p_request->>'Take')::integer BETWEEN 1 AND 1000) IS NOT TRUE
                    """ : """
                    OR pg_catalog.jsonb_typeof(p_request->'Members') IS DISTINCT FROM 'array'
                    OR pg_catalog.jsonb_array_length(p_request->'Members') > 100
                    """)}
            THEN RAISE EXCEPTION 'public_discovery_request_invalid' USING ERRCODE = '22023'; END IF;
            {(paged ? """
                IF p_request->'After' IS DISTINCT FROM 'null'::jsonb
                    OR pg_catalog.jsonb_typeof(p_request->'Offset') IS DISTINCT FROM 'number'
                    OR ((p_request->>'Offset')::integer >= 0) IS NOT TRUE
                THEN RAISE EXCEPTION 'public_discovery_page_invalid' USING ERRCODE = '22023'; END IF;
                """ : "")}
            IF p_request->'After' <> 'null'::jsonb AND (
                pg_catalog.jsonb_typeof(p_request->'After') <> 'object'
                OR (p_request->'After'->>'SourceKey' ~ {Text("^[0-9a-f]{32}$")}) IS NOT TRUE
                OR length(p_request->'After'->>'TitleKey') > 960
                OR (p_request->'After'->>'TitleKey' ~ {Text("^([0-9A-F]{4})*$")}) IS NOT TRUE
                OR p_request->'After'->>'CreatedAt' IS NULL OR p_request->'After'->>'Views' IS NULL)
            THEN RAISE EXCEPTION 'public_discovery_cursor_invalid' USING ERRCODE = '22023'; END IF;
            {(!seek ? local ? """
                IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_request->'Members') m
                    WHERE pg_catalog.jsonb_typeof(m) <> 'object' OR m->>'EventId' IS NULL OR m->>'SessionId' IS NULL
                        OR (m->>'EventId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                        OR (m->>'SessionId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid)
                THEN RAISE EXCEPTION 'public_discovery_members_invalid' USING ERRCODE = '22023'; END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_request->'Members') m
                    GROUP BY m->>'EventId' HAVING count(*) > 1)
                THEN RAISE EXCEPTION 'public_discovery_duplicate_event' USING ERRCODE = '22023'; END IF;
                """ : """
                IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_request->'Members') m
                    WHERE pg_catalog.jsonb_typeof(m) <> 'string'
                        OR (m #>> '{}')::uuid = '00000000-0000-0000-0000-000000000000'::uuid)
                THEN RAISE EXCEPTION 'public_discovery_members_invalid' USING ERRCODE = '22023'; END IF;
                """ : "")}
            {(local ? $"""
                IF pg_catalog.jsonb_typeof(p_request->'Clauses') IS DISTINCT FROM 'array'
                    OR pg_catalog.jsonb_array_length(p_request->'Clauses') > 128
                    OR pg_catalog.jsonb_typeof(p_request->'Occurrence') IS DISTINCT FROM 'object'
                    OR ((p_request->'Occurrence'->>'View')::integer BETWEEN 0 AND 4) IS NOT TRUE
                    OR p_request->'Occurrence'->>'Now' IS NULL
                    OR (p_request->'Occurrence'->>'DateFrom')::date > (p_request->'Occurrence'->>'DateTo')::date
                    OR NOT (p_request->'Occurrence' ? 'LocationIds')
                    OR (p_request->'Occurrence'->'LocationIds' <> 'null'::jsonb
                        AND (pg_catalog.jsonb_typeof(p_request->'Occurrence'->'LocationIds') <> 'array'
                            OR pg_catalog.jsonb_array_length(p_request->'Occurrence'->'LocationIds') > 50))
                THEN RAISE EXCEPTION 'public_discovery_occurrence_invalid' USING ERRCODE = '22023'; END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_request->'Clauses') c
                    WHERE pg_catalog.jsonb_typeof(c) <> 'object' OR ({supported}) IS NOT TRUE
                        OR pg_catalog.jsonb_typeof(c->'Integers') IS DISTINCT FROM 'array'
                        OR pg_catalog.jsonb_typeof(c->'Identifiers') IS DISTINCT FROM 'array'
                        OR pg_catalog.jsonb_array_length(c->'Integers') > 50
                        OR pg_catalog.jsonb_array_length(c->'Identifiers') > 50
                        OR length(c->>'Text') > CASE WHEN c->>'Group' = 'property' THEN 502 ELSE 200 END
                        OR length(c->>'Namespace') > 100 OR length(c->>'Key') > 100
                        OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_object_keys(c) key
                            WHERE key NOT IN ('Group','Kind','Integer','Integers','Identifier','Identifiers','Text','Date','Namespace','Key','Min','Max','From','To'))
                        OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(c->'Integers') item
                            WHERE pg_catalog.jsonb_typeof(item) <> 'number')
                        OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(c->'Identifiers') item
                            WHERE pg_catalog.jsonb_typeof(item) <> 'string')
                        OR (c->>'Kind' IN ('EventType','Format','Madhab','AudienceGender','AudienceAge','Status','Visibility',
                            'GenderMode','ReferencePrayer','PrimaryLanguage','SkillLevel','Language','RegistrationMode','TemporalView')
                            AND pg_catalog.jsonb_typeof(c->'Integer') IS DISTINCT FROM 'number')
                        OR (c->>'Kind' IN ('Actor','Category','Location','OptionMatch')
                            AND pg_catalog.jsonb_typeof(c->'Identifier') IS DISTINCT FROM 'string')
                        OR (c->>'Kind' IN ('SearchTerm','HackathonTrack','TechStack','ExactMatch','TextSearch','GlobalTextSearch')
                            AND pg_catalog.jsonb_typeof(c->'Text') IS DISTINCT FROM 'string')
                        OR (c->>'Kind' IN ('DateFrom','DateTo') AND pg_catalog.jsonb_typeof(c->'Date') IS DISTINCT FROM 'string')
                        OR (c->>'Group' = 'property' AND c->>'Kind' <> 'GlobalTextSearch'
                            AND (c->>'Namespace' IS NULL OR c->>'Key' IS NULL))
                        OR (c->>'Kind' IN ('FutureOnly','TemporalView') AND c->>'From' IS NULL))
                    THEN RAISE EXCEPTION 'public_discovery_clause_invalid' USING ERRCODE = '22023'; END IF;
                IF (SELECT count(*) FROM pg_catalog.jsonb_array_elements(p_request->'Clauses') c WHERE c->>'Group' = 'property') > 11
                THEN RAISE EXCEPTION 'public_discovery_property_budget' USING ERRCODE = '22023'; END IF;
                """ : visible ? "" : """
                IF pg_catalog.jsonb_typeof(p_request->'Modes') IS DISTINCT FROM 'array'
                    OR pg_catalog.jsonb_array_length(p_request->'Modes') > 50
                    OR length(p_request->>'SearchPattern') > 202
                    OR ((p_request->>'Temporal')::integer BETWEEN 1 AND 5) IS NOT TRUE OR p_request->>'Now' IS NULL
                THEN RAISE EXCEPTION 'public_discovery_remote_criteria_invalid' USING ERRCODE = '22023'; END IF;
                """)}
            """;
    }

    private sealed class SourceSql(ExploreDbContext context)
    {
        internal HumanPrincipalSourceSql Names { get; } = new(context);
        internal Dictionary<Type, HashSet<string>> Sources { get; } = [];
        internal string Table<T>() => Names.Table(typeof(T));
        internal string Name<T>(string property) => Names.Column(typeof(T), property);
        internal string Literal<T>(string property, object value) =>
            context.Model.FindEntityType(typeof(T))!.FindProperty(property)!
                .GetRelationalTypeMapping().GenerateSqlLiteral(value);
        internal string Column<T>(string alias, string property) => Column(typeof(T), alias, property);
        private string Column(Type type, string alias, string property)
        {
            if (context.Model.FindEntityType(type)?.FindProperty(property) is null)
                throw new InvalidOperationException($"public_discovery_unmapped_source:{type.Name}.{property}");
            if (!Sources.TryGetValue(type, out var fields)) Sources.Add(type, fields = []);
            fields.Add(property);
            return Names.Quote(alias) + "." + Names.Column(type, property);
        }
        internal string Live<T>(string alias)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var clauses = new List<string>();
            if (entity.FindProperty("TenantId") is not null)
            {
                var tenant = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
                    .SingleOrDefault(table => table.TableName == entity.GetTableName());
                string column = Column<T>(alias, "TenantId");
                clauses.Add(tenant?.AllowsNullTenant is true
                    ? $"({column} IS NULL OR {column} = p_tenant)" : column + " = p_tenant");
            }
            if (entity.FindProperty("IsDeleted") is not null) clauses.Add("NOT " + Column<T>(alias, "IsDeleted"));
            return clauses.Count == 0 ? "true" : string.Join(" AND ", clauses);
        }
        internal string Projection<T>(string alias, params string[] properties) =>
            "pg_catalog.jsonb_build_object(" + string.Join(",", properties.Select(property =>
                Text(property) + "," + Column<T>(alias, property))) + ")";

        internal IReadOnlyDictionary<Type, string> Policies()
        {
            var result = new Dictionary<Type, string>();
            var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
            foreach (var type in Sources.Keys.ToArray())
            {
                var entity = context.Model.FindEntityType(type)!;
                var tenant = tenantTables.SingleOrDefault(table => table.TableName == entity.GetTableName());
                string table = Names.Table(type);
                string predicate = tenant is null ? "true"
                    : tenant.ParentJoin is { } parent ? ParentPolicy(parent, table, 0)
                    : tenant.AllowsNullTenant
                        ? $"({Names.Quote(tenant.TenantIdColumn!)} IS NULL OR {Names.Quote(tenant.TenantIdColumn!)} = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid)"
                        : $"{Names.Quote(tenant.TenantIdColumn!)} = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid";
                if (type == typeof(Event))
                    predicate += $" AND NOT {Names.Column(type, "IsDeleted")} AND {Names.Column(type, "VisibilityTypeId")} = {(int)Explore.Domain.Enums.VisibilityTypeEnum.Public}";
                if (type == typeof(EventCustomPropertyProjection))
                    predicate += $" AND {Names.Column(type, "ExposureLevel")} = {Literal<EventCustomPropertyProjection>("ExposureLevel", Explore.Domain.Enums.ExposureLevel.Public)}";
                result.Add(type, predicate);
            }
            return result;
        }

        private string ParentPolicy(TenantParentJoinMetadata parent, string child, int depth)
        {
            string alias = "discovery_parent_" + depth;
            var entity = context.Model.GetEntityTypes().Single(entity => entity.GetTableName() == parent.ParentTableName);
            string Register(string name)
            {
                var property = entity.GetProperties().Single(property => Names.ColumnName(entity.ClrType, property.Name) == name);
                return Column(entity.ClrType, alias, property.Name);
            }
            string key = Register(parent.ParentKeyColumn);
            string tenantColumn = parent.ParentTenantIdColumn is { } column ? Register(column) : "";
            string tenant = parent.ParentJoin is { } next ? ParentPolicy(next, alias, depth + 1)
                : parent.ParentAllowsNullTenant
                    ? $"({tenantColumn} IS NULL OR {tenantColumn} = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid)"
                    : tenantColumn + " = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid";
            return $"EXISTS (SELECT 1 FROM {Names.Name(parent.ParentTableName)} {alias} WHERE {key} = {child}.{Names.Quote(parent.ForeignKeyColumn)} AND {tenant})";
        }
    }

    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
