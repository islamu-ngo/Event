using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Privacy.ErasureAuthority;

public static partial class PostgresSubjectPrivacyErasureContract
{
    private static readonly string[] TombstoneInsertProperties =
    [
        nameof(StorageObjectDeletionTombstone.Id), nameof(StorageObjectDeletionTombstone.TenantId),
        nameof(StorageObjectDeletionTombstone.Provider), nameof(StorageObjectDeletionTombstone.ProviderBindingId),
        nameof(StorageObjectDeletionTombstone.ObjectKey), nameof(StorageObjectDeletionTombstone.ProviderObjectVersion),
        nameof(StorageObjectDeletionTombstone.State), nameof(StorageObjectDeletionTombstone.ConcurrencyStamp),
        nameof(StorageObjectDeletionTombstone.NextAttemptAtUtc)
    ];
    private static readonly string[] CounterInsertProperties =
    [
        nameof(StorageUsageCounter.Id), nameof(StorageUsageCounter.TenantId), nameof(StorageUsageCounter.Provider),
        nameof(StorageUsageCounter.UsedBytes), nameof(StorageUsageCounter.ReservedBytes),
        nameof(StorageUsageCounter.QuarantinedBytes), nameof(StorageUsageCounter.ObjectCount),
        nameof(StorageUsageCounter.CreatedAt), nameof(StorageUsageCounter.ConcurrencyStamp)
    ];

    private static string StorageDeclarations(SqlMap m) => $"""
        v_object_id uuid; v_tenant uuid; v_provider text; v_binding uuid; v_key text; v_version text;
        v_settled boolean; v_has_source boolean; v_has_target boolean; v_sessions integer; v_has_operation boolean;
        v_source {m.Table<StorageObject>()}%ROWTYPE;
        v_session {m.Table<StorageUploadSession>()}%ROWTYPE;
        v_operation {m.Table<StorageProducerOperation>()}%ROWTYPE;
        v_target {m.Table<StorageObjectDeletionTombstone>()}%ROWTYPE;
        v_pair record; v_totals record;
        """;

