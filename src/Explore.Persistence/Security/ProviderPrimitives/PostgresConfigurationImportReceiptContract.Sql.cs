using System.Globalization;
using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Persistence.Entities;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportReceiptContract
{
    private static readonly string[] NotificationFields =
        ["Key", "OldValue", "NewValue", "Scope", "TenantId", "ActorUserId", "ChangedAt", "IsSensitive"];
    private static int MaximumNotifications => ConfigurationManifestCatalog.InstanceSettings.Count
        + ConfigurationPortabilityContentLimits.MaximumTenantCount * ConfigurationManifestCatalog.TenantSettings.Count;
    private const string Uuid7Pattern = "^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$";
    private const string DigestPattern = "^[0-9a-f]{64}$";
    private const string EffectPayloadDigestPattern = "^[0-9A-F]{64}$";
    private const string UuidPattern = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";
    private const string SubMicrosecondPattern = "([.][0-9]{6})[0-9]+(Z|[+]00:00)$";
    private const string EmptyUuid = "'00000000-0000-0000-0000-000000000000'::uuid";

    private static string PolicyPredicate(ExploreDbContext context, Type type)
    {
        string C(string property) => Column(context, type, property);
        string O(string property) => Column(context, typeof(ConfigurationImportOperation), property);
        string S(string property) => Column(context, typeof(ConfigurationImportSession), property);
        string table = Table(context, type);
        if (type == typeof(ConfigurationImportOperation))
            return $"{C("TargetAuthorityKey")} COLLATE \"C\" = 'instance' AND {C("TargetTenantId")} IS NULL";
        if (type == typeof(ConfigurationImportSession))
            return $"{C("TargetScope")} = 1 AND {C("TargetTenantId")} IS NULL AND {C("TargetAuthorityKey")} COLLATE \"C\" = 'instance'";
        if (type == typeof(ConfigurationImportStoredArtifact))
            return $"""
                xmin::text = pg_catalog.pg_current_xact_id()::xid::text
                OR EXISTS (SELECT 1 FROM {Table(context, typeof(ConfigurationImportSession))} session
                    WHERE session.{S("ArtifactHandleId")} = {table}.{C("Id")} AND session.{S("TargetScope")} = 1
                        AND session.{S("TargetTenantId")} IS NULL AND session.{S("TargetAuthorityKey")} COLLATE "C" = 'instance')
                OR EXISTS (SELECT 1 FROM {Table(context, typeof(ConfigurationImportOperation))} receipt
                    WHERE receipt.{O("SnapshotArtifactHandleId")} = {table}.{C("Id")}
                        AND receipt.{O("TargetAuthorityKey")} COLLATE "C" = 'instance' AND receipt.{O("TargetTenantId")} IS NULL)
                """;
        return $"""
            {C("AggregateType")} COLLATE "C" = 'ConfigurationImportOperation'
            AND {C("EventType")} COLLATE "C" = 'ConfigurationImportEffectsRequested'
            AND EXISTS (SELECT 1 FROM {Table(context, typeof(ConfigurationImportOperation))} receipt
                WHERE receipt.{O("Id")} = {table}.{C("AggregateId")} AND receipt.{O("EffectOutboxId")} = {table}.{C("Id")}
                    AND receipt.{O("TargetAuthorityKey")} COLLATE "C" = 'instance' AND receipt.{O("TargetTenantId")} IS NULL
                    AND receipt.{O("Status")} IN (2, 4))
            """;
    }

    private static string InputShape(ExploreDbContext context, Type type, string[] properties)
    {
        string names = string.Join(", ", properties.Select(property => Text(ColumnName(context, type, property))));
        string fields = string.Join(" AND ", properties.Select(property =>
        {
            var metadata = context.Model.FindEntityType(type)!.FindProperty(property)!;
            Type clr = Nullable.GetUnderlyingType(metadata.ClrType) ?? metadata.ClrType;
            string expression = $"p_value->{Text(ColumnName(context, type, property))}";
            string text = $"p_value->>{Text(ColumnName(context, type, property))}";
            string shape = clr == typeof(bool) ? $"jsonb_typeof({expression}) = 'boolean'"
                : clr == typeof(int) || clr.IsEnum ? $"jsonb_typeof({expression}) = 'number' AND {text} ~ '^-?[0-9]+$'"
                : $"jsonb_typeof({expression}) = 'string'";
            if (clr == typeof(DateTime))
                shape += $" AND {text} ~ '(Z|[+]00:00)$' AND pg_catalog.isfinite(({text})::timestamptz)" +
                    $" AND ({text})::timestamptz >= '0001-01-01T00:00:00Z'::timestamptz" +
                    $" AND ({text})::timestamptz < '10000-01-01T00:00:00Z'::timestamptz";
            if (clr == typeof(Guid))
                shape += $" AND {text} COLLATE \"C\" ~ {Text(UuidPattern)}";
            if (clr == typeof(string) && property.EndsWith("Digest", StringComparison.Ordinal))
                shape += $" AND {text} COLLATE \"C\" ~ {Text(
                    type == typeof(ConfigurationImportOperation)
                    && property == nameof(ConfigurationImportOperation.EffectPayloadDigest)
                        ? EffectPayloadDigestPattern : DigestPattern)}";
            if (metadata.GetMaxLength() is { } maximum)
                shape += $" AND char_length({text}) <= {maximum}";
            return metadata.IsNullable ? $"({expression} = 'null'::jsonb OR ({shape}))" : $"({shape})";
        }));
        // Npgsql truncates .NET ticks to microseconds. PostgreSQL's timestamp text
        // input rounds instead, so normalize JSON dates before populating the row.
        string timestamps = string.Join("\n", properties.Where(property =>
        {
            Type clr = context.Model.FindEntityType(type)!.FindProperty(property)!.ClrType;
            return (Nullable.GetUnderlyingType(clr) ?? clr) == typeof(DateTime);
        }).Select(property =>
        {
            string column = Text(ColumnName(context, type, property));
            return $"""
                IF p_value->{column} <> 'null'::jsonb THEN
                    p_value := jsonb_set(p_value, ARRAY[{column}],
                        to_jsonb(regexp_replace(p_value->>{column}, {Text(SubMicrosecondPattern)}, '\1\2')));
                END IF;
                """;
        }));
        return $"""
            IF jsonb_typeof(p_value) IS DISTINCT FROM 'object' THEN
                RAISE EXCEPTION 'configuration_import_receipt_body_invalid' USING ERRCODE = '22023';
            END IF;
            IF ((SELECT count(*) FROM jsonb_object_keys(p_value)) = {properties.Length}
                AND p_value ?& ARRAY[{names}] AND {fields}) IS DISTINCT FROM true THEN
                RAISE EXCEPTION 'configuration_import_receipt_body_invalid' USING ERRCODE = '22023';
            END IF;
            {timestamps}
            """;
    }

    private static string ReadOperationBody(ExploreDbContext context, bool list)
    {
        string O(string property) => Column(context, typeof(ConfigurationImportOperation), property);
        string shape = list ? "p_maximum BETWEEN 1 AND 200" : $"p_id::text ~ {Text(Uuid7Pattern)}";
        string where = list ? "" : $"AND stored.{O("Id")} = p_id";
        string order = list ? $"ORDER BY stored.{O("StartedAt")} DESC, stored.{O("Id")} DESC LIMIT p_maximum" : "";
        return $"""
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                IF (p_authority COLLATE "C" = 'instance' AND {shape}) IS DISTINCT FROM true THEN
                    RAISE EXCEPTION 'configuration_import_receipt_target_invalid' USING ERRCODE = '22023';
                END IF;
                RETURN QUERY SELECT stored.* FROM {Table(context, typeof(ConfigurationImportOperation))} stored
                    WHERE stored.{O("TargetAuthorityKey")} COLLATE "C" = 'instance'
                        AND stored.{O("TargetTenantId")} IS NULL {where} {order};
            END;
            """;
    }

    private static string StoreOperationBody(ExploreDbContext context)
    {
        string O(string property) => Column(context, typeof(ConfigurationImportOperation), property);
        string S(string property) => Column(context, typeof(ConfigurationImportSession), property);
        string A(string property) => Column(context, typeof(ConfigurationImportStoredArtifact), property);
        string operations = Table(context, typeof(ConfigurationImportOperation));
        string sessions = Table(context, typeof(ConfigurationImportSession));
        string artifacts = Table(context, typeof(ConfigurationImportStoredArtifact));
        string selected = $"v.{O("_selectedSectionKeys")}";
        string omitted = $"v.{O("_omittedSectionKeys")}";
        string allowed = string.Join(", ", ConfigurationPortabilityRegistry.Sections
            .Where(pair => pair.Value.SupportsApply).Select(pair => Text(pair.Key)));
        string omittedAllowed = string.Join(", ", ConfigurationPortabilityRegistry.Sections.Keys.Select(Text));
        string digests = string.Join(" AND ", new[]
        {
            "ArtifactDigest", "TargetRevisionDigest", "SelectedSectionsDigest", "MappingDigest", "ApprovalDigest", "FidelityDigest"
        }.Select(property => $"v.{O(property)} COLLATE \"C\" ~ {Text(DigestPattern)}"));
        string failedDigests = string.Join(" AND ", new[]
        {
            "TargetRevisionDigest", "SelectedSectionsDigest", "MappingDigest", "ApprovalDigest", "FidelityDigest"
        }.Select(property => $"v.{O(property)} COLLATE \"C\" = '{EmptyDigest}'"));
        string preview = string.Join(" AND ", new[]
        {
            ("ArtifactDigest", "PreviewArtifactDigest"), ("TargetRevisionDigest", "PreviewTargetRevisionDigest"),
            ("SelectedSectionsDigest", "PreviewSelectedSectionsDigest"), ("MappingDigest", "PreviewMappingDigest"),
            ("ApprovalDigest", "PreviewRequiredApprovalDigest"), ("ApplyMode", "PreviewApplyMode")
        }.Select(pair => $"v.{O(pair.Item1)} IS NOT DISTINCT FROM session.{S(pair.Item2)}"));
        string retentionSeconds = ConfigurationImportSessionLimits.SnapshotRetention.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        return $"""
            DECLARE v {operations}%ROWTYPE; session record; source record; snapshot record;
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                IF current_setting('transaction_isolation') <> 'serializable' THEN
                    RAISE EXCEPTION 'configuration_import_receipt_transaction_required' USING ERRCODE = '25001';
                END IF;
                {InputShape(context, typeof(ConfigurationImportOperation), OperationColumns)}
                SELECT * INTO v FROM jsonb_populate_record(NULL::{operations}, p_value);
                IF (v.{O("Id")}::text ~ {Text(Uuid7Pattern)} AND v.{O("SessionId")}::text ~ {Text(Uuid7Pattern)}
                    AND v.{O("TargetAuthorityKey")} COLLATE "C" = 'instance' AND v.{O("TargetTenantId")} IS NULL
                    AND v.{O("ActorUserId")} IS NOT NULL AND v.{O("ActorUserId")} <> {EmptyUuid}
                    AND v.{O("ApplyMode")} BETWEEN 1 AND 4 AND v.{O("Status")} IN (2, 3, 4)
                    AND v.{O("Kind")} = (CASE WHEN v.{O("SourceOperationId")} IS NULL THEN 1 ELSE 2 END)
                    AND (v.{O("SourceOperationId")} IS NULL OR v.{O("SourceOperationId")}::text ~ {Text(Uuid7Pattern)})
                    AND v.{O("SourceOperationId")} IS DISTINCT FROM v.{O("Id")}
                    AND pg_catalog.isfinite(v.{O("StartedAt")}) AND pg_catalog.isfinite(v.{O("CompletedAt")})
                    AND v.{O("CompletedAt")} >= v.{O("StartedAt")} AND {digests}
                    AND char_length({selected}) BETWEEN 1 AND {ConfigurationImportOperation.MaximumSectionKeysLength}
                    AND char_length({omitted}) <= {ConfigurationImportOperation.MaximumOmittedSectionKeysLength}
                    AND NOT EXISTS (SELECT 1 FROM unnest(string_to_array({selected}, E'\n')) key
                        WHERE key COLLATE "C" NOT IN ({allowed}))
                    AND {selected} COLLATE "C" = (SELECT string_agg(key, E'\n' ORDER BY key COLLATE "C")
                        FROM (SELECT DISTINCT unnest(string_to_array({selected}, E'\n')) COLLATE "C" AS key) keys)
                    AND ({omitted} = '' OR (
                        NOT EXISTS (SELECT 1 FROM unnest(string_to_array({omitted}, E'\n')) key
                            WHERE key COLLATE "C" NOT IN ({omittedAllowed}))
                        AND {omitted} COLLATE "C" = (SELECT string_agg(key, E'\n' ORDER BY key COLLATE "C")
                            FROM (SELECT DISTINCT unnest(string_to_array({omitted}, E'\n')) COLLATE "C" AS key) keys))))
                    IS DISTINCT FROM true THEN
                    RAISE EXCEPTION 'configuration_import_receipt_shape_invalid' USING ERRCODE = '22023';
                END IF;
                SELECT {string.Join(", ", SessionReads.Select(property => $"s.{S(property)}"))}, s.xmin AS transaction_id
                    INTO session FROM {sessions} s
                    WHERE s.{S("SessionId")} = v.{O("SessionId")} AND s.{S("TargetScope")} = 1
                        AND s.{S("TargetTenantId")} IS NULL AND s.{S("TargetAuthorityKey")} COLLATE "C" = 'instance';
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'configuration_import_receipt_session_missing' USING ERRCODE = '42501';
                END IF;
                IF (session.{S("ArtifactDigest")} COLLATE "C" = v.{O("ArtifactDigest")} COLLATE "C"
                    AND session.{S("CreatedAt")} <= v.{O("StartedAt")}
                    AND EXISTS (SELECT 1 FROM {artifacts} artifact
                        WHERE artifact.{A("Id")} = session.{S("ArtifactHandleId")}
                            AND artifact.{A("Sha256Digest")} COLLATE "C" = session.{S("ArtifactDigest")} COLLATE "C"
                            AND artifact.{A("ByteLength")} = session.{S("ArtifactByteLength")}
                            AND artifact.{A("ByteLength")} BETWEEN 1 AND {ConfigurationImportSessionLimits.MaximumArtifactBytes}
                            AND artifact.{A("CreatedAt")} <= session.{S("CreatedAt")}
                            AND artifact.{A("ExpiresAt")} = session.{S("ArtifactExpiresAt")}
                            AND artifact.{A("ExpiresAt")} >= session.{S("ExpiresAt")}))
                    IS DISTINCT FROM true THEN
                    RAISE EXCEPTION 'configuration_import_receipt_artifact_authority_invalid' USING ERRCODE = '42501';
                END IF;
                IF v.{O("SourceOperationId")} IS NOT NULL THEN
                    SELECT r.{O("SnapshotDigest")}, r.{O("SnapshotExpiresAt")}, r.{O("CompletedAt")},
                        r.{O("SnapshotArtifactHandleId")} INTO source FROM {operations} r
                        WHERE r.{O("Id")} = v.{O("SourceOperationId")}
                            AND r.{O("TargetAuthorityKey")} COLLATE "C" = 'instance' AND r.{O("TargetTenantId")} IS NULL
                            AND ((r.{O("Kind")} = 1 AND r.{O("Status")} = 2 AND r.{O("SourceOperationId")} IS NULL)
                                OR (r.{O("Kind")} = 2 AND r.{O("Status")} = 4 AND r.{O("SourceOperationId")} IS NOT NULL))
                            AND r.{O("FidelityVerified")} AND r.{O("EffectOutboxId")}::text ~ {Text(Uuid7Pattern)}
                            AND r.{O("SnapshotArtifactHandleId")}::text ~ {Text(Uuid7Pattern)}
                            AND r.{O("SnapshotDigest")} COLLATE "C" ~ {Text(DigestPattern)}
                            AND r.{O("FidelityDigest")} COLLATE "C" ~ {Text(DigestPattern)}
                            AND pg_catalog.isfinite(r.{O("CompletedAt")})
                            AND r.{O("CompletedAt")} >= r.{O("StartedAt")}
                            AND pg_catalog.isfinite(r.{O("SnapshotExpiresAt")})
                            AND r.{O("SnapshotExpiresAt")} > r.{O("CompletedAt")}
                            AND r.{O("FailureCode")} IS NULL AND r.{O("FailureReason")} IS NULL;
                    IF NOT FOUND OR source.{O("CompletedAt")} > v.{O("StartedAt")} THEN
                        RAISE EXCEPTION 'configuration_import_receipt_source_invalid' USING ERRCODE = '42501';
                    END IF;
                END IF;
                IF v.{O("Status")} = 3 THEN
                    IF (session.{S("State")} IN (1, 2, 3, 4) AND session.{S("ConsumedAt")} IS NULL
                        AND {failedDigests} AND NOT v.{O("FidelityVerified")}
                        AND v.{O("SnapshotArtifactHandleId")} IS NULL AND v.{O("SnapshotDigest")} IS NULL
                        AND v.{O("SnapshotExpiresAt")} IS NULL AND v.{O("EffectOutboxId")} IS NULL
                        AND v.{O("EffectPayloadDigest")} IS NULL
                        AND {omitted} = '' AND v.{O("FailureCode")} COLLATE "C" ~ {Text("^[a-z0-9_]{1,200}$")}
                        AND v.{O("FailureReason")} COLLATE "C" = {Text(SafeFailureReason)})
                        IS DISTINCT FROM true THEN
                        RAISE EXCEPTION 'configuration_import_failed_receipt_invalid' USING ERRCODE = '22023';
                    END IF;
                ELSE
                    IF (v.{O("Status")} = (CASE WHEN v.{O("SourceOperationId")} IS NULL THEN 2 ELSE 4 END)
                        AND session.{S("State")} = 5 AND session.transaction_id = pg_catalog.pg_current_xact_id()::xid
                        AND session.{S("CancelledAt")} IS NULL AND session.{S("ConsumedAt")} = session.{S("UpdatedAt")}
                        AND session.{S("ConsumedAt")} >= v.{O("StartedAt")}
                        AND session.{S("ConsumedAt")} <= v.{O("CompletedAt")}
                        AND session.{S("ConsumedAt")} < session.{S("ExpiresAt")}
                        AND session.{S("ConsumedAt")} < session.{S("PreviewExpiresAt")}
                        AND session.{S("PreviewExpiresAt")} <= session.{S("ExpiresAt")} AND {preview}
                        AND v.{O("SelectedSectionsDigest")} = encode(sha256(convert_to(
                            replace({selected}, E'\n', chr(30)), 'UTF8')), 'hex')
                        AND v.{O("SnapshotArtifactHandleId")}::text ~ {Text(Uuid7Pattern)}
                        AND v.{O("EffectOutboxId")}::text ~ {Text(Uuid7Pattern)}
                        AND v.{O("EffectPayloadDigest")} COLLATE "C" ~ {Text(EffectPayloadDigestPattern)}
                        AND v.{O("SnapshotDigest")} COLLATE "C" ~ {Text(DigestPattern)}
                        AND v.{O("SnapshotArtifactHandleId")} <> session.{S("ArtifactHandleId")}
                        AND v.{O("SnapshotExpiresAt")} > v.{O("CompletedAt")}
                        AND v.{O("FidelityVerified")} AND v.{O("FailureCode")} IS NULL AND v.{O("FailureReason")} IS NULL)
                        IS DISTINCT FROM true THEN
                        RAISE EXCEPTION 'configuration_import_success_receipt_invalid' USING ERRCODE = '22023';
                    END IF;
                    IF v.{O("SourceOperationId")} IS NOT NULL THEN
                        IF (source.{O("SnapshotDigest")} COLLATE "C" = v.{O("ArtifactDigest")} COLLATE "C"
                            AND source.{O("SnapshotExpiresAt")} > session.{S("ConsumedAt")}
                            AND EXISTS (SELECT 1 FROM {artifacts} a
                                WHERE a.{A("Id")} = source.{O("SnapshotArtifactHandleId")}
                                    AND a.{A("Sha256Digest")} COLLATE "C" = source.{O("SnapshotDigest")} COLLATE "C"
                                    AND a.{A("ExpiresAt")} = source.{O("SnapshotExpiresAt")}))
                            IS DISTINCT FROM true THEN
                            RAISE EXCEPTION 'configuration_import_rollback_source_invalid' USING ERRCODE = '42501';
                        END IF;
                    END IF;
                    SELECT {string.Join(", ", ArtifactReads.Select(property => $"a.{A(property)}"))}, a.xmin AS transaction_id
                        INTO snapshot FROM {artifacts} a WHERE a.{A("Id")} = v.{O("SnapshotArtifactHandleId")};
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'configuration_import_receipt_snapshot_missing' USING ERRCODE = '42501';
                    END IF;
                    IF (snapshot.transaction_id = pg_catalog.pg_current_xact_id()::xid
                        AND snapshot.{A("Sha256Digest")} COLLATE "C" = v.{O("SnapshotDigest")} COLLATE "C"
                        AND snapshot.{A("ByteLength")} BETWEEN 1 AND {ConfigurationImportSessionLimits.MaximumArtifactBytes}
                        AND snapshot.{A("CreatedAt")} = session.{S("ConsumedAt")}
                        AND snapshot.{A("ExpiresAt")} = v.{O("SnapshotExpiresAt")}
                        AND snapshot.{A("ExpiresAt")} = snapshot.{A("CreatedAt")} + interval '{retentionSeconds} seconds'
                        AND NOT EXISTS (SELECT 1 FROM {sessions} s
                            WHERE s.{S("ArtifactHandleId")} = v.{O("SnapshotArtifactHandleId")}))
                        IS DISTINCT FROM true THEN
                        RAISE EXCEPTION 'configuration_import_receipt_snapshot_invalid' USING ERRCODE = '22023';
                    END IF;
                END IF;
                INSERT INTO {operations} ({string.Join(", ", OperationColumns.Select(O))})
                    VALUES ({string.Join(", ", OperationColumns.Select(property => $"v.{O(property)}"))});
                RETURN;
            END;
            """;
    }

    private static string StoreEffectBody(ExploreDbContext context)
    {
        string E(string property) => Column(context, typeof(OutboxMessage), property);
        string O(string property) => Column(context, typeof(ConfigurationImportOperation), property);
        string effects = Table(context, typeof(OutboxMessage));
        string operations = Table(context, typeof(ConfigurationImportOperation));
        string catalog = string.Join(",\n",
            ConfigurationManifestCatalog.InstanceSettings.Values.Select(entry => (Scope: 0, Entry: entry))
                .Concat(ConfigurationManifestCatalog.TenantSettings.Values.Select(entry => (Scope: 1, Entry: entry)))
                .Where(item => !item.Entry.Definition.IsSensitive)
                .Select(item => $"({item.Scope}, {Text(item.Entry.Definition.Key)}, {(int)item.Entry.Definition.ValueType}, " +
                    $"{item.Entry.MaximumStringLength?.ToString(CultureInfo.InvariantCulture) ?? "NULL::integer"}, " +
                    $"{Text(JsonSerializer.Serialize(item.Entry.Definition.AllowedValues))}::jsonb, " +
                    $"{Text(JsonSerializer.Serialize(item.Entry.StringArray?.AllowedValues))}::jsonb, " +
                    $"{(item.Entry.StringArray is not null).ToString().ToLowerInvariant()})"));
        return $"""
            DECLARE v {effects}%ROWTYPE; receipt record; notification jsonb; descriptor record;
                field text; value jsonb; notification_scope integer; notification_tenant uuid;
                notification_time timestamptz;
            BEGIN
                PERFORM {FunctionSql(context, "assert_authority")}();
                IF current_setting('transaction_isolation') <> 'serializable' THEN
                    RAISE EXCEPTION 'configuration_import_receipt_transaction_required' USING ERRCODE = '25001';
                END IF;
                {InputShape(context, typeof(OutboxMessage), EffectColumns)}
                SELECT * INTO v FROM jsonb_populate_record(NULL::{effects}, p_value);
                IF (v.{E("Id")}::text ~ {Text(Uuid7Pattern)} AND v.{E("AggregateId")}::text ~ {Text(Uuid7Pattern)}
                    AND v.{E("AggregateType")} COLLATE "C" = 'ConfigurationImportOperation'
                    AND v.{E("EventType")} COLLATE "C" = 'ConfigurationImportEffectsRequested'
                    AND v.{E("Status")} = 1 AND v.{E("RetryCount")} = 0 AND v.{E("MaxRetries")} = 5
                    AND v.{E("ProcessedAt")} IS NULL AND v.{E("NextRetryAt")} IS NULL
                    AND v.{E("LastError")} IS NULL AND v.{E("DeadLetteredAt")} IS NULL)
                    IS DISTINCT FROM true THEN
                    RAISE EXCEPTION 'configuration_import_effect_shape_invalid' USING ERRCODE = '22023';
                END IF;
                SELECT {string.Join(", ", OperationColumns.Select(property => $"r.{O(property)}"))}, r.xmin AS transaction_id
                    INTO receipt FROM {operations} r WHERE r.{O("Id")} = v.{E("AggregateId")}
                        AND r.{O("TargetAuthorityKey")} COLLATE "C" = 'instance' AND r.{O("TargetTenantId")} IS NULL;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'configuration_import_effect_receipt_missing' USING ERRCODE = '42501';
                END IF;
                IF (receipt.transaction_id = pg_catalog.pg_current_xact_id()::xid
                    AND receipt.{O("Status")} IN (2, 4) AND receipt.{O("FidelityVerified")}
                    AND receipt.{O("EffectOutboxId")} = v.{E("Id")}
                    AND receipt.{O("EffectPayloadDigest")} = upper(encode(
                        sha256(convert_to(COALESCE(v.{E("Payload")}, ''), 'UTF8')), 'hex'))
                    AND receipt.{O("CompletedAt")} = v.{E("CreatedAt")}) IS DISTINCT FROM true THEN
                    RAISE EXCEPTION 'configuration_import_effect_receipt_invalid' USING ERRCODE = '42501';
                END IF;
                IF v.{E("Payload")} IS NOT NULL THEN
                    IF (jsonb_typeof(v.{E("Payload")}::jsonb) = 'array'
                        AND octet_length(v.{E("Payload")}) <= {ConfigurationImportSessionLimits.MaximumArtifactBytes}
                        AND jsonb_array_length(v.{E("Payload")}::jsonb) <= {MaximumNotifications}) IS DISTINCT FROM true THEN
                        RAISE EXCEPTION 'configuration_import_effect_payload_invalid' USING ERRCODE = '22023';
                    END IF;
                    IF EXISTS (SELECT 1 FROM jsonb_array_elements(v.{E("Payload")}::jsonb) n
                        GROUP BY n->>'Key', n->>'TenantId' HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'configuration_import_effect_duplicate_notification' USING ERRCODE = '22023';
                    END IF;
                    FOR notification IN SELECT * FROM jsonb_array_elements(v.{E("Payload")}::jsonb) LOOP
                        IF jsonb_typeof(notification) IS DISTINCT FROM 'object' THEN
                            RAISE EXCEPTION 'configuration_import_effect_notification_invalid' USING ERRCODE = '22023';
                        END IF;
                        IF ((SELECT count(*) FROM jsonb_object_keys(notification)) = {NotificationFields.Length}
                            AND notification ?& ARRAY[{string.Join(", ", NotificationFields.Select(Text))}]
                            AND jsonb_typeof(notification->'Key') = 'string'
                            AND notification->'IsSensitive' = 'false'::jsonb
                            AND notification->'Scope' IN ('0'::jsonb, '1'::jsonb)
                            AND jsonb_typeof(notification->'ActorUserId') = 'string'
                            AND (notification->>'ActorUserId')::uuid = receipt.{O("ActorUserId")}
                            AND jsonb_typeof(notification->'ChangedAt') = 'string'
                            AND notification->>'ChangedAt' ~ '(Z|[+]00:00)$'
                            AND (jsonb_typeof(notification->'OldValue') = 'string' OR notification->'OldValue' = 'null'::jsonb)
                            AND (jsonb_typeof(notification->'NewValue') = 'string' OR notification->'NewValue' = 'null'::jsonb))
                            IS DISTINCT FROM true THEN
                            RAISE EXCEPTION 'configuration_import_effect_notification_invalid' USING ERRCODE = '22023';
                        END IF;
                        notification_scope := (notification->>'Scope')::integer;
                        notification_tenant := (notification->>'TenantId')::uuid;
                        notification_time := regexp_replace(notification->>'ChangedAt',
                            {Text(SubMicrosecondPattern)}, '\1\2')::timestamptz;
                        IF (pg_catalog.isfinite(notification_time)
                            AND notification_time BETWEEN receipt.{O("StartedAt")} AND receipt.{O("CompletedAt")}
                            AND (CASE WHEN notification_scope = 0 THEN
                                notification->'TenantId' = 'null'::jsonb
                                AND 'instance.settings' = ANY (string_to_array(receipt.{O("_selectedSectionKeys")}, E'\n'))
                            ELSE jsonb_typeof(notification->'TenantId') = 'string'
                                AND notification_tenant <> {EmptyUuid}
                                AND 'tenant.settings' = ANY (string_to_array(receipt.{O("_selectedSectionKeys")}, E'\n')) END))
                            IS DISTINCT FROM true THEN
                            RAISE EXCEPTION 'configuration_import_effect_notification_authority_invalid' USING ERRCODE = '42501';
                        END IF;
                        SELECT * INTO descriptor FROM (VALUES {catalog})
                            admitted(scope, key, type, maximum_length, allowed_values, allowed_items, string_array)
                            WHERE admitted.scope = notification_scope
                                AND admitted.key COLLATE "C" = (notification->>'Key') COLLATE "C";
                        IF NOT FOUND THEN
                            RAISE EXCEPTION 'configuration_import_effect_key_invalid' USING ERRCODE = '22023';
                        END IF;
                        FOREACH field IN ARRAY ARRAY['OldValue', 'NewValue'] LOOP
                            IF notification->field = 'null'::jsonb THEN CONTINUE; END IF;
                            value := (notification->>field)::jsonb;
                            IF (CASE descriptor.type
                                WHEN 0 THEN jsonb_typeof(value) = 'string'
                                    AND (descriptor.maximum_length IS NULL
                                        OR char_length(value #>> ARRAY[]::text[]) <= descriptor.maximum_length)
                                    AND (descriptor.allowed_values = 'null'::jsonb
                                        OR descriptor.allowed_values @> jsonb_build_array(value))
                                WHEN 1 THEN jsonb_typeof(value) = 'number' AND value::text ~ '^-?[0-9]+$'
                                    AND (value::text)::numeric BETWEEN -2147483648 AND 2147483647
                                WHEN 2 THEN jsonb_typeof(value) = 'boolean'
                                WHEN 6 THEN jsonb_typeof(value) = 'number' AND value::text ~ '^-?[0-9]+$'
                                    AND (value::text)::numeric BETWEEN -9223372036854775808 AND 9223372036854775807
                                WHEN 4 THEN descriptor.string_array AND (CASE WHEN jsonb_typeof(value) = 'array' THEN
                                    NOT EXISTS (SELECT 1 FROM jsonb_array_elements(value) item
                                        WHERE jsonb_typeof(item) <> 'string'
                                            OR (descriptor.allowed_items <> 'null'::jsonb
                                                AND NOT descriptor.allowed_items @> jsonb_build_array(item)))
                                    ELSE false END)
                                ELSE false END) IS DISTINCT FROM true THEN
                                RAISE EXCEPTION 'configuration_import_effect_value_invalid' USING ERRCODE = '22023';
                            END IF;
                        END LOOP;
                    END LOOP;
                END IF;
                INSERT INTO {effects} ({string.Join(", ", EffectColumns.Select(E))})
                    VALUES ({string.Join(", ", EffectColumns.Select(property => $"v.{E(property)}"))});
                RETURN;
            END;
            """;
    }

    private static string ReadEffectBody(ExploreDbContext context) => $"""
        BEGIN
            PERFORM {FunctionSql(context, "assert_authority")}();
            IF p_id IS NULL OR p_id::text !~ {Text(Uuid7Pattern)} THEN
                RAISE EXCEPTION 'configuration_import_effect_id_invalid' USING ERRCODE = '22023';
            END IF;
            RETURN QUERY SELECT effect.* FROM {Table(context, typeof(OutboxMessage))} effect
                JOIN {Table(context, typeof(ConfigurationImportOperation))} receipt
                    ON receipt.{Column(context, typeof(ConfigurationImportOperation), "Id")} =
                        effect.{Column(context, typeof(OutboxMessage), "AggregateId")}
                    AND receipt.{Column(context, typeof(ConfigurationImportOperation), "EffectOutboxId")} =
                        effect.{Column(context, typeof(OutboxMessage), "Id")}
                WHERE effect.{Column(context, typeof(OutboxMessage), "Id")} = p_id
                    AND effect.{Column(context, typeof(OutboxMessage), "AggregateType")} = 'ConfigurationImportOperation'
                    AND effect.{Column(context, typeof(OutboxMessage), "EventType")} = 'ConfigurationImportEffectsRequested'
                    AND receipt.{Column(context, typeof(ConfigurationImportOperation), "TargetAuthorityKey")} = 'instance'
                    AND receipt.{Column(context, typeof(ConfigurationImportOperation), "TargetTenantId")} IS NULL
                    AND receipt.{Column(context, typeof(ConfigurationImportOperation), "Status")} IN (2, 4);
        END;
        """;
}
