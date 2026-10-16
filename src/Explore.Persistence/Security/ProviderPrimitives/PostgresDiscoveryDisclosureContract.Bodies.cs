using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresDiscoveryDisclosureContract
{
    internal static readonly (string Name, string Signature, string Arguments, string Result, bool Set, string Volatility)[] NestedRoutines =
    [
        ("dependencies", "text,uuid[]", "p_source,p_ids", "uuid", true, "s"),
        ("fence_tenants", "uuid[]", "p_tenants", "void", false, "v"),
        ("finalize", "uuid[],uuid[],boolean[],bigint[],bigint[]",
            "p_tenants,p_ids,p_disclosure,p_expected,p_advances", "void", false, "v")
    ];

    internal static string RoutineAcl(ExploreDbContext context, bool complete)
    {
        string dependencies = FunctionSql(context, "dependencies") + "(text,uuid[])";
        string fence = FunctionSql(context, "fence_tenants") + "(uuid[])";
        string finalize = FunctionSql(context, "finalize") + "(uuid[],uuid[],boolean[],bigint[],bigint[])";
        string[] signatures = [FunctionSql(context, "catalog") + "()", FunctionSql(context, "enrollment") + "()",
            dependencies, fence, finalize];
        IReadOnlyDictionary<string, IReadOnlyList<string>> nestedExecutors =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [dependencies] =
                    ["event_atproto_materialization_owner", "event_subject_erasure_owner",
                        PostgresOrdinarySettingsContract.OwnerRole],
                [fence] =
                    ["event_storage_reconciliation_owner", "event_subject_erasure_owner",
                        "event_subject_erasure_global", PostgresOrdinarySettingsContract.OwnerRole],
                [finalize] =
                    ["event_storage_reconciliation_owner", "event_subject_erasure_global",
                        PostgresOrdinarySettingsContract.OwnerRole]
            };
        return PostgresPrivilegeGuard.BuildRoutineAclAssertSql(signatures, OwnerRole, RuntimeRole, complete, nestedExecutors);
    }

    // Installer and nested callers derive their expected executable bodies from
    // this one source; a replacement body with a restamped comment is not trusted.
    internal static IReadOnlyDictionary<string, string> NestedBodies(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context));
        string Column<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(table)!);
        }
        string currentAcl = RoutineAcl(context, true);
        int maximum = EventDiscoveryDisclosureMutationScope.MaximumAffectedTenants;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dependencies"] = $"""
                DECLARE v_tenants uuid[];
                BEGIN
                    {currentAcl}
                    IF p_source IS NULL OR p_source NOT IN ('record', 'actor', 'user', 'all') OR p_ids IS NULL THEN
                        RAISE EXCEPTION 'invalid discovery dependency source' USING ERRCODE = '22023';
                    END IF;
                    SELECT array_agg(affected.tenant_id ORDER BY affected.tenant_id) INTO v_tenants FROM (
                        SELECT DISTINCT dependency.tenant_id FROM (
                            SELECT e.{Column<Event>(nameof(Event.TenantId))} AS tenant_id FROM {Table<Event>()} e
                            WHERE (p_source = 'record' AND e.{Column<Event>(nameof(Event.AtprotoRecordId))} = ANY(p_ids))
                               OR (p_source = 'actor' AND (e.{Column<Event>(nameof(Event.ActorId))} = ANY(p_ids)
                                   OR e.{Column<Event>(nameof(Event.OrganizerActorId))} = ANY(p_ids)))
                               OR (p_source = 'user' AND e.{Column<Event>(nameof(Event.SubmittedByUserId))} = ANY(p_ids))
                            UNION ALL
                            SELECT p.{Column<AtprotoRecordTenantPresentation>(nameof(AtprotoRecordTenantPresentation.TenantId))}
                            FROM {Table<AtprotoRecordTenantPresentation>()} p
                            WHERE p_source = 'record' AND p.{Column<AtprotoRecordTenantPresentation>(nameof(AtprotoRecordTenantPresentation.AtprotoRecordId))} = ANY(p_ids)
                            UNION ALL
                            SELECT o.{Column<AtprotoOutboundRecordOwnership>(nameof(AtprotoOutboundRecordOwnership.TenantId))}
                            FROM {Table<AtprotoOutboundRecordOwnership>()} o
                            WHERE p_source = 'record' AND o.{Column<AtprotoOutboundRecordOwnership>(nameof(AtprotoOutboundRecordOwnership.AtprotoRecordId))} = ANY(p_ids)
                            UNION ALL
                            SELECT s.{Column<EventSessionSpeaker>(nameof(EventSessionSpeaker.TenantId))} FROM {Table<EventSessionSpeaker>()} s
                            WHERE p_source = 'actor' AND s.{Column<EventSessionSpeaker>(nameof(EventSessionSpeaker.ActorId))} = ANY(p_ids)
                            UNION ALL
                            SELECT s.{Column<EventSeries>(nameof(EventSeries.TenantId))} FROM {Table<EventSeries>()} s
                            WHERE p_source = 'actor' AND s.{Column<EventSeries>(nameof(EventSeries.ActorId))} = ANY(p_ids)
                            UNION ALL
                            SELECT m.{Column<TenantUser>(nameof(TenantUser.TenantId))} FROM {Table<TenantUser>()} m
                            WHERE p_source = 'user' AND m.{Column<TenantUser>(nameof(TenantUser.UserId))} = ANY(p_ids)
                            UNION ALL
                            SELECT s.{Column<StorageObject>(nameof(StorageObject.TenantId))} FROM {Table<StorageObject>()} s
                            JOIN {Table<Actor>()} a ON a.{Column<Actor>(nameof(Actor.Id))} = s.{Column<StorageObject>(nameof(StorageObject.ActorId))}
                            WHERE p_source = 'user' AND a.{Column<Actor>(nameof(Actor.UserId))} = ANY(p_ids)
                            UNION ALL
                            SELECT t.{Column<Tenant>(nameof(Tenant.Id))} FROM {Table<Tenant>()} t WHERE p_source = 'all'
                        ) dependency LIMIT {maximum + 1}
                    ) affected;
                    IF cardinality(v_tenants) > {maximum} THEN
                        RAISE EXCEPTION 'discovery_disclosure_fanout_exceeded' USING ERRCODE = '54000';
                    END IF;
                    RETURN QUERY SELECT unnest(v_tenants);
                END;
                """,
            ["fence_tenants"] = $"""
                DECLARE v_tenant uuid;
                BEGIN
                    {currentAcl}
                    IF p_tenants IS NULL OR cardinality(p_tenants) > {maximum}
                       OR array_position(p_tenants, NULL) IS NOT NULL
                       OR '00000000-0000-0000-0000-000000000000'::uuid = ANY(p_tenants) THEN
                        RAISE EXCEPTION 'discovery_disclosure_fanout_exceeded' USING ERRCODE = '54000';
                    END IF;
                    FOR v_tenant IN SELECT DISTINCT unnest(p_tenants) ORDER BY 1 LOOP
                        UPDATE {Table<Tenant>()} SET {Column<Tenant>(nameof(Tenant.Id))} = {Column<Tenant>(nameof(Tenant.Id))}
                        WHERE {Column<Tenant>(nameof(Tenant.Id))} = v_tenant;
                    END LOOP;
                END;
                """,
            ["finalize"] = $"""
                DECLARE v_index integer; v_tenant uuid;
                BEGIN
                    {currentAcl}
                    IF p_tenants IS NULL OR cardinality(p_tenants) > {maximum}
                       OR cardinality(p_ids) IS DISTINCT FROM cardinality(p_tenants)
                       OR cardinality(p_disclosure) IS DISTINCT FROM cardinality(p_tenants)
                       OR cardinality(p_expected) IS DISTINCT FROM cardinality(p_tenants)
                       OR cardinality(p_advances) IS DISTINCT FROM cardinality(p_tenants)
                       OR array_position(p_ids, NULL) IS NOT NULL
                       OR array_position(p_disclosure, NULL) IS NOT NULL
                       OR array_position(p_advances, NULL) IS NOT NULL
                       OR EXISTS (SELECT 1 FROM unnest(p_advances) value WHERE value < 0)
                       OR (SELECT count(DISTINCT value) FROM unnest(p_tenants) value) <> cardinality(p_tenants) THEN
                        RAISE EXCEPTION 'invalid discovery finalization bounds' USING ERRCODE = '22023';
                    END IF;
                    PERFORM {FunctionSql(context, "fence_tenants")}(p_tenants);
                    FOR v_index IN SELECT i FROM generate_subscripts(p_tenants, 1) i ORDER BY p_tenants[i] LOOP
                        v_tenant := p_tenants[v_index];
                        INSERT INTO {Table<EventDiscoveryRevision>()} ({Column<EventDiscoveryRevision>(nameof(EventDiscoveryRevision.Id))},
                            {Column<EventDiscoveryRevision>(nameof(EventDiscoveryRevision.TenantId))},
                            {Column<EventDiscoveryRevision>(nameof(EventDiscoveryRevision.IdentityEpoch))},
                            {Column<EventDiscoveryRevision>(nameof(EventDiscoveryRevision.DisclosureEpoch))})
                        VALUES (p_ids[v_index], v_tenant, 0, 0)
                        ON CONFLICT ({Column<EventDiscoveryRevision>(nameof(EventDiscoveryRevision.TenantId))}) DO NOTHING;
                        UPDATE {Table<EventDiscoveryRevision>()}
                        SET {Column<EventDiscoveryRevision>(nameof(EventDiscoveryRevision.DisclosureEpoch))} =
                                {Column<EventDiscoveryRevision>(nameof(EventDiscoveryRevision.DisclosureEpoch))} + CASE WHEN p_disclosure[v_index] THEN 1 ELSE 0 END,
                            {Column<EventDiscoveryRevision>(nameof(EventDiscoveryRevision.IdentityEpoch))} =
                                {Column<EventDiscoveryRevision>(nameof(EventDiscoveryRevision.IdentityEpoch))} + p_advances[v_index]
                        WHERE {Column<EventDiscoveryRevision>(nameof(EventDiscoveryRevision.TenantId))} = v_tenant
                          AND (p_expected[v_index] IS NULL OR {Column<EventDiscoveryRevision>(nameof(EventDiscoveryRevision.IdentityEpoch))} = p_expected[v_index]);
                        IF NOT FOUND THEN
                            RAISE EXCEPTION 'discovery_revision_conflict' USING ERRCODE = '40001';
                        END IF;
                    END LOOP;
                END;
                """
        };
    }
}
