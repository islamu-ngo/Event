using System.Globalization;
using System.Text.Json;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

public static partial class PostgresEmailDeliveryPolicyContract
{
    internal sealed record ControlSnapshot(Guid TenantId, long DeliveryPolicyRevision,
        long? OptionalSuppressedThroughRevision, DateTime? OptionalSuppressedThroughUtc);

    internal sealed record ControlChange(Guid TenantId, long BaselineRevision, long ExpectedRevision,
        long Revision, long? SuppressedRevision, DateTime? SuppressedUtc);

    private static string ControlsFunction(ExploreDbContext context, string suffix = "") =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            $"email_delivery_policy_controls{suffix}", Schema(context));

    internal static async Task RequireMutationAsync(
        ExploreDbContext context, Guid? tenantId, CancellationToken cancellationToken)
    {
        RequireControlTransaction(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {ControlsFunction(context, "_require_mutation")}(@tenant)";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid)
        { Value = (object?)tenantId ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<Dictionary<Guid, ControlSnapshot>> ReadControlsAsync(
        ExploreDbContext context, Guid[] tenantIds, CancellationToken cancellationToken)
    {
        RequireControlTransaction(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT * FROM {ControlsFunction(context)}(@tenants)";
        command.Parameters.Add(new NpgsqlParameter("tenants", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = tenantIds });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<Guid, ControlSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new ControlSnapshot(reader.GetGuid(0), reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3));
            result.Add(row.TenantId, row);
        }
        return result;
    }

    internal static async Task ApplyControlsAsync(ExploreDbContext context, ControlChange[] changes,
        DateTime now, Guid? actorId, CancellationToken cancellationToken)
    {
        RequireControlTransaction(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {ControlsFunction(context, "_apply")}(@changes, @now, @actor)";
        command.Parameters.Add(new NpgsqlParameter("changes", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(changes) });
        command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = (object?)actorId ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void RequireControlTransaction(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Email controls require the setting mutation's original transaction.");
    }

    private static string BuildControlsSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var entity = context.Model.FindEntityType(typeof(EmailDispatchTenantControl))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string schema = sql.DelimitIdentifier(Schema(context));
        string table = sql.DelimitIdentifier(entity.GetTableName()!, Schema(context));
        string C(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(store)!);
        string id = C(nameof(EmailDispatchTenantControl.Id)), tenant = C(nameof(EmailDispatchTenantControl.TenantId));
        string revision = C(nameof(EmailDispatchTenantControl.DeliveryPolicyRevision));
        string suppressed = C(nameof(EmailDispatchTenantControl.OptionalSuppressedThroughRevision));
        string cutoff = C(nameof(EmailDispatchTenantControl.OptionalSuppressedThroughUtc));
        string created = C(nameof(EmailDispatchTenantControl.CreatedAt)), creator = C(nameof(EmailDispatchTenantControl.CreatedBy));
        string updated = C(nameof(EmailDispatchTenantControl.UpdatedAt)), updater = C(nameof(EmailDispatchTenantControl.UpdatedBy));
        string paused = C(nameof(EmailDispatchTenantControl.IsPaused));
        string read = ControlsFunction(context), write = ControlsFunction(context, "_apply");
        string receipts = sql.DelimitIdentifier("email_delivery_policy_mutation_receipts", Schema(context));
        string capture = ControlsFunction(context, "_capture");
        string require = ControlsFunction(context, "_require_mutation");
        string tenants = sql.DelimitIdentifier(context.Model.FindEntityType(typeof(Tenant))!.GetTableName()!, Schema(context));
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context));
        string Column<T>(string property)
        {
            var type = context.Model.FindEntityType(typeof(T))!;
            return sql.DelimitIdentifier(type.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema()))!);
        }
        string tenantId = Column<Tenant>(nameof(Tenant.Id));
        string keys = string.Join(", ", EmailDeliverySettingKeys.All.Select(key => $"'{key.Replace("'", "''")}'"));
        string policyLock = RelationalSettingMutationLock.ComputeStableLockKey(GovernanceSettingKeys.Email.DeliveryEnabled)
            .ToString(CultureInfo.InvariantCulture);
        string claimLock = RelationalNamedLock.ComputeStableKey(EmailDispatchOutboxRepository.ClaimAdvisoryLockName)
            .ToString(CultureInfo.InvariantCulture);
        string guard = PrivilegeAssertion(context, enrollment);
        string controlAuthority = $$"""
            {{guard}}
            {{RestrictedCallerSql}}
            IF session_user NOT IN ({{LoginLiteral(enrollment.EmailInstanceMutationLogin)}},
                {{LoginLiteral(enrollment.EmailTenantMutationLogin)}}) THEN
                RAISE EXCEPTION 'email policy mutation login mismatch' USING ERRCODE = '42501';
            END IF;
            IF NOT pg_has_role(session_user, '{{InstanceMutationRole}}', 'USAGE')
                AND NOT pg_has_role(session_user, '{{TenantMutationRole}}', 'USAGE') THEN
                RAISE EXCEPTION 'email policy mutation purpose required' USING ERRCODE = '42501';
            END IF;
            """;
        return $$"""
            CREATE TABLE IF NOT EXISTS {{receipts}} (
                tenant_id uuid PRIMARY KEY,
                mutation_xid xid8 NOT NULL,
                mutation_login name NOT NULL,
                source_tenant_id uuid,
                baseline_revision bigint NOT NULL,
                baseline_suppressed_revision bigint,
                baseline_suppressed_utc timestamp with time zone,
                actor_id uuid,
                mutation_sequence bigint NOT NULL DEFAULT 1,
                applied_sequence bigint,
                applied_boundary integer
            );
            REVOKE ALL ON {{receipts}} FROM PUBLIC, {{OwnerRole}}, {{RuntimeRole}}, {{GlobalRole}},
                {{InstanceMutationRole}}, {{TenantMutationRole}};
            GRANT SELECT, INSERT, UPDATE ON {{receipts}} TO {{OwnerRole}};
            GRANT CREATE ON SCHEMA {{schema}} TO {{OwnerRole}};
            REVOKE ALL ON {{table}} FROM {{OwnerRole}}, {{RuntimeRole}}, {{GlobalRole}},
                {{InstanceMutationRole}}, {{TenantMutationRole}};
            GRANT SELECT ({{tenant}}, {{revision}}, {{suppressed}}, {{cutoff}}) ON {{table}} TO {{OwnerRole}};
            GRANT INSERT ({{id}}, {{tenant}}, {{revision}}, {{suppressed}}, {{cutoff}},
                {{created}}, {{creator}}, {{updated}}, {{updater}}, {{paused}}) ON {{table}} TO {{OwnerRole}};
            GRANT UPDATE ({{revision}}, {{suppressed}}, {{cutoff}}, {{updated}}, {{updater}}) ON {{table}} TO {{OwnerRole}};
            DROP POLICY IF EXISTS email_delivery_control_owner ON {{table}};
            CREATE POLICY email_delivery_control_owner ON {{table}} FOR ALL TO {{OwnerRole}}
                USING (true) WITH CHECK (true);
            DO $install_controls$
            DECLARE v_previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{{OwnerRole}}', true);
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {{capture}}()
                RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE v_source uuid; v_actor uuid; v_key text; v_instance boolean := TG_ARGV[0] = 'instance';
                BEGIN
                    {{guard}}
                    IF TG_OP = 'DELETE' THEN
                        v_key := OLD.{{Column<TenantSetting>(nameof(TenantSetting.SettingKey))}};
                        v_actor := OLD.{{Column<TenantSetting>(nameof(TenantSetting.UpdatedBy))}};
                    ELSE
                        v_key := NEW.{{Column<TenantSetting>(nameof(TenantSetting.SettingKey))}};
                        v_actor := COALESCE(NEW.{{Column<TenantSetting>(nameof(TenantSetting.UpdatedBy))}},
                            NEW.{{Column<TenantSetting>(nameof(TenantSetting.CreatedBy))}});
                    END IF;
                    IF TG_OP = 'UPDATE' AND OLD.{{Column<TenantSetting>(nameof(TenantSetting.SettingKey))}}
                        IS DISTINCT FROM NEW.{{Column<TenantSetting>(nameof(TenantSetting.SettingKey))}}
                        AND (OLD.{{Column<TenantSetting>(nameof(TenantSetting.SettingKey))}} IN ({{keys}})
                            OR v_key IN ({{keys}})) THEN
                        RAISE EXCEPTION 'email policy setting identity cannot move' USING ERRCODE = '42501';
                    END IF;
                    IF v_key NOT IN ({{keys}}) OR pg_has_role(session_user, '{{MigratorRole}}', 'MEMBER') THEN
                        IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                        RETURN NEW;
                    END IF;
                    IF session_user NOT IN ({{LoginLiteral(enrollment.MigratorLogin)}},
                        {{LoginLiteral(enrollment.RuntimeLogin)}}, {{LoginLiteral(enrollment.GlobalLogin)}},
                        {{LoginLiteral(enrollment.EmailInstanceMutationLogin)}},
                        {{LoginLiteral(enrollment.EmailTenantMutationLogin)}}) THEN
                        RAISE EXCEPTION 'email policy capture login mismatch' USING ERRCODE = '42501';
                    END IF;
                    IF NOT v_instance AND TG_OP = 'DELETE' THEN
                        v_source := OLD.{{Column<TenantSetting>(nameof(TenantSetting.TenantId))}};
                        -- A native tenant cascade has no surviving policy to reconcile.
                        -- A direct override removal still has its parent and needs custody.
                        IF NOT EXISTS (SELECT 1 FROM {{tenants}} t WHERE t.{{tenantId}} = v_source) THEN
                            RETURN OLD;
                        END IF;
                    END IF;
                    IF session_user = {{LoginLiteral(enrollment.GlobalLogin)}} THEN
                        {{RestrictedCallerSql}}
                        IF TG_LEVEL <> 'ROW' OR TG_WHEN <> 'BEFORE' OR TG_NARGS <> 1
                            OR TG_OP NOT IN ('INSERT','UPDATE','DELETE')
                            OR NOT (v_instance AND TG_ARGV[0] = 'instance' AND TG_RELID = {{LoginLiteral(Table<SystemSetting>())}}::regclass
                                OR NOT v_instance AND TG_ARGV[0] = 'tenant' AND TG_RELID = {{LoginLiteral(Table<TenantSetting>())}}::regclass)
                            OR pg_catalog.to_regprocedure({{LoginLiteral(PostgresMixedSettingsContract.FunctionSql(context, "email_consume")
                                + "(" + PostgresMixedSettingsContract.EmailConsumeSignature + ")")}}) IS NULL
                        THEN RAISE EXCEPTION 'email policy original mixed effect required' USING ERRCODE = '42501'; END IF;
                        PERFORM {{PostgresMixedSettingsContract.FunctionSql(context, "email_consume")}}(
                            TG_RELID,TG_OP,
                            CASE WHEN TG_OP = 'INSERT' THEN NULL ELSE pg_catalog.to_jsonb(OLD) END,
                            CASE WHEN TG_OP = 'DELETE' THEN NULL ELSE pg_catalog.to_jsonb(NEW) END);
                        IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                        RETURN NEW;
                    END IF;
                    {{controlAuthority}}
                    IF v_instance THEN
                        IF NOT pg_has_role(session_user, '{{InstanceMutationRole}}', 'USAGE') THEN
                            RAISE EXCEPTION 'email instance mutation purpose required' USING ERRCODE = '42501';
                        END IF;
                    ELSE
                        IF TG_OP = 'DELETE' THEN
                            v_source := OLD.{{Column<TenantSetting>(nameof(TenantSetting.TenantId))}};
                        ELSE
                            v_source := NEW.{{Column<TenantSetting>(nameof(TenantSetting.TenantId))}};
                        END IF;
                        IF v_source IS NULL OR v_source = '00000000-0000-0000-0000-000000000000'::uuid THEN
                            RAISE EXCEPTION 'email policy tenant authority required' USING ERRCODE = '42501';
                        END IF;
                        IF TG_OP = 'UPDATE' AND OLD.{{Column<TenantSetting>(nameof(TenantSetting.TenantId))}}
                            IS DISTINCT FROM NEW.{{Column<TenantSetting>(nameof(TenantSetting.TenantId))}} THEN
                            RAISE EXCEPTION 'email policy tenant cannot move' USING ERRCODE = '42501';
                        END IF;
                        IF NOT pg_has_role(session_user, '{{InstanceMutationRole}}', 'USAGE')
                            AND v_source IS DISTINCT FROM NULLIF(current_setting(
                                '{{PostgresTenantSessionInterceptor.CurrentTenantSettingName}}', true), '')::uuid THEN
                            RAISE EXCEPTION 'email policy tenant authority mismatch' USING ERRCODE = '42501';
                        END IF;
                    END IF;
                    IF TG_OP = 'UPDATE'
                        AND OLD.{{Column<TenantSetting>(nameof(TenantSetting.Value))}} IS NOT DISTINCT FROM
                            NEW.{{Column<TenantSetting>(nameof(TenantSetting.Value))}}
                        AND OLD.{{Column<TenantSetting>(nameof(TenantSetting.IsLocked))}} IS NOT DISTINCT FROM
                            NEW.{{Column<TenantSetting>(nameof(TenantSetting.IsLocked))}} THEN
                        RETURN NEW;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_locks WHERE locktype = 'advisory'
                        AND pid = pg_backend_pid() AND granted AND mode = 'ExclusiveLock' AND objsubid = 1
                        AND classid = (({{policyLock}}::bigint >> 32) & 4294967295)::oid
                        AND objid = ({{policyLock}}::bigint & 4294967295)::oid) THEN
                        RAISE EXCEPTION 'email policy requires its owning setting lock' USING ERRCODE = '42501';
                    END IF;
                    PERFORM pg_advisory_xact_lock({{claimLock}});
                    INSERT INTO {{receipts}} AS receipt (tenant_id, mutation_xid, mutation_login, source_tenant_id,
                        baseline_revision, baseline_suppressed_revision, baseline_suppressed_utc, actor_id)
                    SELECT scope.id, pg_current_xact_id(), session_user, v_source,
                        COALESCE(c.{{revision}}, 0), c.{{suppressed}}, c.{{cutoff}}, v_actor
                    FROM (SELECT t.{{tenantId}} AS id FROM {{tenants}} t WHERE v_instance OR t.{{tenantId}} = v_source
                        UNION ALL SELECT '00000000-0000-0000-0000-000000000000'::uuid WHERE v_instance) scope
                    LEFT JOIN {{table}} c ON c.{{tenant}} = scope.id
                    ON CONFLICT (tenant_id) DO UPDATE SET
                        mutation_xid = EXCLUDED.mutation_xid, mutation_login = EXCLUDED.mutation_login,
                        source_tenant_id = EXCLUDED.source_tenant_id,
                        baseline_revision = CASE WHEN receipt.mutation_xid = EXCLUDED.mutation_xid
                            AND receipt.mutation_login = EXCLUDED.mutation_login THEN receipt.baseline_revision
                            ELSE EXCLUDED.baseline_revision END,
                        baseline_suppressed_revision = CASE WHEN receipt.mutation_xid = EXCLUDED.mutation_xid
                            AND receipt.mutation_login = EXCLUDED.mutation_login THEN receipt.baseline_suppressed_revision
                            ELSE EXCLUDED.baseline_suppressed_revision END,
                        baseline_suppressed_utc = CASE WHEN receipt.mutation_xid = EXCLUDED.mutation_xid
                            AND receipt.mutation_login = EXCLUDED.mutation_login THEN receipt.baseline_suppressed_utc
                            ELSE EXCLUDED.baseline_suppressed_utc END,
                        mutation_sequence = CASE WHEN receipt.mutation_xid = EXCLUDED.mutation_xid
                            AND receipt.mutation_login = EXCLUDED.mutation_login THEN receipt.mutation_sequence + 1 ELSE 1 END,
                        applied_sequence = CASE WHEN receipt.mutation_xid = EXCLUDED.mutation_xid
                            AND receipt.mutation_login = EXCLUDED.mutation_login THEN receipt.applied_sequence ELSE NULL END,
                        applied_boundary = CASE WHEN receipt.mutation_xid = EXCLUDED.mutation_xid
                            AND receipt.mutation_login = EXCLUDED.mutation_login THEN receipt.applied_boundary ELSE NULL END,
                        actor_id = EXCLUDED.actor_id;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END $body$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {{require}}(p_tenant uuid)
                RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    {{controlAuthority}}
                    IF p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                        OR NOT pg_has_role(session_user, '{{InstanceMutationRole}}', 'USAGE')
                            AND (p_tenant IS NULL OR p_tenant IS DISTINCT FROM NULLIF(current_setting(
                                '{{PostgresTenantSessionInterceptor.CurrentTenantSettingName}}', true), '')::uuid) THEN
                        RAISE EXCEPTION 'email policy mutation scope mismatch' USING ERRCODE = '42501';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM {{receipts}} receipt
                        WHERE receipt.tenant_id = COALESCE(p_tenant, '00000000-0000-0000-0000-000000000000'::uuid)
                            AND receipt.mutation_xid = pg_current_xact_id() AND receipt.mutation_login = session_user) THEN
                        RAISE EXCEPTION 'email policy native setting mutation required' USING ERRCODE = '42501';
                    END IF;
                END $body$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {{read}}(p_tenants uuid[])
                RETURNS TABLE ("TenantId" uuid, "DeliveryPolicyRevision" bigint,
                    "OptionalSuppressedThroughRevision" bigint, "OptionalSuppressedThroughUtc" timestamp with time zone)
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                BEGIN
                    {{controlAuthority}}
                    IF p_tenants IS NULL OR cardinality(p_tenants) NOT BETWEEN 1 AND 500
                        OR EXISTS (SELECT 1 FROM unnest(p_tenants) t WHERE t IS NULL OR t = '00000000-0000-0000-0000-000000000000'::uuid) THEN
                        RAISE EXCEPTION 'invalid email policy control scope' USING ERRCODE = '22023';
                    END IF;
                    IF NOT pg_has_role(session_user, '{{InstanceMutationRole}}', 'USAGE')
                        AND EXISTS (SELECT 1 FROM unnest(p_tenants) t WHERE t IS DISTINCT FROM
                            NULLIF(current_setting('{{PostgresTenantSessionInterceptor.CurrentTenantSettingName}}', true), '')::uuid) THEN
                        RAISE EXCEPTION 'email policy tenant authority mismatch' USING ERRCODE = '42501';
                    END IF;
                    -- A tenant created after the instance snapshot still belongs to this native mutation.
                    INSERT INTO {{receipts}} AS receipt (tenant_id, mutation_xid, mutation_login, source_tenant_id,
                        baseline_revision, baseline_suppressed_revision, baseline_suppressed_utc, actor_id)
                    SELECT t.{{tenantId}}, owner.mutation_xid, owner.mutation_login, NULL,
                        COALESCE(c.{{revision}}, 0), c.{{suppressed}}, c.{{cutoff}}, owner.actor_id
                    FROM {{tenants}} t
                    JOIN {{receipts}} owner ON owner.tenant_id = '00000000-0000-0000-0000-000000000000'::uuid
                        AND owner.mutation_xid = pg_current_xact_id() AND owner.mutation_login = session_user
                    LEFT JOIN {{table}} c ON c.{{tenant}} = t.{{tenantId}}
                    WHERE t.{{tenantId}} = ANY(p_tenants)
                    ON CONFLICT (tenant_id) DO UPDATE SET mutation_xid = EXCLUDED.mutation_xid,
                        mutation_login = EXCLUDED.mutation_login, source_tenant_id = NULL,
                        baseline_revision = EXCLUDED.baseline_revision,
                        baseline_suppressed_revision = EXCLUDED.baseline_suppressed_revision,
                        baseline_suppressed_utc = EXCLUDED.baseline_suppressed_utc, actor_id = EXCLUDED.actor_id,
                        mutation_sequence = 1, applied_sequence = NULL, applied_boundary = NULL
                    WHERE receipt.mutation_xid <> EXCLUDED.mutation_xid
                        OR receipt.mutation_login <> EXCLUDED.mutation_login;
                    IF EXISTS (SELECT 1 FROM unnest(p_tenants) t WHERE NOT EXISTS (
                        SELECT 1 FROM {{receipts}} receipt WHERE receipt.tenant_id = t
                            AND receipt.mutation_xid = pg_current_xact_id() AND receipt.mutation_login = session_user)) THEN
                        RAISE EXCEPTION 'email policy native setting mutation required' USING ERRCODE = '42501';
                    END IF;
                    RETURN QUERY SELECT c.{{tenant}}, c.{{revision}}, c.{{suppressed}}, c.{{cutoff}}
                        FROM {{table}} c WHERE c.{{tenant}} = ANY(p_tenants) ORDER BY c.{{tenant}};
                END $body$ $ddl$;
                EXECUTE $ddl$ CREATE OR REPLACE FUNCTION {{write}}(
                    p_changes jsonb, p_now timestamp with time zone, p_actor uuid)
                RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE v_change record; v_current bigint; v_rows integer; v_baseline record;
                    v_revision bigint; v_suppressed bigint; v_cutoff timestamptz; v_now timestamptz; v_boundary integer;
                    v_current_cutoff timestamptz; v_exists boolean;
                BEGIN
                    {{controlAuthority}}
                    IF p_changes IS NULL OR jsonb_typeof(p_changes) <> 'array'
                        OR jsonb_array_length(p_changes) NOT BETWEEN 1 AND 500
                        OR p_now IS NULL OR NOT isfinite(p_now) THEN
                        RAISE EXCEPTION 'invalid email policy control mutation' USING ERRCODE = '22023';
                    END IF;
                    IF EXISTS (SELECT 1 FROM jsonb_to_recordset(p_changes) AS x("TenantId" uuid)
                        GROUP BY x."TenantId" HAVING count(*) <> 1) THEN
                        RAISE EXCEPTION 'duplicate email policy control scope' USING ERRCODE = '22023';
                    END IF;
                    FOR v_change IN SELECT * FROM jsonb_to_recordset(p_changes) AS x(
                        "TenantId" uuid, "BaselineRevision" bigint, "ExpectedRevision" bigint,
                        "Revision" bigint, "SuppressedRevision" bigint, "SuppressedUtc" timestamp with time zone)
                        ORDER BY x."TenantId"
                    LOOP
                        IF NOT pg_has_role(session_user, '{{InstanceMutationRole}}', 'USAGE')
                            AND v_change."TenantId" IS DISTINCT FROM NULLIF(current_setting(
                                '{{PostgresTenantSessionInterceptor.CurrentTenantSettingName}}', true), '')::uuid THEN
                            RAISE EXCEPTION 'email policy tenant authority mismatch' USING ERRCODE = '42501';
                        END IF;
                        SELECT * INTO v_baseline FROM {{receipts}} receipt
                            WHERE receipt.tenant_id = v_change."TenantId"
                                AND receipt.mutation_xid = pg_current_xact_id()
                                AND receipt.mutation_login = session_user;
                        IF NOT FOUND THEN
                            RAISE EXCEPTION 'email policy native setting mutation required' USING ERRCODE = '42501';
                        END IF;
                        IF v_change."TenantId" IS NULL OR v_change."TenantId" = '00000000-0000-0000-0000-000000000000'::uuid
                            OR v_change."BaselineRevision" IS NULL OR v_change."BaselineRevision" < 0
                            OR v_change."BaselineRevision" = 9223372036854775807
                            OR v_change."ExpectedRevision" IS NULL
                            OR v_change."ExpectedRevision" NOT IN (v_change."BaselineRevision", v_change."BaselineRevision" + 1)
                            OR v_change."Revision" IS NULL OR v_change."Revision" <> v_change."BaselineRevision" + 1
                            OR v_change."SuppressedRevision" < 0 OR v_change."SuppressedRevision" > v_change."Revision"
                            OR (v_change."SuppressedUtc" IS NOT NULL AND NOT isfinite(v_change."SuppressedUtc")) THEN
                            RAISE EXCEPTION 'invalid email policy revision fence' USING ERRCODE = '22023';
                        END IF;
                        IF v_change."BaselineRevision" IS DISTINCT FROM v_baseline.baseline_revision THEN
                            RAISE EXCEPTION 'email policy native baseline mismatch' USING ERRCODE = '42501';
                        END IF;
                        SELECT c.{{revision}}, c.{{cutoff}} INTO v_current, v_current_cutoff FROM {{table}} c
                            WHERE c.{{tenant}} = v_baseline.tenant_id FOR UPDATE;
                        v_exists := FOUND;
                        v_revision := v_baseline.baseline_revision + 1;
                        v_now := clock_timestamp();
                        IF v_change."SuppressedRevision" IS NOT DISTINCT FROM v_baseline.baseline_suppressed_revision
                            AND v_change."SuppressedUtc" IS NOT DISTINCT FROM v_baseline.baseline_suppressed_utc THEN
                            v_suppressed := v_baseline.baseline_suppressed_revision;
                            v_cutoff := v_baseline.baseline_suppressed_utc;
                            v_boundary := 0;
                        ELSIF v_change."SuppressedRevision" IN (v_baseline.baseline_revision, v_revision)
                            AND v_change."SuppressedUtc" IS NOT NULL
                            AND (v_change."SuppressedUtc" <= v_now
                                OR v_change."SuppressedUtc" = v_current_cutoff
                                OR v_change."SuppressedUtc" = v_baseline.baseline_suppressed_utc)
                            AND (v_baseline.baseline_suppressed_utc IS NULL
                                OR v_change."SuppressedUtc" >= v_baseline.baseline_suppressed_utc) THEN
                            v_suppressed := greatest(v_baseline.baseline_suppressed_revision, v_change."SuppressedRevision");
                            v_cutoff := greatest(v_baseline.baseline_suppressed_utc, v_current_cutoff, v_now);
                            v_boundary := CASE WHEN v_change."SuppressedRevision" = v_revision THEN 2 ELSE 1 END;
                        ELSE
                            RAISE EXCEPTION 'email policy suppression history cannot be authored' USING ERRCODE = '42501';
                        END IF;
                        IF v_exists THEN
                            IF v_current <> v_change."ExpectedRevision" THEN
                                RAISE EXCEPTION 'email policy revision changed' USING ERRCODE = '40001';
                            END IF;
                            IF v_baseline.applied_sequence = v_baseline.mutation_sequence THEN
                                IF v_baseline.applied_boundary IS DISTINCT FROM v_boundary THEN
                                    RAISE EXCEPTION 'email policy transition requires a new setting mutation' USING ERRCODE = '42501';
                                END IF;
                                CONTINUE;
                            END IF;
                            UPDATE {{table}} c SET {{revision}} = v_revision,
                                {{suppressed}} = v_suppressed, {{cutoff}} = v_cutoff,
                                {{updated}} = v_now, {{updater}} = p_actor WHERE c.{{tenant}} = v_change."TenantId";
                        ELSE
                            IF v_change."ExpectedRevision" <> 0 OR v_change."BaselineRevision" <> 0 THEN
                                RAISE EXCEPTION 'email policy control disappeared' USING ERRCODE = '40001';
                            END IF;
                            INSERT INTO {{table}} ({{id}}, {{tenant}}, {{revision}}, {{suppressed}}, {{cutoff}},
                                {{created}}, {{creator}}, {{updated}}, {{updater}}, {{paused}})
                                VALUES (uuidv7(), v_baseline.tenant_id, v_revision,
                                    v_suppressed, v_cutoff,
                                    v_now, p_actor, v_now, p_actor, false)
                                ON CONFLICT ({{tenant}}) DO NOTHING;
                            GET DIAGNOSTICS v_rows = ROW_COUNT;
                            IF v_rows <> 1 THEN
                                RAISE EXCEPTION 'email policy control appeared' USING ERRCODE = '40001';
                            END IF;
                        END IF;
                        UPDATE {{receipts}} SET applied_sequence = mutation_sequence, applied_boundary = v_boundary
                            WHERE tenant_id = v_baseline.tenant_id AND mutation_xid = pg_current_xact_id()
                                AND mutation_login = session_user;
                    END LOOP;
                END $body$ $ddl$;
                EXECUTE 'REVOKE ALL ON FUNCTION {{capture}}(), {{require}}(uuid), {{read}}(uuid[]),
                    {{write}}(jsonb, timestamp with time zone, uuid) FROM PUBLIC, {{RuntimeRole}}, {{GlobalRole}},
                    {{InstanceMutationRole}}, {{TenantMutationRole}}';
                EXECUTE 'GRANT EXECUTE ON FUNCTION {{require}}(uuid), {{read}}(uuid[]), {{write}}(jsonb, timestamp with time zone, uuid)
                    TO {{InstanceMutationRole}}, {{TenantMutationRole}}';
                PERFORM set_config('role', v_previous_role, true);
            END $install_controls$;
            DROP TRIGGER IF EXISTS email_policy_control_custody ON {{Table<SystemSetting>()}};
            CREATE TRIGGER email_policy_control_custody BEFORE INSERT OR UPDATE OR DELETE ON {{Table<SystemSetting>()}}
                FOR EACH ROW EXECUTE FUNCTION {{capture}}('instance');
            DROP TRIGGER IF EXISTS email_policy_control_custody ON {{Table<TenantSetting>()}};
            CREATE TRIGGER email_policy_control_custody BEFORE INSERT OR UPDATE OR DELETE ON {{Table<TenantSetting>()}}
                FOR EACH ROW EXECUTE FUNCTION {{capture}}('tenant');
            REVOKE CREATE ON SCHEMA {{schema}} FROM {{OwnerRole}};
            """;
    }
}
