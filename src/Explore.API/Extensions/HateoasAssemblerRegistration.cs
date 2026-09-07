namespace Explore.API.Extensions;

using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.API.Hateoas.Policies;
using Explore.API.Hateoas.Resources;
using Explore.Application.Contracts.Hateoas;  // For ILinkPolicy, ICollectionLinkPolicy
using Explore.Application.DTOs.Actor;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.DTOs.Admissions;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.DTOs.Ai;
using Explore.Application.DTOs.Category;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.CustomPropertyGovernance;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.DTOs.EventDay;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.DTOs.Footer;
using Explore.Application.DTOs.Geocoding;
using Explore.Application.Features.Promotions;
using Explore.Application.DTOs.Group;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.DTOs.Location;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.DTOs.Notification;
using Explore.Application.DTOs.Waitlist;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Organization;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.DTOs.OrganizerPaymentConnections;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.DTOs.PlatformMonetization;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.DTOs.Registration;
using Explore.Application.DTOs.RegistrationAnalytics;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.DTOs.Scheduling;
using Explore.Application.DTOs.Settings;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.DTOs.Studio;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.DTOs.Tag;
using Explore.Application.DTOs.Tenant;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.DTOs.User;
using Explore.Application.DTOs.Webhooks;

/// <summary>
/// Registers all HATEOAS resource assemblers and their link policies.
/// </summary>
public static class HateoasAssemblerRegistration
{
    /// <summary>
    /// Registers all resource assemblers for the application.
    /// Call this after AddHateoas() in Program.cs.
    /// </summary>
    public static IServiceCollection AddHateoasAssemblers(this IServiceCollection services)
    {
        // Organization
        services.AddHalResourceWithAssembler<OrganizationDto, OrganizationListDto, OrganizationDetailLinkPolicy, OrganizationCollectionLinkPolicy, OrganizationResourceAssembler>();
        services.AddHalResource<OrganizationTenantEvidenceDto, OrganizationTenantEvidenceDetailLinkPolicy, OrganizationTenantEvidenceCollectionLinkPolicy>();

        // Event
        services.AddHalResourceWithAssembler<EventDto, EventListDto, EventDetailLinkPolicy, EventCollectionLinkPolicy, EventResourceAssembler>();
        services.AddHalResource<EventTeamMemberDto, EventTeamMemberDetailLinkPolicy, EventTeamMemberCollectionLinkPolicy>();
        services.AddScoped<EventTicketCatalogManagementLinkPolicy>();
        services.AddHalResourceWithAssembler<EventTicketCatalogManagementDto, EventTicketCatalogManagementLinkPolicy, EventTicketCatalogManagementCollectionLinkPolicy, EventTicketCatalogManagementResourceAssembler>();
        services.AddHalResource<PaidEventPublicationPreflightDto, PaidEventPublicationPreflightLinkPolicy, PaidEventPublicationPreflightCollectionLinkPolicy>();
        services.AddHalResource<EventOrganizerPaymentConnectionManagementDto, OrganizerPaymentConnectionLinkPolicy, OrganizerPaymentConnectionCollectionLinkPolicy>();
        services.AddHalResource<PromotionManagementDto, PromotionManagementLinkPolicy, PromotionManagementCollectionLinkPolicy>();
        services.AddHalResourceWithSharedPolicy<RegistrationWorkflowDto, RegistrationWorkflowLinkPolicy, RegistrationWorkflowCollectionLinkPolicy, RegistrationWorkflowResourceAssembler>();
        services.AddHalResourceWithSharedPolicy<RegistrationFormDto, RegistrationFormLinkPolicy, RegistrationFormCollectionLinkPolicy, RegistrationFormResourceAssembler>();
        services.AddHalResourceWithSharedPolicy<RegistrationFormVersionDto, RegistrationFormVersionLinkPolicy, RegistrationFormVersionCollectionLinkPolicy, RegistrationFormVersionResourceAssembler>();
        services.AddHalResourceWithSharedPolicy<RegistrationFormPublishPreflightDto, RegistrationFormPublishPreflightLinkPolicy, RegistrationFormPublishPreflightCollectionLinkPolicy, RegistrationFormPublishPreflightResourceAssembler>();
        services.AddHalResource<RegistrationAnswerAnalyticsDto, RegistrationAnswerAnalyticsLinkPolicy, RegistrationAnswerAnalyticsCollectionLinkPolicy>();
        services.AddHalResourceWithSharedPolicy<RegistrationFormTemplateDto, RegistrationFormTemplateLinkPolicy, RegistrationFormTemplateCollectionLinkPolicy>();
        services.AddHalResourceWithAssembler<RegistrationProviderBindingHealthDto, RegistrationProviderHealthLinkPolicy, RegistrationProviderHealthCollectionLinkPolicy, RegistrationProviderHealthResourceAssembler>();
        services.AddHalResourceWithAssembler<RegistrationProviderParkedQueueItemDto, RegistrationProviderQueueLinkPolicy, RegistrationProviderQueueCollectionLinkPolicy, RegistrationProviderQueueResourceAssembler>();
        services.AddHalResourceWithAssembler<RegistrationProviderConnectionDto, RegistrationProviderConnectionLinkPolicy, RegistrationProviderConnectionCollectionLinkPolicy, RegistrationProviderConnectionResourceAssembler>();
        services.AddHalResourceWithAssembler<RegistrationProviderBindingDto, RegistrationProviderBindingLinkPolicy, RegistrationProviderBindingCollectionLinkPolicy, RegistrationProviderBindingResourceAssembler>();
        services.AddHalResourceWithAssembler<Explore.Application.DTOs.RegistrationProviders.RegistrationChannelDto, RegistrationChannelLinkPolicy, RegistrationChannelCollectionLinkPolicy, RegistrationChannelResourceAssembler>();
        services.AddScoped<RegistrationProviderLaunchDescriptorLinkPolicy>();
        services.AddScoped<ILinkPolicy<RegistrationProviderLaunchDescriptorDto>>(provider => provider.GetRequiredService<RegistrationProviderLaunchDescriptorLinkPolicy>());
        services.AddScoped<ICollectionLinkPolicy<RegistrationProviderLaunchDescriptorDto>>(provider => provider.GetRequiredService<RegistrationProviderLaunchDescriptorLinkPolicy>());
        services.AddScoped<IResourceAssembler<RegistrationProviderLaunchDescriptorDto, RegistrationProviderLaunchDescriptorDto>, RegistrationProviderLaunchDescriptorResourceAssembler>();
        services.AddHalResourceWithSharedPolicy<OptionalQuestionnaireDto, OptionalQuestionnaireLinkPolicy, OptionalQuestionnaireCollectionLinkPolicy, OptionalQuestionnaireResourceAssembler>();
        services.AddHalResourceWithAssembler<EventPublicActionDto, EventPublicActionDetailLinkPolicy, EventPublicActionCollectionLinkPolicy, EventPublicActionResourceAssembler>();
        services.AddHalResourceWithAssembler<EventOrganizerClaimDto, EventOrganizerClaimDetailLinkPolicy, EventOrganizerClaimCollectionLinkPolicy, EventOrganizerClaimResourceAssembler>();
        services.AddScoped<EventDiscoveryLinkPolicy>();
        services.AddScoped<ILinkPolicy<EventDiscoveryItemDto>>(provider =>
            provider.GetRequiredService<EventDiscoveryLinkPolicy>());
        services.AddScoped<ICollectionLinkPolicy<EventDiscoveryItemDto>>(provider =>
            provider.GetRequiredService<EventDiscoveryLinkPolicy>());
        services.AddScoped<IResourceAssembler<EventDiscoveryItemDto>, EventDiscoveryResourceAssembler>();

        services.AddHalResource<EventSeriesDto, EventSeriesListDto, EventSeriesDetailLinkPolicy, EventSeriesCollectionLinkPolicy>();

        services.AddHalResourceWithAssembler<EventReportOptionsDto, EventReportOptionsDetailLinkPolicy, EventReportOptionsCollectionLinkPolicy, EventReportOptionsResourceAssembler>();
        services.AddHalResourceWithAssembler<MyEventReportDto, MyEventReportDetailLinkPolicy, MyEventReportCollectionLinkPolicy, MyEventReportResourceAssembler>();
        services.AddHalResource<ModerationReportDetailDto, ModerationReportQueueItemDto, ModerationReportDetailLinkPolicy, ModerationReportQueueCollectionLinkPolicy>();
        services.AddHalResource<ReportingRoutingStateDto, ReportingRoutingStateLinkPolicy, ReportingRoutingStateCollectionLinkPolicy>();
        services.AddHalResource<TenantModerationReportingDashboardDto, TenantModerationReportingDashboardLinkPolicy, TenantModerationReportingDashboardCollectionLinkPolicy>();

        // EventSession
        services.AddHalResourceWithAssembler<EventSessionDto, EventSessionListDto, EventSessionDetailLinkPolicy, EventSessionCollectionLinkPolicy, EventSessionResourceAssembler>();

        services.AddHalResource<EventSessionLanguageDto, EventSessionLanguageListDto, EventSessionLanguageDetailLinkPolicy, EventSessionLanguageCollectionLinkPolicy>();

        // EventSessionGroup (program sections/tracks/devrooms)
        services.AddHalResourceWithAssembler<EventSessionGroupDto, EventSessionGroupListDto, EventSessionGroupDetailLinkPolicy, EventSessionGroupCollectionLinkPolicy, EventSessionGroupResourceAssembler>();

        services.AddHalResourceWithAssembler<EventSessionSpeakerDto, EventSessionSpeakerListDto, EventSessionSpeakerDetailLinkPolicy, EventSessionSpeakerCollectionLinkPolicy, EventSessionSpeakerResourceAssembler>();

        // Template Sync helper resources
        services.AddScoped<ILinkPolicy<EventTemplateSyncResource>, EventTemplateSyncLinkPolicy>();
        services.AddScoped<ILinkPolicy<EventSessionTemplateSyncResource>, EventSessionTemplateSyncLinkPolicy>();

        // Actor
        services.AddHalResource<ActorDto, ActorListDto, ActorDetailLinkPolicy, ActorCollectionLinkPolicy>();

        // ActorSubscription (current-user notification subscriptions)
        services.AddHalResourceWithAssembler<ActorSubscriptionDto, ActorSubscriptionListDto, ActorSubscriptionDetailLinkPolicy, ActorSubscriptionCollectionLinkPolicy, ActorSubscriptionResourceAssembler>();

        // AI assistant conversations (private authenticated history)
        services.AddHalResourceWithAssembler<AiConversationDto, AiConversationSummaryDto, AiConversationDetailLinkPolicy, AiConversationCollectionLinkPolicy, AiConversationResourceAssembler>();

        services.AddHalResource<StudioContextDto, StudioContextLinkPolicy, StudioContextCollectionLinkPolicy>();

        // Location
        services.AddHalResource<LocationDto, LocationListDto, LocationDetailLinkPolicy, LocationCollectionLinkPolicy>();
        services.AddHalResource<AddressSuggestionDto, AddressSuggestionDetailLinkPolicy, AddressSuggestionCollectionLinkPolicy>();

        services.AddHalResource<EventLocationManagementDto, EventLocationManagementLinkPolicy, EventLocationManagementCollectionLinkPolicy>();

        // Category
        services.AddHalResource<CategoryDto, CategoryListDto, CategoryDetailLinkPolicy, CategoryCollectionLinkPolicy>();

        // CustomPropertyDefinition
        services.AddHalResource<CustomPropertyDefinitionDto, CustomPropertyDefinitionListDto, CustomPropertyDefinitionDetailLinkPolicy, CustomPropertyDefinitionCollectionLinkPolicy>();

        // EventTemplate
        services.AddHalResource<EventTemplateDto, EventTemplateListDto, EventTemplateDetailLinkPolicy, EventTemplateCollectionLinkPolicy>();

        // EventCustomProperty
        services.AddHalResource<EventCustomPropertyDefinitionDto, EventCustomPropertyDefinitionListDto, EventCustomPropertyDefinitionDetailLinkPolicy, EventCustomPropertyDefinitionCollectionLinkPolicy>();

        // EventSessionTemplate
        services.AddHalResource<EventSessionTemplateDto, EventSessionTemplateListDto, EventSessionTemplateDetailLinkPolicy, EventSessionTemplateCollectionLinkPolicy>();

        // EventSessionCustomProperty
        services.AddHalResource<EventSessionCustomPropertyDefinitionDto, EventSessionCustomPropertyDefinitionListDto, EventSessionCustomPropertyDefinitionDetailLinkPolicy, EventSessionCustomPropertyDefinitionCollectionLinkPolicy>();

        // Group
        services.AddHalResourceWithAssembler<GroupDto, GroupListDto, GroupDetailLinkPolicy, GroupCollectionLinkPolicy, GroupResourceAssembler>();

        // GroupMember (relationship with payload, same DTO for detail and list)
        services.AddHalResourceWithAssembler<GroupMemberDto, GroupMemberDetailLinkPolicy, GroupMemberCollectionLinkPolicy, GroupMemberResourceAssembler>();

        // Tag
        services.AddHalResource<TagDto, TagListDto, TagDetailLinkPolicy, TagCollectionLinkPolicy>();

        // User (same DTO for detail and list)
        services.AddHalResourceWithAssembler<UserDto, UserDetailLinkPolicy, UserCollectionLinkPolicy, UserResourceAssembler>();

        // Tenant
        services.AddHalResourceWithAssembler<TenantDto, TenantListDto, TenantDetailLinkPolicy, TenantCollectionLinkPolicy, TenantResourceAssembler>();

        // TenantUserRoleGrant (auditable tenant-local role grant)
        services.AddHalResourceWithAssembler<TenantUserRoleGrantDto, TenantUserRoleGrantListDto, TenantUserRoleGrantDetailLinkPolicy, TenantUserRoleGrantCollectionLinkPolicy, TenantUserRoleGrantResourceAssembler>();

        services.AddHalResourceWithAssembler<SupportAccessSessionDto, SupportAccessSessionDetailLinkPolicy, SupportAccessSessionCollectionLinkPolicy, SupportAccessSessionResourceAssembler>();
        services.AddHalResourceWithAssembler<SupportAccessAuditEventDto, SupportAccessAuditEventDetailLinkPolicy, SupportAccessAuditEventCollectionLinkPolicy, SupportAccessAuditEventResourceAssembler>();

        // Tenant typed settings documents
        services.AddHalResource<TenantBrandingSettingsDocumentDto, TenantBrandingSettingsDocumentLinkPolicy, TenantBrandingSettingsDocumentCollectionLinkPolicy>();
        services.AddHalResource<
            TenantDirectoryOperatorIdentityDocumentDto,
            TenantDirectoryOperatorIdentityDocumentLinkPolicy,
            TenantDirectoryOperatorIdentityDocumentCollectionLinkPolicy>();

        services.AddHalResource<TenantFooterSettingsDto, TenantFooterSettingsLinkPolicy, TenantFooterSettingsCollectionLinkPolicy>();

        // OrganizationMember (relationship with payload, same DTO for detail and list)
        services.AddHalResourceWithAssembler<OrganizationMemberDto, OrganizationMemberDetailLinkPolicy, OrganizationMemberCollectionLinkPolicy, OrganizationMemberResourceAssembler>();

        services.AddHalResource<RegistrationOrderDto, RegistrationOrderLinkPolicy, RegistrationOrderCollectionLinkPolicy>();
        services.AddHalResource<ParticipantReadinessDto, ParticipantReadinessLinkPolicy, ParticipantReadinessCollectionLinkPolicy>();
        services.AddHalResource<TicketTransferDto, TicketTransferLinkPolicy, TicketTransferCollectionLinkPolicy>();
        services.AddHalResource<FairReturnWaitlistDto, FairReturnWaitlistLinkPolicy, FairReturnWaitlistCollectionLinkPolicy>();
        services.AddHalResource<RegistrationAnswerFileDto, RegistrationAnswerFileLinkPolicy, RegistrationAnswerFileCollectionLinkPolicy>();

        // EventSessionAgendaItem
        services.AddHalResourceWithAssembler<EventSessionAgendaItemDto, EventSessionAgendaItemListDto, EventSessionAgendaItemDetailLinkPolicy, EventSessionAgendaItemCollectionLinkPolicy, EventSessionAgendaItemResourceAssembler>();

        // StorageObject
        services.AddHalResourceWithAssembler<StorageObjectDto, StorageObjectListDto, StorageObjectDetailLinkPolicy, StorageObjectCollectionLinkPolicy, StorageObjectResourceAssembler>();

        // Scheduler administration
        services.AddHalResource<SchedulerAdminOverviewDto, SchedulerAdminOverviewLinkPolicy, SchedulerAdminOverviewCollectionLinkPolicy>();
        services.AddHalResource<SchedulerAdminJobDto, SchedulerAdminJobLinkPolicy, SchedulerAdminJobCollectionLinkPolicy>();

        services.AddHalResource<ControlPlaneOverviewDto, ControlPlaneOverviewLinkPolicy, ControlPlaneOverviewCollectionLinkPolicy>();
        services.AddHalResource<ControlPlaneDomainOverviewDto, ControlPlaneDomainLinkPolicy, ControlPlaneDomainCollectionLinkPolicy>();
        services.AddHalResource<ControlPlaneOperationsDto, ControlPlaneOperationsLinkPolicy, ControlPlaneOperationsCollectionLinkPolicy>();
        services.AddHalResource<ControlPlaneDeploymentModeRunbookDto, ControlPlaneDeploymentModeRunbookLinkPolicy, ControlPlaneDeploymentModeRunbookCollectionLinkPolicy>();
        services.AddHalResource<ControlPlaneTenantDetailDto, ControlPlaneTenantListItemDto, ControlPlaneTenantDetailLinkPolicy, ControlPlaneTenantCollectionLinkPolicy>();
        services.AddHalResourceWithAssembler<ControlPlaneTenantPlanDetailDto, ControlPlaneTenantPlanListItemDto, ControlPlaneTenantPlanDetailLinkPolicy, ControlPlaneTenantPlanCollectionLinkPolicy, ControlPlaneTenantPlanResourceAssembler>();
        services.AddHalResourceWithAssembler<ControlPlaneTenantEffectiveConfigurationDto, ControlPlaneTenantEffectiveConfigurationLinkPolicy, ControlPlaneTenantEffectiveConfigurationCollectionLinkPolicy, ControlPlaneTenantEffectiveConfigurationResourceAssembler>();

        services.AddHalResource<InstanceOnboardingStatusDto, InstanceOnboardingStatusLinkPolicy, InstanceOnboardingStatusCollectionLinkPolicy>();
        services.AddHalResource<TenantOnboardingStatusDto, TenantOnboardingStatusLinkPolicy, TenantOnboardingStatusCollectionLinkPolicy>();

        // Storage administration
        services.AddHalResourceWithAssembler<InstanceStorageSettingsDto, InstanceStorageSettingsLinkPolicy, InstanceStorageSettingsCollectionLinkPolicy, InstanceStorageSettingsResourceAssembler>();
        services.AddHalResource<PlatformMonetizationSettingsDto, PlatformMonetizationSettingsLinkPolicy, PlatformMonetizationSettingsCollectionLinkPolicy>();
        services.AddHalResourceWithAssembler<PaidEventPolicyDto, InstancePaidEventPolicyLinkPolicy, InstancePaidEventPolicyCollectionLinkPolicy, InstancePaidEventPolicyResourceAssembler>();
        services.AddHalResourceWithAssembler<TenantPaidEventPolicyConfigurationDto, TenantPaidEventPolicyConfigurationLinkPolicy, TenantPaidEventPolicyConfigurationCollectionLinkPolicy, TenantPaidEventPolicyConfigurationResourceAssembler>();
        services.AddHalResource<SettingGroupResponseDto, AtprotoInstanceSettingGroupLinkPolicy, AtprotoInstanceSettingGroupCollectionLinkPolicy>();
        services.AddHalResourceWithAssembler<TenantStorageSettingsDto, TenantStorageSettingsLinkPolicy, TenantStorageSettingsCollectionLinkPolicy, TenantStorageSettingsResourceAssembler>();
        services.AddHalResource<TenantReportingIntakePolicyDto, TenantReportingIntakePolicyLinkPolicy, TenantReportingIntakePolicyCollectionLinkPolicy>();

        // OrganizationReview (same DTO for detail and list)
        services.AddHalResourceWithAssembler<OrganizationReviewDto, OrganizationReviewDetailLinkPolicy, OrganizationReviewCollectionLinkPolicy, OrganizationReviewResourceAssembler>();

        // Notification (personal user notifications)
        services.AddHalResourceWithAssembler<NotificationDto, NotificationListDto, NotificationDetailLinkPolicy, NotificationCollectionLinkPolicy, NotificationResourceAssembler>();
        services.AddScoped<ILinkPolicy<NotificationPreferenceMatrixDto>, NotificationPreferenceMatrixLinkPolicy>();
        services.AddScoped<ICollectionLinkPolicy<NotificationPreferenceMatrixDto>, NotificationPreferenceMatrixLinkPolicy>();
        services.AddScoped<IResourceAssembler<NotificationPreferenceMatrixDto>, NotificationPreferenceMatrixResourceAssembler>();
        services.AddScoped<ILinkPolicy<WebPushSubscriptionDto>, WebPushSubscriptionLinkPolicy>();
        services.AddScoped<ICollectionLinkPolicy<WebPushSubscriptionDto>, WebPushSubscriptionLinkPolicy>();
        services.AddScoped<IResourceAssembler<WebPushSubscriptionDto>, WebPushSubscriptionResourceAssembler>();

        // Custom Property Projection Admin (D2 Operability)
        services.AddHalResource<ProjectionStatusDto, ProjectionStatusDetailLinkPolicy, ProjectionStatusCollectionLinkPolicy>();
        services.AddHalResource<ProjectionDirtyScopeDto, ProjectionDirtyScopeDetailLinkPolicy, ProjectionDirtyScopeCollectionLinkPolicy>();

        // Event-bound add-on catalog and private order lifecycle
        services.AddHalResource<EventAddOnCatalogDto, EventAddOnCatalogLinkPolicy, EventAddOnCatalogCollectionLinkPolicy>();
        services.AddScoped<RegistrationOrderAddOnLineLinkPolicy>();
        services.AddHalResourceWithAssembler<
            RegistrationOrderAddOnSummaryDto,
            RegistrationOrderAddOnLinkPolicy,
            RegistrationOrderAddOnCollectionLinkPolicy,
            RegistrationOrderAddOnResourceAssembler>();
        services.AddScoped<ILinkPolicy<RebuildProjectionResponseDto>, RebuildProjectionResponseLinkPolicy>();
        services.AddScoped<ILinkPolicy<DrainDirtyScopesResponseDto>, DrainDirtyScopesResponseLinkPolicy>();

        // Email Dispatch Admin (operator replay/park affordances)
        services.AddHalResource<EmailDispatchStatusDto, EmailDispatchStatusDetailLinkPolicy, EmailDispatchStatusCollectionLinkPolicy>();
        services.AddHalResource<EmailDispatchProcessorControlDto, EmailDispatchProcessorControlDetailLinkPolicy, EmailDispatchProcessorControlCollectionLinkPolicy>();
        services.AddHalResource<IncomingWebhookEffectStatusDto, IncomingWebhookEffectStatusDetailLinkPolicy, IncomingWebhookEffectStatusCollectionLinkPolicy>();

        services.AddHalResourceWithAssembler<WebhookConsumerDto, WebhookConsumerDetailLinkPolicy, WebhookConsumerCollectionLinkPolicy, WebhookConsumerResourceAssembler>();
        services.AddHalResource<WebhookEndpointDto, WebhookEndpointDetailLinkPolicy, WebhookEndpointCollectionLinkPolicy>();
        services.AddHalResource<WebhookMessageDto, WebhookMessageDetailLinkPolicy, WebhookMessageCollectionLinkPolicy>();
        services.AddHalResource<WebhookDeliveryAttemptDto, WebhookDeliveryAttemptDetailLinkPolicy, WebhookDeliveryAttemptCollectionLinkPolicy>();
        services.AddHalResource<WebhookProviderPublicationDto, WebhookProviderPublicationDetailLinkPolicy, WebhookProviderPublicationCollectionLinkPolicy>();
        services.AddHalResource<WebhookBulkReplayOperationDto, WebhookBulkReplayDetailLinkPolicy, WebhookBulkReplayCollectionLinkPolicy>();

        // Custom Property Governance (D2 Operability)
        services.AddScoped<ICollectionLinkPolicy<CustomPropertyGovernanceRowDto>, CustomPropertyGovernanceCollectionLinkPolicy>();

        // EventDay (event scheduling)
        services.AddHalResource<EventDayDto, EventDayListDto, EventDayDetailLinkPolicy, EventDayCollectionLinkPolicy>();

        // EventAgendaItem (event scheduling)
        services.AddHalResourceWithAssembler<EventAgendaItemDto, EventAgendaItemListDto, EventAgendaItemDetailLinkPolicy, EventAgendaItemCollectionLinkPolicy, EventAgendaItemResourceAssembler>();

        // LocationRoom (event scheduling)
        services.AddHalResource<LocationRoomDto, LocationRoomListDto, LocationRoomDetailLinkPolicy, LocationRoomCollectionLinkPolicy>();

        return services;
    }

