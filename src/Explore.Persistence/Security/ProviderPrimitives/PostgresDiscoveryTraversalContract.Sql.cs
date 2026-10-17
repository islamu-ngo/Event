using Explore.Domain;
using Explore.Domain.Services.Discovery;

namespace Explore.Persistence.Security;

public static partial class PostgresDiscoveryTraversalContract
{
    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, string operation)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string T<T>() => sql.Table(typeof(T));
        string C<T>(string property) => sql.Column(typeof(T), property);
        string revisions = T<EventDiscoveryRevision>(), reservations = T<EventDiscoverySnapshotReservation>();
        string snapshots = T<EventDiscoverySnapshot>(), items = T<EventDiscoverySnapshotItem>();
        string revisionTenant = C<EventDiscoveryRevision>("TenantId"), identity = C<EventDiscoveryRevision>("IdentityEpoch");
        string disclosure = C<EventDiscoveryRevision>("DisclosureEpoch"), reservationTenant = C<EventDiscoverySnapshotReservation>("TenantId");
        string snapshotTenant = C<EventDiscoverySnapshot>("TenantId"), snapshotId = C<EventDiscoverySnapshot>("Id");
        string itemTenant = C<EventDiscoverySnapshotItem>("TenantId"), itemSnapshot = C<EventDiscoverySnapshotItem>("SnapshotId");
        string expiry = C<EventDiscoverySnapshot>("ExpiresAtUtc");
        string transaction = """
            IF pg_catalog.current_setting('transaction_isolation') NOT IN ('read committed','repeatable read','serializable')
            THEN RAISE EXCEPTION 'discovery_traversal_isolation' USING ERRCODE = '42501'; END IF;
            """;
        string serializable = """
            IF pg_catalog.current_setting('transaction_isolation') <> 'serializable'
            THEN RAISE EXCEPTION 'discovery_traversal_serializable_required' USING ERRCODE = '42501'; END IF;
            """;
        // xmin is native transaction state written by our mandatory physical no-op, not a caller-set GUC.
        string ownTransaction = "(pg_catalog.pg_current_xact_id()::text::numeric % 4294967296)::text::xid";
        string reservationFence = $"""
            {serializable}
            IF NOT EXISTS (SELECT 1 FROM {reservations} r WHERE r.{reservationTenant} = p_tenant AND r.xmin = {ownTransaction})
            THEN RAISE EXCEPTION 'discovery_traversal_reservation_required' USING ERRCODE = '42501'; END IF;
            """;
        string caller = $"""
            {Guard(context, enrollment)}
            {PostgresPrivilegeGuard.RestrictedCallerSql}
            IF session_user <> {Text(enrollment.RuntimeLogin)} OR current_user <> '{OwnerRole}'
                OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                OR p_tenant IS NULL OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
            THEN RAISE EXCEPTION 'discovery_traversal_original_runtime_tenant' USING ERRCODE = '42501'; END IF;
            """;
        string declarations = "";
        string work = operation switch
        {
            "ready" => "RETURN;",
            "tenant_fence" => $"""
                {transaction}
                UPDATE {T<Tenant>()} SET {C<Tenant>("Id")} = {C<Tenant>("Id")} WHERE {C<Tenant>("Id")} = p_tenant;
                """,
            "reservation_fence" => $"""
                {serializable}
                INSERT INTO {reservations} ({reservationTenant}) VALUES (p_tenant) ON CONFLICT ({reservationTenant}) DO NOTHING;
                UPDATE {reservations} SET {reservationTenant} = {reservationTenant} WHERE {reservationTenant} = p_tenant;
                IF NOT FOUND THEN RAISE EXCEPTION 'discovery_traversal_reservation_missing' USING ERRCODE = '55000'; END IF;
                """,
            "revision_read" => $"RETURN QUERY SELECT r.* FROM {revisions} r WHERE r.{revisionTenant} = p_tenant;",
            "revision_acquire" => $"""
                {transaction}
                IF p_id IS NULL OR p_id = '00000000-0000-0000-0000-000000000000'::uuid
                THEN RAISE EXCEPTION 'discovery_traversal_revision_id' USING ERRCODE = '22023'; END IF;
                UPDATE {revisions} SET {disclosure} = {disclosure} WHERE {revisionTenant} = p_tenant;
                PERFORM 1 FROM {revisions} r WHERE r.{revisionTenant} = p_tenant FOR UPDATE;
                IF NOT FOUND THEN
                    INSERT INTO {revisions} ({C<EventDiscoveryRevision>("Id")},{revisionTenant},{identity},{disclosure})
                        VALUES (p_id,p_tenant,0,0);
                END IF;
                RETURN QUERY SELECT r.* FROM {revisions} r WHERE r.{revisionTenant} = p_tenant FOR UPDATE;
                """,
            "revision_write" => $"""
                {transaction}
                IF p_identity IS NULL OR p_disclosure IS NULL OR p_identity < 0 OR p_disclosure < 0
                THEN RAISE EXCEPTION 'discovery_traversal_revision_bounds' USING ERRCODE = '22023'; END IF;
                UPDATE {revisions} r SET {identity} = p_identity, {disclosure} = p_disclosure
                    WHERE r.{revisionTenant} = p_tenant AND r.xmin = {ownTransaction}
                        AND r.{identity} <= p_identity AND r.{disclosure} <= p_disclosure;
                IF NOT FOUND THEN RAISE EXCEPTION 'discovery_traversal_revision_fence' USING ERRCODE = '40001'; END IF;
                RETURN 1;
                """,
            "snapshot_get" => $"RETURN QUERY SELECT s.* FROM {snapshots} s WHERE s.{snapshotTenant} = p_tenant AND s.{snapshotId} = p_id;",
            "snapshot_reuse" => $"""
                IF p_now IS NULL OR NOT pg_catalog.isfinite(p_now) OR p_hash IS NULL OR length(p_hash) <> 64 OR p_hash !~ '^[0-9a-f]+$'
                    OR p_identity IS NULL OR p_identity < 0 OR p_disclosure IS NULL OR p_disclosure < 0
                THEN RAISE EXCEPTION 'discovery_traversal_reuse_bounds' USING ERRCODE = '22023'; END IF;
                RETURN QUERY SELECT s.* FROM {snapshots} s WHERE s.{snapshotTenant} = p_tenant
                    AND s.{C<EventDiscoverySnapshot>("CriteriaHash")} = p_hash
                    AND s.{C<EventDiscoverySnapshot>("IdentityEpoch")} = p_identity
                    AND s.{C<EventDiscoverySnapshot>("DisclosureEpoch")} = p_disclosure
                    AND s.{C<EventDiscoverySnapshot>("CreatedAtUtc")} <= p_now AND s.{expiry} > p_now
                    ORDER BY s.{expiry} DESC,s.{snapshotId} LIMIT 1;
                """,
            "snapshot_items" => $"""
                IF p_after IS NULL OR p_after < -1 OR p_take IS NULL OR p_take NOT BETWEEN 1 AND {EventDiscoveryTraversalLimits.PageSizeCeiling}
                THEN RAISE EXCEPTION 'discovery_traversal_page_bounds' USING ERRCODE = '22023'; END IF;
                RETURN QUERY SELECT i.* FROM {items} i WHERE i.{itemTenant} = p_tenant AND i.{itemSnapshot} = p_id
                    AND i.{C<EventDiscoverySnapshotItem>("Ordinal")} > p_after
                    ORDER BY i.{C<EventDiscoverySnapshotItem>("Ordinal")} LIMIT p_take;
                """,
            "snapshot_purge" => $"""
                {reservationFence}
                IF p_now IS NULL OR NOT pg_catalog.isfinite(p_now)
                    OR p_take IS NULL OR p_take NOT BETWEEN 1 AND {EventDiscoveryTraversalLimits.PurgeBatchCeiling}
                THEN RAISE EXCEPTION 'discovery_traversal_purge_bounds' USING ERRCODE = '22023'; END IF;
                SELECT array_agg(expired.id) INTO selected_ids FROM
                    (SELECT s.{snapshotId} AS id FROM {snapshots} s
                        WHERE s.{snapshotTenant} = p_tenant AND s.{expiry} <= LEAST(p_now,statement_timestamp())
                        ORDER BY s.{expiry},s.{snapshotId} LIMIT p_take) expired;
                DELETE FROM {items} WHERE {itemTenant} = p_tenant AND {itemSnapshot} = ANY(selected_ids);
                DELETE FROM {snapshots} WHERE {snapshotTenant} = p_tenant AND {snapshotId} = ANY(selected_ids);
                GET DIAGNOSTICS removed = ROW_COUNT;
                RETURN removed;
                """,
            "snapshot_capture" => CaptureBody(context, reservationFence, ownTransaction),
            "bindings" or "aliases" => BindingBody(context, operation),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        if (operation == "snapshot_purge") declarations = "DECLARE selected_ids uuid[]; removed integer;";
        if (operation == "snapshot_capture")
            declarations = $"DECLARE header {snapshots}%ROWTYPE; reusable uuid; item_count integer; live_count bigint; physical_count bigint;";
        return $"""
            {declarations}
            BEGIN
                {caller}
                {work}
            END
            """;
    }

    private static string CaptureBody(ExploreDbContext context, string reservationFence, string ownTransaction)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string T<T>() => sql.Table(typeof(T));
        string C<T>(string property) => sql.Column(typeof(T), property);
        string S(string property) => C<EventDiscoverySnapshot>(property);
        string I(string property) => C<EventDiscoverySnapshotItem>(property);
        string R(string property) => C<EventDiscoveryRevision>(property);
        string headerMap = string.Join(",", context.Model.FindEntityType(typeof(EventDiscoverySnapshot))!.GetProperties()
            .Select(property => $"{Text(sql.ColumnName(typeof(EventDiscoverySnapshot), property.Name))},p_header->{Text(property.Name)}"));
        return $"""
            {reservationFence}
            IF p_now IS NULL OR NOT pg_catalog.isfinite(p_now)
                OR p_identities IS NULL OR p_identities NOT BETWEEN 1 AND {EventDiscoveryTraversalLimits.IdentityCeiling}
                OR p_lifetime IS NULL OR p_lifetime NOT BETWEEN 1 AND {EventDiscoveryTraversalLimits.LifetimeMinutesCeiling}
                OR p_live IS NULL OR p_live NOT BETWEEN 1 AND {EventDiscoveryTraversalLimits.LiveSnapshotCeiling}
                OR p_physical IS NULL OR p_physical NOT BETWEEN 1 AND {EventDiscoveryTraversalLimits.PhysicalItemCeiling}
                OR pg_catalog.jsonb_typeof(p_header) IS DISTINCT FROM 'object'
                OR pg_catalog.jsonb_typeof(p_items) IS DISTINCT FROM 'array'
            THEN RAISE EXCEPTION 'discovery_traversal_capture_bounds' USING ERRCODE = '22023'; END IF;
            item_count := pg_catalog.jsonb_array_length(p_items);
            IF item_count > p_identities OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(p_header)) <> 11
                OR NOT p_header ?& ARRAY['Id','TenantId','CriteriaHash','IdentityEpoch','DisclosureEpoch','CreatedAtUtc',
                    'ExpiresAtUtc','ItemCount','Truncated','LocalSourceComplete','RemoteSourceComplete']
            THEN RAISE EXCEPTION 'discovery_traversal_capture_shape' USING ERRCODE = '22023'; END IF;
            SELECT * INTO header FROM pg_catalog.jsonb_populate_record(NULL::{T<EventDiscoverySnapshot>()},
                pg_catalog.jsonb_build_object({headerMap}));
            IF header.{S("Id")} IS NULL OR header.{S("Id")} = '00000000-0000-0000-0000-000000000000'::uuid
                OR header.{S("TenantId")} IS DISTINCT FROM p_tenant
                OR header.{S("CriteriaHash")} IS NULL OR length(header.{S("CriteriaHash")}) <> 64 OR header.{S("CriteriaHash")} !~ '^[0-9a-f]+$'
                OR header.{S("IdentityEpoch")} IS NULL OR header.{S("IdentityEpoch")} < 0
                OR header.{S("DisclosureEpoch")} IS NULL OR header.{S("DisclosureEpoch")} < 0
                OR header.{S("ItemCount")} IS DISTINCT FROM item_count
                OR header.{S("CreatedAtUtc")} IS NULL OR NOT pg_catalog.isfinite(header.{S("CreatedAtUtc")})
                OR header.{S("ExpiresAtUtc")} IS NULL OR NOT pg_catalog.isfinite(header.{S("ExpiresAtUtc")})
                OR header.{S("CreatedAtUtc")} > p_now OR header.{S("ExpiresAtUtc")} <= p_now
                OR header.{S("ExpiresAtUtc")} <= header.{S("CreatedAtUtc")}
                OR header.{S("ExpiresAtUtc")} - header.{S("CreatedAtUtc")} > pg_catalog.make_interval(mins => p_lifetime)
                OR header.{S("Truncated")} IS NULL OR header.{S("LocalSourceComplete")} IS NULL OR header.{S("RemoteSourceComplete")} IS NULL
            THEN RAISE EXCEPTION 'discovery_traversal_capture_header' USING ERRCODE = '22023'; END IF;
            IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_items) entry
                WHERE pg_catalog.jsonb_typeof(entry) <> 'object'
                    OR NOT entry ?& ARRAY['SourceKind','SourceId','CanonicalKind','CanonicalId','MatchingSessionId']
                    OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(entry)) <> 5
                    OR (entry->>'SourceKind')::integer IS NULL OR (entry->>'SourceKind')::integer NOT IN (1,2)
                    OR (entry->>'CanonicalKind')::integer IS NULL OR (entry->>'CanonicalKind')::integer NOT IN (1,2,3)
                    OR (entry->>'SourceId')::uuid IS NULL OR (entry->>'SourceId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                    OR (entry->>'CanonicalId')::uuid IS NULL OR (entry->>'CanonicalId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                    OR (entry->>'MatchingSessionId')::uuid = '00000000-0000-0000-0000-000000000000'::uuid)
                OR (SELECT count(DISTINCT ((entry->>'CanonicalKind')::integer,(entry->>'CanonicalId')::uuid))
                    FROM pg_catalog.jsonb_array_elements(p_items) entry) <> item_count
            THEN RAISE EXCEPTION 'discovery_traversal_capture_membership' USING ERRCODE = '22023'; END IF;
            -- No source-row reads or locks follow the terminal epoch: only its already-written tuple is checked.
            IF NOT EXISTS (SELECT 1 FROM {T<EventDiscoveryRevision>()} r WHERE r.{R("TenantId")} = p_tenant
                AND r.xmin = {ownTransaction} AND r.{R("IdentityEpoch")} = header.{S("IdentityEpoch")}
                AND r.{R("DisclosureEpoch")} = header.{S("DisclosureEpoch")})
            THEN RAISE EXCEPTION 'discovery_revision_conflict' USING ERRCODE = '40001'; END IF;
            SELECT s.{S("Id")} INTO reusable FROM {T<EventDiscoverySnapshot>()} s WHERE s.{S("TenantId")} = p_tenant
                AND s.{S("CriteriaHash")} = header.{S("CriteriaHash")}
                AND s.{S("IdentityEpoch")} = header.{S("IdentityEpoch")} AND s.{S("DisclosureEpoch")} = header.{S("DisclosureEpoch")}
                AND s.{S("CreatedAtUtc")} <= p_now AND s.{S("ExpiresAtUtc")} > p_now
                ORDER BY s.{S("ExpiresAtUtc")} DESC,s.{S("Id")} LIMIT 1;
            IF FOUND THEN RETURN reusable; END IF;
            SELECT count(*) INTO live_count FROM {T<EventDiscoverySnapshot>()}
                WHERE {S("TenantId")} = p_tenant AND {S("ExpiresAtUtc")} > p_now;
            SELECT count(*) INTO physical_count FROM {T<EventDiscoverySnapshotItem>()} WHERE {I("TenantId")} = p_tenant;
            IF live_count >= p_live OR physical_count > p_physical OR item_count > p_physical - physical_count
                OR (SELECT count(*) FROM {T<EventDiscoverySnapshot>()} WHERE {S("TenantId")} = p_tenant) >= p_physical
            THEN RETURN NULL; END IF;
            INSERT INTO {T<EventDiscoverySnapshot>()} SELECT header.*;
            INSERT INTO {T<EventDiscoverySnapshotItem>()}
                ({I("TenantId")},{I("SnapshotId")},{I("Ordinal")},{I("SourceKind")},{I("SourceId")},
                    {I("CanonicalKind")},{I("CanonicalId")},{I("MatchingSessionId")})
                SELECT p_tenant,header.{S("Id")},ordinal - 1,(entry->>'SourceKind')::integer,(entry->>'SourceId')::uuid,
                    (entry->>'CanonicalKind')::integer,(entry->>'CanonicalId')::uuid,(entry->>'MatchingSessionId')::uuid
                FROM pg_catalog.jsonb_array_elements(p_items) WITH ORDINALITY entries(entry,ordinal);
            RETURN header.{S("Id")};
            """;
    }

    private static string BindingBody(ExploreDbContext context, string operation)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string identities = sql.Table(typeof(EventDiscoveryIdentity)), aliases = sql.Table(typeof(EventDiscoveryAlias));
        string I(string property) => sql.Column(typeof(EventDiscoveryIdentity), property);
        string A(string property) => sql.Column(typeof(EventDiscoveryAlias), property);
        string roots = $"""
            SELECT i.{I("Id")} FROM {identities} i WHERE i.{I("TenantId")} = p_tenant
                AND i.{I("SourceKind")} = p_kind AND i.{I("SourceKeyHash")} = ANY(p_hashes)
            """;
        string closure = $"""
            WITH roots AS ({roots}), selected AS (
                SELECT * FROM roots UNION
                SELECT a.{A("PrimaryIdentityId")} FROM {aliases} a
                    WHERE a.{A("TenantId")} = p_tenant AND a.{A("MemberIdentityId")} IN (SELECT * FROM roots))
            """;
        return $"""
            IF p_kind IS NULL OR p_kind NOT IN (1,2) OR p_hashes IS NULL
                OR pg_catalog.cardinality(p_hashes) > {EventDiscoveryTraversalLimits.IdentityCeiling}
                OR pg_catalog.array_position(p_hashes,NULL) IS NOT NULL
                OR EXISTS (SELECT 1 FROM unnest(p_hashes) hash WHERE length(hash) <> 64 OR hash !~ '^[0-9a-f]+$')
            THEN RAISE EXCEPTION 'discovery_traversal_binding_bounds' USING ERRCODE = '22023'; END IF;
            RETURN QUERY {closure}
                {(operation == "bindings"
                    ? $"SELECT i.* FROM {identities} i WHERE i.{I("TenantId")} = p_tenant AND i.{I("Id")} IN (SELECT * FROM selected)"
                    : $"SELECT a.* FROM {aliases} a WHERE a.{A("TenantId")} = p_tenant AND a.{A("MemberIdentityId")} IN (SELECT * FROM selected)")};
            """;
    }
}
