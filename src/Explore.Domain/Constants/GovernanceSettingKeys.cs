namespace Explore.Domain.Constants;

public static class GovernanceSettingKeys
{
    public static class Deployment
    {
        public const string Mode = "deployment.mode";
    }

    public static class Tenants
    {
        public const string SelfServiceRegistration = "tenants.self_service_registration";
        public const string WhiteLabelingEnabled = "tenants.white_labeling_enabled";
    }

    public static class AdminPortal
    {
        public const string Enabled = "admin_portal.enabled";
        public const string PublicUrl = "admin_portal.public_url";
        public const string AllowTenantAdminAccess = "admin_portal.allow_tenant_admin_access";
    }

    public static class Routing
    {
        public const string DefaultPublicHomePage = "routing.default_public_home_page";
        public const string ResolverHeaderEnabled = "routing.resolver_header_enabled";
        public const string ResolverSubdomainEnabled = "routing.resolver_subdomain_enabled";
        public const string ResolverCustomDomainEnabled = "routing.resolver_custom_domain_enabled";
        public const string ResolverPathEnabled = "routing.resolver_path_enabled";
        public const string PathPrefix = "routing.path_prefix";

        public static class RenderPolicy
        {
            private const string Base = "routing.render_policy";

            public const string Version = Base + ".version";
            public const string Preset = Base + ".preset";
            public const string AdvancedEnabled = Base + ".advanced_enabled";
            public const string DisallowInteractiveServerOnOnboarding = Base + ".onboarding.disallow_interactive_server";
            public const string AllowTenantOverride = Base + ".allow_tenant_override";
            public const string LockTenantPublicSeo = Base + ".lock_tenant_public_seo";
            public const string LockTenantOperational = Base + ".lock_tenant_operational";
            public const string LockTenantAdmin = Base + ".lock_tenant_admin";

            public static class Fallback
            {
                private const string SubBase = Base + ".global";

                public const string RenderMode = SubBase + ".render_mode";
                public const string PrerenderEnabled = SubBase + ".prerender_enabled";
            }

            public static class PublicSeo
            {
                private const string SubBase = Base + ".public_seo";

                public const string RenderMode = SubBase + ".render_mode";
                public const string PrerenderEnabled = SubBase + ".prerender_enabled";
            }

            public static class Operational
            {
                private const string SubBase = Base + ".operational";

                public const string RenderMode = SubBase + ".render_mode";
                public const string PrerenderEnabled = SubBase + ".prerender_enabled";
            }

            public static class Admin
            {
                private const string SubBase = Base + ".admin";

                public const string RenderMode = SubBase + ".render_mode";
                public const string PrerenderEnabled = SubBase + ".prerender_enabled";
            }

            public static class Onboarding
            {
                private const string SubBase = Base + ".onboarding";

                public const string RenderMode = SubBase + ".render_mode";
                public const string PrerenderEnabled = SubBase + ".prerender_enabled";
            }
        }
    }

    public static class Events
    {
        public const string UserSubmissionEnabled = "events.user_submission_enabled";
        public const string OrganizationSubmissionEnabled = "events.organization_submission_enabled";
        public const string GroupSubmissionEnabled = "events.group_submission_enabled";
        public const string RequireApproval = "events.require_approval";
        public const string CardClickOpensDetailPage = "events.card_click_opens_detail_page";
    }

    public static class EventReporting
    {
        public const string IntakeEnabled = "event_reporting.intake_enabled";
    }

    public static class Organizations
    {
        public const string VerificationRequired = "organizations.verification_required";
        public const string TenantCanOmitVerification = "organizations.tenant_can_omit_verification";
        public const string SelfRegistrationEnabled = "organizations.self_registration_enabled";
    }

    public static class Groups
    {
        public const string SelfRegistrationEnabled = "groups.self_registration_enabled";
    }

    public static class Modules
    {
        public const string IslamicEnabled = "modules.islamic_enabled";
        public const string TechEnabled = "modules.tech_enabled";
    }

