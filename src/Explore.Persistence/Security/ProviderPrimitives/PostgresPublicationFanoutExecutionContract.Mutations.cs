using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Persistence.Security;

public static partial class PostgresPublicationFanoutExecutionContract
{
    private static string UpdateRunSql(SourceSql sql, string predicate) => $"""
        IF v_run IS NULL OR (p_request->>'Id')::uuid IS DISTINCT FROM (v_run->>'Id')::uuid
            OR (p_request->>'ConcurrencyStamp')::uuid IS DISTINCT FROM (v_run->>'ConcurrencyStamp')::uuid
        THEN RAISE EXCEPTION 'publication_fanout_run_changed' USING ERRCODE = '40001'; END IF;
        IF p_request->>'Status' IS NULL OR p_request->>'Status' NOT IN ('processing','completed','failed')
            OR v_run->>'Status' = 'completed'
            OR (p_request->>'ProcessedCount')::integer IS NULL
            OR (p_request->>'ProcessedCount')::integer < (v_run->>'ProcessedCount')::integer
            OR (p_request->>'ProcessedCount')::integer > (v_run->>'ProcessedCount')::integer + 250
            OR (p_request->>'CreatedNotificationCount')::integer IS NULL
            OR (p_request->>'CreatedNotificationCount')::integer < (v_run->>'CreatedNotificationCount')::integer
            OR (p_request->>'CreatedNotificationCount')::integer > (p_request->>'ProcessedCount')::integer
            OR (p_request->>'CreatedNotificationCount')::integer - (v_run->>'CreatedNotificationCount')::integer
                > (p_request->>'ProcessedCount')::integer - (v_run->>'ProcessedCount')::integer
            OR (v_run->>'CursorSubscriberTenantUserId')::uuid IS NOT NULL
                AND ((p_request->>'CursorSubscriberTenantUserId')::uuid IS NULL
                    OR (p_request->>'CursorSubscriberTenantUserId')::uuid < (v_run->>'CursorSubscriberTenantUserId')::uuid)
            OR (p_request->>'ProcessedCount')::integer > (v_run->>'ProcessedCount')::integer
                AND ((p_request->>'CursorSubscriberTenantUserId')::uuid IS NULL
                    OR (p_request->>'CursorSubscriberTenantUserId')::uuid IS NOT DISTINCT FROM (v_run->>'CursorSubscriberTenantUserId')::uuid)
        THEN RAISE EXCEPTION 'publication_fanout_run_transition' USING ERRCODE = '22023'; END IF;
        IF (p_request->>'CursorSubscriberTenantUserId')::uuid IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM {sql.T<ActorSubscription>()} s
            WHERE {sql.C<ActorSubscription>("s", "TenantId")} = v_tenant
                AND {sql.C<ActorSubscription>("s", "TargetActorId")} = v_actor
                AND {sql.C<ActorSubscription>("s", "SubscriberTenantUserId")} = (p_request->>'CursorSubscriberTenantUserId')::uuid)
        THEN RAISE EXCEPTION 'publication_fanout_cursor_source' USING ERRCODE = '42501'; END IF;
        UPDATE {sql.T<NotificationFanoutRun>()} r SET
            {sql.Write<NotificationFanoutRun>("UPDATE", "Status")} = p_request->>'Status',
            {sql.Write<NotificationFanoutRun>("UPDATE", "CursorSubscriberTenantUserId")} = (p_request->>'CursorSubscriberTenantUserId')::uuid,
            {sql.Write<NotificationFanoutRun>("UPDATE", "ProcessedCount")} = (p_request->>'ProcessedCount')::integer,
            {sql.Write<NotificationFanoutRun>("UPDATE", "CreatedNotificationCount")} = (p_request->>'CreatedNotificationCount')::integer,
            {sql.Write<NotificationFanoutRun>("UPDATE", "StartedAt")} = COALESCE({sql.C<NotificationFanoutRun>("r", "StartedAt")},pg_catalog.clock_timestamp()),
            {sql.Write<NotificationFanoutRun>("UPDATE", "CompletedAt")} = CASE WHEN p_request->>'Status' = 'completed' THEN pg_catalog.clock_timestamp() ELSE NULL END,
            {sql.Write<NotificationFanoutRun>("UPDATE", "FailedAt")} = CASE WHEN p_request->>'Status' = 'failed' THEN pg_catalog.clock_timestamp() ELSE NULL END,
            {sql.Write<NotificationFanoutRun>("UPDATE", "LastError")} = CASE WHEN p_request->>'Status' = 'failed' THEN 'event_publication_fanout_failed' ELSE NULL END,
            {sql.Write<NotificationFanoutRun>("UPDATE", "UpdatedAt")} = pg_catalog.clock_timestamp(),
            {sql.Write<NotificationFanoutRun>("UPDATE", "ConcurrencyStamp")} = pg_catalog.uuidv7()
            WHERE {predicate} AND {sql.C<NotificationFanoutRun>("r", "Id")} = (p_request->>'Id')::uuid;
        """;

