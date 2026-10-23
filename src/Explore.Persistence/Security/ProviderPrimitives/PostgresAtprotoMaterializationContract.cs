using System.Globalization;
using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Executes private ATProto graph persistence without lending its table permissions
/// to the Global login. Every function is rooted in a fenced consumer operation.
/// </summary>
public static class PostgresAtprotoMaterializationContract
{
    public const string OwnerRole = "event_atproto_materialization_owner";
    public const string MigratorRole = "event_atproto_materialization_migrator";
    public const string GlobalRole = "event_atproto_materialization_global";
    public const string PolicyName = "atproto_materialization_authority";

    internal static readonly (string Name, Type Type)[] PrivateRelations =
    [
        ("storage", typeof(StorageObject)),
        ("events", typeof(Event)),
        ("sessions", typeof(EventSession)),
        ("participation", typeof(EventParticipationConfiguration)),
        ("actions", typeof(EventPublicAction)),
        ("presentations", typeof(AtprotoRecordTenantPresentation)),
        ("producers", typeof(StorageProducerOperation)),
        ("uploads", typeof(StorageUploadSession)),
        ("tombstones", typeof(StorageObjectDeletionTombstone))
    ];

    public static string FunctionSql(ExploreDbContext context, string operation) =>
        context.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier("atproto_materialization_" + operation, Schema(context));

