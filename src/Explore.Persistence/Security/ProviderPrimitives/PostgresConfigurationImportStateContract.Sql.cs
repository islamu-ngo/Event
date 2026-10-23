using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Persistence.Entities;

namespace Explore.Persistence.Security;

public static partial class PostgresConfigurationImportStateContract
{
    private static string BuildFunctions(ExploreDbContext context, string authorityGuard)
    {
        string S(string property) => Column<ConfigurationImportSession>(context, property);
        string A(string property) => Column<ConfigurationImportStoredArtifact>(context, property);
        string O(string property) => Column<ConfigurationImportOperation>(context, property);
        string sessions = Table<ConfigurationImportSession>(context);
        string artifacts = Table<ConfigurationImportStoredArtifact>(context);
        string operations = Table<ConfigurationImportOperation>(context);
        string sessionProjection = string.Join(", ", SessionColumns.Select(column => $"stored.{S(column.Property)}::{column.Type}"));
        string sessionReturns = string.Join(", ", SessionColumns.Select(column => $"{S(column.Property)} {column.Type}"));
        string artifactProjection = string.Join(", ", ArtifactColumns.Select(column => $"stored.{A(column.Property)}::{column.Type}"));
        string artifactReturns = string.Join(", ", ArtifactColumns.Select(column => $"{A(column.Property)} {column.Type}"));
        string sessionScope = $"stored.{S("TargetScope")} = 1 AND stored.{S("TargetTenantId")} IS NULL " +
            $"AND stored.{S("TargetAuthorityKey")} COLLATE \"C\" = 'instance'";
        string emptyUuid = "'00000000-0000-0000-0000-000000000000'::uuid";
        string digestPattern = "'^[0-9a-f]{64}$'";
        string idGuard = $"""
            IF p_id IS NULL OR p_id = {emptyUuid} THEN
                RAISE EXCEPTION 'configuration_import_state_identity_invalid' USING ERRCODE = '22023';
            END IF;
            """;
        string[] immutableProperties =
        [
            "TargetScope", "TargetTenantId", "TargetAuthorityKey", "ArtifactHandleId",
            "ArtifactDigest", "ArtifactByteLength", "ArtifactExpiresAt",
            "AccessTokenDigest", "CreatedAt", "ExpiresAt"
        ];
        string[] immutableParameters =
        [
            "p_scope", "p_tenant", "p_authority", "p_handle", "p_digest", "p_length",
            "p_artifact_expiry", "p_token_digest", "p_created", "p_expiry"
        ];
        string immutableGuard = string.Join(" OR ", immutableProperties.Zip(immutableParameters,
            (property, parameter) => $"v_session.{S(property)} IS DISTINCT FROM {parameter}"));
        string[] previewProperties =
        [
            "PreviewArtifactDigest", "PreviewTargetRevisionDigest", "PreviewSelectedSectionsDigest",
            "PreviewMappingDigest", "PreviewRequiredApprovalDigest", "PreviewApplyMode", "PreviewExpiresAt"
        ];
        string[] previewParameters =
        [
            "p_preview_artifact", "p_preview_target", "p_preview_sections",
            "p_preview_mapping", "p_preview_approval", "p_preview_mode", "p_preview_expiry"
        ];
        string previewUnchanged = string.Join(" AND ", previewProperties.Zip(previewParameters,
            (property, parameter) => $"v_session.{S(property)} IS NOT DISTINCT FROM {parameter}"));
        string previewDigestsValid = string.Join(" AND ", previewParameters.Take(5)
            .Select(parameter => $"{parameter} COLLATE \"C\" ~ '^[0-9a-f]{{64}}$'"));
        string[] updateParameters =
        [
            "p_state", "p_updated", "p_cancelled", "p_consumed", "p_preview_artifact",
            "p_preview_target", "p_preview_sections", "p_preview_mapping", "p_preview_approval",
            "p_preview_mode", "p_preview_expiry", "p_revision"
        ];
        string assignments = string.Join(", ", SessionUpdates.Zip(updateParameters,
            (property, parameter) => $"{S(property)} = {parameter}"));
        string sessionCreate = $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, "create_session")}(
                p_id uuid, p_handle uuid, p_digest text, p_length integer,
                p_artifact_expiry timestamp with time zone, p_token_digest text,
                p_created timestamp with time zone, p_expiry timestamp with time zone)
            RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS $body$
            BEGIN
                {authorityGuard}
                {idGuard}
                IF (pg_catalog.substring(p_id::text, 15, 1) = '7'
                    AND pg_catalog.substring(p_handle::text, 15, 1) = '7'
                    AND p_digest COLLATE "C" ~ {digestPattern}
                    AND p_token_digest COLLATE "C" ~ {digestPattern}
                    AND p_length BETWEEN 1 AND {ConfigurationImportSessionLimits.MaximumArtifactBytes}
                    AND pg_catalog.isfinite(p_created) AND pg_catalog.isfinite(p_expiry)
                    AND pg_catalog.isfinite(p_artifact_expiry)
                    AND p_expiry > p_created
                    AND p_expiry <= p_created + INTERVAL '1 hour'
                    AND p_artifact_expiry >= p_expiry) IS DISTINCT FROM true THEN
                    RAISE EXCEPTION 'configuration_import_state_session_shape_invalid' USING ERRCODE = '22023';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM {artifacts} stored
                        WHERE stored.{A("Id")} = p_handle
                            AND stored.{A("Sha256Digest")} COLLATE "C" = p_digest COLLATE "C"
                            AND stored.{A("ByteLength")} = p_length
                            AND stored.{A("CreatedAt")} = p_created
                            AND stored.{A("ExpiresAt")} = p_artifact_expiry
                            AND stored.xmin::text = pg_catalog.pg_current_xact_id()::xid::text
                            AND NOT EXISTS (SELECT 1 FROM {sessions} session
                                WHERE session.{S("ArtifactHandleId")} = p_handle)) THEN
                    RAISE EXCEPTION 'configuration_import_state_session_artifact_invalid' USING ERRCODE = '42501';
                END IF;
                INSERT INTO {sessions} ({string.Join(", ", SessionColumns.Select(column => S(column.Property)))})
                    VALUES (p_id, 1, NULL, 'instance', p_handle, p_digest, p_length,
                        p_artifact_expiry, p_token_digest, 1, p_created, p_created, p_expiry,
                        NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1);
            END; $body$;
            """;
        string sessionRead = $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, "read_session")}(p_id uuid)
            RETURNS TABLE ({sessionReturns})
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS $body$
            BEGIN
                {authorityGuard}
                {idGuard}
                RETURN QUERY SELECT {sessionProjection}
                    FROM {sessions} stored
                    WHERE stored.{S("SessionId")} = p_id AND {sessionScope}
                    FOR UPDATE OF stored;
            END; $body$;
            """;
        string sessionWrite = $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, "write_session")}(
                p_id uuid, p_expected bigint, p_scope integer, p_tenant uuid,
                p_authority text, p_handle uuid, p_digest text, p_length integer,
                p_artifact_expiry timestamp with time zone, p_token_digest text,
                p_state integer, p_created timestamp with time zone,
                p_updated timestamp with time zone, p_expiry timestamp with time zone,
                p_cancelled timestamp with time zone, p_consumed timestamp with time zone,
                p_preview_artifact text, p_preview_target text, p_preview_sections text,
                p_preview_mapping text, p_preview_approval text, p_preview_mode integer,
                p_preview_expiry timestamp with time zone, p_revision bigint)
            RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS $body$
            DECLARE v_session record;
            BEGIN
                {authorityGuard}
                {idGuard}
                IF (p_expected > 0 AND p_expected < 9223372036854775807
                    AND p_revision = p_expected + 1
                    AND p_scope = 1 AND p_tenant IS NULL
                    AND p_authority COLLATE "C" = 'instance'
                    AND p_state BETWEEN 2 AND 5
                    AND p_updated IS NOT NULL AND pg_catalog.isfinite(p_updated)) IS DISTINCT FROM true THEN
                    RAISE EXCEPTION 'configuration_import_state_shape_invalid' USING ERRCODE = '22023';
                END IF;
                SELECT {string.Join(", ", SessionColumns.Select(column => $"stored.{S(column.Property)}"))}
                    INTO v_session FROM {sessions} stored
                    WHERE stored.{S("SessionId")} = p_id AND {sessionScope}
                    FOR UPDATE OF stored;
                IF NOT FOUND OR v_session.{S("Revision")} IS DISTINCT FROM p_expected THEN
                    RAISE EXCEPTION 'configuration_import_state_revision_conflict' USING ERRCODE = '40001';
                END IF;
                IF {immutableGuard} THEN
                    RAISE EXCEPTION 'configuration_import_state_immutable_field_changed' USING ERRCODE = '22023';
                END IF;
                IF p_updated < v_session.{S("CreatedAt")} THEN
                    RAISE EXCEPTION 'configuration_import_state_time_invalid' USING ERRCODE = '22023';
                END IF;
                IF p_state = 2 THEN
                    IF (v_session.{S("State")} IN (1, 2)
                        AND p_updated < v_session.{S("ExpiresAt")}
                        AND p_cancelled IS NULL AND p_consumed IS NULL
                        AND {previewDigestsValid}
                        AND p_preview_artifact COLLATE "C" = v_session.{S("ArtifactDigest")} COLLATE "C"
                        AND p_preview_mode BETWEEN 0 AND 5
                        AND pg_catalog.isfinite(p_preview_expiry)
                        AND p_preview_expiry > p_updated
                        AND p_preview_expiry <= v_session.{S("ExpiresAt")}) IS DISTINCT FROM true THEN
                        RAISE EXCEPTION 'configuration_import_state_transition_invalid' USING ERRCODE = '22023';
                    END IF;
                ELSE
                    IF NOT ({previewUnchanged}) THEN
                        RAISE EXCEPTION 'configuration_import_state_preview_changed' USING ERRCODE = '22023';
                    END IF;
                    IF p_state = 3 THEN
                        IF (v_session.{S("State")} IN (1, 2)
                            AND p_updated < v_session.{S("ExpiresAt")}
                            AND p_cancelled IS NOT DISTINCT FROM p_updated
                            AND p_consumed IS NULL) IS DISTINCT FROM true THEN
                            RAISE EXCEPTION 'configuration_import_state_transition_invalid' USING ERRCODE = '22023';
                        END IF;
                    ELSIF p_state = 4 THEN
                        IF (v_session.{S("State")} IN (1, 2, 4)
                            AND p_updated >= v_session.{S("ExpiresAt")}
                            AND p_cancelled IS NULL AND p_consumed IS NULL) IS DISTINCT FROM true THEN
                            RAISE EXCEPTION 'configuration_import_state_transition_invalid' USING ERRCODE = '22023';
                        END IF;
                    ELSIF p_state = 5 THEN
                        IF (v_session.{S("State")} = 2
                            AND p_updated < v_session.{S("ExpiresAt")}
                            AND v_session.{S("PreviewExpiresAt")} > p_updated
                            AND p_consumed IS NOT DISTINCT FROM p_updated
                            AND p_cancelled IS NULL) IS DISTINCT FROM true THEN
                            RAISE EXCEPTION 'configuration_import_state_transition_invalid' USING ERRCODE = '22023';
                        END IF;
                    END IF;
                END IF;
                UPDATE {sessions} stored SET {assignments}
                    WHERE stored.{S("SessionId")} = p_id AND {sessionScope}
                        AND stored.{S("Revision")} = p_expected;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'configuration_import_state_revision_conflict' USING ERRCODE = '40001';
                END IF;
            END; $body$;
            """;
        string artifactRead = $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, "read_artifact")}(p_id uuid)
            RETURNS TABLE ({artifactReturns})
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS $body$
            BEGIN
                {authorityGuard}
                {idGuard}
                RETURN QUERY SELECT {artifactProjection} FROM {artifacts} stored
                    WHERE stored.{A("Id")} = p_id
                        AND (EXISTS (SELECT 1 FROM {sessions} session
                                WHERE session.{S("ArtifactHandleId")} = p_id
                                    AND session.{S("TargetScope")} = 1
                                    AND session.{S("TargetTenantId")} IS NULL
                                    AND session.{S("TargetAuthorityKey")} COLLATE "C" = 'instance')
                            OR EXISTS (SELECT 1 FROM {operations} receipt
                                WHERE receipt.{O("SnapshotArtifactHandleId")} = p_id
                                    AND receipt.{O("TargetAuthorityKey")} COLLATE "C" = 'instance'
                                    AND receipt.{O("TargetTenantId")} IS NULL
                                    AND ((receipt.{O("Kind")} = 1 AND receipt.{O("Status")} = 2)
                                        OR (receipt.{O("Kind")} = 2 AND receipt.{O("Status")} = 4))));
            END; $body$;
            """;
        string artifactStore = $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, "store_artifact")}(
                p_id uuid, p_payload bytea, p_digest text, p_length integer,
                p_created timestamp with time zone, p_expiry timestamp with time zone)
            RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS $body$
            DECLARE v_artifact record;
            BEGIN
                {authorityGuard}
                {idGuard}
                IF (p_digest COLLATE "C" ~ {digestPattern}
                    AND p_length BETWEEN 1 AND {ConfigurationImportSessionLimits.MaximumArtifactBytes}
                    AND pg_catalog.octet_length(p_payload) BETWEEN 1 AND {ConfigurationImportSessionLimits.MaximumArtifactBytes + 16 * 1024}
                    AND pg_catalog.isfinite(p_created) AND pg_catalog.isfinite(p_expiry)
                    AND p_expiry > p_created) IS DISTINCT FROM true THEN
                    RAISE EXCEPTION 'configuration_import_state_artifact_shape_invalid' USING ERRCODE = '22023';
                END IF;
                IF EXISTS (SELECT 1 FROM {sessions} session
                        WHERE session.{S("ArtifactHandleId")} = p_id
                            AND (session.{S("TargetScope")} = 1
                                AND session.{S("TargetTenantId")} IS NULL
                                AND session.{S("TargetAuthorityKey")} COLLATE "C" = 'instance') IS DISTINCT FROM true)
                    OR EXISTS (SELECT 1 FROM {operations} receipt
                        WHERE receipt.{O("SnapshotArtifactHandleId")} = p_id
                            AND (receipt.{O("TargetAuthorityKey")} COLLATE "C" = 'instance'
                                AND receipt.{O("TargetTenantId")} IS NULL) IS DISTINCT FROM true) THEN
                    RAISE EXCEPTION 'configuration_import_state_artifact_target_invalid' USING ERRCODE = '42501';
                END IF;
                INSERT INTO {artifacts} ({string.Join(", ", ArtifactColumns.Select(column => A(column.Property)))})
                    VALUES (p_id, p_payload, p_digest, p_length, p_created, p_expiry)
                    ON CONFLICT ({A("Id")}) DO NOTHING;
                SELECT stored.{A("Sha256Digest")}, stored.{A("ByteLength")}, stored.{A("ExpiresAt")}
                    INTO v_artifact FROM {artifacts} stored WHERE stored.{A("Id")} = p_id;
                IF NOT FOUND OR v_artifact.{A("Sha256Digest")} COLLATE "C" IS DISTINCT FROM p_digest COLLATE "C"
                    OR v_artifact.{A("ByteLength")} IS DISTINCT FROM p_length
                    OR v_artifact.{A("ExpiresAt")} IS DISTINCT FROM p_expiry THEN
                    RAISE EXCEPTION '{ConfigurationImportFailureCodes.ArtifactIntegrityInvalid}' USING ERRCODE = '22000';
                END IF;
            END; $body$;
            """;
        string artifactDelete = $"""
            CREATE OR REPLACE FUNCTION {FunctionSql(context, "delete_artifact")}(p_id uuid)
            RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS $body$
            BEGIN
                {authorityGuard}
                {idGuard}
                IF NOT EXISTS (SELECT 1 FROM {sessions} session
                        WHERE session.{S("ArtifactHandleId")} = p_id
                            AND session.{S("TargetScope")} = 1
                            AND session.{S("TargetTenantId")} IS NULL
                            AND session.{S("TargetAuthorityKey")} COLLATE "C" = 'instance'
                            AND session.{S("State")} IN (3, 4)
                            AND session.xmin::text = pg_catalog.pg_current_xact_id()::xid::text)
                    OR EXISTS (SELECT 1 FROM {sessions} session
                        WHERE session.{S("ArtifactHandleId")} = p_id
                            AND (session.{S("TargetScope")} = 1
                                AND session.{S("TargetTenantId")} IS NULL
                                AND session.{S("TargetAuthorityKey")} COLLATE "C" = 'instance'
                                AND session.{S("State")} IN (3, 4)) IS DISTINCT FROM true)
                    OR EXISTS (SELECT 1 FROM {operations} receipt
                        WHERE receipt.{O("SnapshotArtifactHandleId")} = p_id) THEN
                    RAISE EXCEPTION 'configuration_import_state_artifact_delete_invalid' USING ERRCODE = '42501';
                END IF;
                DELETE FROM {artifacts} stored WHERE stored.{A("Id")} = p_id;
            END; $body$;
            """;
        return sessionCreate + sessionRead + sessionWrite + artifactRead + artifactStore + artifactDelete;
    }
}