    public static class Branding
    {
        public const string DisplayName = "branding.display_name";
        public const string SupportEmail = "branding.support_email";
        public const string LogoUrl = "branding.logo_url";
        public const string FaviconUrl = "branding.favicon_url";
        public const string CustomCssUrl = "branding.custom_css_url";
    }

    public static class Appearance
    {
        public const string ActiveProfileId = "appearance.active_profile_id";
        public const string DefaultPresetId = "appearance.default_preset_id";
        public const string DefaultThemeMode = "appearance.default_theme_mode";
        public const string ThemeMode = "appearance.theme_mode";
        public const string Direction = "appearance.direction";
        public const string Language = "appearance.language";

        [System.Obsolete("Use ActiveProfileId for user scope or DefaultPresetId for tenant/instance scope.")]
        public const string LegacyDefaultThemeId = "appearance.default_theme_id";
    }

    public static class Domains
    {
        public const string InstanceBaseDomain = "domains.instance_base_domain";
        public const string AdminHost = "domains.admin_host";
        public const string AllowTenantCustomDomain = "domains.allow_tenant_custom_domain";
        public const string TenantSubdomain = "domains.tenant_subdomain";
        public const string TenantCustomDomain = "domains.tenant_custom_domain";
    }

    public static class Email
    {
        public const string DeliveryEnabled = "email.delivery_enabled";
        public const string SmtpHost = "email.smtp_host";
        public const string SmtpPort = "email.smtp_port";
        public const string SmtpSecurity = "email.smtp_security";
        public const string FromAddress = "email.from_address";
        public const string FromName = "email.from_name";
        public const string SmtpTimeoutSeconds = "email.smtp_timeout_seconds";
        public const string SmtpSkipCertValidation = "email.smtp_skip_cert_validation";
    }

    public static class WebhookDelivery
    {
        private const string Base = "webhook.delivery";

        public const string MaxConcurrentDeliveries = Base + ".max_concurrent_deliveries";
        public const string MaxConcurrentDeliveriesPerTenant = Base + ".max_concurrent_deliveries_per_tenant";
        public const string MaxConcurrentDeliveriesPerEndpoint = Base + ".max_concurrent_deliveries_per_endpoint";
        public const string MaxItemsPerTenantPerClaimCycle = Base + ".max_items_per_tenant_per_claim_cycle";
        public const string MaxAttempts = Base + ".max_attempts";
        public const string EndpointTimeoutSeconds = Base + ".endpoint_timeout_seconds";
        public const string AutoPauseThreshold = Base + ".auto_pause_threshold";
    }

    public static class Integrations
    {
        public static class Listmonk
        {
            public const string Enabled = "integrations.listmonk.enabled";
            public const string InstanceUrl = "integrations.listmonk.instance_url";
            public const string DefaultListId = "integrations.listmonk.default_list_id";
            public const string PreconfirmSubscriptions = "integrations.listmonk.preconfirm_subscriptions";
            public const string SyncOnRegistration = "integrations.listmonk.sync_on_registration";
        }
    }

    public static class Storage
    {
        public const string Provider = "storage.provider";
        public const string DefaultMaxUploadBytes = "storage.default_max_upload_bytes";
        public const string DefaultTenantQuotaBytes = "storage.default_tenant_quota_bytes";
        public const string InstanceMaxUploadBytes = "storage.instance_max_upload_bytes";
        public const string RouteMatrix = "storage.route_matrix";
        public const string Endpoint = "s3.endpoint";
        public const string PublicEndpoint = "s3.public_endpoint";
        public const string BucketName = "s3.bucket_name";
        public const string Region = "s3.region";
        public const string ForcePathStyle = "s3.force_path_style";
        public const string UploadUrlExpirationMinutes = "s3.upload_url_expiration_minutes";
    }

    public static class Security
    {
        public const string AuthorizationProvider = "authorization.provider";
        public const string RequireHttpsExternalUrls = "security.require_https_external_urls";
    }

    public static class SupportAccess
    {
        public const string Enabled = "support_access.enabled";
        public const string MaxReadOnlyMinutes = "support_access.max_read_only_minutes";
        public const string MaxWriteMinutes = "support_access.max_write_minutes";
        public const string AllowWriteMode = "support_access.allow_write_mode";
        public const string RequireTicketReference = "support_access.require_ticket_reference";
        public const string OneActiveSessionPerActor = "support_access.one_active_session_per_actor";
    }

