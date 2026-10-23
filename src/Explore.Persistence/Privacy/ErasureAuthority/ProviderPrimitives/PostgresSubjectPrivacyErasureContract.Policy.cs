using System.Text;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Enums;

namespace Explore.Persistence.Privacy.ErasureAuthority;

public static partial class PostgresSubjectPrivacyErasureContract
{
    /// <summary>
    /// The fixed disposal policy, including soft-deleted ownership. Arrays are
    /// captured before unlinking; no caller can supply an object or tenant set.
    /// </summary>
    private static string PolicySql(SqlMap m)
    {
        var sql = new StringBuilder();
        string Subject<T>(string property = "UserId") => $"r.{m.Column<T>(property)} = p_subject";
        string Actor<T>(string property = "ActorId") => $"r.{m.Column<T>(property)} = ANY(v_actors)";
        void Delete<T>(string predicate) => sql.AppendLine(m.Delete<T>(predicate));
        void Update<T>(string predicate, params (string Property, string Value)[] changes) =>
            sql.AppendLine(m.Update<T>(predicate, changes));
        void Clear<T>(string property) => Update<T>(Subject<T>(property), (property, "NULL"));
        string Recipients<T>(string property = "NotificationIntentId") => $"""
            EXISTS (SELECT 1 FROM {m.Table<NotificationIntent>()} n
                WHERE n.{m.Column<NotificationIntent>("Id")} = r.{m.Column<T>(property)}
                  AND n.{m.Column<NotificationIntent>("RecipientUserId")} = p_subject)
            """;
        string Report<T>() => $"""
            EXISTS (SELECT 1 FROM {m.Table<EventReport>()} report
                WHERE report.{m.Column<EventReport>("Id")} = r.{m.Column<T>("ReportId")}
                  AND report.{m.Column<EventReport>("ReporterUserId")} = p_subject)
            """;
        string Consent<T>() => $"""
            EXISTS (SELECT 1 FROM {m.Table<EventContactShareConsent>()} consent
                WHERE consent.{m.Column<EventContactShareConsent>("Id")} = r.{m.Column<T>("ConsentId")}
                  AND consent.{m.Column<EventContactShareConsent>("UserSubjectId")} = p_subject)
            """;

        Delete<UserIdentityEmailEvidence>(Subject<UserIdentityEmailEvidence>());
        Delete<UserIdentityEmailClaim>(Subject<UserIdentityEmailClaim>());
        Delete<UserExternalLogin>(Subject<UserExternalLogin>());
        Delete<WebPushDispatchOutbox>(Subject<WebPushDispatchOutbox>());
        Delete<WebPushSubscription>(Subject<WebPushSubscription>());
        Delete<NotificationDelivery>(Recipients<NotificationDelivery>());
        Delete<EmailDispatchOutbox>(Subject<EmailDispatchOutbox>("RecipientUserId"));
        Delete<IntegrationSyncOutbox>(Subject<IntegrationSyncOutbox>());

        Update<WebhookLocalTargetSnapshot>($"""
            EXISTS (SELECT 1 FROM {m.Table<WebhookEndpoint>()} endpoint
                WHERE endpoint.{m.Column<WebhookEndpoint>("Id")} = r.{m.Column<WebhookLocalTargetSnapshot>("WebhookEndpointId")}
                  AND endpoint.{m.Column<WebhookEndpoint>("ConsumerId")} = ANY(v_consumers))
            """, ("DestinationUrl", "''"), ("CredentialReference", "''"), ("UpdatedAt", "p_now"));
        Update<WebhookEndpoint>($"r.{m.Column<WebhookEndpoint>("ConsumerId")} = ANY(v_consumers)",
            ("Url", "''"), ("Description", "NULL"), ("SecretRef", "''"), ("PreviousSecretRef", "NULL"),
            ("PreviousSecretValidUntil", "NULL"), ("ProviderEndpointId", "NULL"),
            ("StatusId", ((int)WebhookEndpointStatus.Archived).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("AutoPauseReason", "NULL"), ("LastResumedAt", "NULL"), ("LastResumedBy", "NULL"), ("UpdatedAt", "p_now"));
        Update<WebhookConsumer>($"r.{m.Column<WebhookConsumer>("Id")} = ANY(v_consumers)",
            ("OwnerUserId", "NULL"),
            ("ConsumerKindId", ((int)WebhookConsumerKind.Tenant).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Name", $"'Deleted user ' || replace(r.{m.Column<WebhookConsumer>("Id")}::text, '-', '')"),
            ("StatusId", ((int)WebhookConsumerStatus.Archived).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("ExternalProviderAppId", "NULL"),
            ("ProviderModeId", ((int)WebhookProviderMode.Local).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("ConfigurationVersion", $"r.{m.Column<WebhookConsumer>("ConfigurationVersion")} + 1"),
            ("UpdatedAt", "p_now"));
        // The original workflow clears provider metadata and subsequently removes
        // these links as retained-evidence remediation. The final native effect is deletion.
        Delete<EventReportExternalLink>(Report<EventReportExternalLink>());

        string resourceAudits = $"""
            EXISTS (SELECT 1 FROM {m.Table<EventResourceAuditEntry>()} audit
                WHERE audit.{m.Column<EventResourceAuditEntry>("ResponsibleManagerUserId")} = p_subject
                  AND audit.{m.Column<EventResourceAuditEntry>("TenantId")} = r.{m.Column<EventResource>("TenantId")}
                  AND audit.{m.Column<EventResourceAuditEntry>("EventResourceId")} = r.{m.Column<EventResource>("Id")})
            """;
        Update<EventResource>($"{Subject<EventResource>("CreatedBy")} OR {Subject<EventResource>("UpdatedBy")} OR {Subject<EventResource>("DeletedBy")} OR {resourceAudits}",
            ("CreatedBy", $"CASE WHEN {Subject<EventResource>("CreatedBy")} THEN NULL ELSE r.{m.Column<EventResource>("CreatedBy")} END"),
            ("UpdatedBy", $"CASE WHEN {Subject<EventResource>("UpdatedBy")} THEN NULL ELSE r.{m.Column<EventResource>("UpdatedBy")} END"),
            ("DeletedBy", $"CASE WHEN {Subject<EventResource>("DeletedBy")} THEN NULL ELSE r.{m.Column<EventResource>("DeletedBy")} END"),
            ("ConcurrencyStamp", "uuidv7()"));
        Clear<EventResourceAuditEntry>("ResponsibleManagerUserId");
        Delete<TenantInvitation>(Subject<TenantInvitation>("AcceptedByUserId"));
        Clear<TenantInvitation>("InvitedByUserId");
        Clear<EventContactShareExport>("ExportedByUserId");
        Clear<EventContactShareConsentHistory>("UserId");
        Clear<ConfigurationChangeLog>("UserId");
        Clear<EventOrganizerClaim>("ReviewerUserId");
        Clear<Event>("SubmittedByUserId");
        Update<OrganizationReview>(Subject<OrganizationReview>(), ("UserId", "NULL"), ("ReviewerName", "'Deleted user'"));
        Clear<SupportAccessAuditEvent>("ActorUserId");
        Update<SupportAccessAuditEvent>($"r.{m.Column<SupportAccessAuditEvent>("TargetTenantUserId")} = ANY(v_tenant_users)",
            ("TargetTenantUserId", "NULL"));
        Update<SupportAccessSession>(Subject<SupportAccessSession>("ActorUserId"),
            ("ActorUserId", "NULL"),
            ("StatusId", ((int)SupportAccessSessionStatusEnum.Revoked).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("EndReasonId", ((int)SupportAccessEndReasonEnum.RevokedByPolicy).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("EndedAtUtc", "p_now"));
        Clear<SupportAccessSession>("ApprovedByUserId");
        Update<SupportAccessSession>($"r.{m.Column<SupportAccessSession>("TargetTenantUserId")} = ANY(v_tenant_users)",
            ("TargetTenantUserId", "NULL"));
        Clear<TenantLifecycleLog>("TransitionedByUserId");
        Clear<TenantPlanApplicationLog>("AppliedByUserId");
        Clear<TenantPlanAssignment>("AssignedByUserId");
        Delete<EventReportEvidence>(Subject<EventReportEvidence>("CreatedByUserId"));
        Update<EventReport>(Subject<EventReport>("ReporterUserId"),
            ("ReporterUserId", "NULL"), ("ReporterActorId", "NULL"), ("ReporterLocale", "NULL"),
            ("ReporterIpHash", "NULL"), ("ReporterUserAgentHash", "NULL"),
            ("ReportCaseUpdatesConsent", "false"), ("ReportFollowUpContactConsent", "false"));
        Clear<EventModerationRecord>("ModeratorUserId");
        Clear<EventReportCase>("AssignedModeratorUserId");
        Clear<EventReportDecision>("ModeratorUserId");
        Update<AuditLog>(Subject<AuditLog>("ActorId"), ("ActorId", "NULL"), ("OldValues", "NULL"), ("NewValues", "NULL"));
        Update<EventRoleAssignment>($"{Subject<EventRoleAssignment>("RevokedByUserId")} AND r.{m.Column<EventRoleAssignment>("UserId")} <> p_subject",
            ("RevokedByUserId", "NULL"));
        Clear<EventLocation>("LastPolicyActorUserId");
        Clear<InstanceBootstrapState>("CompletedByUserId");
        Clear<ManagedTenantProvisioningOperation>("TenantAdministratorUserId");

        Update<NotificationFanoutRun>($"{Subject<NotificationFanoutRun>("CursorUserId")} OR r.{m.Column<NotificationFanoutRun>("CursorSubscriberTenantUserId")} = ANY(v_tenant_users)",
            ("CursorUserId", "NULL"), ("CursorSubscriberTenantUserId", "NULL"));
        Delete<NotificationExternalDelegation>(Recipients<NotificationExternalDelegation>());
        Delete<Notification>(Subject<Notification>());
        Delete<NotificationIntent>(Subject<NotificationIntent>("RecipientUserId"));
        Delete<EventContactShareExportItem>(Consent<EventContactShareExportItem>());
        Delete<EventContactShareConsentHistory>(Consent<EventContactShareConsentHistory>());
        Delete<EventContactShareConsent>(Subject<EventContactShareConsent>("UserSubjectId"));
        Delete<RegistrationParticipantPii>($"""
            EXISTS (SELECT 1 FROM {m.Table<RegistrationParticipant>()} participant
                WHERE participant.{m.Column<RegistrationParticipant>("Id")} = r.{m.Column<RegistrationParticipantPii>("RegistrationParticipantId")}
                  AND participant.{m.Column<RegistrationParticipant>("LinkedUserId")} = p_subject)
            """);
        Update<ParticipantAdmissionEligibility>(Subject<ParticipantAdmissionEligibility>("SubjectUserId"),
            ("SubjectUserId", "NULL"), ("RequirementsCompletedAt", "NULL"),
            ("SubjectConsentRecordId", "NULL"), ("SubjectConsentGrantedAt", "NULL"));
        Clear<RegistrationParticipant>("LinkedUserId");
        Delete<EventRegistration>(Subject<EventRegistration>("LinkedUserId"));
        Update<IdempotencyRecord>($"""
            r.{m.Column<IdempotencyRecord>("UserId")} = p_subject::text
            OR r.{m.Column<IdempotencyRecord>("UserId")} = replace(p_subject::text,'-','')
            """, ("StatusCode", "-1"), ("UserId", "NULL"),
            ("RequestMethod", "''"), ("RequestTarget", "''"), ("RequestContentType", "NULL"),
            ("RequestBodyHash", "''"), ("PrincipalFingerprint", "''"),
            ("ResponseBody", "NULL"), ("ContentType", "NULL"));

        Delete<ActorKeyStore>(Actor<ActorKeyStore>());
        Delete<AiConsentGrant>(Subject<AiConsentGrant>("SubjectUserId"));
        Delete<ExternalApiKey>($"{Subject<ExternalApiKey>("OwnerId")} AND r.{m.Column<ExternalApiKey>("ExternalApiKeyOwnerTypeId")} = {(int)ExternalApiKeyOwnerType.User}");
        Delete<UserAppearancePreference>(Subject<UserAppearancePreference>());
        Delete<UserAppearanceProfile>(Subject<UserAppearanceProfile>());
        Delete<NotificationChannelPreference>(Subject<NotificationChannelPreference>());
        Delete<NotificationPreferenceProfile>(Subject<NotificationPreferenceProfile>());
        Delete<UserNotificationPreference>(Subject<UserNotificationPreference>());
        Delete<UserPreference>(Subject<UserPreference>());
        Delete<EventRoleAssignment>(Subject<EventRoleAssignment>());
        Delete<ActorSubscription>(Subject<ActorSubscription>("SubscriberUserId"));
        Delete<GroupMember>(Subject<GroupMember>());
        Delete<OrganizationMember>(Subject<OrganizationMember>());
        Delete<TenantUser>(Subject<TenantUser>());
        Delete<PlatformUserRole>(Subject<PlatformUserRole>());
        // The same verified physical FK cascade used by AiConversationRepository.
        Delete<AiConversation>(Subject<AiConversation>());
        return sql.ToString();
    }
}
