using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only bounded observations and exact-target metadata quarantine.
/// No accounting authority, byte deletion, or general storage CRUD is granted.
/// </summary>
public static class PostgresStorageReconciliationContract
{
    public const string OwnerRole = "event_storage_reconciliation_owner";
    public const string MigratorRole = "event_storage_reconciliation_migrator";
    public const string GlobalRole = "event_storage_reconciliation_global";
    private const string PolicyName = "storage_reconciliation_custody";
    private const string Fingerprint = "event-storage-reconciliation:v1:";
    private static readonly string[] SnapshotProperties =
    [
        nameof(StorageObject.Id), nameof(StorageObject.ConcurrencyStamp), nameof(StorageObject.TenantId),
        nameof(StorageObject.Provider), nameof(StorageObject.StorageProviderBindingId), nameof(StorageObject.ObjectKey),
        nameof(StorageObject.ProviderVersionId), nameof(StorageObject.LifecycleState), nameof(StorageObject.IsDeleted),
        nameof(StorageObject.DeletedAt), nameof(StorageObject.CreatedAt), nameof(StorageObject.Purpose),
        nameof(StorageObject.OwningResourceKind), nameof(StorageObject.OwningResourceId), nameof(StorageObject.ActorId)
    ];
    private static readonly string[] WriteProperties =
    [
        nameof(StorageObject.ConcurrencyStamp), nameof(StorageObject.LifecycleState), nameof(StorageObject.QuarantinedAt),
        nameof(StorageObject.QuarantinedBy), nameof(StorageObject.QuarantineReason),
        nameof(StorageObject.UpdatedAt), nameof(StorageObject.UpdatedBy)
    ];

    public static string ListFunctionSql(ExploreDbContext context) => Function(context, "storage_reconciliation_candidates");
    public static string QuarantineFunctionSql(ExploreDbContext context) => Function(context, "storage_reconciliation_quarantine");