    public static class Cerbos
    {
        public const string GrpcEndpoint = "cerbos.grpc_endpoint";
        public const string TenantCustomizationEnabled = "cerbos.tenant_customization_enabled";
        public const string Mode = "cerbos.mode";
        public const string CustomEndpoint = "cerbos.custom_endpoint";
        public const string CustomAdminEndpoint = "cerbos.custom_admin_endpoint";
    }

    public static class Authentication
    {
        public const string PrimaryProviderId = "auth.primary_provider_id";
        public const string KeycloakAuthority = "auth.keycloak_authority";
        public const string KeycloakClientId = "auth.keycloak_client_id";
        public const string KeycloakPublicOnboardingPolicy = "auth.keycloak_public_onboarding_policy";
        public const string KeycloakPublicSignupUrl = "auth.keycloak_public_signup_url";
        public const string AtprotoLoginEnabled = "auth.atproto_login_enabled";
        public const string AtprotoPublicUrl = "auth.atproto_public_url";
        public const string GoogleSsoEnabled = "auth.google_sso_enabled";
        public const string GoogleClientId = "auth.google_client_id";
        public const string GooglePublicOnboardingPolicy = "auth.google_public_onboarding_policy";
        public const string GooglePublicSignupUrl = "auth.google_public_signup_url";
    }

    public static class Federation
    {
        public const string AtprotoEventsEnabled = "federation.atproto_events_enabled";
        public const string AtprotoEventValidationProfile = "federation.atproto_event_validation_profile";
        public const string AtprotoEventsBackfillEnabled = "federation.atproto_events_backfill_enabled";
        public const string AtprotoEventsBackfillMode = "federation.atproto_events_backfill_mode";
        public const string AtprotoPublishMyEvents = "federation.atproto_publish_my_events";
    }

    public static class LocationPrivacy
    {
        private const string Base = "location_privacy";

        public const string AllowHomeLocations = Base + ".allow_home_locations";
        public const string AllowPublicExactAddress = Base + ".allow_public_exact_address";
        public const string AllowPublicCoordinates = Base + ".allow_public_coordinates";
        public const string MinimumHomeAudience = Base + ".minimum_home_audience";
        public const string DefaultRevealOffset = Base + ".default_reveal_offset";
    }

    public static class AddressGovernance
    {
        private const string Base = "address_governance";

        public const string CreationMode = Base + ".creation_mode";
        public const string OrganizationCreationGrant = Base + ".organization_creation_grant";
    }

    public static class Reporting
    {
        public const string TenantExternalSyncEnabled = "reporting.tenant_external_sync_enabled";
        public const string EnableTenantOspreyProvider = "reporting.enable_tenant_osprey_provider";
        public const string EnableTenantCoopProvider = "reporting.enable_tenant_coop_provider";
        public const string OspreyRoutingMode = "reporting.osprey_routing_mode";
        public const string CoopRoutingMode = "reporting.coop_routing_mode";
        public const string EvidenceMode = "reporting.evidence_mode";
        public const string OspreyEndpointUrl = "reporting.osprey_endpoint_url";
        public const string CoopEndpointUrl = "reporting.coop_endpoint_url";
    }

    public static class Analytics
    {
        public const string Provider = "analytics.provider";
        public const string Enabled = "analytics.enabled";
        public const string ConsentMode = "analytics.consent_mode";
        public const string TransportMode = "analytics.transport_mode";
        public const string ApiKey = "analytics.api_key";
        public const string EndpointUrl = "analytics.endpoint_url";

        // Cookie consent & storage governance
        public const string CookieConsentEnabled = "analytics.cookie_consent_enabled";
        public const string DeclineBehavior = "analytics.decline_behavior";
        public const string ConsentCookieLifetimeDays = "analytics.consent_cookie_lifetime_days";
        public const string GlobalDisableClientTracking = "analytics.global_disable_client_tracking";

