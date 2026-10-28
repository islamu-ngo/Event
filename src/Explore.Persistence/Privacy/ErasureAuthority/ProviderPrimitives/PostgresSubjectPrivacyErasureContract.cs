using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Privacy.ErasureAuthority;

/// <summary>
/// Execute-only, subject/intent-qualified disposal. The NOLOGIN owner has only
/// the compiled policy's relation grants; Global receives no legacy private table grant.
/// Application-owned learning disposal has separate fixed root-only storage rights.
/// </summary>
public static partial class PostgresSubjectPrivacyErasureContract
{
    public const string OwnerRole = "event_subject_erasure_owner";
    public const string MigratorRole = "event_subject_erasure_migrator";
    public const string GlobalRole = "event_subject_erasure_global";
    public const string PolicyName = "subject_erasure_disposal";
    private const string Signature = "uuid,uuid,bigint,integer,integer,timestamptz";
    private const string Parameters =
        "p_intent uuid, p_subject uuid, p_sequence bigint, p_policy integer, p_reason integer, p_recorded timestamptz";
    private const string Arguments = "p_intent,p_subject,p_sequence,p_policy,p_reason,p_recorded";

    internal static string FunctionSql(ExploreDbContext context, string operation) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            "subject_erasure_" + operation, Schema(context));

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var m = new SqlMap(context);
        string qualify = FunctionSql(context, "qualify");
        string candidates = FunctionSql(context, "candidates");
        string prepare = FunctionSql(context, "prepare");
        string dispose = FunctionSql(context, "dispose");
        string schema = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(Schema(context));
        string dependencies = PostgresDiscoveryDisclosureContract.FunctionSql(context, "dependencies");
        string fenceTenants = PostgresDiscoveryDisclosureContract.FunctionSql(context, "fence_tenants");
        string finalize = PostgresDiscoveryDisclosureContract.FunctionSql(context, "finalize");
        string sagaPredicate = $"""
            r.{m.Column<PrivacyErasureSaga>(nameof(PrivacyErasureSaga.IntentId))} = p_intent
            AND r.{m.Column<PrivacyErasureSaga>(nameof(PrivacyErasureSaga.SubjectId))} = p_subject
            AND r.{m.Column<PrivacyErasureSaga>(nameof(PrivacyErasureSaga.SubjectKind))} = {(int)PrivacyErasureSubjectKind.User}
            AND r.{m.Column<PrivacyErasureSaga>(nameof(PrivacyErasureSaga.FenceToken))} = p_sequence
            AND r.{m.Column<PrivacyErasureSaga>(nameof(PrivacyErasureSaga.PolicyVersion))} = p_policy
            AND r.{m.Column<PrivacyErasureSaga>(nameof(PrivacyErasureSaga.FencedAtUtc))} >= p_recorded
            """;
        string sagaFence = m.Update<PrivacyErasureSaga>(sagaPredicate,
            (nameof(PrivacyErasureSaga.UpdatedAtUtc), $"r.{m.Column<PrivacyErasureSaga>(nameof(PrivacyErasureSaga.UpdatedAtUtc))}"));
        string userFence = m.Update<User>($"r.{m.Column<User>(nameof(User.Id))} = p_subject",
            (nameof(User.ConcurrencyStamp), $"r.{m.Column<User>(nameof(User.ConcurrencyStamp))}"));
        string actorFence = m.Update<Actor>($"r.{m.Column<Actor>(nameof(Actor.UserId))} = p_subject",
            (nameof(Actor.ConcurrencyStamp), $"r.{m.Column<Actor>(nameof(Actor.ConcurrencyStamp))}"));
        string roots = RootsSql(m, dependencies);
        string locatorSql = CandidatesSql(m);
        string storageCapture = StorageCaptureSql(m);
        string policy = PolicySql(m);
        string storageDisposal = StorageDisposalSql(m, context);
        string tombstones = TombstoneSql(m);
        m.Write<LocationPrivacyCorrectionReceipt>("INSERT");
        string correctionReceipts = $"""
            INSERT INTO {m.Table<LocationPrivacyCorrectionReceipt>()}
                ({string.Join(", ", new[] {
                    nameof(LocationPrivacyCorrectionReceipt.Id), nameof(LocationPrivacyCorrectionReceipt.TenantId),
                    nameof(LocationPrivacyCorrectionReceipt.EventId), nameof(LocationPrivacyCorrectionReceipt.EventLocationId),
                    nameof(LocationPrivacyCorrectionReceipt.PolicyVersion), nameof(LocationPrivacyCorrectionReceipt.CreatedAtUtc),
                    nameof(LocationPrivacyCorrectionReceipt.EventType), nameof(LocationPrivacyCorrectionReceipt.ErasureIntentId),
                    nameof(LocationPrivacyCorrectionReceipt.AuthoritySequence), nameof(LocationPrivacyCorrectionReceipt.LocationId)
                }.Select(m.Column<LocationPrivacyCorrectionReceipt>))})
            SELECT uuidv7(), r.{m.Column<EventLocation>(nameof(EventLocation.TenantId))},
                r.{m.Column<EventLocation>(nameof(EventLocation.EventId))},
                r.{m.Column<EventLocation>(nameof(EventLocation.Id))},
                r.{m.Column<EventLocation>(nameof(EventLocation.PolicyVersion))}, p_now,
                'LocationPrivacyCorrectionRequested', p_intent, p_sequence,
                r.{m.Column<EventLocation>(nameof(EventLocation.LocationId))}
            FROM {m.Table<EventLocation>()} r
            WHERE r.{m.Column<EventLocation>(nameof(EventLocation.Id))} = ANY(v_event_locations);
            """;
        string effects = $"""
            jsonb_build_object(
                'tenants', to_jsonb(v_tenants),
                'homes', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                    'id', r.{m.Column<Location>(nameof(Location.Id))},
                    'tenant', r.{m.Column<Location>(nameof(Location.TenantId))},
                    'stamp', r.{m.Column<Location>(nameof(Location.ConcurrencyStamp))}))
                    FROM {m.Table<Location>()} r WHERE r.{m.Column<Location>(nameof(Location.Id))} = ANY(v_homes)), '[]'::jsonb),
                'eventLocations', COALESCE((SELECT jsonb_agg(jsonb_build_object(
                    'message', r.{m.Column<LocationPrivacyCorrectionReceipt>(nameof(LocationPrivacyCorrectionReceipt.Id))},
                    'id', r.{m.Column<LocationPrivacyCorrectionReceipt>(nameof(LocationPrivacyCorrectionReceipt.EventLocationId))},
                    'tenant', r.{m.Column<LocationPrivacyCorrectionReceipt>(nameof(LocationPrivacyCorrectionReceipt.TenantId))},
                    'event', r.{m.Column<LocationPrivacyCorrectionReceipt>(nameof(LocationPrivacyCorrectionReceipt.EventId))},
                    'location', r.{m.Column<LocationPrivacyCorrectionReceipt>(nameof(LocationPrivacyCorrectionReceipt.LocationId))},
                    'version', r.{m.Column<LocationPrivacyCorrectionReceipt>(nameof(LocationPrivacyCorrectionReceipt.PolicyVersion))}))
                    FROM {m.Table<LocationPrivacyCorrectionReceipt>()} r
                    WHERE r.{m.Column<LocationPrivacyCorrectionReceipt>(nameof(LocationPrivacyCorrectionReceipt.ErasureIntentId))} = p_intent
                        AND r.{m.Column<LocationPrivacyCorrectionReceipt>(nameof(LocationPrivacyCorrectionReceipt.AuthoritySequence))} = p_sequence
                        AND r.{m.Column<LocationPrivacyCorrectionReceipt>(nameof(LocationPrivacyCorrectionReceipt.EventLocationId))} = ANY(v_event_locations)
                        AND r.{m.Column<LocationPrivacyCorrectionReceipt>(nameof(LocationPrivacyCorrectionReceipt.CreatedAtUtc))} = p_now), '[]'::jsonb))
            """;
        string workCheck = $"""
            IF EXISTS (
                SELECT 1 FROM jsonb_array_elements({candidates}({Arguments})) candidate
                WHERE NOT EXISTS (
                    SELECT 1 FROM {m.Table<PrivacyErasureProviderWork>()} w
                    WHERE w.{m.Column<PrivacyErasureProviderWork>(nameof(PrivacyErasureProviderWork.IntentId))} = p_intent
                      AND w.{m.Column<PrivacyErasureProviderWork>(nameof(PrivacyErasureProviderWork.SubjectId))} = p_subject
                      AND w.{m.Column<PrivacyErasureProviderWork>(nameof(PrivacyErasureProviderWork.SubjectKind))} = {(int)PrivacyErasureSubjectKind.User}
                      AND w.{m.Column<PrivacyErasureProviderWork>(nameof(PrivacyErasureProviderWork.ProviderKind))} = (candidate->>'provider')::integer
                      AND w.{m.Column<PrivacyErasureProviderWork>(nameof(PrivacyErasureProviderWork.Action))} = (candidate->>'action')::integer
                      AND w.{m.Column<PrivacyErasureProviderWork>(nameof(PrivacyErasureProviderWork.TenantId))} IS NOT DISTINCT FROM (candidate->>'tenant')::uuid
                      AND w.{m.Column<PrivacyErasureProviderWork>(nameof(PrivacyErasureProviderWork.TargetId))} = (candidate->>'target')::uuid
                      AND w.{m.Column<PrivacyErasureProviderWork>(nameof(PrivacyErasureProviderWork.LocatorKind))} = (candidate->>'kind')::integer
                      AND (w.{m.Column<PrivacyErasureProviderWork>(nameof(PrivacyErasureProviderWork.ProtectedLocator))} IS NOT NULL
                        OR w.{m.Column<PrivacyErasureProviderWork>(nameof(PrivacyErasureProviderWork.Status))} IN (
                            {(int)PrivacyErasureProviderWorkStatus.Completed}, {(int)PrivacyErasureProviderWorkStatus.DeadLettered}))))
            THEN RAISE EXCEPTION 'subject_erasure_protected_work_required' USING ERRCODE = '55000'; END IF;
            """;
        string declarations = """
            v_actors uuid[]; v_tenant_users uuid[]; v_consumers uuid[]; v_homes uuid[];
            v_event_locations uuid[]; v_tenants uuid[]; v_files uuid[]; v_storage uuid[];
            v_retiring uuid[]; v_upload_scopes jsonb;
            """;
        string functions = $"""
            CREATE OR REPLACE FUNCTION {qualify}({Parameters}) RETURNS void
            LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS $body$
            BEGIN
                IF current_setting('transaction_isolation') <> 'serializable'
                   OR p_intent IS NULL OR p_intent = '00000000-0000-0000-0000-000000000000'::uuid
                   OR p_subject IS NULL OR p_subject = '00000000-0000-0000-0000-000000000000'::uuid
                   OR p_sequence IS NULL OR p_sequence <= 0 OR p_policy IS NULL OR p_policy <= 0
                   OR p_reason IS NULL OR p_reason NOT IN (1,2,3) OR p_recorded IS NULL
                   OR NOT pg_has_role(session_user, '{GlobalRole}', 'MEMBER') THEN
                    RAISE EXCEPTION 'subject_erasure_authority_invalid' USING ERRCODE = '42501';
                END IF;
                {sagaFence}
                IF NOT FOUND THEN RAISE EXCEPTION 'subject_erasure_authority_mismatch' USING ERRCODE = '42501'; END IF;
            END; $body$;

            CREATE OR REPLACE FUNCTION {candidates}({Parameters}) RETURNS jsonb
            LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS $body$
            BEGIN
                PERFORM {qualify}({Arguments});
                RETURN (SELECT COALESCE(jsonb_agg(candidate ORDER BY candidate->>'provider',
                    candidate->>'tenant', candidate->>'target'), '[]'::jsonb) FROM ({locatorSql}) locators(candidate));
            END; $body$;

            CREATE OR REPLACE FUNCTION {prepare}({Parameters}) RETURNS jsonb
            LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS $body$
            DECLARE {declarations}
            BEGIN
                PERFORM {qualify}({Arguments});
                {userFence}
                {actorFence}
                {roots}
                PERFORM {fenceTenants}(v_tenants);
                RETURN jsonb_build_object('tenants', to_jsonb(v_tenants), 'candidates', {candidates}({Arguments}));
            END; $body$;

            CREATE OR REPLACE FUNCTION {dispose}({Parameters}, p_now timestamptz) RETURNS jsonb
            LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS $body$
            DECLARE {declarations}
                {StorageDeclarations(m)}
            BEGIN
                PERFORM {qualify}({Arguments});
                IF p_now IS NULL OR p_now < p_recorded THEN
                    RAISE EXCEPTION 'subject_erasure_timestamp_invalid' USING ERRCODE = '22023';
                END IF;
                {userFence}
                {actorFence}
                {roots}
                PERFORM {fenceTenants}(v_tenants);
                {workCheck}
                {storageCapture}
                {policy}
                {storageDisposal}
                {tombstones}
                {correctionReceipts}
                RETURN {effects};
            END; $body$;
            """;
        return $"""
            DO $roles$
            DECLARE role_name text;
            BEGIN
                FOREACH role_name IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', role_name);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name
                        AND (rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication)) THEN
                        RAISE EXCEPTION 'subject_erasure_role_invalid';
                    END IF;
                END LOOP;
                IF pg_has_role('{GlobalRole}', '{OwnerRole}', 'MEMBER')
                   OR pg_has_role('{GlobalRole}', '{MigratorRole}', 'MEMBER')
                    OR EXISTS (
                        WITH RECURSIVE memberships(roleid) AS (
                            SELECT roleid FROM pg_catalog.pg_auth_members
                            WHERE member = pg_catalog.to_regrole(current_user)::oid
                            UNION
                            SELECT grants.roleid FROM pg_catalog.pg_auth_members grants
                            JOIN memberships inherited ON grants.member = inherited.roleid
                        )
                        SELECT 1 FROM memberships
                        WHERE roleid = pg_catalog.to_regrole('{GlobalRole}')::oid
                    ) THEN
                    RAISE EXCEPTION 'subject_erasure_roles_must_remain_separate';
                END IF;
            END; $roles$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            {m.Grants()}
            GRANT EXECUTE ON FUNCTION {dependencies}(text,uuid[]), {fenceTenants}(uuid[]) TO {OwnerRole};
            GRANT EXECUTE ON FUNCTION {fenceTenants}(uuid[]),
                {finalize}(uuid[],uuid[],boolean[],bigint[],bigint[]) TO {GlobalRole};
            DO $install$
            DECLARE previous_role text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$ {functions} $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {qualify}({Signature}), {candidates}({Signature}),
                        {prepare}({Signature}), {dispose}({Signature},timestamptz) FROM PUBLIC, {GlobalRole};
                    GRANT EXECUTE ON FUNCTION {prepare}({Signature}), {dispose}({Signature},timestamptz) TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', previous_role, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            """;
    }

    private static string RootsSql(SqlMap m, string dependencies) => $"""
        SELECT COALESCE(array_agg(r.{m.Column<Actor>(nameof(Actor.Id))}), ARRAY[]::uuid[]) INTO v_actors
        FROM {m.Table<Actor>()} r WHERE r.{m.Column<Actor>(nameof(Actor.UserId))} = p_subject;
        SELECT COALESCE(array_agg(r.{m.Column<TenantUser>(nameof(TenantUser.Id))}), ARRAY[]::uuid[]) INTO v_tenant_users
        FROM {m.Table<TenantUser>()} r WHERE r.{m.Column<TenantUser>(nameof(TenantUser.UserId))} = p_subject;
        SELECT COALESCE(array_agg(r.{m.Column<WebhookConsumer>(nameof(WebhookConsumer.Id))}), ARRAY[]::uuid[]) INTO v_consumers
        FROM {m.Table<WebhookConsumer>()} r WHERE r.{m.Column<WebhookConsumer>(nameof(WebhookConsumer.OwnerUserId))} = p_subject;
        SELECT COALESCE(array_agg(r.{m.Column<Location>(nameof(Location.Id))}), ARRAY[]::uuid[]) INTO v_homes
        FROM {m.Table<Location>()} r WHERE r.{m.Column<Location>(nameof(Location.OwnerUserId))} = p_subject
            AND r.{m.Column<Location>(nameof(Location.LocationKindId))} = {(int)LocationKindEnum.PrivateHome};
        SELECT COALESCE(array_agg(r.{m.Column<EventLocation>(nameof(EventLocation.Id))}), ARRAY[]::uuid[]) INTO v_event_locations
        FROM {m.Table<EventLocation>()} r
        WHERE r.{m.Column<EventLocation>(nameof(EventLocation.LocationId))} = ANY(v_homes)
            AND NOT r.{m.Column<EventLocation>(nameof(EventLocation.IsDeleted))};
        SELECT COALESCE(array_agg(DISTINCT tenant ORDER BY tenant), ARRAY[]::uuid[]) INTO v_tenants FROM (
            SELECT {dependencies}('user', ARRAY[p_subject]) tenant
            UNION SELECT {dependencies}('actor', v_actors)
            UNION SELECT r.{m.Column<Location>(nameof(Location.TenantId))} FROM {m.Table<Location>()} r
                WHERE r.{m.Column<Location>(nameof(Location.Id))} = ANY(v_homes)
            UNION SELECT r.{m.Column<EventLocation>(nameof(EventLocation.TenantId))} FROM {m.Table<EventLocation>()} r
                WHERE r.{m.Column<EventLocation>(nameof(EventLocation.Id))} = ANY(v_event_locations)
            UNION SELECT r.{m.Column<StorageObject>(nameof(StorageObject.TenantId))} FROM {m.Table<StorageObject>()} r
                WHERE r.{m.Column<StorageObject>(nameof(StorageObject.CreatedBy))} = p_subject
                   OR r.{m.Column<StorageObject>(nameof(StorageObject.UpdatedBy))} = p_subject
                   OR r.{m.Column<StorageObject>(nameof(StorageObject.DeletedBy))} = p_subject
                   OR r.{m.Column<StorageObject>(nameof(StorageObject.QuarantinedBy))} = p_subject
            UNION SELECT r.{m.Column<StorageUploadSession>(nameof(StorageUploadSession.TenantId))}
                FROM {m.Table<StorageUploadSession>()} r
                WHERE r.{m.Column<StorageUploadSession>(nameof(StorageUploadSession.UserId))} = p_subject
            UNION SELECT source.{m.Column<StorageObject>(nameof(StorageObject.TenantId))}
                FROM {m.Table<StorageObject>()} source JOIN {m.Table<ActorPii>()} picture
                    ON picture.{m.Column<ActorPii>(nameof(ActorPii.ProfilePictureStorageObjectId))} = source.{m.Column<StorageObject>(nameof(StorageObject.Id))}
                WHERE picture.{m.Column<ActorPii>(nameof(ActorPii.ActorId))} = ANY(v_actors)
            UNION SELECT r.{m.Column<EventReportEvidence>(nameof(EventReportEvidence.TenantId))}
                FROM {m.Table<EventReportEvidence>()} r
                WHERE r.{m.Column<EventReportEvidence>(nameof(EventReportEvidence.CreatedByUserId))} = p_subject
            UNION SELECT o.{m.Column<RegistrationOrder>(nameof(RegistrationOrder.TenantId))}
                FROM {m.Table<RegistrationOrder>()} o
                WHERE o.{m.Column<RegistrationOrder>(nameof(RegistrationOrder.AccountUserId))} = p_subject
        ) affected;
        """;

    private static string CandidatesSql(SqlMap m)
    {
        string Candidate(PrivacyErasureProviderKind provider, PrivacyErasureProviderAction action,
            string tenant, string target, PrivacyErasureProviderLocatorKind kind, string locator) =>
            $"jsonb_build_object('provider',{(int)provider},'action',{(int)action},'tenant',{tenant}," +
            $"'target',{target},'kind',{(int)kind},'locator',{locator})";
        return $"""
            SELECT {Candidate(PrivacyErasureProviderKind.Keycloak, PrivacyErasureProviderAction.RevokeOrUnlinkExternalIdentity,
                "NULL", $"r.{m.Column<UserExternalLogin>(nameof(UserExternalLogin.Id))}", PrivacyErasureProviderLocatorKind.AccountIdentifier,
                $"r.{m.Column<UserExternalLogin>(nameof(UserExternalLogin.ProviderKey))}")}
            FROM {m.Table<UserExternalLogin>()} r WHERE r.{m.Column<UserExternalLogin>(nameof(UserExternalLogin.UserId))} = p_subject
                AND r.{m.Column<UserExternalLogin>(nameof(UserExternalLogin.AuthenticationProviderId))} = {(int)AuthenticationProviderKind.Keycloak}
            UNION ALL
            SELECT {Candidate(PrivacyErasureProviderKind.Atproto, PrivacyErasureProviderAction.RevokeOrUnlinkExternalIdentity,
                "NULL", $"r.{m.Column<AtprotoIdentity>(nameof(AtprotoIdentity.Id))}", PrivacyErasureProviderLocatorKind.Did,
                $"r.{m.Column<AtprotoIdentity>(nameof(AtprotoIdentity.Did))}")}
            FROM {m.Table<AtprotoIdentity>()} r JOIN {m.Table<Actor>()} a
                ON a.{m.Column<Actor>(nameof(Actor.Id))} = r.{m.Column<AtprotoIdentity>(nameof(AtprotoIdentity.ActorId))}
            WHERE a.{m.Column<Actor>(nameof(Actor.UserId))} = p_subject
            UNION ALL
            SELECT {Candidate(PrivacyErasureProviderKind.WebPush, PrivacyErasureProviderAction.InvalidateSubscription,
                $"r.{m.Column<WebPushSubscription>(nameof(WebPushSubscription.TenantId))}", $"r.{m.Column<WebPushSubscription>(nameof(WebPushSubscription.Id))}",
                PrivacyErasureProviderLocatorKind.WebPushEndpoint, $"r.{m.Column<WebPushSubscription>(nameof(WebPushSubscription.Endpoint))}")}
            FROM {m.Table<WebPushSubscription>()} r WHERE r.{m.Column<WebPushSubscription>(nameof(WebPushSubscription.UserId))} = p_subject
            UNION ALL
            SELECT {Candidate(PrivacyErasureProviderKind.Smtp, PrivacyErasureProviderAction.ExpireLocalMetadataWithoutRecall,
                $"r.{m.Column<EmailDispatchOutbox>(nameof(EmailDispatchOutbox.TenantId))}", $"r.{m.Column<EmailDispatchOutbox>(nameof(EmailDispatchOutbox.Id))}",
                PrivacyErasureProviderLocatorKind.ProviderResourceIdentifier, $"r.{m.Column<EmailDispatchOutbox>(nameof(EmailDispatchOutbox.ProviderMessageId))}")}
            FROM {m.Table<EmailDispatchOutbox>()} r
            WHERE r.{m.Column<EmailDispatchOutbox>(nameof(EmailDispatchOutbox.RecipientUserId))} = p_subject
                AND r.{m.Column<EmailDispatchOutbox>(nameof(EmailDispatchOutbox.ProviderMessageId))} IS NOT NULL
            UNION ALL
            SELECT {Candidate(PrivacyErasureProviderKind.Webhook, PrivacyErasureProviderAction.DeleteOrAnonymizeProviderCopy,
                $"r.{m.Column<WebhookEndpoint>(nameof(WebhookEndpoint.TenantId))}", $"r.{m.Column<WebhookEndpoint>(nameof(WebhookEndpoint.Id))}",
                PrivacyErasureProviderLocatorKind.ProviderResourceIdentifier, $"r.{m.Column<WebhookEndpoint>(nameof(WebhookEndpoint.ProviderEndpointId))}")}
            FROM {m.Table<WebhookEndpoint>()} r JOIN {m.Table<WebhookConsumer>()} c
                ON c.{m.Column<WebhookConsumer>(nameof(WebhookConsumer.Id))} = r.{m.Column<WebhookEndpoint>(nameof(WebhookEndpoint.ConsumerId))}
            WHERE c.{m.Column<WebhookConsumer>(nameof(WebhookConsumer.OwnerUserId))} = p_subject
                AND r.{m.Column<WebhookEndpoint>(nameof(WebhookEndpoint.ProviderEndpointId))} IS NOT NULL
            UNION ALL
            SELECT jsonb_build_object('provider', CASE WHEN r.{m.Column<EventReportExternalLink>(nameof(EventReportExternalLink.Provider))}
                = {m.Literal<EventReportExternalLink>(nameof(EventReportExternalLink.Provider), EventReportExternalProvider.Osprey)}
                THEN {(int)PrivacyErasureProviderKind.Osprey} ELSE {(int)PrivacyErasureProviderKind.Coop} END,
                'action', {(int)PrivacyErasureProviderAction.CorrectOrDeleteProviderCopy},
                'tenant', r.{m.Column<EventReportExternalLink>(nameof(EventReportExternalLink.TenantId))},
                'target', r.{m.Column<EventReportExternalLink>(nameof(EventReportExternalLink.Id))},
                'kind', {(int)PrivacyErasureProviderLocatorKind.ProviderResourceIdentifier},
                'locator', COALESCE(r.{m.Column<EventReportExternalLink>(nameof(EventReportExternalLink.ProviderCaseId))},
                    r.{m.Column<EventReportExternalLink>(nameof(EventReportExternalLink.ProviderSignalId))}))
            FROM {m.Table<EventReportExternalLink>()} r JOIN {m.Table<EventReport>()} report
                ON report.{m.Column<EventReport>(nameof(EventReport.Id))} = r.{m.Column<EventReportExternalLink>(nameof(EventReportExternalLink.ReportId))}
            WHERE report.{m.Column<EventReport>(nameof(EventReport.ReporterUserId))} = p_subject
                AND (r.{m.Column<EventReportExternalLink>(nameof(EventReportExternalLink.ProviderCaseId))} IS NOT NULL
                    OR r.{m.Column<EventReportExternalLink>(nameof(EventReportExternalLink.ProviderSignalId))} IS NOT NULL)
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(PrivacyErasureSaga))!.GetSchema() ?? "public";

    // This map generates installation-time SQL only. Callers cannot supply a type,
    // property, predicate, or relation to any installed function.
    private sealed class SqlMap(ExploreDbContext context)
    {
        private readonly Dictionary<Type, HashSet<string>> _operations = [];
        private readonly Dictionary<Type, HashSet<string>> _updates = [];
        internal string Table<T>() => Table(typeof(T));
        internal string Table(Type type)
        {
            if (!_operations.ContainsKey(type)) _operations[type] = ["SELECT"];
            var entity = context.Model.FindEntityType(type)!;
            return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, Schema(context));
        }
        internal string Column<T>(string property) => Column(typeof(T), property);
        private string Column(Type type, string property)
        {
            var entity = context.Model.FindEntityType(type)!;
            return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }
        internal string Literal<T>(string property, object value) => context.Model.FindEntityType(typeof(T))!
            .FindProperty(property)!.GetRelationalTypeMapping().GenerateSqlLiteral(value);
        internal void Write<T>(params string[] operations)
        {
            Table<T>();
            _operations[typeof(T)].UnionWith(operations);
        }
        /// <summary>
        /// PostgreSQL FOR UPDATE needs UPDATE on at least one column; the
        /// disposal owner receives only the row identity for this lock.
        /// </summary>
        internal void RequireRowLock<T>()
        {
            Table<T>();
            if (!_updates.ContainsKey(typeof(T))) _updates[typeof(T)] = [];
            _updates[typeof(T)].Add("Id");
        }
        internal string Delete<T>(string predicate)
        {
            Write<T>("DELETE");
            return $"DELETE FROM {Table<T>()} r WHERE {predicate};";
        }
        internal string Update<T>(string predicate, params (string Property, string Value)[] changes)
        {
            Table<T>();
            if (!_updates.ContainsKey(typeof(T))) _updates[typeof(T)] = [];
            _updates[typeof(T)].UnionWith(changes.Select(change => change.Property));
            return $"UPDATE {Table<T>()} r SET {string.Join(", ", changes.Select(change => $"{Column<T>(change.Property)} = {change.Value}"))} WHERE {predicate};";
        }
        internal string Grants()
        {
            var result = new StringBuilder();
            var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
                .Select(table => table.TableName).ToHashSet(StringComparer.Ordinal);
            foreach (var pair in _operations.ToArray())
            {
                string table = Table(pair.Key);
                result.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"REVOKE ALL ON TABLE {table} FROM {OwnerRole}, {GlobalRole};");
                result.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"GRANT {string.Join(", ", pair.Value)} ON TABLE {table} TO {OwnerRole};");
                if (_updates.TryGetValue(pair.Key, out var columns) && !pair.Value.Contains("UPDATE"))
                    result.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"GRANT UPDATE ({string.Join(", ", columns.Select(column => Column(pair.Key, column)))}) ON TABLE {table} TO {OwnerRole};");
                if (tenantTables.Contains(context.Model.FindEntityType(pair.Key)!.GetTableName()!))
                {
                    result.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"DROP POLICY IF EXISTS \"{PolicyName}\" ON {table};");
                    result.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"CREATE POLICY \"{PolicyName}\" ON {table} TO {OwnerRole} USING (true) WITH CHECK (true);");
                }
            }
            return result.ToString();
        }
    }
}