    internal static async Task<IReadOnlyList<StorageReconciliationSnapshot>> ListAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, DateTime cutoff, int limit, CancellationToken ct)
    {
        await ValidateReadinessAsync(context, enrollment, ct);
        return await context.Database.SqlQueryRaw<StorageReconciliationSnapshot>(
            $"SELECT * FROM {ListFunctionSql(context)}({{0}}, {{1}})", cutoff, limit).ToArrayAsync(ct);
    }

    internal static async Task<bool> TryQuarantineAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        StorageReconciliationSnapshot snapshot, DateTime utcNow, CancellationToken ct)
    {
        await ValidateReadinessAsync(context, enrollment, ct);
        return await context.Database.SqlQueryRaw<bool>(
            $"SELECT {QuarantineFunctionSql(context)}(@snapshot, @now) AS \"Value\"",
            new NpgsqlParameter("snapshot", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(snapshot) },
            new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = utcNow }).SingleAsync(ct);
    }

    public static async Task ValidateReadinessAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken ct = default)
    {
        enrollment.Validate();
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || context.TenantFilterTenantId is not null)
            throw new InvalidOperationException("storage_reconciliation_global_lease_required");
        var routines = Routines(context, enrollment);
        var readinessParameters = new object[routines.Length * 2];
        string exactBodies = string.Join(" AND ", routines.Select((routine, index) =>
        {
            string signatureParameter = $"routine_signature_{index}";
            string bodyParameter = $"routine_body_{index}";
            readinessParameters[index * 2] = new NpgsqlParameter(signatureParameter, NpgsqlDbType.Text)
            { Value = routine.Signature };
            readinessParameters[index * 2 + 1] = new NpgsqlParameter(bodyParameter, NpgsqlDbType.Text)
            { Value = routine.Body };
            return $"EXISTS (SELECT 1 FROM pg_catalog.pg_proc p WHERE p.oid = pg_catalog.to_regprocedure(@{signatureParameter})" +
                $" AND p.prosrc = @{bodyParameter})";
        }));
        bool ready = await context.Database.SqlQueryRaw<bool>(
            $"""
            SELECT ({PostgresPrivilegeGuard.BuildReadySql(Privileges(context, enrollment))}
                AND {AuthorityReadySql(context, enrollment)} AND {exactBodies}) AS "Value"
            """, readinessParameters).SingleAsync(ct);
        if (!ready)
            throw new InvalidOperationException("storage_reconciliation_authority_not_ready");
    }

    public static async Task ApplyAsync(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        enrollment.Validate();
        if (!context.Database.IsNpgsql()) return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static (Type Type, string[] Columns)[] Relations() =>
    [
        (typeof(StorageObject), SnapshotProperties.Concat(WriteProperties).Distinct().ToArray()),
        (typeof(EventResource), [nameof(EventResource.StorageObjectId)]),
        (typeof(OrganizationTenantEvidence), [nameof(OrganizationTenantEvidence.DocumentStorageObjectId)]),
        (typeof(StorageObjectDeletionTombstone), [nameof(StorageObjectDeletionTombstone.Id)]),
        (typeof(StorageUploadSession), ["Id", "StorageObjectId", "TenantId", "Provider",
            "StorageProviderBindingId", "ObjectKey", "ProviderVersionId", "ProducerSettled",
            "Purpose", "OwningResourceKind", "OwningResourceId"]),
        (typeof(StorageProducerOperation), ["Id", "TenantId", "Provider", "ProviderBindingId",
            "ObjectKey", "ProviderVersionId", "ProducerSettled"]),
        (typeof(StorageProviderBinding), ["Id", "Provider"])
    ];

    private static (string Name, string Arguments, string Volatility)[] RoutineMetadata() =>
    [
        ("storage_reconciliation_candidates", "timestamp with time zone,integer", "s"),
        ("storage_reconciliation_quarantine", "jsonb,timestamp with time zone", "v")
    ];

    private static PostgresPrivilegeGuard.Contract Privileges(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        new(Schema(context), [OwnerRole, MigratorRole, GlobalRole],
            RoutineMetadata().Select(routine => new PostgresPrivilegeGuard.Routine(
                routine.Name, routine.Arguments, OwnerRole, [GlobalRole])).ToArray(),
            Relations().Select(relation =>
            {
                var entity = context.Model.FindEntityType(relation.Type)!;
                var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
                return new PostgresPrivilegeGuard.Relation(mapping.Name, OwnerRole, [],
                    relation.Columns.Select(name => new PostgresPrivilegeGuard.Column(
                        entity.FindProperty(name)!.GetColumnName(mapping)!,
                        relation.Type == typeof(StorageObject) && WriteProperties.Contains(name, StringComparer.Ordinal)
                            ? ["SELECT", "UPDATE"] : ["SELECT"])).ToArray());
            }).ToArray(),
            [
                new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(GlobalRole, enrollment.GlobalLogin, true, false, false)
            ]);

    private static string EligibleSql(ExploreDbContext context)
    {
        string S(string name) => "s." + Column(context, typeof(StorageObject), name);
        string U(string name) => "u." + Column(context, typeof(StorageUploadSession), name);
        string P(string name) => "p." + Column(context, typeof(StorageProducerOperation), name);
        string sessions = Table(context, typeof(StorageUploadSession));
        string producers = Table(context, typeof(StorageProducerOperation));
        string sessionIdentity = $"{U("StorageObjectId")} = {S("Id")} OR {U("Id")} = {S("Id")}";
        string VersionMatch(string version) => $"({version} IS NULL OR {version} IS NOT DISTINCT FROM {S("ProviderVersionId")})";
        return $"""
            NOT {S("IsDeleted")} AND {S("DeletedAt")} IS NULL AND {S("LifecycleState")} = '{StorageObjectLifecycleStates.Active}'
            AND {S("Provider")} IN ('{StorageProviders.Local}', '{StorageProviders.S3Compatible}')
            AND {S("StorageProviderBindingId")} IS NOT NULL AND {S("ObjectKey")} IS NOT NULL
            AND length({S("ObjectKey")}) BETWEEN 1 AND 1024
            AND {S("Purpose")} <> '{StorageObjectPurposes.EventResource}'
            AND {S("OwningResourceKind")} IS DISTINCT FROM '{StorageOwningResourceKinds.EventResource}'
            AND NOT EXISTS (SELECT 1 FROM {Table(context, typeof(EventResource))} r
                WHERE r.{Column(context, typeof(EventResource), "StorageObjectId")} = {S("Id")})
            AND NOT EXISTS (SELECT 1 FROM {Table(context, typeof(OrganizationTenantEvidence))} e
                WHERE e.{Column(context, typeof(OrganizationTenantEvidence), "DocumentStorageObjectId")} = {S("Id")})
            AND NOT EXISTS (SELECT 1 FROM {Table(context, typeof(StorageObjectDeletionTombstone))} t
                WHERE t.{Column(context, typeof(StorageObjectDeletionTombstone), "Id")} = {S("Id")})
            AND EXISTS (SELECT 1 FROM {Table(context, typeof(StorageProviderBinding))} b
                WHERE b.{Column(context, typeof(StorageProviderBinding), "Id")} = {S("StorageProviderBindingId")}
                  AND b.{Column(context, typeof(StorageProviderBinding), "Provider")} = {S("Provider")})
            AND (SELECT count(*) FROM {sessions} u WHERE {sessionIdentity}) <= 1
            AND NOT (EXISTS (SELECT 1 FROM {sessions} u WHERE {sessionIdentity})
                AND EXISTS (SELECT 1 FROM {producers} p WHERE {P("Id")} = {S("Id")}))
            AND NOT EXISTS (SELECT 1 FROM {sessions} u WHERE ({sessionIdentity}) AND (
                NOT {U("ProducerSettled")} OR {U("TenantId")} IS DISTINCT FROM {S("TenantId")}
                OR COALESCE({U("StorageObjectId")}, {U("Id")}) IS DISTINCT FROM {S("Id")}
                OR {U("Provider")} IS DISTINCT FROM {S("Provider")}
                OR {U("StorageProviderBindingId")} IS DISTINCT FROM {S("StorageProviderBindingId")}
                OR {U("ObjectKey")} IS DISTINCT FROM {S("ObjectKey")} OR NOT ({VersionMatch(U("ProviderVersionId"))})
                OR {U("Purpose")} IS DISTINCT FROM {S("Purpose")}
                OR {U("OwningResourceKind")} IS DISTINCT FROM {S("OwningResourceKind")}
                OR {U("OwningResourceId")} IS DISTINCT FROM {S("OwningResourceId")}))
            AND NOT EXISTS (SELECT 1 FROM {producers} p WHERE {P("Id")} = {S("Id")} AND (
                NOT {P("ProducerSettled")} OR {P("TenantId")} IS DISTINCT FROM {S("TenantId")}
                OR {P("Provider")} IS DISTINCT FROM {S("Provider")}
                OR {P("ProviderBindingId")} IS DISTINCT FROM {S("StorageProviderBindingId")}
                OR {P("ObjectKey")} IS DISTINCT FROM {S("ObjectKey")} OR NOT ({VersionMatch(P("ProviderVersionId"))})))
            """;
    }

    private static (string Signature, string Declaration, string Body, string Volatility)[] Routines(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var entity = context.Model.FindEntityType(typeof(StorageObject))!;
        string Type(string name) => entity.FindProperty(name)!.GetRelationalTypeMapping().StoreType;
        string S(string name) => "s." + Column(context, typeof(StorageObject), name);
        string snapshotReturns = string.Join(", ", SnapshotProperties.Select(name =>
            $"{Column(context, typeof(StorageObject), name)} {Type(name)}")) +
            ", eligibility_cutoff_utc timestamp with time zone";
        string ready = AuthorityReadySql(context, enrollment);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(Privileges(context, enrollment));
        string listBody = $"""
            BEGIN
                {guard}
                IF ({ready}) IS NOT TRUE THEN
                    RAISE EXCEPTION 'storage_reconciliation_authority_not_ready' USING ERRCODE = '55000';
                END IF;
                IF p_cutoff IS NULL OR p_limit IS NULL OR p_limit < 1 OR p_limit > {IStorageReconciliationOwner.MaximumCandidates} THEN
                    RAISE EXCEPTION 'storage_reconciliation_page_invalid' USING ERRCODE = '22023';
                END IF;
                RETURN QUERY SELECT {string.Join(", ", SnapshotProperties.Select(S))}, p_cutoff
                    FROM {Table(context, typeof(StorageObject))} s
                    WHERE {EligibleSql(context)} AND {S("CreatedAt")} <= p_cutoff
                    ORDER BY {S("CreatedAt")}, {S("Id")} LIMIT p_limit;
            END;
            """;
        string equality = string.Join(" AND ", SnapshotProperties.Select(name =>
            $"{S(name)} IS NOT DISTINCT FROM (p_snapshot->>{Text(name)})::{Type(name)}"));
        string Q(string name) => Column(context, typeof(StorageObject), name);
        string quarantineBody = $"""
            DECLARE v_id uuid; v_tenant uuid; v_stamp uuid; v_rows integer;
                v_cutoff timestamp with time zone;
            BEGIN
                {guard}
                IF ({ready}) IS NOT TRUE THEN
                    RAISE EXCEPTION 'storage_reconciliation_authority_not_ready' USING ERRCODE = '55000';
                END IF;
                v_id := (p_snapshot->>'Id')::uuid;
                v_tenant := (p_snapshot->>'TenantId')::uuid;
                v_stamp := (p_snapshot->>'ConcurrencyStamp')::uuid;
                v_cutoff := (p_snapshot->>'EligibilityCutoffUtc')::timestamp with time zone;
                IF p_now IS NULL OR v_cutoff IS NULL OR v_id IS NULL OR v_tenant IS NULL OR v_stamp IS NULL
                    OR v_id = '00000000-0000-0000-0000-000000000000'::uuid
                    OR v_tenant = '00000000-0000-0000-0000-000000000000'::uuid THEN
                    RETURN false;
                END IF;
                PERFORM {PostgresDiscoveryDisclosureContract.FunctionSql(context, "fence_tenants")}(ARRAY[v_tenant]);
                -- Current-row fence precedes eligibility; do not replace the captured stamp.
                UPDATE {Table(context, typeof(StorageObject))} s SET {Q("ConcurrencyStamp")} = s.{Q("ConcurrencyStamp")}
                    WHERE {S("Id")} = v_id AND {S("TenantId")} = v_tenant AND {S("ConcurrencyStamp")} = v_stamp;
                GET DIAGNOSTICS v_rows = ROW_COUNT;
                IF v_rows <> 1 THEN RETURN false; END IF;
                IF NOT EXISTS (SELECT 1 FROM {Table(context, typeof(StorageObject))} s
                    WHERE {S("Id")} = v_id AND {equality} AND {S("CreatedAt")} <= v_cutoff AND {EligibleSql(context)}) THEN
                    RETURN false;
                END IF;
                -- The shared storage-reference stamp is changed only for this original target.
                UPDATE {Table(context, typeof(StorageObject))} s SET {Q("ConcurrencyStamp")} = uuidv7(),
                    {Q("LifecycleState")} = '{StorageObjectLifecycleStates.Quarantined}',
                    {Q("QuarantinedAt")} = p_now, {Q("QuarantinedBy")} = NULL,
                    {Q("QuarantineReason")} = 'backing_object_missing', {Q("UpdatedAt")} = p_now, {Q("UpdatedBy")} = NULL
                    WHERE {S("Id")} = v_id AND {S("TenantId")} = v_tenant AND {S("ConcurrencyStamp")} = v_stamp;
                GET DIAGNOSTICS v_rows = ROW_COUNT;
                IF v_rows <> 1 THEN RAISE EXCEPTION 'storage_reconciliation_conflict' USING ERRCODE = '40001'; END IF;
                PERFORM pg_catalog.pg_advisory_xact_lock(
                    ('x' || substr(encode(sha256(convert_to('discovery-epoch:' || replace(v_tenant::text, '-', ''), 'UTF8')), 'hex'), 1, 16))::bit(64)::bigint);
                PERFORM {PostgresDiscoveryDisclosureContract.FunctionSql(context, "finalize")}(
                    ARRAY[v_tenant], ARRAY[uuidv7()], ARRAY[true], ARRAY[NULL::bigint], ARRAY[0::bigint]);
                RETURN true;
            END;
            """;
        return
        [
            (ListFunctionSql(context) + "(timestamp with time zone,integer)",
                $"{ListFunctionSql(context)}(p_cutoff timestamp with time zone, p_limit integer) RETURNS TABLE ({snapshotReturns})",
                listBody, "STABLE"),
            (QuarantineFunctionSql(context) + "(jsonb,timestamp with time zone)",
                $"{QuarantineFunctionSql(context)}(p_snapshot jsonb, p_now timestamp with time zone) RETURNS boolean",
                quarantineBody, "VOLATILE")
        ];
    }

    private static string AuthorityReadySql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var relations = Relations();
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string expectedPolicies = string.Join(", ", relations.Where(relation => tenantTables
            .Any(table => table.TableName == context.Model.FindEntityType(relation.Type)!.GetTableName()))
            .Select(relation =>
            {
                var table = tenantTables.Single(table => table.TableName == context.Model.FindEntityType(relation.Type)!.GetTableName());
                return $"({Text(Table(context, relation.Type))}::regclass, " +
                    $"{Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(table, Schema(context)))}, " +
                    $"{(relation.Type == typeof(StorageObject) ? "'*'" : "'r'")})";
            }));
        string ownerColumns = string.Join(" OR ", relations.SelectMany(relation =>
        {
            var entity = context.Model.FindEntityType(relation.Type)!;
            return entity.GetProperties().SelectMany(property =>
            {
                string column = Text(property.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
                var denied = new List<string> { "INSERT", "REFERENCES" };
                if (!relation.Columns.Contains(property.Name, StringComparer.Ordinal)) denied.Add("SELECT");
                if (relation.Type != typeof(StorageObject) || !WriteProperties.Contains(property.Name, StringComparer.Ordinal))
                    denied.Add("UPDATE");
                return denied.Select(privilege =>
                    $"pg_catalog.has_column_privilege('{OwnerRole}', {Text(Table(context, relation.Type))}::regclass, {column}, '{privilege}')");
            });
        }));
        string requiredColumns = string.Join(" AND ", relations.SelectMany(relation =>
            relation.Columns.Select(name =>
                $"pg_catalog.has_column_privilege('{OwnerRole}', {Text(Table(context, relation.Type))}::regclass, " +
                $"{Text(context.Model.FindEntityType(relation.Type)!.FindProperty(name)!.GetColumnName(StoreObjectIdentifier.Table(
                    context.Model.FindEntityType(relation.Type)!.GetTableName()!, context.Model.FindEntityType(relation.Type)!.GetSchema()))!)}, 'SELECT')"))
            .Concat(WriteProperties.Select(name =>
                $"pg_catalog.has_column_privilege('{OwnerRole}', {Text(Table(context, typeof(StorageObject)))}::regclass, " +
                $"{Text(context.Model.FindEntityType(typeof(StorageObject))!.FindProperty(name)!.GetColumnName(StoreObjectIdentifier.Table(
                    context.Model.FindEntityType(typeof(StorageObject))!.GetTableName()!, context.Model.FindEntityType(typeof(StorageObject))!.GetSchema()))!)}, 'UPDATE')")));
        string signatures = string.Join(", ", RoutineMetadata().Select(routine =>
            $"({Text(Function(context, routine.Name) + "(" + routine.Arguments + ")")}, '{routine.Volatility}')"));
        return $"""
            (SELECT count(*) FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')
                AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                AND NOT rolcreaterole AND NOT rolreplication AND NOT rolinherit) = 3
            AND session_user::text = {Text(enrollment.GlobalLogin)}
            AND pg_catalog.pg_has_role(session_user, '{GlobalRole}', 'USAGE')
            AND NOT pg_catalog.pg_has_role(session_user, '{OwnerRole}', 'MEMBER')
            AND NOT pg_catalog.pg_has_role(session_user, '{MigratorRole}', 'MEMBER')
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user, r.oid, 'USAGE') OR pg_catalog.pg_has_role(session_user, r.oid, 'SET'))
                  AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members e
                WHERE e.member IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}', '{GlobalRole}'))
                   OR e.member = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{MigratorRole}')
                      AND (e.roleid <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                           OR e.inherit_option OR NOT e.set_option OR e.admin_option)
                   OR e.roleid = (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                      AND e.member <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{MigratorRole}'))
            AND NOT ({ownerColumns})
            AND {requiredColumns}
            AND {PostgresManagedTenantWorkerCreationPermissions.GlobalApplicationCataloguePredicate(context, enrollment)}
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class r
                JOIN pg_catalog.pg_namespace n ON n.oid = r.relnamespace
                WHERE n.nspname = {Text(Schema(context))} AND r.relkind IN ('r','p','v','m','f') AND (
                    pg_catalog.has_table_privilege('{GlobalRole}', r.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR pg_catalog.has_any_column_privilege('{GlobalRole}', r.oid, 'SELECT,INSERT,UPDATE,REFERENCES')
                    OR pg_catalog.has_table_privilege('{OwnerRole}', r.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR r.oid NOT IN ({string.Join(", ", relations.Select(relation => Text(Table(context, relation.Type)) + "::regclass"))})
                       AND pg_catalog.has_any_column_privilege('{OwnerRole}', r.oid, 'SELECT,INSERT,UPDATE,REFERENCES')))
            AND NOT pg_catalog.has_schema_privilege('{OwnerRole}', {Text(Schema(context))}, 'CREATE')
            AND NOT pg_catalog.has_schema_privilege('{GlobalRole}', {Text(Schema(context))}, 'CREATE')
            AND NOT pg_catalog.has_schema_privilege(session_user, {Text(Schema(context))}, 'CREATE')
            AND pg_catalog.has_function_privilege('{OwnerRole}',
                {Text(PostgresDiscoveryDisclosureContract.FunctionSql(context, "fence_tenants") + "(uuid[])")}, 'EXECUTE')
            AND pg_catalog.has_function_privilege('{OwnerRole}',
                {Text(PostgresDiscoveryDisclosureContract.FunctionSql(context, "finalize") + "(uuid[],uuid[],boolean[],bigint[],bigint[])")}, 'EXECUTE')
            AND NOT EXISTS (SELECT 1 FROM (VALUES {signatures}) expected(signature, volatility)
                LEFT JOIN pg_catalog.pg_proc p ON p.oid = pg_catalog.to_regprocedure(expected.signature)
                WHERE p.oid IS NULL OR p.proowner <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')
                   OR NOT p.prosecdef OR p.provolatile::text <> expected.volatility
                   OR p.proconfig IS DISTINCT FROM ARRAY['search_path=pg_catalog']::text[]
                   OR pg_catalog.obj_description(p.oid, 'pg_proc') IS DISTINCT FROM '{Fingerprint}' || pg_catalog.md5(p.prosrc)
                   OR NOT pg_catalog.has_function_privilege('{GlobalRole}', p.oid, 'EXECUTE')
                   OR EXISTS (SELECT 1 FROM pg_catalog.aclexplode(COALESCE(p.proacl, pg_catalog.acldefault('f', p.proowner))) a
                       WHERE a.grantee NOT IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}','{GlobalRole}'))
                          OR a.privilege_type <> 'EXECUTE' OR a.is_grantable AND a.grantee <> p.proowner))
            AND NOT EXISTS (SELECT 1 FROM (VALUES {expectedPolicies}) expected(relation_id, tenant_fingerprint, command)
                LEFT JOIN pg_catalog.pg_class r ON r.oid = expected.relation_id
                LEFT JOIN pg_catalog.pg_policy p ON p.polrelid = r.oid AND p.polname = '{PolicyName}'
                LEFT JOIN pg_catalog.pg_policy t ON t.polrelid = r.oid AND t.polname = 'tenant_isolation'
                WHERE r.oid IS NULL OR NOT r.relrowsecurity OR NOT r.relforcerowsecurity OR p.oid IS NULL
                   OR p.polcmd::text <> expected.command OR NOT p.polpermissive
                   OR p.polroles <> ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')]
                   OR pg_catalog.pg_get_expr(p.polqual, p.polrelid) IS DISTINCT FROM 'true'
                   OR (expected.command = '*' AND pg_catalog.pg_get_expr(p.polwithcheck, p.polrelid) IS DISTINCT FROM 'true')
                   OR (expected.command = 'r' AND p.polwithcheck IS NOT NULL)
                   OR pg_catalog.obj_description(p.oid, 'pg_policy') IS DISTINCT FROM
                        '{Fingerprint}' || pg_catalog.md5(p.polqual::text) || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text), '')
                   OR t.oid IS NULL OR t.polcmd <> '*' OR NOT t.polpermissive OR t.polroles <> ARRAY[0::oid]
                   OR t.polqual IS NULL OR t.polwithcheck IS NULL
                   OR pg_catalog.obj_description(t.oid, 'pg_policy') IS DISTINCT FROM
                        expected.tenant_fingerprint || pg_catalog.md5(t.polqual::text) || ':' || pg_catalog.md5(t.polwithcheck::text)
                   OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy other WHERE other.polrelid = r.oid
                        AND other.polname NOT IN ('{PolicyName}', 'tenant_isolation') AND other.polpermissive
                        AND EXISTS (SELECT 1 FROM unnest(other.polroles) role_id
                            WHERE role_id = 0 OR pg_catalog.pg_has_role('{OwnerRole}', role_id, 'USAGE'))))
            """;
    }

    public static string BuildSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var contract = Privileges(context, enrollment);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(contract);
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        string grants = string.Join("\n", Relations().Select(relation =>
        {
            string table = Table(context, relation.Type);
            string read = $"""
                REVOKE ALL ON {table} FROM {OwnerRole}, {GlobalRole};
                GRANT SELECT ({string.Join(", ", relation.Columns.Select(name => Column(context, relation.Type, name)))})
                    ON {table} TO {OwnerRole};
                """;
            if (!tenantTables.Any(candidate => candidate.TableName == context.Model.FindEntityType(relation.Type)!.GetTableName()))
                return read;
            bool writable = relation.Type == typeof(StorageObject);
            return read + $"""

                DROP POLICY IF EXISTS "{PolicyName}" ON {table};
                CREATE POLICY "{PolicyName}" ON {table} FOR {(writable ? "ALL" : "SELECT")} TO {OwnerRole}
                    USING (true) {(writable ? "WITH CHECK (true)" : "")};
                DO $stamp$
                BEGIN
                    EXECUTE (SELECT format('COMMENT ON POLICY %I ON %s IS %L', p.polname, {Text(table)},
                        '{Fingerprint}' || md5(p.polqual::text) || ':' || COALESCE(md5(p.polwithcheck::text), ''))
                        FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(table)}::regclass AND p.polname = '{PolicyName}');
                END $stamp$;
                """;
        }));
        string functions = string.Join("\n", Routines(context, enrollment).Select(routine => $"""
            EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {routine.Declaration}
                LANGUAGE plpgsql {routine.Volatility} SECURITY DEFINER SET search_path = pg_catalog
                AS $body${routine.Body}$body$ $ddl$;
            EXECUTE 'REVOKE ALL ON FUNCTION {routine.Signature} FROM PUBLIC, {GlobalRole}';
            EXECUTE 'GRANT EXECUTE ON FUNCTION {routine.Signature} TO {GlobalRole}';
            EXECUTE (SELECT format('COMMENT ON FUNCTION %s IS %L', {Text(routine.Signature)},
                '{Fingerprint}' || md5(p.prosrc)) FROM pg_catalog.pg_proc p
                WHERE p.oid = {Text(routine.Signature)}::regprocedure);
            """));
        return $"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                IF session_user::text IS DISTINCT FROM {Text(enrollment.MigratorLogin)}
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = {Text(enrollment.MigratorLogin)} AND rolcanlogin)
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = {Text(enrollment.GlobalLogin)}
                        AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                        AND NOT rolcreaterole AND NOT rolreplication) THEN
                    RAISE EXCEPTION 'storage_reconciliation_enrollment_invalid' USING ERRCODE = '55000';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles
                    WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{GlobalRole}')) THEN
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_proc routine
                        JOIN pg_catalog.pg_namespace namespace ON namespace.oid = routine.pronamespace
                        WHERE namespace.nspname = {Text(Schema(context))}
                            AND routine.proname IN ({string.Join(", ", contract.Routines.Select(routine => Text(routine.Name)))}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_policy policy
                            JOIN pg_catalog.pg_class relation ON relation.oid = policy.polrelid
                            JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                            WHERE namespace.nspname = {Text(Schema(context))} AND policy.polname = '{PolicyName}') THEN
                        RAISE EXCEPTION 'storage_reconciliation_partial_bootstrap' USING ERRCODE = '55000';
                    END IF;
                    FOREACH role_name IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                    END LOOP;
                    GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
                    EXECUTE format('GRANT {GlobalRole} TO %I WITH INHERIT TRUE, SET FALSE, ADMIN FALSE', {Text(enrollment.GlobalLogin)});
                    EXECUTE format('GRANT {MigratorRole} TO %I WITH INHERIT FALSE, SET TRUE, ADMIN FALSE', {Text(enrollment.MigratorLogin)});
                    -- Creator administration is needed until all declared grants exist.
                    FOREACH role_name IN ARRAY ARRAY['{OwnerRole}', '{GlobalRole}'] LOOP
                        IF EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                            JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                            JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                            WHERE parent.rolname = role_name AND member.rolname = {Text(enrollment.MigratorLogin)}) THEN
                            EXECUTE format('REVOKE %I FROM %I', role_name, {Text(enrollment.MigratorLogin)});
                        END IF;
                    END LOOP;
                ELSE
                    {guard}
                END IF;
            END $roles$;
            GRANT USAGE, CREATE ON SCHEMA {context.GetService<ISqlGenerationHelper>().DelimitIdentifier(Schema(context))} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {context.GetService<ISqlGenerationHelper>().DelimitIdentifier(Schema(context))} TO {GlobalRole};
            {grants}
            GRANT UPDATE ({string.Join(", ", WriteProperties.Select(name => Column(context, typeof(StorageObject), name)))})
                ON {Table(context, typeof(StorageObject))} TO {OwnerRole};
            DO $disclosure_grants$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{PostgresDiscoveryDisclosureContract.OwnerRole}', true);
                EXECUTE 'GRANT EXECUTE ON FUNCTION {PostgresDiscoveryDisclosureContract.FunctionSql(context, "fence_tenants")}(uuid[]),
                    {PostgresDiscoveryDisclosureContract.FunctionSql(context, "finalize")}(uuid[],uuid[],boolean[],bigint[],bigint[]) TO {OwnerRole}';
                PERFORM set_config('role', previous_role, true);
            END $disclosure_grants$;
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                {functions}
                PERFORM set_config('role', previous_role, true);
            END $install$;
            REVOKE CREATE ON SCHEMA {context.GetService<ISqlGenerationHelper>().DelimitIdentifier(Schema(context))} FROM {OwnerRole};
            DO $contract$
            BEGIN
                {guard}
            END $contract$;
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(StorageObject))!.GetSchema() ?? "public";

    private static string Table(ExploreDbContext context, Type type) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Model.FindEntityType(type)!.GetTableName()!, Schema(context));

    private static string Column(ExploreDbContext context, Type type, string name)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.FindProperty(name)!
            .GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
    }

    private static string Function(ExploreDbContext context, string name) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, Schema(context));

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