        // PostHog privacy & feature controls
        public const string PosthogCookielessMode = "analytics.posthog_cookieless_mode";
        public const string PosthogPersonProfiles = "analytics.posthog_person_profiles";
        public const string PosthogSessionReplay = "analytics.posthog_session_replay";
        public const string PosthogAutocapture = "analytics.posthog_autocapture";
        public const string PosthogHeatmaps = "analytics.posthog_heatmaps";
        public const string PosthogToolbar = "analytics.posthog_toolbar";
    }

    public static class Mcp
    {
        public const string Enabled = "mcp.enabled";
        public const string EnableLegacySse = "mcp.enable_legacy_sse";
    }

    public static class AiAssistant
    {
        public const string Enabled = "ai_assistant.enabled";
        public const string Provider = "ai_assistant.provider";
        public const string EndpointUrl = "ai_assistant.endpoint_url";
        public const string ApiKey = "ai_assistant.api_key";
        public const string ModelId = "ai_assistant.model_id";
        public const string AllowedModelIds = "ai_assistant.allowed_model_ids";
        public const string MaxInputTokens = "ai_assistant.max_input_tokens";
        public const string MaxOutputTokens = "ai_assistant.max_output_tokens";
        public const string Temperature = "ai_assistant.temperature";
        public const string TimeoutSeconds = "ai_assistant.timeout_seconds";
        public const string RetentionDays = "ai_assistant.retention_days";
        public const string DailyMessageLimit = "ai_assistant.daily_message_limit";
        public const string DailyTenantMessageLimit = "ai_assistant.daily_tenant_message_limit";
        public const string ConcurrentRunLimit = "ai_assistant.concurrent_run_limit";
        public const string SelectedReferenceLimit = "ai_assistant.selected_reference_limit";
        public const string ToolProposalsEnabled = "ai_assistant.tool_proposals_enabled";
        public const string StreamingEnabled = "ai_assistant.streaming_enabled";
        public const string AllowAnonymousAccess = "ai_assistant.allow_anonymous_access";
        public const string MaxAiContextSensitivity = "ai_assistant.max_ai_context_sensitivity";
        public const string PiiDisclosureEnabled = "ai_assistant.pii_disclosure_enabled";
    }

    public static class AiAssistantPreferences
    {
        public const string ShowNavbarButton = "ai_assistant_preferences.show_navbar_button";
    }

    public static class UiShell
    {
        public const string RailPublicVisibility = "ui_shell.rail_public_visibility";
        public const string DefaultNavModeEvents = "ui_shell.default_nav_mode.events";
        public const string DefaultNavModeStudio = "ui_shell.default_nav_mode.studio";
        public const string DefaultNavModeAi = "ui_shell.default_nav_mode.ai";
        public const string AllowUserNavOverride = "ui_shell.allow_user_nav_override";
        public const string OrganizerDefaultWorkspace = "ui_shell.organizer_default_workspace";
    }

    public static class UiShellPreferences
    {
        public const string Layout = "ui_shell_preferences.layout.v1";
        public const string LastWorkspace = "ui_shell_preferences.last_workspace";
        public const string LastActor = "ui_shell_preferences.last_actor";
        public const string LastSettingsScope = "ui.settings.last_scope.v1";
    }

    public static class TenantDelegation
    {
        public const string LockSmtp = "governance.lock_tenant_smtp";
        public const string LockStorage = "governance.lock_tenant_storage";
        public const string LockAnalytics = "governance.lock_tenant_analytics";
        public const string LockAiAssistant = "governance.lock_tenant_ai_assistant";
        public const string LockIntegrations = "governance.lock_tenant_integrations";
        public const string LockReportingProviders = "governance.lock_tenant_reporting_providers";
        public const string LockTenantOspreyProvider = "governance.lock_tenant_osprey_provider";
        public const string LockTenantCoopProvider = "governance.lock_tenant_coop_provider";
        public const string LockMcp = "governance.lock_tenant_mcp";
        public const string LockMcpLegacySse = "governance.lock_tenant_mcp_legacy_sse";
    }