    /// <summary>
    /// Registers the detail policy, collection policy, and assembler that together make one DTO family
    /// HAL-addressable, for families whose list projection differs from the detail projection.
    /// <para>
    /// The type arguments stay explicit at every call site on purpose: this is compile-time registration, not
    /// assembly scanning, so a missing policy is a build error and every closed contract remains greppable by
    /// its concrete type. The helper removes the repetition, not the visibility.
    /// </para>
    /// </summary>
    private static IServiceCollection AddHalResourceWithAssembler<TDetail, TList, TDetailPolicy, TCollectionPolicy, TAssembler>(
        this IServiceCollection services)
        where TDetail : class
        where TList : class
        where TDetailPolicy : class, ILinkPolicy<TDetail>
        where TCollectionPolicy : class, ICollectionLinkPolicy<TList>
        where TAssembler : class, IResourceAssembler<TDetail, TList>
    {
        services.AddScoped<ILinkPolicy<TDetail>, TDetailPolicy>();
        services.AddScoped<ICollectionLinkPolicy<TList>, TCollectionPolicy>();
        services.AddScoped<IResourceAssembler<TDetail, TList>, TAssembler>();
        return services;
    }

    /// <summary>
    /// Registers a family that needs no assembly behavior of its own, using the default
    /// <see cref="HalResourceAssembler{TDto,TListDto}"/>. Prefer this overload: declaring an empty assembler
    /// subclass adds a type without adding behavior, and the absence of one is the signal that a family is
    /// ordinary. Pass an explicit assembler type only when the family really does assemble differently.
    /// </summary>
    private static IServiceCollection AddHalResource<TDetail, TList, TDetailPolicy, TCollectionPolicy>(
        this IServiceCollection services)
        where TDetail : class
        where TList : class
        where TDetailPolicy : class, ILinkPolicy<TDetail>
        where TCollectionPolicy : class, ICollectionLinkPolicy<TList>
        => services.AddHalResourceWithAssembler<TDetail, TList, TDetailPolicy, TCollectionPolicy, HalResourceAssembler<TDetail, TList>>();