    private static string CreateNotificationSql(SourceSql sql) => $"""
        IF p_request->>'DeduplicationKey' IS DISTINCT FROM v_key
            OR p_request->>'Title' IS DISTINCT FROM 'New event: ' || v_title
            OR p_request->>'Body' IS DISTINCT FROM 'An organization or group you follow published a new event.'
            OR (p_request->>'NotificationTypeId')::integer IS DISTINCT FROM {(int)NotificationTypeEnum.EventCreated}
            OR (p_request->>'NotificationEntityTypeId')::integer IS DISTINCT FROM {(int)NotificationEntityTypeEnum.Event}
            OR (p_request->>'NotificationReasonId')::integer IS DISTINCT FROM {(int)NotificationReasonEnum.Subscription}
            OR (p_request->>'NotificationScopeId')::integer IS DISTINCT FROM (v_subscription->>'TargetActorTypeId')::integer
            OR (p_request->>'RecipientContextActorId')::uuid IS DISTINCT FROM v_actor
            OR (p_request->>'SourceActorId')::uuid IS DISTINCT FROM v_actor
            OR (p_request->>'EntityId')::uuid IS DISTINCT FROM v_event
            OR (p_request->>'Id')::uuid IS NULL
        THEN RAISE EXCEPTION 'publication_fanout_notification_source' USING ERRCODE = '42501'; END IF;
        v_channel := {(int)NotificationPreferenceChannelEnum.InApp};
        {RequirePreferenceSql(sql)}
        INSERT INTO {sql.T<Notification>()}
            ({sql.Write<Notification>("INSERT", "Id", "TenantId", "UserId", "NotificationTypeId", "Title", "Body",
                "DeduplicationKey", "NotificationEntityTypeId", "EntityId", "NotificationScopeId",
                "SourceActorId", "RecipientContextActorId", "NotificationReasonId", "CreatedAt",
                "IsRead", "IsArchived", "IsDeleted")})
        VALUES ((p_request->>'Id')::uuid,v_tenant,v_user,{(int)NotificationTypeEnum.EventCreated},
            'New event: ' || v_title,'An organization or group you follow published a new event.',v_key,
            {(int)NotificationEntityTypeEnum.Event},v_event::text,(v_subscription->>'TargetActorTypeId')::integer,
            v_actor,v_actor,{(int)NotificationReasonEnum.Subscription},pg_catalog.clock_timestamp(),false,false,false);
        v_result := pg_catalog.jsonb_build_object('Id',(p_request->>'Id')::uuid);
        """;