    public static async Task ApplyAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(BuildSql(context), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task RequireReadyAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = 5;
        string[] signatures =
        [
            FunctionSql(context, "read") + "(jsonb)",
            FunctionSql(context, "fence_storage") + "(jsonb)",
            FunctionSql(context, "apply") + "(jsonb,jsonb)"
        ];
        command.CommandText = $"""
            SELECT pg_has_role(current_user, '{GlobalRole}', 'USAGE')
              AND NOT pg_has_role(current_user, '{OwnerRole}', 'MEMBER')
              AND NOT pg_has_role(current_user, '{MigratorRole}', 'MEMBER')
              AND NOT pg_has_role(current_user, '{PostgresDiscoveryDisclosureContract.OwnerRole}', 'MEMBER')
              AND NOT pg_has_role(current_user, '{PostgresDiscoveryDisclosureContract.MigratorRole}', 'MEMBER')
              AND (SELECT count(*) = 3 FROM unnest(@functions) signature
                  JOIN pg_proc p ON p.oid = to_regprocedure(signature)
                  JOIN pg_roles owner ON owner.oid = p.proowner
                  WHERE owner.rolname = '{OwnerRole}' AND NOT owner.rolcanlogin
                    AND NOT owner.rolsuper AND NOT owner.rolbypassrls
                    AND p.prosecdef AND p.proconfig @> ARRAY['search_path=pg_catalog']
                    AND has_function_privilege(current_user, p.oid, 'EXECUTE')
                    AND NOT EXISTS (SELECT 1 FROM aclexplode(COALESCE(p.proacl, acldefault('f', p.proowner))) acl
                        WHERE acl.grantee = 0 AND acl.privilege_type = 'EXECUTE'))
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "functions";
        parameter.Value = signatures;
        command.Parameters.Add(parameter);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("atproto_materialization_authority_not_ready");
    }

    public static string BuildSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = sql.DelimitIdentifier(Schema(context));
        string Table(Type type) => sql.DelimitIdentifier(
            context.Model.FindEntityType(type)!.GetTableName()!, Schema(context));
        string T<T>() => Table(typeof(T));
        string C<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }
        string Literal<T>(string property, object value) => context.Model.FindEntityType(typeof(T))!
            .FindProperty(property)!.GetRelationalTypeMapping().GenerateSqlLiteral(value);
        string records = T<AtprotoRecord>();
        string events = T<Event>();
        string presentations = T<AtprotoRecordTenantPresentation>();
        string storage = T<StorageObject>();
        string producers = T<StorageProducerOperation>();
        string uploads = T<StorageUploadSession>();
        string tombstones = T<StorageObjectDeletionTombstone>();
        string counters = T<StorageUsageCounter>();
        string recordId = C<AtprotoRecord>(nameof(AtprotoRecord.Id));
        string eventId = C<Event>(nameof(Event.Id));
        string eventTenant = C<Event>(nameof(Event.TenantId));
        string eventRecord = C<Event>(nameof(Event.AtprotoRecordId));
        string sourceId = C<StorageObject>(nameof(StorageObject.Id));
        string sourceTenant = C<StorageObject>(nameof(StorageObject.TenantId));
        string sourceStamp = C<StorageObject>(nameof(StorageObject.ConcurrencyStamp));
        string producerId = C<StorageProducerOperation>(nameof(StorageProducerOperation.Id));
        string producerTenant = C<StorageProducerOperation>(nameof(StorageProducerOperation.TenantId));
        string producerStamp = C<StorageProducerOperation>(nameof(StorageProducerOperation.ConcurrencyStamp));
        string read = FunctionSql(context, "read");
        string roots = FunctionSql(context, "roots");
        string fence = FunctionSql(context, "fence_storage");
        string apply = FunctionSql(context, "apply");
        string dependencies = PostgresDiscoveryDisclosureContract.FunctionSql(context, "dependencies");
        string inbound = Literal<AtprotoRecord>(nameof(AtprotoRecord.Direction), AtprotoRecordDirection.Inbound);
        string originalSource = Literal<EventPublicAction>(
            nameof(EventPublicAction.EventPublicActionKindId), (int)EventPublicActionKindEnum.OriginalSource);
        string active = Literal<StorageObject>(nameof(StorageObject.LifecycleState), StorageObjectLifecycleStates.Active);
        string deleteRequested = Literal<StorageObject>(
            nameof(StorageObject.LifecycleState), StorageObjectLifecycleStates.DeleteRequested);
        string reserved = Literal<StorageUploadSession>(nameof(StorageUploadSession.Status), StorageUploadSessionStates.Reserved);
        string uploading = Literal<StorageUploadSession>(nameof(StorageUploadSession.Status), StorageUploadSessionStates.Uploading);
        string finalized = Literal<StorageUploadSession>(nameof(StorageUploadSession.Status), StorageUploadSessionStates.Finalized);
        string failed = Literal<StorageUploadSession>(nameof(StorageUploadSession.Status), StorageUploadSessionStates.Failed);
        string physicalReferences = StorageObjectPhysicalReferenceQuery.BuildPredicate(context, "v_object_id");
        string holds = StorageObjectRetentionQuery.BuildPredicate(context, "v_object_id", "v_now");
        string totals = StorageScopeTotalsQuery.BuildSql(context, "v_tenant", "v_provider");
        var ownerTypes = PrivateRelations.Select(relation => relation.Type).Concat(
        [
            typeof(Tenant), typeof(Actor), typeof(AtprotoIdentity), typeof(AtprotoRecord),
            typeof(AtprotoJetstreamConsumerState), typeof(StorageProviderBinding), typeof(StorageUsageCounter),
            typeof(RegistrationSubmission), typeof(RegistrationOrder), typeof(RegistrationFormVersion),
            typeof(RegistrationFormField), typeof(RegistrationRetentionPolicy),
            typeof(RegistrationProviderSubmissionWriteEffect)
        ]).Concat(context.Model.FindEntityType(typeof(StorageObject))!.GetReferencingForeignKeys()
            .Select(key => key.DeclaringEntityType.ClrType)).Distinct().ToArray();
        string reads = string.Join(", ", ownerTypes.Select(Table).Distinct());
        string privateWrites = string.Join(", ", PrivateRelations.Select(relation => Table(relation.Type))
            .Append(counters).Distinct());
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .Select(table => table.TableName).ToHashSet(StringComparer.Ordinal);
        string policies = string.Join(Environment.NewLine, ownerTypes
            .Where(type => tenantTables.Contains(context.Model.FindEntityType(type)!.GetTableName()!))
            .Select(Table).Distinct().Select(table => $"""
                DROP POLICY IF EXISTS "{PolicyName}" ON {table};
                CREATE POLICY "{PolicyName}" ON {table} TO {OwnerRole} USING (true) WITH CHECK (true);
                """));
        string sharedTables = string.Join(", ", new[]
        {
            typeof(AtprotoJetstreamConsumerState), typeof(AtprotoJetstreamQuarantine),
            typeof(AtprotoRecord), typeof(AtprotoEventProjection), typeof(AtprotoIdentity),
            typeof(Actor), typeof(ExternalActorSubject)
        }.Select(Table));
        string graph = string.Join(",\n", new[]
        {
            Rows("events", events, $"r.{eventId} = ANY(v_events)"),
            Rows("sessions", T<EventSession>(), $"r.{C<EventSession>(nameof(EventSession.EventId))} = ANY(v_events)"),
            Rows("participation", T<EventParticipationConfiguration>(),
                $"r.{C<EventParticipationConfiguration>(nameof(EventParticipationConfiguration.Id))} = ANY(v_events)"),
            Rows("actions", T<EventPublicAction>(),
                $"r.{C<EventPublicAction>(nameof(EventPublicAction.EventId))} = ANY(v_events) AND r.{C<EventPublicAction>(nameof(EventPublicAction.EventPublicActionKindId))} = {originalSource}"),
            Rows("presentations", presentations, $"r.{C<AtprotoRecordTenantPresentation>(nameof(AtprotoRecordTenantPresentation.AtprotoRecordId))} = ANY(v_records)"),
            Rows("storage", storage, $"r.{sourceId} = ANY(v_storage)"),
            Rows("producers", producers, $"r.{producerId} = ANY(v_storage) OR EXISTS (SELECT 1 FROM jsonb_array_elements(p_scope->'receipts') receipt WHERE (receipt->>'id')::uuid = r.{producerId} AND (receipt->>'tenant')::uuid = r.{producerTenant})"),
            Rows("uploads", uploads, $"r.{C<StorageUploadSession>(nameof(StorageUploadSession.StorageObjectId))} = ANY(v_storage) OR r.{C<StorageUploadSession>(nameof(StorageUploadSession.Id))} = ANY(v_storage)"),
            Rows("tombstones", tombstones, $"r.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Id))} = ANY(v_storage)")
        });
        string writes = BuildGraphWrites(context);
        return $"""
            DO $roles$
            DECLARE v_role text;
            BEGIN
                FOREACH v_role IN ARRAY ARRAY['{OwnerRole}', '{MigratorRole}', '{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = v_role) THEN
                        EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', v_role);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = v_role
                        AND (rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication)) THEN
                        RAISE EXCEPTION 'atproto_materialization_authority_not_ready';
                    END IF;
                END LOOP;
                IF pg_has_role('{GlobalRole}', '{OwnerRole}', 'MEMBER')
                   OR pg_has_role('{GlobalRole}', '{MigratorRole}', 'MEMBER') THEN
                    RAISE EXCEPTION 'atproto_materialization_authority_not_ready';
                END IF;
            END; $roles$;
            GRANT {OwnerRole} TO {MigratorRole};
            GRANT {MigratorRole} TO CURRENT_USER;
            GRANT USAGE, CREATE ON SCHEMA {schema} TO {OwnerRole};
            GRANT USAGE ON SCHEMA {schema} TO {GlobalRole};
            GRANT SELECT ON {reads} TO {OwnerRole};
            GRANT INSERT, UPDATE, DELETE ON {privateWrites} TO {OwnerRole};
            GRANT EXECUTE ON FUNCTION {dependencies}(text,uuid[]) TO {OwnerRole};
            GRANT {PostgresDiscoveryDisclosureContract.RuntimeRole} TO {GlobalRole} WITH INHERIT TRUE, SET FALSE;
            GRANT SELECT, INSERT, UPDATE ON {sharedTables} TO {GlobalRole};
            GRANT DELETE ON {T<AtprotoEventProjection>()} TO {GlobalRole};
            GRANT INSERT ON {T<ActorPii>()} TO {GlobalRole};
            {policies}
            DO $install$
            DECLARE v_previous text := current_setting('role');
            BEGIN
                PERFORM set_config('role', '{OwnerRole}', true);
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {roots}(p_scope jsonb)
                RETURNS uuid[] LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE v_ids uuid[]; v_dids text[];
                BEGIN
                    IF current_setting('transaction_isolation') <> 'serializable'
                       OR COALESCE(p_scope->>'kind', '') NOT IN ('snapshot','record','purge','quarantine')
                       OR jsonb_typeof(p_scope->'dids') IS DISTINCT FROM 'array'
                       OR jsonb_array_length(p_scope->'dids') > 100
                       OR jsonb_typeof(p_scope->'imports') IS DISTINCT FROM 'array'
                       OR jsonb_typeof(p_scope->'receipts') IS DISTINCT FROM 'array'
                       OR jsonb_typeof(p_scope->'presentations') IS DISTINCT FROM 'array'
                       OR jsonb_typeof(p_scope->'records') IS DISTINCT FROM 'array' THEN
                        RAISE EXCEPTION 'atproto_materialization_scope_invalid' USING ERRCODE = '22023';
                    END IF;
                    IF (p_scope->>'kind' = 'snapshot' AND jsonb_array_length(p_scope->'dids') = 0)
                       OR EXISTS (SELECT 1 FROM jsonb_array_elements(p_scope->'imports') input
                           WHERE NOT EXISTS (SELECT 1 FROM jsonb_array_elements(p_scope->'records') record
                               WHERE input->>'did' = record->>'did'
                                 AND input->>'uri' = 'at://' || (record->>'did') || '/'
                                     || (record->>'collection') || '/' || (record->>'key')))
                       OR EXISTS (SELECT 1 FROM jsonb_array_elements(p_scope->'receipts') receipt
                           WHERE NOT EXISTS (SELECT 1 FROM jsonb_array_elements(p_scope->'imports') input
                               WHERE input->>'tenant' = receipt->>'tenant'
                                 AND input->>'did' = receipt->>'did'
                                 AND input->>'uri' = receipt->>'uri')) THEN
                        RAISE EXCEPTION 'atproto_materialization_scope_invalid' USING ERRCODE = '22023';
                    END IF;
                    IF NOT EXISTS (
                        SELECT 1 FROM {T<AtprotoJetstreamConsumerState>()} c
                        WHERE c.{C<AtprotoJetstreamConsumerState>(nameof(AtprotoJetstreamConsumerState.Id))} = (p_scope->>'consumer')::uuid
                          AND c.{C<AtprotoJetstreamConsumerState>(nameof(AtprotoJetstreamConsumerState.Service))} = p_scope->>'service'
                          AND c.{C<AtprotoJetstreamConsumerState>(nameof(AtprotoJetstreamConsumerState.LeaseToken))} = (p_scope->>'token')::uuid
                          AND c.{C<AtprotoJetstreamConsumerState>(nameof(AtprotoJetstreamConsumerState.LeaseFence))} = (p_scope->>'fence')::bigint) THEN
                        RAISE EXCEPTION 'atproto_materialization_claim_unavailable' USING ERRCODE = '40001';
                    END IF;
                    SELECT COALESCE(array_agg(value), ARRAY[]::text[]) INTO v_dids
                    FROM jsonb_array_elements_text(p_scope->'dids');
                    SELECT COALESCE(array_agg(r.{recordId}), ARRAY[]::uuid[]) INTO v_ids
                    FROM {records} r
                    WHERE r.{C<AtprotoRecord>(nameof(AtprotoRecord.Collection))} IN
                        ('community.lexicon.calendar.event','community.lexicon.calendar.rsvp')
                      AND ((p_scope->>'kind' = 'snapshot' AND r.{C<AtprotoRecord>(nameof(AtprotoRecord.Did))} = ANY(v_dids))
                        OR (p_scope->>'kind' = 'purge' AND r.{C<AtprotoRecord>(nameof(AtprotoRecord.Did))} = ANY(v_dids)
                            AND r.{C<AtprotoRecord>(nameof(AtprotoRecord.Direction))} = {inbound})
                        OR (p_scope->>'kind' = 'record' AND EXISTS (
                            SELECT 1 FROM jsonb_array_elements(p_scope->'records') input
                            WHERE input->>'did' = r.{C<AtprotoRecord>(nameof(AtprotoRecord.Did))}
                              AND input->>'collection' = r.{C<AtprotoRecord>(nameof(AtprotoRecord.Collection))}
                              AND input->>'key' = r.{C<AtprotoRecord>(nameof(AtprotoRecord.RecordKey))})));
                    RETURN ARRAY(
                        SELECT DISTINCT id FROM (
                            SELECT unnest(v_ids) id
                            UNION ALL
                            SELECT dependent.{recordId} FROM {records} dependent
                            WHERE dependent.{C<AtprotoRecord>(nameof(AtprotoRecord.Collection))} = 'community.lexicon.calendar.rsvp'
                              AND dependent.{C<AtprotoRecord>(nameof(AtprotoRecord.SubjectUri))} IN (
                                  SELECT root.{C<AtprotoRecord>(nameof(AtprotoRecord.Uri))} FROM {records} root
                                  WHERE root.{recordId} = ANY(v_ids)
                                    AND root.{C<AtprotoRecord>(nameof(AtprotoRecord.Collection))} = 'community.lexicon.calendar.event')
                        ) selected ORDER BY id);
                END; $body$;
                $ddl$;
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {read}(p_scope jsonb)
                RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE v_records uuid[]; v_events uuid[]; v_storage uuid[]; v_tenants uuid[]; v_actors uuid[];
                BEGIN
                    v_records := {roots}(p_scope);
                    SELECT COALESCE(array_agg(e.{eventId}), ARRAY[]::uuid[]) INTO v_events
                    FROM {events} e WHERE e.{eventRecord} = ANY(v_records);
                    SELECT COALESCE(array_agg(DISTINCT id), ARRAY[]::uuid[]) INTO v_actors FROM (
                        SELECT e.{C<Event>(nameof(Event.ActorId))} id FROM {events} e WHERE e.{eventId} = ANY(v_events)
                        UNION ALL
                        SELECT i.{C<AtprotoIdentity>(nameof(AtprotoIdentity.ActorId))} FROM {T<AtprotoIdentity>()} i
                        WHERE EXISTS (SELECT 1 FROM jsonb_array_elements(p_scope->'imports') input
                            WHERE input->>'did' = i.{C<AtprotoIdentity>(nameof(AtprotoIdentity.Did))})
                    ) owners;
                    SELECT COALESCE(array_agg(DISTINCT id), ARRAY[]::uuid[]) INTO v_tenants FROM (
                        SELECT e.{eventTenant} id FROM {events} e WHERE e.{eventId} = ANY(v_events)
                        UNION ALL
                        SELECT p.{C<AtprotoRecordTenantPresentation>(nameof(AtprotoRecordTenantPresentation.TenantId))}
                        FROM {presentations} p WHERE p.{C<AtprotoRecordTenantPresentation>(nameof(AtprotoRecordTenantPresentation.AtprotoRecordId))} = ANY(v_records)
                        UNION ALL SELECT (value->>'tenant')::uuid FROM jsonb_array_elements(p_scope->'imports')
                        UNION ALL SELECT value::uuid FROM jsonb_array_elements_text(p_scope->'presentations')
                        UNION ALL SELECT * FROM {dependencies}('record', v_records)
                        UNION ALL SELECT * FROM {dependencies}('actor', v_actors)
                    ) affected;
                    IF cardinality(v_tenants) > 1024 OR array_position(v_tenants, NULL) IS NOT NULL
                       OR EXISTS (SELECT 1 FROM unnest(v_tenants) id
                           WHERE NOT EXISTS (SELECT 1 FROM {T<Tenant>()} t WHERE t.{C<Tenant>(nameof(Tenant.Id))} = id)) THEN
                        RAISE EXCEPTION 'discovery_disclosure_fanout_exceeded' USING ERRCODE = '54000';
                    END IF;
                    SELECT COALESCE(array_agg(s.{sourceId}), ARRAY[]::uuid[]) INTO v_storage
                    FROM {storage} s WHERE s.{sourceId} IN (
                        SELECT e.{C<Event>(nameof(Event.FeaturedImageId))} FROM {events} e WHERE e.{eventId} = ANY(v_events)
                        UNION SELECT e.{C<Event>(nameof(Event.BackgroundImageId))} FROM {events} e WHERE e.{eventId} = ANY(v_events)
                        UNION SELECT session.{C<EventSession>(nameof(EventSession.FeaturedImageId))}
                            FROM {T<EventSession>()} session WHERE session.{C<EventSession>(nameof(EventSession.EventId))} = ANY(v_events))
                      OR (s.{C<StorageObject>(nameof(StorageObject.OwningResourceKind))} = 'event'
                        AND s.{C<StorageObject>(nameof(StorageObject.OwningResourceId))} = ANY(v_events));
                    RETURN jsonb_build_object(
                        'tenants', to_jsonb(v_tenants), 'actors', to_jsonb(v_actors),
                        'recordIds', to_jsonb(v_records), 'storageIds', to_jsonb(v_storage),
                        {graph});
                END; $body$;
                $ddl$;
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {fence}(p_scope jsonb)
                RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE v_graph jsonb; v_row record; v_receipt jsonb;
                BEGIN
                    v_graph := {read}(p_scope);
                    FOR v_row IN SELECT s.* FROM {storage} s
                        WHERE s.{sourceId} IN (SELECT value::uuid FROM jsonb_array_elements_text(v_graph->'storageIds'))
                        ORDER BY s.{sourceTenant}, s.{sourceId} LOOP
                        UPDATE {storage} SET {sourceStamp} = uuidv7()
                        WHERE {sourceId} = v_row.{sourceId} AND {sourceStamp} = v_row.{sourceStamp};
                        IF NOT FOUND THEN
                            RAISE EXCEPTION 'storage_reference_conflict' USING ERRCODE = '40001';
                        END IF;
                    END LOOP;
                    FOR v_receipt IN SELECT value FROM jsonb_array_elements(p_scope->'receipts')
                        ORDER BY value->>'tenant', value->>'id' LOOP
                        IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements(p_scope->'imports') input
                            WHERE input->>'tenant' = v_receipt->>'tenant'
                              AND input->>'did' = v_receipt->>'did'
                              AND input->>'uri' = v_receipt->>'uri') THEN
                            RAISE EXCEPTION 'storage_producer_identity_mismatch' USING ERRCODE = '22023';
                        END IF;
                        UPDATE {producers} SET {producerStamp} = uuidv7()
                        WHERE {producerId} = (v_receipt->>'id')::uuid
                          AND {producerTenant} = (v_receipt->>'tenant')::uuid
                          AND NOT EXISTS (SELECT 1 FROM {tombstones} t
                              WHERE t.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Id))} = (v_receipt->>'id')::uuid);
                    END LOOP;
                    RETURN {read}(p_scope);
                END; $body$;
                $ddl$;
                EXECUTE $ddl$
                CREATE OR REPLACE FUNCTION {apply}(p_scope jsonb, p_changes jsonb)
                RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog
                AS $body$
                DECLARE v_graph jsonb; v_change jsonb; v_old jsonb; v_new jsonb; v_affected integer;
                    v_retirement jsonb; v_object_id uuid; v_tenant uuid; v_provider text;
                    v_now timestamptz := (p_scope->>'observedAt')::timestamptz;
                    v_source {storage}%ROWTYPE; v_target {tombstones}%ROWTYPE;
                    v_counter {counters}%ROWTYPE; v_totals record; v_pair record;
                BEGIN
                    v_graph := {read}(p_scope);
                    {writes}
                    FOR v_retirement IN SELECT value FROM jsonb_array_elements(p_changes->'retirements')
                        ORDER BY value->>'tenant', value->>'id' LOOP
                        v_object_id := (v_retirement->>'id')::uuid;
                        v_tenant := (v_retirement->>'tenant')::uuid;
                        IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements(v_graph->'storage') source
                            WHERE (source->>'{Unquoted<StorageObject>(nameof(StorageObject.Id))}')::uuid = v_object_id
                              AND (source->>'{Unquoted<StorageObject>(nameof(StorageObject.TenantId))}')::uuid = v_tenant) THEN
                            RAISE EXCEPTION 'storage_retirement_scope_invalid' USING ERRCODE = '22023';
                        END IF;
                        SELECT * INTO STRICT v_source FROM {storage}
                        WHERE {sourceId} = v_object_id AND {sourceTenant} = v_tenant;
                        IF ({physicalReferences}) OR ({holds}) THEN CONTINUE; END IF;
                        SELECT * INTO v_target FROM jsonb_populate_record(NULL::{tombstones}, v_retirement->'target');
                        IF v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Id))} <> v_object_id
                          OR v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.TenantId))} <> v_tenant
                          OR v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Provider))} IS DISTINCT FROM v_source.{C<StorageObject>(nameof(StorageObject.Provider))}
                          OR v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderBindingId))} IS DISTINCT FROM v_source.{C<StorageObject>(nameof(StorageObject.StorageProviderBindingId))}
                          OR v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ObjectKey))} IS DISTINCT FROM v_source.{C<StorageObject>(nameof(StorageObject.ObjectKey))}
                          OR (v_source.{C<StorageObject>(nameof(StorageObject.ProviderVersionId))} IS NOT NULL
                            AND v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderObjectVersion))} IS DISTINCT FROM v_source.{C<StorageObject>(nameof(StorageObject.ProviderVersionId))})
                          OR NOT EXISTS (SELECT 1 FROM {T<StorageProviderBinding>()} b
                            WHERE b.{C<StorageProviderBinding>(nameof(StorageProviderBinding.Id))} = v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderBindingId))}
                              AND b.{C<StorageProviderBinding>(nameof(StorageProviderBinding.Provider))} = v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Provider))}) THEN
                            RAISE EXCEPTION 'storage_producer_identity_mismatch' USING ERRCODE = '22023';
                        END IF;
                        IF EXISTS (SELECT 1 FROM {producers} p WHERE p.{producerId} = v_object_id
                            AND (p.{producerTenant} <> v_tenant
                              OR p.{C<StorageProducerOperation>(nameof(StorageProducerOperation.ProviderBindingId))} <> v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderBindingId))}
                              OR p.{C<StorageProducerOperation>(nameof(StorageProducerOperation.ObjectKey))} <> v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ObjectKey))}
                              OR p.{C<StorageProducerOperation>(nameof(StorageProducerOperation.Provider))} <> v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Provider))}
                              OR (p.{C<StorageProducerOperation>(nameof(StorageProducerOperation.ProviderVersionId))} IS NOT NULL
                                AND p.{C<StorageProducerOperation>(nameof(StorageProducerOperation.ProviderVersionId))} IS DISTINCT FROM v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderObjectVersion))})
                              OR (NOT p.{C<StorageProducerOperation>(nameof(StorageProducerOperation.ProducerSettled))}
                                AND v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.State))} <> 1))) THEN
                            RAISE EXCEPTION 'storage_producer_identity_mismatch' USING ERRCODE = '22023';
                        END IF;
                        IF EXISTS (SELECT 1 FROM {uploads} u
                            WHERE COALESCE(u.{C<StorageUploadSession>(nameof(StorageUploadSession.StorageObjectId))}, u.{C<StorageUploadSession>(nameof(StorageUploadSession.Id))}) = v_object_id
                              AND (u.{C<StorageUploadSession>(nameof(StorageUploadSession.TenantId))} <> v_tenant
                                OR u.{C<StorageUploadSession>(nameof(StorageUploadSession.Provider))} <> v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Provider))}
                                OR u.{C<StorageUploadSession>(nameof(StorageUploadSession.StorageProviderBindingId))} IS DISTINCT FROM v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderBindingId))}
                                OR u.{C<StorageUploadSession>(nameof(StorageUploadSession.ObjectKey))} IS DISTINCT FROM v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ObjectKey))}
                                OR (u.{C<StorageUploadSession>(nameof(StorageUploadSession.ProviderVersionId))} IS NOT NULL
                                  AND u.{C<StorageUploadSession>(nameof(StorageUploadSession.ProviderVersionId))} IS DISTINCT FROM v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderObjectVersion))})
                                OR (NOT u.{C<StorageUploadSession>(nameof(StorageUploadSession.ProducerSettled))}
                                  AND v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.State))} <> 1))) THEN
                            RAISE EXCEPTION 'storage_producer_identity_mismatch' USING ERRCODE = '22023';
                        END IF;
                        INSERT INTO {tombstones} SELECT v_target.* ON CONFLICT ({C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Id))}) DO NOTHING;
                        IF NOT EXISTS (SELECT 1 FROM {tombstones} t
                            WHERE t.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Id))} = v_object_id
                              AND t.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.TenantId))} = v_tenant
                              AND t.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderBindingId))} = v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderBindingId))}
                              AND t.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Provider))} = v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Provider))}
                              AND t.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ObjectKey))} = v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ObjectKey))}
                              AND (v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderObjectVersion))} IS NULL
                                OR t.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderObjectVersion))} = v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.ProviderObjectVersion))})
                              AND (v_target.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.State))} <> 1
                                OR t.{C<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.State))} = 1)) THEN
                            RAISE EXCEPTION 'storage_retirement_custody_conflict' USING ERRCODE = '40001';
                        END IF;
                        UPDATE {storage} SET {C<StorageObject>(nameof(StorageObject.LifecycleState))} = {deleteRequested}
                        WHERE {sourceId} = v_object_id AND {sourceStamp} = v_source.{sourceStamp};
                        IF NOT FOUND THEN RAISE EXCEPTION 'storage_reference_conflict' USING ERRCODE = '40001'; END IF;
                        UPDATE {uploads} SET {C<StorageUploadSession>(nameof(StorageUploadSession.Status))} = {failed}
                        WHERE COALESCE({C<StorageUploadSession>(nameof(StorageUploadSession.StorageObjectId))}, {C<StorageUploadSession>(nameof(StorageUploadSession.Id))}) = v_object_id
                          AND {C<StorageUploadSession>(nameof(StorageUploadSession.Status))} IN ({reserved}, {uploading}, {finalized});
                        DELETE FROM {producers} WHERE {producerId} = v_object_id AND {producerTenant} = v_tenant;
                        DELETE FROM {uploads} WHERE COALESCE({C<StorageUploadSession>(nameof(StorageUploadSession.StorageObjectId))}, {C<StorageUploadSession>(nameof(StorageUploadSession.Id))}) = v_object_id
                            AND {C<StorageUploadSession>(nameof(StorageUploadSession.TenantId))} = v_tenant;
                        DELETE FROM {storage} WHERE {sourceId} = v_object_id AND {sourceTenant} = v_tenant;
                    END LOOP;
                    FOR v_pair IN
                        SELECT DISTINCT tenant, provider FROM (
                            SELECT (value->>'tenant')::uuid tenant, value->>'provider' provider
                            FROM jsonb_array_elements(p_changes->'accounting')
                            UNION ALL
                            SELECT (value->>'tenant')::uuid, value->'target'->>'{Unquoted<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Provider))}'
                            FROM jsonb_array_elements(p_changes->'retirements')
                        ) affected ORDER BY tenant, provider
                    LOOP
                        v_tenant := v_pair.tenant; v_provider := v_pair.provider;
                        IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements_text(v_graph->'tenants') id WHERE id::uuid = v_tenant) THEN
                            RAISE EXCEPTION 'storage_accounting_scope_invalid' USING ERRCODE = '22023';
                        END IF;
                        INSERT INTO {counters} (
                            {C<StorageUsageCounter>(nameof(StorageUsageCounter.Id))}, {C<StorageUsageCounter>(nameof(StorageUsageCounter.TenantId))},
                            {C<StorageUsageCounter>(nameof(StorageUsageCounter.Provider))}, {C<StorageUsageCounter>(nameof(StorageUsageCounter.UsedBytes))},
                            {C<StorageUsageCounter>(nameof(StorageUsageCounter.ReservedBytes))}, {C<StorageUsageCounter>(nameof(StorageUsageCounter.QuarantinedBytes))},
                            {C<StorageUsageCounter>(nameof(StorageUsageCounter.ObjectCount))}, {C<StorageUsageCounter>(nameof(StorageUsageCounter.CreatedAt))},
                            {C<StorageUsageCounter>(nameof(StorageUsageCounter.ConcurrencyStamp))})
                        VALUES (uuidv7(), v_tenant, v_provider, 0, 0, 0, 0, clock_timestamp(), uuidv7())
                        ON CONFLICT ({C<StorageUsageCounter>(nameof(StorageUsageCounter.TenantId))}, {C<StorageUsageCounter>(nameof(StorageUsageCounter.Provider))}) DO NOTHING;
                        SELECT * INTO STRICT v_counter FROM {counters}
                        WHERE {C<StorageUsageCounter>(nameof(StorageUsageCounter.TenantId))} = v_tenant
                          AND {C<StorageUsageCounter>(nameof(StorageUsageCounter.Provider))} = v_provider;
                        UPDATE {counters} SET {C<StorageUsageCounter>(nameof(StorageUsageCounter.ConcurrencyStamp))} = uuidv7()
                        WHERE {C<StorageUsageCounter>(nameof(StorageUsageCounter.Id))} = v_counter.{C<StorageUsageCounter>(nameof(StorageUsageCounter.Id))}
                          AND {C<StorageUsageCounter>(nameof(StorageUsageCounter.ConcurrencyStamp))} = v_counter.{C<StorageUsageCounter>(nameof(StorageUsageCounter.ConcurrencyStamp))};
                        IF NOT FOUND THEN RAISE EXCEPTION 'storage_accounting_conflict' USING ERRCODE = '40001'; END IF;
                        SELECT * INTO STRICT v_totals FROM ({totals}) aggregate;
                        UPDATE {counters} SET
                            {C<StorageUsageCounter>(nameof(StorageUsageCounter.UsedBytes))} = v_totals."Used",
                            {C<StorageUsageCounter>(nameof(StorageUsageCounter.ReservedBytes))} = v_totals."Reserved",
                            {C<StorageUsageCounter>(nameof(StorageUsageCounter.QuarantinedBytes))} = v_totals."Quarantined",
                            {C<StorageUsageCounter>(nameof(StorageUsageCounter.ObjectCount))} = v_totals."Count",
                            {C<StorageUsageCounter>(nameof(StorageUsageCounter.LastRecalculatedAt))} = v_now,
                            {C<StorageUsageCounter>(nameof(StorageUsageCounter.UpdatedAt))} = clock_timestamp()
                        WHERE {C<StorageUsageCounter>(nameof(StorageUsageCounter.Id))} = v_counter.{C<StorageUsageCounter>(nameof(StorageUsageCounter.Id))};
                    END LOOP;
                    RETURN {read}(p_scope);
                END; $body$;
                $ddl$;
                EXECUTE $acl$
                    REVOKE ALL ON FUNCTION {roots}(jsonb), {read}(jsonb), {fence}(jsonb), {apply}(jsonb,jsonb) FROM PUBLIC;
                    GRANT EXECUTE ON FUNCTION {read}(jsonb), {fence}(jsonb), {apply}(jsonb,jsonb) TO {GlobalRole};
                $acl$;
                PERFORM set_config('role', v_previous, true);
            END; $install$;
            REVOKE CREATE ON SCHEMA {schema} FROM {OwnerRole};
            """;

        string Unquoted<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
        }
        static string Rows(string name, string table, string predicate) =>
            $"'{name}', (SELECT COALESCE(jsonb_agg(to_jsonb(r)), '[]'::jsonb) FROM {table} r WHERE {predicate})";
    }

    private static string BuildGraphWrites(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var result = new StringBuilder();
        foreach (var (name, type) in PrivateRelations.Where(relation =>
                     relation.Name is not ("uploads" or "tombstones")))
        {
            var entity = context.Model.FindEntityType(type)!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string table = sql.DelimitIdentifier(entity.GetTableName()!, Schema(context));
            var properties = entity.GetProperties().Where(property => property.GetColumnName(store) is not null
                && property.GetComputedColumnSql() is null).ToArray();
            var writable = UpdateProperties(type).ToHashSet(StringComparer.Ordinal);
            string Column(IProperty property) => sql.DelimitIdentifier(property.GetColumnName(store)!);
            string predicate = string.Join(" AND ", entity.FindPrimaryKey()!.Properties
                .Select(property => $"target.{Column(property)} = original.{Column(property)}"));
            string concurrency = string.Join("", properties.Where(property => property.IsConcurrencyToken)
                .Select(property => $" AND target.{Column(property)} IS NOT DISTINCT FROM original.{Column(property)}"));
            string updates = string.Join(", ", properties.Where(property => writable.Contains(property.Name))
                .Select(property => $"{Column(property)} = proposed.{Column(property)}"));
            string immutable = string.Join(" OR ", properties.Where(property => !writable.Contains(property.Name))
                .Select(property => $"v_new->'{property.GetColumnName(store)}' IS DISTINCT FROM v_old->'{property.GetColumnName(store)}'"));
            string columns = string.Join(", ", properties.Select(Column));
            string values = string.Join(", ", properties.Select(property => "proposed." + Column(property)));
            string oldKey = string.Join(" AND ", entity.FindPrimaryKey()!.Properties.Select(property =>
                $"candidate->>'{property.GetColumnName(store)}' IS NOT DISTINCT FROM v_old->>'{property.GetColumnName(store)}'"));
            string scope = BuildRowScope(context, type, "v_new");
            result.AppendLine(CultureInfo.InvariantCulture, $"""
                FOR v_change IN SELECT value FROM jsonb_array_elements(COALESCE(p_changes->'{name}', '[]'::jsonb)) LOOP
                    v_old := v_change->'original'; v_new := v_change->'current';
                    IF COALESCE(v_change->>'operation', '') NOT IN ('insert','update','delete') THEN
                        RAISE EXCEPTION 'atproto_materialization_write_invalid' USING ERRCODE = '22023';
                    END IF;
                    IF v_change->>'operation' <> 'insert' AND NOT EXISTS (
                        SELECT 1 FROM jsonb_array_elements(v_graph->'{name}') candidate WHERE {oldKey}) THEN
                        RAISE EXCEPTION 'atproto_materialization_write_scope_invalid' USING ERRCODE = '22023';
                    END IF;
                    IF v_change->>'operation' <> 'delete' AND ({scope}) IS NOT TRUE THEN
                        RAISE EXCEPTION 'atproto_materialization_write_scope_invalid' USING ERRCODE = '22023';
                    END IF;
                    IF v_change->>'operation' = 'update' AND ({immutable}) THEN
                        RAISE EXCEPTION 'atproto_materialization_immutable_field' USING ERRCODE = '22023';
                    END IF;
                """);
            if (name == "storage")
                result.AppendLine(BuildPromotionCheck(context));
            result.AppendLine(CultureInfo.InvariantCulture, $"""
                    IF v_change->>'operation' = 'insert' THEN
                        INSERT INTO {table} ({columns})
                        SELECT {values} FROM jsonb_populate_record(NULL::{table}, v_new) proposed;
                    ELSIF v_change->>'operation' = 'update' THEN
                """);
            result.AppendLine(writable.Count == 0
                ? "RAISE EXCEPTION 'atproto_materialization_write_invalid' USING ERRCODE = '22023';"
                : $"""
                    UPDATE {table} target SET {updates}
                    FROM jsonb_populate_record(NULL::{table}, v_new) proposed,
                        jsonb_populate_record(NULL::{table}, v_old) original
                    WHERE {predicate}{concurrency};
                    GET DIAGNOSTICS v_affected = ROW_COUNT;
                    IF v_affected <> 1 THEN RAISE EXCEPTION 'atproto_materialization_conflict' USING ERRCODE = '40001'; END IF;
                  """);
            result.AppendLine("ELSE");
            result.AppendLine(name == "producers"
                ? $"""
                        IF NOT EXISTS (
                            SELECT 1 FROM {sql.DelimitIdentifier(context.Model.FindEntityType(typeof(StorageObject))!.GetTableName()!, Schema(context))} source
                            WHERE source.id = (v_old->>'id')::uuid AND source.tenant_id = (v_old->>'tenant_id')::uuid
                              AND source.storage_provider_binding_id = (v_old->>'provider_binding_id')::uuid
                              AND source.provider = v_old->>'provider' AND source.object_key = v_old->>'object_key'
                              AND source.provider_version_id IS NOT DISTINCT FROM v_old->>'provider_version_id') THEN
                            RAISE EXCEPTION 'storage_producer_custody_not_transferred' USING ERRCODE = '22023';
                        END IF;
                        DELETE FROM {table} target USING jsonb_populate_record(NULL::{table}, v_old) original
                        WHERE {predicate}{concurrency};
                        GET DIAGNOSTICS v_affected = ROW_COUNT;
                        IF v_affected <> 1 THEN RAISE EXCEPTION 'storage_producer_conflict' USING ERRCODE = '40001'; END IF;
                  """
                : "RAISE EXCEPTION 'atproto_materialization_delete_invalid' USING ERRCODE = '22023';");
            result.AppendLine("END IF; END LOOP;");
        }
        return result.ToString();
    }

    private static string[] UpdateProperties(Type type)
    {
        string[] audit =
        [
            nameof(Event.UpdatedAt), nameof(Event.IsDeleted), nameof(Event.DeletedAt),
            nameof(Event.DeletedBy), nameof(Event.ConcurrencyStamp)
        ];
        if (type == typeof(Event))
            return
            [
                .. audit, nameof(Event.ActorId), nameof(Event.EventProvenanceTypeId),
                nameof(Event.Title), nameof(Event.DiscoveryTitleSortKey), nameof(Event.Content), nameof(Event.Description),
                nameof(Event.EventFormatId), nameof(Event.EventTimeZoneId), nameof(Event.Timezone),
                nameof(Event.ProvenanceSource), nameof(Event.ProvenanceExternalId), nameof(Event.CreatedAt),
                nameof(Event.FeaturedImageId), nameof(Event.EventStatusId), nameof(Event.SessionCount),
                nameof(Event.FirstSessionDate), nameof(Event.LastSessionDate), nameof(Event.FirstSessionStartUtc),
                nameof(Event.LastSessionStartUtc), nameof(Event.LastSessionEndUtc)
            ];
        if (type == typeof(EventSession))
            return
            [
                .. audit, nameof(EventSession.Title), nameof(EventSession.Description), nameof(EventSession.CreatedAt),
                nameof(EventSession.EventSessionStatusId), nameof(EventSession.StartTime), nameof(EventSession.EndTime),
                nameof(EventSession.EndTimeType), nameof(EventSession.LocalStartDate), nameof(EventSession.LocalEndDate),
                nameof(EventSession.LocalStartTime), nameof(EventSession.LocalEndTime),
                nameof(EventSession.LocalStartMinuteOfDay), nameof(EventSession.LocalEndMinuteOfDay)
            ];
        if (type == typeof(EventParticipationConfiguration))
            return
            [
                nameof(EventParticipationConfiguration.ParticipationHandlingModeId),
                nameof(EventParticipationConfiguration.AdvanceRegistrationObligationId),
                nameof(EventParticipationConfiguration.IdentityAccessModeId),
                nameof(EventParticipationConfiguration.GuestRecoveryPolicy),
                nameof(EventParticipationConfiguration.UpdatedAt),
                nameof(EventParticipationConfiguration.ConcurrencyStamp)
            ];
        if (type == typeof(EventPublicAction))
            return
            [
                .. audit, nameof(EventPublicAction.Url), nameof(EventPublicAction.DestinationDomain),
                nameof(EventPublicAction.HealthStateId)
            ];
        if (type == typeof(AtprotoRecordTenantPresentation))
            return
            [
                nameof(AtprotoRecordTenantPresentation.IsVisible),
                nameof(AtprotoRecordTenantPresentation.SourceVersion),
                nameof(AtprotoRecordTenantPresentation.EvaluatedAt)
            ];
        if (type == typeof(StorageObject))
            return [nameof(StorageObject.SourceUri), nameof(StorageObject.ConcurrencyStamp), nameof(StorageObject.UpdatedAt)];
        return [];
    }

    private static string BuildRowScope(ExploreDbContext context, Type type, string row)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context));
        string Column<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
        }
        string Value(string property) => $"{row}->>'{ColumnName(property)}'";
        string ColumnName(string property)
        {
            var entity = context.Model.FindEntityType(type)!;
            return entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
        }
        string eventTable = Table<Event>();
        string eventId = sql.DelimitIdentifier(Column<Event>(nameof(Event.Id)));
        string eventTenant = sql.DelimitIdentifier(Column<Event>(nameof(Event.TenantId)));
        string eventRecord = sql.DelimitIdentifier(Column<Event>(nameof(Event.AtprotoRecordId)));
        string records = FunctionSql(context, "roots") + "(p_scope)";
        if (type == typeof(Event))
            return $"""
                ({Value(nameof(Event.AtprotoRecordId))})::uuid = ANY({records})
                AND (EXISTS (SELECT 1 FROM jsonb_array_elements(v_graph->'events') old
                        WHERE old->>'{Column<Event>(nameof(Event.Id))}' = {Value(nameof(Event.Id))}
                          AND old->>'{Column<Event>(nameof(Event.TenantId))}' = {Value(nameof(Event.TenantId))}
                          AND old->>'{Column<Event>(nameof(Event.AtprotoRecordId))}' = {Value(nameof(Event.AtprotoRecordId))})
                    OR EXISTS (SELECT 1 FROM jsonb_array_elements(p_scope->'imports') input
                        JOIN {Table<AtprotoRecord>()} r ON r.{sql.DelimitIdentifier(Column<AtprotoRecord>(nameof(AtprotoRecord.Id)))} = ({Value(nameof(Event.AtprotoRecordId))})::uuid
                        WHERE input->>'tenant' = {Value(nameof(Event.TenantId))}
                          AND input->>'did' = r.{sql.DelimitIdentifier(Column<AtprotoRecord>(nameof(AtprotoRecord.Did)))}
                          AND input->>'uri' = r.{sql.DelimitIdentifier(Column<AtprotoRecord>(nameof(AtprotoRecord.Uri)))}))
                """;
        if (type == typeof(AtprotoRecordTenantPresentation))
            return $"""
                ({Value(nameof(AtprotoRecordTenantPresentation.AtprotoRecordId))})::uuid = ANY({records})
                AND (EXISTS (SELECT 1 FROM jsonb_array_elements(v_graph->'presentations') old
                        WHERE old->>'{Column<AtprotoRecordTenantPresentation>(nameof(AtprotoRecordTenantPresentation.TenantId))}' = {Value(nameof(AtprotoRecordTenantPresentation.TenantId))}
                          AND old->>'{Column<AtprotoRecordTenantPresentation>(nameof(AtprotoRecordTenantPresentation.AtprotoRecordId))}' = {Value(nameof(AtprotoRecordTenantPresentation.AtprotoRecordId))})
                    OR {Value(nameof(AtprotoRecordTenantPresentation.TenantId))} IN
                        (SELECT value FROM jsonb_array_elements_text(p_scope->'presentations')))
                """;
        if (type == typeof(StorageProducerOperation))
            return "false";
        if (type == typeof(StorageObject))
            return $"""
                EXISTS (SELECT 1 FROM jsonb_array_elements(v_graph->'storage') old
                    WHERE old->>'{Column<StorageObject>(nameof(StorageObject.Id))}' = {Value(nameof(StorageObject.Id))}
                      AND old->>'{Column<StorageObject>(nameof(StorageObject.TenantId))}' = {Value(nameof(StorageObject.TenantId))})
                OR EXISTS (SELECT 1 FROM jsonb_array_elements(p_scope->'receipts') receipt
                    WHERE receipt->>'id' = {Value(nameof(StorageObject.Id))}
                      AND receipt->>'tenant' = {Value(nameof(StorageObject.TenantId))})
                """;
        string parent = type == typeof(EventParticipationConfiguration) ? "Id" : "EventId";
        string allowedAction = type == typeof(EventPublicAction)
            ? $" AND ({Value(nameof(EventPublicAction.EventPublicActionKindId))})::integer = {(int)EventPublicActionKindEnum.OriginalSource}"
            : "";
        return $"""
            EXISTS (SELECT 1 FROM {eventTable} e
                WHERE e.{eventId} = ({Value(parent)})::uuid
                  AND e.{eventTenant} = ({Value("TenantId")})::uuid
                  AND e.{eventRecord} = ANY({records})){allowedAction}
            """;
    }

    private static string BuildPromotionCheck(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context));
        string Column<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return entity.FindProperty(property)!.GetColumnName(
                StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
        }
        string P(string property) => "p." + sql.DelimitIdentifier(Column<StorageProducerOperation>(property));
        string S(string property) => $"v_new->>'{Column<StorageObject>(property)}'";
        return $"""
            IF v_change->>'operation' = 'insert' AND NOT EXISTS (
                SELECT 1 FROM {Table<StorageProducerOperation>()} p
                WHERE {P(nameof(StorageProducerOperation.Id))} = ({S(nameof(StorageObject.Id))})::uuid
                  AND {P(nameof(StorageProducerOperation.TenantId))} = ({S(nameof(StorageObject.TenantId))})::uuid
                  AND {P(nameof(StorageProducerOperation.ProducerSettled))}
                  AND {P(nameof(StorageProducerOperation.ProviderBindingId))} = ({S(nameof(StorageObject.StorageProviderBindingId))})::uuid
                  AND {P(nameof(StorageProducerOperation.Provider))} = {S(nameof(StorageObject.Provider))}
                  AND {P(nameof(StorageProducerOperation.ObjectKey))} = {S(nameof(StorageObject.ObjectKey))}
                  AND {P(nameof(StorageProducerOperation.ProviderVersionId))} IS NOT DISTINCT FROM {S(nameof(StorageObject.ProviderVersionId))}
                  AND NOT EXISTS (SELECT 1 FROM {Table<StorageObjectDeletionTombstone>()} t
                      WHERE t.{sql.DelimitIdentifier(Column<StorageObjectDeletionTombstone>(nameof(StorageObjectDeletionTombstone.Id)))} = {P(nameof(StorageProducerOperation.Id))})
            ) THEN RAISE EXCEPTION 'storage_producer_identity_mismatch' USING ERRCODE = '22023'; END IF;
            """;
    }

    private static string Schema(ExploreDbContext context) =>
        context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(Tenant))!.GetSchema() ?? "public";
}