    private static string StorageCaptureSql(SqlMap m)
    {
        string files = $"""
            SELECT f.{m.Column<RegistrationAnswerFile>("Id")}
            FROM {m.Table<RegistrationAnswerFile>()} f
            JOIN {m.Table<RegistrationSubmission>()} submission
                ON submission.{m.Column<RegistrationSubmission>("TenantId")} = f.{m.Column<RegistrationAnswerFile>("TenantId")}
               AND submission.{m.Column<RegistrationSubmission>("EventId")} = f.{m.Column<RegistrationAnswerFile>("EventId")}
               AND submission.{m.Column<RegistrationSubmission>("Id")} = f.{m.Column<RegistrationAnswerFile>("RegistrationSubmissionId")}
            JOIN {m.Table<RegistrationOrder>()} orders
                ON orders.{m.Column<RegistrationOrder>("TenantId")} = submission.{m.Column<RegistrationSubmission>("TenantId")}
               AND orders.{m.Column<RegistrationOrder>("EventId")} = submission.{m.Column<RegistrationSubmission>("EventId")}
               AND orders.{m.Column<RegistrationOrder>("Id")} = submission.{m.Column<RegistrationSubmission>("RegistrationOrderId")}
            WHERE orders.{m.Column<RegistrationOrder>("AccountUserId")} = p_subject
            """;
        string retirementSources = $"""
            SELECT r.{m.Column<StorageObject>("Id")}
            FROM {m.Table<StorageObject>()} r
            WHERE r.{m.Column<StorageObject>("ActorId")} = ANY(v_actors)
               OR EXISTS (SELECT 1 FROM {m.Table<ActorPii>()} picture
                    WHERE picture.{m.Column<ActorPii>("ActorId")} = ANY(v_actors)
                      AND picture.{m.Column<ActorPii>("ProfilePictureStorageObjectId")} = r.{m.Column<StorageObject>("Id")})
               OR EXISTS (SELECT 1 FROM {m.Table<RegistrationAnswerFile>()} file
                    WHERE file.{m.Column<RegistrationAnswerFile>("Id")} = ANY(v_files)
                      AND file.{m.Column<RegistrationAnswerFile>("StorageObjectId")} = r.{m.Column<StorageObject>("Id")})
               OR EXISTS (SELECT 1 FROM {m.Table<EventReportEvidence>()} evidence
                    WHERE evidence.{m.Column<EventReportEvidence>("CreatedByUserId")} = p_subject
                      AND evidence.{m.Column<EventReportEvidence>("TenantId")} = r.{m.Column<StorageObject>("TenantId")}
                      AND evidence.{m.Column<EventReportEvidence>("StorageObjectId")} = r.{m.Column<StorageObject>("Id")})
               OR EXISTS (SELECT 1 FROM {m.Table<StorageUploadSession>()} upload
                    WHERE upload.{m.Column<StorageUploadSession>("UserId")} = p_subject
                      AND upload.{m.Column<StorageUploadSession>("TenantId")} = r.{m.Column<StorageObject>("TenantId")}
                      AND (upload.{m.Column<StorageUploadSession>("StorageObjectId")} = r.{m.Column<StorageObject>("Id")}
                        OR upload.{m.Column<StorageUploadSession>("Id")} = r.{m.Column<StorageObject>("Id")}))
            UNION
            SELECT COALESCE(r.{m.Column<StorageUploadSession>("StorageObjectId")}, r.{m.Column<StorageUploadSession>("Id")})
            FROM {m.Table<StorageUploadSession>()} r
            WHERE r.{m.Column<StorageUploadSession>("UserId")} = p_subject
              AND r.{m.Column<StorageUploadSession>("ObjectKey")} IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM {m.Table<StorageObject>()} source
                WHERE source.{m.Column<StorageObject>("TenantId")} = r.{m.Column<StorageUploadSession>("TenantId")}
                  AND (source.{m.Column<StorageObject>("Id")} = r.{m.Column<StorageUploadSession>("StorageObjectId")}
                    OR source.{m.Column<StorageObject>("Id")} = r.{m.Column<StorageUploadSession>("Id")}))
            """;
        return $"""
            SELECT COALESCE(array_agg(id), ARRAY[]::uuid[]) INTO v_files FROM ({files}) selected(id);
            SELECT COALESCE(array_agg(DISTINCT id), ARRAY[]::uuid[]) INTO v_retiring FROM ({retirementSources}) selected(id);
            SELECT COALESCE(array_agg(r.{m.Column<StorageObject>("Id")} ORDER BY r.{m.Column<StorageObject>("TenantId")}, r.{m.Column<StorageObject>("Id")}), ARRAY[]::uuid[])
                INTO v_storage FROM {m.Table<StorageObject>()} r
            WHERE r.{m.Column<StorageObject>("Id")} = ANY(v_retiring)
               OR r.{m.Column<StorageObject>("CreatedBy")} = p_subject OR r.{m.Column<StorageObject>("UpdatedBy")} = p_subject
               OR r.{m.Column<StorageObject>("DeletedBy")} = p_subject OR r.{m.Column<StorageObject>("QuarantinedBy")} = p_subject;
            FOREACH v_object_id IN ARRAY v_storage LOOP
                {m.Update<StorageObject>($"r.{m.Column<StorageObject>("Id")} = v_object_id", ("ConcurrencyStamp", "uuidv7()"))}
                IF NOT FOUND THEN RAISE EXCEPTION 'storage_reference_conflict' USING ERRCODE = '40001'; END IF;
            END LOOP;
            SELECT COALESCE(jsonb_agg(jsonb_build_object('tenant', tenant, 'provider', provider)), '[]'::jsonb)
                INTO v_upload_scopes FROM (
                SELECT r.{m.Column<StorageUploadSession>("TenantId")} tenant, r.{m.Column<StorageUploadSession>("Provider")} provider
                FROM {m.Table<StorageUploadSession>()} r WHERE r.{m.Column<StorageUploadSession>("UserId")} = p_subject
                UNION SELECT r.{m.Column<StorageObject>("TenantId")}, r.{m.Column<StorageObject>("Provider")}
                FROM {m.Table<StorageObject>()} r WHERE r.{m.Column<StorageObject>("Id")} = ANY(v_retiring)
            ) scopes;
            {m.Delete<RegistrationAnswerFileRelease>($"r.{m.Column<RegistrationAnswerFileRelease>("RegistrationAnswerFileId")} = ANY(v_files)")}
            {m.Delete<RegistrationAnswerFile>($"r.{m.Column<RegistrationAnswerFile>("Id")} = ANY(v_files)")}
            {m.Update<ActorPii>($"r.{m.Column<ActorPii>("ActorId")} = ANY(v_actors)",
                ("ExternalProfilePictureUri", "NULL"), ("ProfilePictureStorageObjectId", "NULL"))}
            """;
    }