    private static string EnqueuePushSql(SourceSql sql, string notificationPredicate) => $"""
        IF (p_request->>'CategoryId')::integer IS DISTINCT FROM {(int)NotificationPreferenceCategoryEnum.EventUpdates}
            OR (p_request->>'Status')::integer IS DISTINCT FROM {(int)WebPushDispatchStatus.Pending}
            OR p_request->>'PayloadJson' IS DISTINCT FROM {Text("{\"kind\":\"event-published\"}")}
            OR (p_request->>'Id')::uuid IS NULL
        THEN RAISE EXCEPTION 'publication_fanout_push_shape' USING ERRCODE = '42501'; END IF;
        PERFORM {sql.C<Notification>("n", "Id")} FROM {sql.T<Notification>()} n
            WHERE {notificationPredicate} AND {sql.C<Notification>("n", "Id")} = (p_request->>'NotificationId')::uuid
            {sql.Lock<Notification>()};
        IF NOT FOUND THEN RAISE EXCEPTION 'publication_fanout_push_notification' USING ERRCODE = '42501'; END IF;
        PERFORM {sql.C<WebPushSubscription>("w", "Id")} FROM {sql.T<WebPushSubscription>()} w
            WHERE {sql.C<WebPushSubscription>("w", "Id")} = (p_request->>'SubscriptionId')::uuid
                AND {sql.C<WebPushSubscription>("w", "TenantId")} = v_tenant
                AND {sql.C<WebPushSubscription>("w", "UserId")} = v_user
                AND {sql.C<WebPushSubscription>("w", "IsActive")} AND NOT {sql.C<WebPushSubscription>("w", "IsDeleted")}
            {sql.Lock<WebPushSubscription>()};
        IF NOT FOUND THEN RAISE EXCEPTION 'publication_fanout_push_subscription' USING ERRCODE = '42501'; END IF;
        v_channel := {(int)NotificationPreferenceChannelEnum.Push};
        {RequirePreferenceSql(sql)}
        IF EXISTS (SELECT 1 FROM {sql.T<WebPushDispatchOutbox>()} d
            WHERE {sql.C<WebPushDispatchOutbox>("d", "TenantId")} = v_tenant
                AND {sql.C<WebPushDispatchOutbox>("d", "NotificationId")} = (p_request->>'NotificationId')::uuid
                AND {sql.C<WebPushDispatchOutbox>("d", "SubscriptionId")} = (p_request->>'SubscriptionId')::uuid
                AND NOT {sql.C<WebPushDispatchOutbox>("d", "IsDeleted")})
        THEN v_result := 'false'::jsonb;
        ELSE
            INSERT INTO {sql.T<WebPushDispatchOutbox>()}
                ({sql.Write<WebPushDispatchOutbox>("INSERT", "Id", "TenantId", "NotificationId", "CategoryId", "SubscriptionId",
                    "UserId", "PayloadJson", "Status", "CreatedAt", "AttemptCount", "MaxAttempts", "IsDeleted")})
            VALUES ((p_request->>'Id')::uuid,v_tenant,(p_request->>'NotificationId')::uuid,
                {(int)NotificationPreferenceCategoryEnum.EventUpdates},(p_request->>'SubscriptionId')::uuid,
                v_user,{Text("{\"kind\":\"event-published\"}")}::jsonb,{(int)WebPushDispatchStatus.Pending},pg_catalog.clock_timestamp(),0,5,false)
            ON CONFLICT ({sql.Write<WebPushDispatchOutbox>("SELECT", "TenantId", "NotificationId", "SubscriptionId")})
                WHERE NOT {sql.Write<WebPushDispatchOutbox>("SELECT", "IsDeleted")} DO NOTHING;
            GET DIAGNOSTICS v_rows = ROW_COUNT;
            v_result := pg_catalog.to_jsonb(v_rows = 1);
        END IF;
        """;

    private static string PreferenceScopeSql<T>(SourceSql sql, string alias) => $"""
        {sql.C<T>(alias, "TenantId")} = v_tenant AND NOT {sql.C<T>(alias, "IsDeleted")}
        AND ({sql.C<T>(alias, "ScopeId")} IN ({(int)ConfigurationScopeEnum.System},{(int)ConfigurationScopeEnum.Instance},{(int)ConfigurationScopeEnum.Tenant})
            OR {sql.C<T>(alias, "ScopeId")} = {(int)ConfigurationScopeEnum.User} AND {sql.C<T>(alias, "UserId")} = v_user)
        """;

    private static string PreferencesSql(SourceSql sql) => $"""
        SELECT pg_catalog.jsonb_build_object(
            'Categories',(SELECT COALESCE(pg_catalog.jsonb_agg({sql.Projection<NotificationPreferenceCategory>("c",
                "Id", "MasterCode", "IsRequired", "DefaultInAppEnabled", "DefaultPushEnabled")}), '[]'::jsonb)
                FROM {sql.T<NotificationPreferenceCategory>()} c
                WHERE {sql.C<NotificationPreferenceCategory>("c", "Id")} = {(int)NotificationPreferenceCategoryEnum.EventUpdates}
                    AND {sql.C<NotificationPreferenceCategory>("c", "MasterCode")} = {Text(NotificationPreferenceCategoryCodes.EventUpdates)}),
            'Channels',(SELECT COALESCE(pg_catalog.jsonb_agg({sql.Projection<NotificationPreferenceChannel>("c", "Id", "MasterCode")}), '[]'::jsonb)
                FROM {sql.T<NotificationPreferenceChannel>()} c
                WHERE ({sql.C<NotificationPreferenceChannel>("c", "Id")} = {(int)NotificationPreferenceChannelEnum.InApp}
                    AND {sql.C<NotificationPreferenceChannel>("c", "MasterCode")} = {Text(NotificationPreferenceChannelCodes.InApp)})
                    OR ({sql.C<NotificationPreferenceChannel>("c", "Id")} = {(int)NotificationPreferenceChannelEnum.Push}
                    AND {sql.C<NotificationPreferenceChannel>("c", "MasterCode")} = {Text(NotificationPreferenceChannelCodes.Push)})),
            'Preferences',(SELECT COALESCE(pg_catalog.jsonb_agg({sql.Projection<NotificationChannelPreference>("p",
                "ScopeId", "UserId", "CategoryId", "ChannelId", "IsEnabled", "IsLocked")}), '[]'::jsonb)
                FROM {sql.T<NotificationChannelPreference>()} p WHERE {PreferenceScopeSql<NotificationChannelPreference>(sql, "p")}
                    AND {sql.C<NotificationChannelPreference>("p", "CategoryId")} = {(int)NotificationPreferenceCategoryEnum.EventUpdates}
                    AND {sql.C<NotificationChannelPreference>("p", "ChannelId")} IN
                        ({(int)NotificationPreferenceChannelEnum.InApp},{(int)NotificationPreferenceChannelEnum.Push})),
            'Profiles',(SELECT COALESCE(pg_catalog.jsonb_agg({sql.Projection<NotificationPreferenceProfile>("p",
                "ScopeId", "UserId", "IsMuted", "IsLocked")}), '[]'::jsonb)
                FROM {sql.T<NotificationPreferenceProfile>()} p WHERE {PreferenceScopeSql<NotificationPreferenceProfile>(sql, "p")}))
            INTO v_result;
        """;

