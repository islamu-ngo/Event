using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresPublicationFanoutExecutionContract
{
    private sealed record Definition(SourceSql Sql, PostgresPrivilegeGuard.Contract Privileges,
        IReadOnlyDictionary<Type, string> Policies, string Guard, string Body);

    private static Definition Define(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.RuntimeLogin, enrollment.GlobalLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("publication_fanout_enrollment_collision");
        var sql = new SourceSql(context);
        string work = WorkSql(sql);
        string claimRead = PostgresNotificationOutboxAdmissionContract.ReadFunctionSql(context);
        string source = $"""
            PERFORM {sql.C<OutboxMessage>("o", "Id")} FROM {sql.T<OutboxMessage>()} o
                WHERE {sql.C<OutboxMessage>("o", "Id")} = (p_claim->>'MessageId')::uuid
                    AND {sql.C<OutboxMessage>("o", "Status")} = {(int)OutboxMessageStatus.Processing}
                    AND {sql.C<OutboxMessage>("o", "NextRetryAt")} = v_expiry {sql.Lock<OutboxMessage>()};
            IF NOT FOUND THEN RAISE EXCEPTION 'publication_fanout_claim_replaced' USING ERRCODE = '42501'; END IF;
            SELECT pg_catalog.to_jsonb(c) INTO v_claim FROM {claimRead}((p_claim->>'MessageId')::uuid,v_expiry) c;
            IF v_claim IS NULL OR v_claim IS DISTINCT FROM
                pg_catalog.jsonb_set(p_claim,ARRAY['ProcessingLeaseExpiresAt'],pg_catalog.to_jsonb(v_expiry))
            THEN RAISE EXCEPTION 'publication_fanout_claim_replaced' USING ERRCODE = '42501'; END IF;
            SELECT {sql.C<Event>("e", "ActorId")},{sql.C<Event>("e", "Title")}
                INTO v_actor,v_title FROM {sql.T<Event>()} e
                WHERE {sql.C<Event>("e", "Id")} = v_event AND {sql.C<Event>("e", "TenantId")} = v_tenant
                    AND {sql.C<Event>("e", "EventStatusId")} = {(int)EventStatusEnum.Published}
                    AND NOT {sql.C<Event>("e", "IsDeleted")} {sql.Lock<Event>()};
            IF NOT FOUND OR p_request ? 'SourceActorId' AND (p_request->>'SourceActorId')::uuid IS DISTINCT FROM v_actor
                OR p_request ? 'EntityId' AND (p_request->>'EntityId')::uuid IS DISTINCT FROM v_event
            THEN RAISE EXCEPTION 'publication_fanout_source_unavailable' USING ERRCODE = '42501'; END IF;
            """;
        var policies = sql.Policies(context);
        var privileges = new PostgresPrivilegeGuard.Contract(sql.Names.Schema, Roles,
            [new(Routine, Signature, OwnerRole, [RuntimeRole])],
            sql.Sources.Select(sourceRow => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(sourceRow.Key)!.GetTableName()!, OwnerRole, [],
                sourceRow.Value.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
                    new PostgresPrivilegeGuard.Column(sql.Names.ColumnName(sourceRow.Key, pair.Key),
                        pair.Value.Order(StringComparer.Ordinal).ToArray())).ToArray())).ToArray(),
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)]);
        string guard = Guard(context, sql, privileges, policies, enrollment);
        string body = $"""
            DECLARE
                v_tenant uuid := (p_claim->>'TenantId')::uuid;
                v_event uuid := (p_claim->>'SourceId')::uuid;
                v_expiry timestamptz := (p_claim->>'ProcessingLeaseExpiresAt')::timestamptz;
                v_user uuid := (p_request->>'UserId')::uuid;
                v_actor uuid; v_title text; v_claim jsonb; v_result jsonb; v_subscription jsonb;
                v_run jsonb; v_key text; v_rows integer;
                v_enabled boolean; v_muted boolean; v_required boolean; v_preference record;
                v_channel integer;
            BEGIN
                {guard}
                {PostgresPrivilegeGuard.RestrictedCallerSql}
                IF session_user <> {Text(enrollment.RuntimeLogin)} OR current_user <> '{OwnerRole}'
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                    OR pg_catalog.current_setting('session_replication_role') <> 'origin'
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM v_tenant
                    OR pg_catalog.pg_has_role(session_user,{Text(enrollment.MigratorLogin)},'SET')
                    OR pg_catalog.pg_has_role(session_user,{Text(enrollment.MigratorLogin)},'USAGE')
                    OR v_tenant IS NULL OR v_tenant = '00000000-0000-0000-0000-000000000000'::uuid
                    OR v_event IS NULL OR v_event IS DISTINCT FROM (p_claim->>'AggregateId')::uuid
                    OR p_claim->>'AggregateType' IS DISTINCT FROM 'Event'
                    OR p_claim->>'EventType' IS DISTINCT FROM {Text(EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType)}
                    OR v_expiry IS NULL OR NOT pg_catalog.isfinite(v_expiry) OR v_expiry <= pg_catalog.clock_timestamp()
                    OR p_request->>'TenantId' IS DISTINCT FROM v_tenant::text
                    OR p_operation IS NULL OR p_operation NOT IN
                        ('read_run','create_run','update_run','subscribers','privacy','notification',
                         'create_notification','preferences','push_subscriptions','enqueue_push')
                THEN RAISE EXCEPTION 'publication_fanout_original_service_required' USING ERRCODE = '42501'; END IF;
                IF p_operation IN ('create_notification','enqueue_push')
                    AND pg_catalog.current_setting('transaction_isolation') <> 'serializable'
                THEN RAISE EXCEPTION 'publication_fanout_serializable_recipient_required' USING ERRCODE = '42501'; END IF;
                {source}
                {work}
                IF v_expiry <= pg_catalog.clock_timestamp()
                THEN RAISE EXCEPTION 'publication_fanout_claim_expired' USING ERRCODE = '42501'; END IF;
                RETURN COALESCE(v_result,'null'::jsonb);
            END;
            """;
        return new(sql, privileges, policies, guard, body);
    }

    private static readonly string[] RunFields =
    [
        "Id", "TenantId", "FanoutKind", "NotificationEntityTypeId", "EntityId", "SourceActorId", "Status",
        "CursorSubscriberTenantUserId", "ProcessedCount", "CreatedNotificationCount", "StartedAt",
        "CompletedAt", "FailedAt", "LastError", "CreatedAt", "ConcurrencyStamp"
    ];

    private static string WorkSql(SourceSql sql)
    {
        string runPredicate = $"""
            {sql.C<NotificationFanoutRun>("r", "TenantId")} = v_tenant
            AND {sql.C<NotificationFanoutRun>("r", "EntityId")} = v_event
            AND {sql.C<NotificationFanoutRun>("r", "SourceActorId")} = v_actor
            AND {sql.C<NotificationFanoutRun>("r", "FanoutKind")} = 'event-published'
            AND {sql.C<NotificationFanoutRun>("r", "NotificationEntityTypeId")} = {(int)NotificationEntityTypeEnum.Event}
            AND {sql.C<NotificationFanoutRun>("r", "FanoutOccurrenceId")} IS NULL
            """;
        string runRead = $"""
            SELECT {sql.Projection<NotificationFanoutRun>("r", RunFields)} INTO v_run
                FROM {sql.T<NotificationFanoutRun>()} r WHERE {runPredicate} {sql.Lock<NotificationFanoutRun>(update: true)};
            """;
        string subscribers = SubscriberSql(sql);
        string readSubscription = $"""
            SELECT {sql.Projection<ActorSubscription>("s", "Id", "TenantId", "SubscriberTenantUserId", "SubscriberUserId", "TargetActorId", "TargetActorTypeId")}
                INTO v_subscription {subscribers}
                AND {sql.C<ActorSubscription>("s", "SubscriberUserId")} = v_user
                {sql.Lock<ActorSubscription>(alias: "s")} {sql.Lock<TenantUser>(alias: "tu", append: true)}
                {sql.Lock<User>(alias: "u", append: true)};
            IF v_subscription IS NULL THEN
                RAISE EXCEPTION 'publication_fanout_recipient_unavailable' USING ERRCODE = '42501'; END IF;
            """;
        string privacy = $"EXISTS (SELECT 1 FROM {sql.T<PrivacyErasureSaga>()} ps WHERE {sql.C<PrivacyErasureSaga>("ps", "SubjectId")} = v_user)";
        string notificationPredicate = $"""
            {sql.C<Notification>("n", "TenantId")} = v_tenant AND {sql.C<Notification>("n", "UserId")} = v_user
            AND {sql.C<Notification>("n", "DeduplicationKey")} = v_key AND NOT {sql.C<Notification>("n", "IsDeleted")}
            AND {sql.C<Notification>("n", "SourceActorId")} = v_actor
            AND {sql.C<Notification>("n", "RecipientContextActorId")} = v_actor
            AND {sql.C<Notification>("n", "NotificationEntityTypeId")} = {(int)NotificationEntityTypeEnum.Event}
            AND {sql.C<Notification>("n", "EntityId")} = v_event::text
            AND {sql.C<Notification>("n", "NotificationTypeId")} = {(int)NotificationTypeEnum.EventCreated}
            AND {sql.C<Notification>("n", "NotificationReasonId")} = {(int)NotificationReasonEnum.Subscription}
            AND {sql.C<Notification>("n", "NotificationScopeId")} = (v_subscription->>'TargetActorTypeId')::integer
            """;
        return $"""
            IF p_operation IN ('read_run','create_run','update_run') THEN
                IF p_request->>'FanoutKind' IS DISTINCT FROM 'event-published'
                    OR (p_request->>'NotificationEntityTypeId')::integer IS DISTINCT FROM {(int)NotificationEntityTypeEnum.Event}
                    OR (p_request->>'EntityId')::uuid IS DISTINCT FROM v_event
                    OR (p_request->>'SourceActorId')::uuid IS DISTINCT FROM v_actor
                THEN RAISE EXCEPTION 'publication_fanout_run_source_required' USING ERRCODE = '42501'; END IF;
                {runRead}
                IF p_operation = 'create_run' AND v_run IS NULL THEN
                    IF (p_request->>'Id')::uuid IS NULL OR (p_request->>'ConcurrencyStamp')::uuid IS NULL
                        OR p_request->>'Status' IS DISTINCT FROM 'processing'
                    THEN RAISE EXCEPTION 'publication_fanout_run_shape' USING ERRCODE = '22023'; END IF;
                    INSERT INTO {sql.T<NotificationFanoutRun>()}
                        ({sql.Write<NotificationFanoutRun>("INSERT", "Id", "TenantId", "FanoutKind", "NotificationEntityTypeId",
                            "EntityId", "SourceActorId", "Status", "StartedAt", "CreatedAt", "ConcurrencyStamp",
                            "ProcessedCount", "CreatedNotificationCount", "ProcessingGeneration", "ProcessingFence")})
                    VALUES ((p_request->>'Id')::uuid,v_tenant,'event-published',{(int)NotificationEntityTypeEnum.Event},
                        v_event,v_actor,'processing',pg_catalog.clock_timestamp(),pg_catalog.clock_timestamp(),
                        (p_request->>'ConcurrencyStamp')::uuid,0,0,0,0)
                    ON CONFLICT ({sql.Write<NotificationFanoutRun>("SELECT", "TenantId", "FanoutKind",
                        "NotificationEntityTypeId", "EntityId", "SourceActorId")})
                        WHERE {sql.Write<NotificationFanoutRun>("SELECT", "FanoutOccurrenceId")} IS NULL DO NOTHING;
                    {runRead}
                ELSIF p_operation = 'update_run' THEN
                    {UpdateRunSql(sql, runPredicate)}
                    {runRead}
                END IF;
                v_result := v_run;
            ELSIF p_operation = 'subscribers' THEN
                IF (p_request->>'SourceActorId')::uuid IS DISTINCT FROM v_actor
                    OR (p_request->>'Take')::integer IS NULL OR (p_request->>'Take')::integer NOT BETWEEN 1 AND 250
                THEN RAISE EXCEPTION 'publication_fanout_batch_bounds' USING ERRCODE = '22023'; END IF;
                SELECT COALESCE(pg_catalog.jsonb_agg(q.value),'[]'::jsonb) INTO v_result FROM (
                    SELECT {sql.Projection<ActorSubscription>("s", "Id", "TenantId", "SubscriberTenantUserId", "SubscriberUserId", "TargetActorId", "TargetActorTypeId")} AS value
                    {subscribers}
                    AND ((p_request->>'After')::uuid IS NULL
                        OR {sql.C<ActorSubscription>("s", "SubscriberTenantUserId")} > (p_request->>'After')::uuid)
                    ORDER BY {sql.C<ActorSubscription>("s", "SubscriberTenantUserId")} LIMIT (p_request->>'Take')::integer) q;
            ELSE
                {readSubscription}
                v_key := 'event-published:' || replace(v_tenant::text,'-','') || ':' ||
                    replace(v_event::text,'-','') || ':' || replace(v_subscription->>'SubscriberTenantUserId','-','');
                IF p_operation = 'privacy' THEN
                    IF {privacy} THEN v_result := pg_catalog.jsonb_build_object('SubjectId',v_user); END IF;
                ELSE
                    IF {privacy} THEN RAISE EXCEPTION 'publication_fanout_recipient_fenced' USING ERRCODE = '42501'; END IF;
                    CASE p_operation
                        WHEN 'notification' THEN
                            IF p_request->>'DeduplicationKey' IS DISTINCT FROM v_key
                            THEN RAISE EXCEPTION 'publication_fanout_deduplication_source' USING ERRCODE = '42501'; END IF;
                            SELECT {sql.Projection<Notification>("n", "Id")} INTO v_result FROM {sql.T<Notification>()} n
                                WHERE {notificationPredicate};
                        WHEN 'create_notification' THEN
                            {CreateNotificationSql(sql)}
                        WHEN 'preferences' THEN
                            {PreferencesSql(sql)}
                        WHEN 'push_subscriptions' THEN
                            SELECT COALESCE(pg_catalog.jsonb_agg(q.value),'[]'::jsonb) INTO v_result FROM (
                                SELECT {sql.Projection<WebPushSubscription>("w", "Id")} AS value
                                FROM {sql.T<WebPushSubscription>()} w
                                WHERE {sql.C<WebPushSubscription>("w", "TenantId")} = v_tenant
                                    AND {sql.C<WebPushSubscription>("w", "UserId")} = v_user
                                    AND {sql.C<WebPushSubscription>("w", "IsActive")}
                                    AND NOT {sql.C<WebPushSubscription>("w", "IsDeleted")}
                                ORDER BY {sql.C<WebPushSubscription>("w", "CreatedAt")}) q;
                        WHEN 'enqueue_push' THEN
                            {EnqueuePushSql(sql, notificationPredicate)}
                    END CASE;
                END IF;
            END IF;
            """;
    }

    private static string SubscriberSql(SourceSql sql) => $"""
        FROM {sql.T<ActorSubscription>()} s
        JOIN {sql.T<TenantUser>()} tu ON {sql.C<TenantUser>("tu", "Id")} = {sql.C<ActorSubscription>("s", "SubscriberTenantUserId")}
            AND {sql.C<TenantUser>("tu", "TenantId")} = v_tenant
        JOIN {sql.T<User>()} u ON {sql.C<User>("u", "Id")} = {sql.C<TenantUser>("tu", "UserId")}
        WHERE {sql.C<ActorSubscription>("s", "TenantId")} = v_tenant
            AND {sql.C<ActorSubscription>("s", "TargetActorId")} = v_actor
            AND {sql.C<ActorSubscription>("s", "SubscriberUserId")} = {sql.C<TenantUser>("tu", "UserId")}
            AND {sql.C<ActorSubscription>("s", "StatusId")} = {(int)ActorSubscriptionStatusEnum.Active}
            AND {sql.C<ActorSubscription>("s", "NotificationLevelId")} = {(int)ActorSubscriptionNotificationLevelEnum.All}
            AND NOT {sql.C<ActorSubscription>("s", "IsDeleted")}
            AND {sql.C<TenantUser>("tu", "StatusId")} = {(int)TenantUserStatusEnum.Active}
            AND NOT {sql.C<TenantUser>("tu", "IsDeleted")} AND NOT {sql.C<User>("u", "IsDeleted")}
            AND EXISTS (SELECT 1 FROM {sql.T<Actor>()} a WHERE {sql.C<Actor>("a", "Id")} = v_actor
                AND {sql.C<Actor>("a", "ActorTypeId")} = {sql.C<ActorSubscription>("s", "TargetActorTypeId")}
                AND NOT {sql.C<Actor>("a", "IsDeleted")} AND NOT {sql.C<Actor>("a", "IsSuspended")}
                AND ({ParticipationSql<OrganizationTenant>(sql, "OrganizationId")}
                    OR {ParticipationSql<GroupTenant>(sql, "GroupId")}
                    OR {FederatedDiscoverySql(sql)})
                {sql.Lock<Actor>()})
        """;

    private static string ParticipationSql<T>(SourceSql sql, string key) => $"""
        {sql.C<Actor>("a", key)} IS NOT NULL AND EXISTS (
            SELECT 1 FROM {sql.T<T>()} p WHERE {sql.C<T>("p", "TenantId")} = v_tenant
                AND {sql.C<T>("p", key)} = {sql.C<Actor>("a", key)}
                AND {sql.C<T>("p", "ApprovalStatusId")} = {(int)ApprovalStatusEnum.Approved}
                AND {sql.C<T>("p", "IsVisible")} AND NOT {sql.C<T>("p", "IsSuspended")}
                AND NOT {sql.C<T>("p", "IsDeleted")} {sql.Lock<T>()})
        """;

    private static string FederatedDiscoverySql(SourceSql sql) => $"""
        EXISTS (SELECT 1 FROM {sql.T<Event>()} f WHERE {sql.C<Event>("f", "TenantId")} = v_tenant
            AND {sql.C<Event>("f", "ActorId")} = v_actor AND {sql.C<Event>("f", "AtprotoRecordId")} IS NOT NULL
            AND NOT {sql.C<Event>("f", "IsDeleted")} AND {sql.C<Event>("f", "EventStatusId")} = {(int)EventStatusEnum.Published}
            AND {sql.C<Event>("f", "VisibilityTypeId")} = {(int)VisibilityTypeEnum.Public}
            AND CASE WHEN EXISTS (SELECT 1 FROM {sql.T<AtprotoOutboundRecordOwnership>()} o
                WHERE {sql.C<AtprotoOutboundRecordOwnership>("o", "TenantId")} = v_tenant
                    AND {sql.C<AtprotoOutboundRecordOwnership>("o", "AtprotoRecordId")} = {sql.C<Event>("f", "AtprotoRecordId")}
                    AND {sql.C<AtprotoOutboundRecordOwnership>("o", "SourceEntityType")} = 'Event'
                    AND {sql.C<AtprotoOutboundRecordOwnership>("o", "SourceEntityId")} = {sql.C<Event>("f", "Id")}
                    {sql.Lock<AtprotoOutboundRecordOwnership>()})
            THEN ({sql.C<Actor>("a", "ActorTypeId")} = {(int)ActorTypeEnum.Organization} AND ({ParticipationSql<OrganizationTenant>(sql, "OrganizationId")})
                OR {sql.C<Actor>("a", "ActorTypeId")} = {(int)ActorTypeEnum.Group} AND ({ParticipationSql<GroupTenant>(sql, "GroupId")}))
            ELSE EXISTS (SELECT 1 FROM {sql.T<AtprotoRecord>()} ar
                WHERE {sql.C<AtprotoRecord>("ar", "Id")} = {sql.C<Event>("f", "AtprotoRecordId")}
                    AND {sql.C<AtprotoRecord>("ar", "TombstonedAt")} IS NULL
                    AND EXISTS (SELECT 1 FROM {sql.T<AtprotoRecordTenantPresentation>()} pr
                        WHERE {sql.C<AtprotoRecordTenantPresentation>("pr", "TenantId")} = v_tenant
                            AND {sql.C<AtprotoRecordTenantPresentation>("pr", "AtprotoRecordId")} = {sql.C<AtprotoRecord>("ar", "Id")}
                            AND {sql.C<AtprotoRecordTenantPresentation>("pr", "IsVisible")}
                            AND {sql.C<AtprotoRecordTenantPresentation>("pr", "SourceVersion")} = {sql.C<AtprotoRecord>("ar", "SourceVersion")}
                            {sql.Lock<AtprotoRecordTenantPresentation>()})
                    AND EXISTS (SELECT 1 FROM {sql.T<AtprotoIdentity>()} ai
                        WHERE {sql.C<AtprotoIdentity>("ai", "ActorId")} = v_actor
                            AND {sql.C<AtprotoIdentity>("ai", "Did")} = {sql.C<AtprotoRecord>("ar", "Did")}
                            AND {sql.C<AtprotoIdentity>("ai", "IsActive")}
                            AND NOT {sql.C<AtprotoIdentity>("ai", "IsSuspended")}
                            AND NOT {sql.C<AtprotoIdentity>("ai", "IsDeleted")} {sql.Lock<AtprotoIdentity>()})
                    {sql.Lock<AtprotoRecord>()})
            END {sql.Lock<Event>()})
        """;

    private sealed class SourceSql(ExploreDbContext context)
    {
        internal HumanPrincipalSourceSql Names { get; } = new(context);
        internal Dictionary<Type, Dictionary<string, HashSet<string>>> Sources { get; } = [];
        internal string T<T>() => Names.Table(typeof(T));
        internal string C<T>(string alias, string property)
        {
            Register(typeof(T), property, "SELECT");
            return Names.Quote(alias) + "." + Names.Column(typeof(T), property);
        }
        internal string Write<T>(string privilege, params string[] properties) => string.Join(",", properties.Select(property =>
        {
            Register(typeof(T), property, privilege);
            return Names.Column(typeof(T), property);
        }));
        internal string Lock<T>(bool update = false, string? alias = null, bool append = false)
        {
            Register(typeof(T), context.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties[0].Name, "UPDATE");
            return append ? "," + Names.Quote(alias!)
                : (update ? "FOR UPDATE" : "FOR SHARE") + (alias is null ? "" : " OF " + Names.Quote(alias));
        }
        internal string Projection<T>(string alias, params string[] properties) =>
            "pg_catalog.jsonb_build_object(" + string.Join(",", properties.Select(property =>
                Text(property) + "," + C<T>(alias, property))) + ")";
        private void Register(Type type, string property, string privilege)
        {
            if (context.Model.FindEntityType(type)?.FindProperty(property) is null)
                throw new InvalidOperationException($"publication_fanout_unmapped_source:{type.Name}.{property}");
            if (!Sources.TryGetValue(type, out var fields)) Sources.Add(type, fields = []);
            if (!fields.TryGetValue(property, out var privileges)) fields.Add(property, privileges = []);
            privileges.Add(privilege);
        }
        internal IReadOnlyDictionary<Type, string> Policies(ExploreDbContext owner)
        {
            var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(owner.Model);
            return Sources.Keys.ToDictionary(type => type, type =>
            {
                var entity = owner.Model.FindEntityType(type)!;
                var tenant = tenantTables.SingleOrDefault(table => table.TableName == entity.GetTableName());
                if (tenant?.ParentJoin is not null || tenant?.AllowsNullTenant is true)
                    throw new InvalidOperationException("publication_fanout_source_tenancy_changed");
                return tenant is null ? "true"
                    : $"{Names.Quote(tenant.TenantIdColumn!)} = NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid";
            });
        }
    }
}