    public static class Policies
    {
        public const string CommunityGuidelinesContent = "policies.community_guidelines_content";
    }

    public static class Localization
    {
        public const string DefaultLanguage = "localization.default_language";
        public const string TmsProvider = "localization.tms_provider";
        public const string TmsApiUrl = "localization.tms_api_url";
        public const string TmsProjectId = "localization.tms_project_id";
        public const string TmsComponent = "localization.tms_component";
        public const string EnabledLanguages = "localization.enabled_languages";
        public const string FallbackLanguage = "localization.fallback_language";
        public const string ClientPickerEnabled = "localization.client_picker_enabled";
        public const string ForceOfflineMode = "localization.force_offline_mode";
    }

    public static class EventList
    {
        public const string BrowseMode = "event_list.browse_mode";
        public const string PageSize = "event_list.page_size";
        public const string DefaultLayout = "event_list.default_layout";

        public static class Card
        {
            public const string ShowDate = "event_list.card.show_date";
            public const string ShowLocation = "event_list.card.show_location";
            public const string ShowOrganizer = "event_list.card.show_organizer";
            public const string ShowDescription = "event_list.card.show_description";
            public const string ShowTags = "event_list.card.show_tags";
            public const string ShowCategories = "event_list.card.show_categories";
            public const string ShowCapacity = "event_list.card.show_capacity";
            public const string ShowPrice = "event_list.card.show_price";
            public const string ShowStatus = "event_list.card.show_status";
        }
    }

    public static class AnonymousRegistration
    {
        public const string RetentionDays = "anonymous_registration.retention_days";
    }

    public static class AnonymousRegistrationChallenge
    {
        public const string TenantPermitsPerMinute = "registration.anonymous_challenge.tenant_per_minute";
        public const string EventPermitsPerMinute = "registration.anonymous_challenge.event_per_minute";
        public const string Difficulty = "registration.anonymous_challenge.difficulty";
    }

    public static class PublicExperience
    {
        public const string Mode = "public_experience.mode";
        public const string VisitorAccessMode = "public_experience.visitor_access_mode";
        public const string EventCatalogLabel = "public_experience.event_catalog_label";
        public const string PrimaryOrganizationId = "public_experience.primary_organization_id";
        public const string HomeBlocks = "public_experience.home_blocks";
        public const string Ctas = "public_experience.ctas";
        public const string EventSectionPresets = "public_experience.event_section_presets";
        public const string DiscoveryAreas = "public_experience.discovery_areas";
        public const string AnnouncementBarEnabled = "public_experience.announcement_bar.enabled";
        public const string AnnouncementBarMessage = "public_experience.announcement_bar.message";
        public const string AnnouncementBarLinkText = "public_experience.announcement_bar.link_text";
        public const string AnnouncementBarLinkUrl = "public_experience.announcement_bar.link_url";
        public const string AnnouncementBarRevision = "public_experience.announcement_bar.revision";
    }

    public static class PublicExperiencePreferences
    {
        public const string AnnouncementBarDismissedRevision = "public_experience_preferences.announcement_bar.dismissed_revision";
    }

    public static class HomeDiscoveryPreferences
    {
        public const string AreaId = "home_discovery.area_id";
        public const string Mode = "home_discovery.mode";
    }

    public static class Footer
    {
        public const string Enabled = "footer.enabled";
        public const string Template = "footer.template";
        public const string ShowDescription = "footer.show_description";
        public const string DescriptionText = "footer.description_text";
        public const string ShowSocialLinks = "footer.show_social_links";
        public const string SocialLinks = "footer.social_links";
        public const string CopyrightText = "footer.copyright_text";
        public const string ShowCookieSettingsLink = "footer.show_cookie_settings_link";

        // Instance-level lock flags (prevent tenant override)
        public const string LockTenantTemplate = "footer.lock_tenant_template";
        public const string LockTenantLinkGroups = "footer.lock_tenant_link_groups";
        public const string LockTenantSocialLinks = "footer.lock_tenant_social_links";
        public const string LockTenantDescription = "footer.lock_tenant_description";
        public const string LockTenantCopyright = "footer.lock_tenant_copyright";
    }
}