    private static string StorageDisposalSql(SqlMap m, ExploreDbContext context)
    {
        string physical = StorageObjectPhysicalReferenceQuery.BuildPredicate(context, "v_object_id");
        string holds = StorageObjectRetentionQuery.BuildPredicate(context, "v_object_id", "p_now");
        string totals = StorageScopeTotalsQuery.BuildSql(context, "v_tenant", "v_provider");
        // The shared predicates read physical owner FKs, not a second invented usage inventory.
        foreach (var key in context.Model.FindEntityType(typeof(StorageObject))!.GetReferencingForeignKeys())
            m.Table(key.DeclaringEntityType.ClrType);
        m.Table<RegistrationFormVersion>();
        m.Table<RegistrationFormField>();
        m.Table<RegistrationRetentionPolicy>();
        m.Table<RegistrationProviderSubmissionWriteEffect>();
        m.Write<StorageObjectDeletionTombstone>("INSERT");
        m.RequireRowLock<StorageObjectDeletionTombstone>();
        m.RequireRowLock<StorageProducerOperation>();
        m.Write<StorageUsageCounter>("INSERT", "UPDATE");
        string tombstoneColumns = string.Join(", ", TombstoneInsertProperties.Select(m.Column<StorageObjectDeletionTombstone>));
        string counterColumns = string.Join(", ", CounterInsertProperties.Select(m.Column<StorageUsageCounter>));
        string sessionMatch = $"(r.{m.Column<StorageUploadSession>("Id")} = v_object_id OR r.{m.Column<StorageUploadSession>("StorageObjectId")} = v_object_id)";
        string versionMismatch = $"v_version IS NOT NULL AND v_session.{m.Column<StorageUploadSession>("ProviderVersionId")} IS NOT NULL AND v_version <> v_session.{m.Column<StorageUploadSession>("ProviderVersionId")}";
        return $"""
            FOREACH v_object_id IN ARRAY v_retiring LOOP
                SELECT * INTO v_source FROM {m.Table<StorageObject>()} r WHERE r.{m.Column<StorageObject>("Id")} = v_object_id;
                v_has_source := FOUND;
                SELECT * INTO v_target FROM {m.Table<StorageObjectDeletionTombstone>()} r
                    WHERE r.{m.Column<StorageObjectDeletionTombstone>("Id")} = v_object_id FOR UPDATE;
                v_has_target := FOUND;
                IF v_has_source AND (({physical}) OR ({holds})) THEN CONTINUE; END IF;
                IF v_has_source AND v_source.{m.Column<StorageObject>("LifecycleState")}
                    = {m.Literal<StorageObject>("LifecycleState", StorageObjectLifecycleStates.Deleted)} THEN
                    RAISE EXCEPTION 'storage_retirement_source_invalid' USING ERRCODE = '55000';
                END IF;
                SELECT count(*) INTO v_sessions FROM {m.Table<StorageUploadSession>()} r WHERE {sessionMatch};
                SELECT * INTO v_operation FROM {m.Table<StorageProducerOperation>()} r
                    WHERE r.{m.Column<StorageProducerOperation>("Id")} = v_object_id FOR UPDATE;
                v_has_operation := FOUND;
                IF (v_has_source AND (v_sessions > 1 OR (v_sessions > 0 AND v_has_operation)))
                    OR (NOT v_has_source AND NOT v_has_target AND (v_sessions <> 1 OR v_has_operation)) THEN
                    RAISE EXCEPTION 'storage_retirement_custody_invalid' USING ERRCODE = '55000';
                END IF;
                IF v_has_source THEN
                    v_tenant := v_source.{m.Column<StorageObject>("TenantId")};
                    v_provider := v_source.{m.Column<StorageObject>("Provider")};
                    v_binding := v_source.{m.Column<StorageObject>("StorageProviderBindingId")};
                    v_key := v_source.{m.Column<StorageObject>("ObjectKey")};
                    v_version := v_source.{m.Column<StorageObject>("ProviderVersionId")};
                    v_settled := v_source.{m.Column<StorageObject>("LifecycleState")} = {m.Literal<StorageObject>("LifecycleState", StorageObjectLifecycleStates.Active)};
                    IF v_sessions > 0 OR v_has_operation THEN v_settled := true; END IF;
                ELSIF v_has_target THEN
                    v_tenant := v_target.{m.Column<StorageObjectDeletionTombstone>("TenantId")};
                    v_provider := v_target.{m.Column<StorageObjectDeletionTombstone>("Provider")};
                    v_binding := v_target.{m.Column<StorageObjectDeletionTombstone>("ProviderBindingId")};
                    v_key := v_target.{m.Column<StorageObjectDeletionTombstone>("ObjectKey")};
                    v_version := v_target.{m.Column<StorageObjectDeletionTombstone>("ProviderObjectVersion")};
                    v_settled := true;
                ELSE
                    SELECT * INTO STRICT v_session FROM {m.Table<StorageUploadSession>()} r WHERE {sessionMatch};
                    v_tenant := v_session.{m.Column<StorageUploadSession>("TenantId")};
                    v_provider := v_session.{m.Column<StorageUploadSession>("Provider")};
                    v_binding := v_session.{m.Column<StorageUploadSession>("StorageProviderBindingId")};
                    v_key := v_session.{m.Column<StorageUploadSession>("ObjectKey")};
                    v_version := v_session.{m.Column<StorageUploadSession>("ProviderVersionId")};
                    v_settled := v_session.{m.Column<StorageUploadSession>("ProducerSettled")};
                END IF;
                IF v_tenant IS NULL OR NOT (v_tenant = ANY(v_tenants)) THEN
                    RAISE EXCEPTION 'storage_retirement_scope_invalid' USING ERRCODE = '55000';
                END IF;
                FOR v_session IN SELECT * FROM {m.Table<StorageUploadSession>()} r WHERE {sessionMatch}
                    ORDER BY r.{m.Column<StorageUploadSession>("Id")} FOR UPDATE LOOP
                    IF v_session.{m.Column<StorageUploadSession>("TenantId")} IS DISTINCT FROM v_tenant
                        OR COALESCE(v_session.{m.Column<StorageUploadSession>("StorageObjectId")}, v_session.{m.Column<StorageUploadSession>("Id")}) <> v_object_id
                        OR v_session.{m.Column<StorageUploadSession>("Provider")} IS DISTINCT FROM v_provider
                        OR v_session.{m.Column<StorageUploadSession>("StorageProviderBindingId")} IS DISTINCT FROM v_binding
                        OR v_session.{m.Column<StorageUploadSession>("ObjectKey")} IS DISTINCT FROM v_key
                        OR ({versionMismatch}) THEN
                        RAISE EXCEPTION 'storage_producer_identity_mismatch' USING ERRCODE = '55000';
                    END IF;
                    v_version := COALESCE(v_version, v_session.{m.Column<StorageUploadSession>("ProviderVersionId")});
                    v_settled := v_settled AND v_session.{m.Column<StorageUploadSession>("ProducerSettled")};
                    {m.Update<StorageUploadSession>($"r.{m.Column<StorageUploadSession>("Id")} = v_session.{m.Column<StorageUploadSession>("Id")}",
                        ("ConcurrencyStamp", "uuidv7()"))}
                END LOOP;
                IF v_has_operation THEN
                    IF v_operation.{m.Column<StorageProducerOperation>("TenantId")} IS DISTINCT FROM v_tenant
                        OR v_operation.{m.Column<StorageProducerOperation>("Provider")} IS DISTINCT FROM v_provider
                        OR v_operation.{m.Column<StorageProducerOperation>("ProviderBindingId")} IS DISTINCT FROM v_binding
                        OR v_operation.{m.Column<StorageProducerOperation>("ObjectKey")} IS DISTINCT FROM v_key
                        OR (v_version IS NOT NULL AND v_operation.{m.Column<StorageProducerOperation>("ProviderVersionId")} IS NOT NULL
                            AND v_version <> v_operation.{m.Column<StorageProducerOperation>("ProviderVersionId")}) THEN
                        RAISE EXCEPTION 'storage_producer_identity_mismatch' USING ERRCODE = '55000';
                    END IF;
                    v_version := COALESCE(v_version, v_operation.{m.Column<StorageProducerOperation>("ProviderVersionId")});
                    v_settled := v_settled AND v_operation.{m.Column<StorageProducerOperation>("ProducerSettled")};
                END IF;
                IF v_provider NOT IN ('{StorageProviders.Local}', '{StorageProviders.S3Compatible}')
                    OR v_binding IS NULL OR v_key IS NULL OR btrim(v_key) = '' OR length(v_key) > 1024
                    OR v_key ~ '[[:cntrl:]]' OR v_key ~ '^[a-zA-Z][a-zA-Z0-9+.-]*:' OR left(v_key, 1) = '/'
                    OR (v_version IS NOT NULL AND (length(v_version) = 0 OR length(v_version) > 1024))
                    OR NOT EXISTS (SELECT 1 FROM {m.Table<StorageProviderBinding>()} b
                        WHERE b.{m.Column<StorageProviderBinding>("Id")} = v_binding
                          AND b.{m.Column<StorageProviderBinding>("Provider")} = v_provider) THEN
                    RAISE EXCEPTION 'storage_retirement_custody_invalid' USING ERRCODE = '55000';
                END IF;
                IF v_has_target THEN
                    IF v_target.{m.Column<StorageObjectDeletionTombstone>("TenantId")} IS DISTINCT FROM v_tenant
                        OR v_target.{m.Column<StorageObjectDeletionTombstone>("Provider")} IS DISTINCT FROM v_provider
                        OR v_target.{m.Column<StorageObjectDeletionTombstone>("ProviderBindingId")} IS DISTINCT FROM v_binding
                        OR v_target.{m.Column<StorageObjectDeletionTombstone>("ObjectKey")} IS DISTINCT FROM v_key
                        OR (v_version IS NOT NULL AND v_target.{m.Column<StorageObjectDeletionTombstone>("ProviderObjectVersion")} IS DISTINCT FROM v_version)
                        OR (NOT v_settled AND (v_sessions > 0 OR v_has_operation)
                            AND v_target.{m.Column<StorageObjectDeletionTombstone>("State")} <> {(int)StorageObjectDeletionState.AwaitingProducer}) THEN
                        RAISE EXCEPTION 'storage_retirement_custody_conflict' USING ERRCODE = '40001';
                    END IF;
                ELSE
                    INSERT INTO {m.Table<StorageObjectDeletionTombstone>()} ({tombstoneColumns})
                    VALUES (v_object_id, v_tenant, v_provider, v_binding, v_key, v_version,
                        CASE WHEN v_settled THEN {(int)StorageObjectDeletionState.Ready} ELSE {(int)StorageObjectDeletionState.AwaitingProducer} END,
                        uuidv7(), CASE WHEN v_settled THEN p_now ELSE NULL END);
                END IF;
                IF ({physical}) OR ({holds}) THEN
                    RAISE EXCEPTION 'storage_retirement_reference_conflict' USING ERRCODE = '40001';
                END IF;
                {m.Delete<StorageProducerOperation>($"r.{m.Column<StorageProducerOperation>("Id")} = v_object_id AND r.{m.Column<StorageProducerOperation>("TenantId")} = v_tenant")}
                {m.Delete<StorageUploadSession>($"{sessionMatch} AND r.{m.Column<StorageUploadSession>("TenantId")} = v_tenant")}
                {m.Delete<StorageObject>($"r.{m.Column<StorageObject>("Id")} = v_object_id AND r.{m.Column<StorageObject>("TenantId")} = v_tenant")}
            END LOOP;
            {m.Delete<StorageUploadSession>($"""
                r.{m.Column<StorageUploadSession>("UserId")} = p_subject AND (
                    (r.{m.Column<StorageUploadSession>("Status")} = {m.Literal<StorageUploadSession>("Status", StorageUploadSessionStates.Finalized)}
                        AND r.{m.Column<StorageUploadSession>("ProducerSettled")}
                        AND EXISTS (SELECT 1 FROM {m.Table<StorageObject>()} source
                            WHERE source.{m.Column<StorageObject>("Id")} = r.{m.Column<StorageUploadSession>("StorageObjectId")}
                              AND source.{m.Column<StorageObject>("TenantId")} = r.{m.Column<StorageUploadSession>("TenantId")}
                              AND source.{m.Column<StorageObject>("Provider")} = r.{m.Column<StorageUploadSession>("Provider")}
                              AND source.{m.Column<StorageObject>("StorageProviderBindingId")} = r.{m.Column<StorageUploadSession>("StorageProviderBindingId")}
                              AND source.{m.Column<StorageObject>("ObjectKey")} = r.{m.Column<StorageUploadSession>("ObjectKey")}
                              AND source.{m.Column<StorageObject>("ProviderVersionId")} IS NOT DISTINCT FROM r.{m.Column<StorageUploadSession>("ProviderVersionId")}
                              AND source.{m.Column<StorageObject>("LifecycleState")} = {m.Literal<StorageObject>("LifecycleState", StorageObjectLifecycleStates.Active)}))
                    OR (r.{m.Column<StorageUploadSession>("StorageObjectId")} IS NULL
                        AND r.{m.Column<StorageUploadSession>("ObjectKey")} IS NULL
                        AND r.{m.Column<StorageUploadSession>("Status")} <> {m.Literal<StorageUploadSession>("Status", StorageUploadSessionStates.Uploading)}))
                """)}
            {m.Update<StorageUploadSession>($"r.{m.Column<StorageUploadSession>("UserId")} = p_subject",
                ("UserId", "NULL"), ("Sha256Checksum", "NULL"), ("OriginalFileName", "NULL"), ("SafeDisplayName", "''"),
                ("IdempotencyKey", "NULL"), ("FailureMessage", "NULL"), ("CreatedBy", "NULL"),
                ("UpdatedAt", "p_now"), ("UpdatedBy", "NULL"))}
            {m.Update<StorageObject>($"""
                r.{m.Column<StorageObject>("ActorId")} = ANY(v_actors)
                OR r.{m.Column<StorageObject>("CreatedBy")} = p_subject OR r.{m.Column<StorageObject>("UpdatedBy")} = p_subject
                OR r.{m.Column<StorageObject>("DeletedBy")} = p_subject OR r.{m.Column<StorageObject>("QuarantinedBy")} = p_subject
                """,
                ("ActorId", $"CASE WHEN r.{m.Column<StorageObject>("ActorId")} = ANY(v_actors) THEN NULL ELSE r.{m.Column<StorageObject>("ActorId")} END"),
                ("CreatedBy", $"CASE WHEN r.{m.Column<StorageObject>("CreatedBy")} = p_subject THEN NULL ELSE r.{m.Column<StorageObject>("CreatedBy")} END"),
                ("UpdatedBy", $"CASE WHEN r.{m.Column<StorageObject>("UpdatedBy")} = p_subject THEN NULL ELSE r.{m.Column<StorageObject>("UpdatedBy")} END"),
                ("DeletedBy", $"CASE WHEN r.{m.Column<StorageObject>("DeletedBy")} = p_subject THEN NULL ELSE r.{m.Column<StorageObject>("DeletedBy")} END"),
                ("QuarantinedBy", $"CASE WHEN r.{m.Column<StorageObject>("QuarantinedBy")} = p_subject THEN NULL ELSE r.{m.Column<StorageObject>("QuarantinedBy")} END"),
                ("SourceUri", $"CASE WHEN r.{m.Column<StorageObject>("ActorId")} = ANY(v_actors) THEN NULL ELSE r.{m.Column<StorageObject>("SourceUri")} END"),
                ("UpdatedAt", "p_now"), ("ConcurrencyStamp", "uuidv7()"))}
            FOR v_pair IN SELECT DISTINCT (value->>'tenant')::uuid tenant, value->>'provider' provider
                FROM jsonb_array_elements(v_upload_scopes) ORDER BY tenant, provider LOOP
                v_tenant := v_pair.tenant; v_provider := v_pair.provider;
                INSERT INTO {m.Table<StorageUsageCounter>()} ({counterColumns})
                VALUES (uuidv7(), v_tenant, v_provider, 0, 0, 0, 0, p_now, uuidv7())
                ON CONFLICT ({m.Column<StorageUsageCounter>("TenantId")}, {m.Column<StorageUsageCounter>("Provider")}) DO NOTHING;
                UPDATE {m.Table<StorageUsageCounter>()} SET {m.Column<StorageUsageCounter>("ConcurrencyStamp")} = uuidv7()
                    WHERE {m.Column<StorageUsageCounter>("TenantId")} = v_tenant
                      AND {m.Column<StorageUsageCounter>("Provider")} = v_provider;
                IF NOT FOUND THEN RAISE EXCEPTION 'storage_accounting_conflict' USING ERRCODE = '40001'; END IF;
                SELECT * INTO STRICT v_totals FROM ({totals}) aggregate;
                IF v_totals."Used" < 0 OR v_totals."Reserved" < 0 OR v_totals."Quarantined" < 0 OR v_totals."Count" < 0 THEN
                    RAISE EXCEPTION 'storage_accounting_invalid' USING ERRCODE = '55000';
                END IF;
                UPDATE {m.Table<StorageUsageCounter>()} SET
                    {m.Column<StorageUsageCounter>("UsedBytes")} = v_totals."Used",
                    {m.Column<StorageUsageCounter>("ReservedBytes")} = v_totals."Reserved",
                    {m.Column<StorageUsageCounter>("QuarantinedBytes")} = v_totals."Quarantined",
                    {m.Column<StorageUsageCounter>("ObjectCount")} = v_totals."Count",
                    {m.Column<StorageUsageCounter>("LastRecalculatedAt")} = p_now,
                    {m.Column<StorageUsageCounter>("UpdatedAt")} = p_now
                WHERE {m.Column<StorageUsageCounter>("TenantId")} = v_tenant
                    AND {m.Column<StorageUsageCounter>("Provider")} = v_provider;
            END LOOP;
            """;
    }
}