    /// <summary>Default assembler, for families that expose one DTO for both detail and collection items.</summary>
    private static IServiceCollection AddHalResource<TDto, TDetailPolicy, TCollectionPolicy>(
        this IServiceCollection services)
        where TDto : class
        where TDetailPolicy : class, ILinkPolicy<TDto>
        where TCollectionPolicy : class, ICollectionLinkPolicy<TDto>
        => services.AddHalResource<TDto, TDto, TDetailPolicy, TCollectionPolicy>();

    /// <summary>Explicit assembler, for families that expose one DTO for both detail and collection items.</summary>
    private static IServiceCollection AddHalResourceWithAssembler<TDto, TDetailPolicy, TCollectionPolicy, TAssembler>(
        this IServiceCollection services)
        where TDto : class
        where TDetailPolicy : class, ILinkPolicy<TDto>
        where TCollectionPolicy : class, ICollectionLinkPolicy<TDto>
        where TAssembler : class, IResourceAssembler<TDto, TDto>
        => services.AddHalResourceWithAssembler<TDto, TDto, TDetailPolicy, TCollectionPolicy, TAssembler>();

    /// <summary>Shared detail policy with the default assembler, for families that need no custom assembly.</summary>
    private static IServiceCollection AddHalResourceWithSharedPolicy<TDto, TPolicy, TCollectionPolicy>(
        this IServiceCollection services)
        where TDto : class
        where TPolicy : class, ILinkPolicy<TDto>
        where TCollectionPolicy : class, ICollectionLinkPolicy<TDto>
        => services.AddHalResourceWithSharedPolicy<TDto, TPolicy, TCollectionPolicy, HalResourceAssembler<TDto, TDto>>();

    /// <summary>
    /// Registers a family whose detail policy is also resolved directly by concrete type elsewhere. The
    /// interface registration forwards to the concrete registration rather than declaring the implementation
    /// twice, so both resolution paths share one instance per request — which matters because these policies
    /// cache authorization decisions for the duration of a request.
    /// </summary>
    private static IServiceCollection AddHalResourceWithSharedPolicy<TDto, TPolicy, TCollectionPolicy, TAssembler>(
        this IServiceCollection services)
        where TDto : class
        where TPolicy : class, ILinkPolicy<TDto>
        where TCollectionPolicy : class, ICollectionLinkPolicy<TDto>
        where TAssembler : class, IResourceAssembler<TDto, TDto>
    {
        services.AddScoped<TPolicy>();
        services.AddScoped<ILinkPolicy<TDto>>(provider => provider.GetRequiredService<TPolicy>());
        services.AddScoped<ICollectionLinkPolicy<TDto>, TCollectionPolicy>();
        services.AddScoped<IResourceAssembler<TDto, TDto>, TAssembler>();
        return services;
    }
}