    // The resolver keeps the decision algorithm. This repeats its fixed publication hierarchy at
    // the write boundary so an enabled result or a stale caller assertion cannot authorize a write.
    private static string RequirePreferenceSql(SourceSql sql) => $"""
        SELECT {sql.C<NotificationPreferenceCategory>("c", "IsRequired")},
            CASE WHEN v_channel = {(int)NotificationPreferenceChannelEnum.InApp}
                THEN {sql.C<NotificationPreferenceCategory>("c", "DefaultInAppEnabled")}
                ELSE {sql.C<NotificationPreferenceCategory>("c", "DefaultPushEnabled")} END
            INTO v_required,v_enabled FROM {sql.T<NotificationPreferenceCategory>()} c
            WHERE {sql.C<NotificationPreferenceCategory>("c", "Id")} = {(int)NotificationPreferenceCategoryEnum.EventUpdates}
                AND {sql.C<NotificationPreferenceCategory>("c", "MasterCode")} = {Text(NotificationPreferenceCategoryCodes.EventUpdates)}
            {sql.Lock<NotificationPreferenceCategory>()};
        IF NOT FOUND THEN RAISE EXCEPTION 'publication_fanout_category_unavailable' USING ERRCODE = '42501'; END IF;
        PERFORM {sql.C<NotificationPreferenceChannel>("c", "Id")} FROM {sql.T<NotificationPreferenceChannel>()} c
            WHERE {sql.C<NotificationPreferenceChannel>("c", "Id")} = v_channel
                AND {sql.C<NotificationPreferenceChannel>("c", "MasterCode")} = CASE WHEN v_channel = {(int)NotificationPreferenceChannelEnum.InApp}
                    THEN {Text(NotificationPreferenceChannelCodes.InApp)} ELSE {Text(NotificationPreferenceChannelCodes.Push)} END
            {sql.Lock<NotificationPreferenceChannel>()};
        IF NOT FOUND THEN RAISE EXCEPTION 'publication_fanout_channel_unavailable' USING ERRCODE = '42501'; END IF;
        FOR v_preference IN SELECT {sql.C<NotificationChannelPreference>("p", "IsEnabled")} AS enabled,
            {sql.C<NotificationChannelPreference>("p", "IsLocked")} AS locked
            FROM {sql.T<NotificationChannelPreference>()} p WHERE {PreferenceScopeSql<NotificationChannelPreference>(sql, "p")}
                AND {sql.C<NotificationChannelPreference>("p", "CategoryId")} = {(int)NotificationPreferenceCategoryEnum.EventUpdates}
                AND {sql.C<NotificationChannelPreference>("p", "ChannelId")} = v_channel
            ORDER BY {sql.C<NotificationChannelPreference>("p", "ScopeId")} {sql.Lock<NotificationChannelPreference>()}
        LOOP
            v_enabled := v_preference.enabled;
            EXIT WHEN v_preference.locked;
        END LOOP;
        v_muted := false;
        FOR v_preference IN SELECT {sql.C<NotificationPreferenceProfile>("p", "IsMuted")} AS muted,
            {sql.C<NotificationPreferenceProfile>("p", "IsLocked")} AS locked
            FROM {sql.T<NotificationPreferenceProfile>()} p WHERE {PreferenceScopeSql<NotificationPreferenceProfile>(sql, "p")}
            ORDER BY {sql.C<NotificationPreferenceProfile>("p", "ScopeId")} {sql.Lock<NotificationPreferenceProfile>()}
        LOOP
            v_muted := v_preference.muted;
            EXIT WHEN v_preference.locked;
        END LOOP;
        IF NOT (v_required OR v_enabled AND NOT v_muted)
        THEN RAISE EXCEPTION 'publication_fanout_preference_disabled' USING ERRCODE = '42501'; END IF;
        """;
}
