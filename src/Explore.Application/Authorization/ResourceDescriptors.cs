using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.DTOs.Ai;
using Explore.Application.DTOs.Category;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.DTOs.EventDay;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.DTOs.Geocoding;
using Explore.Application.DTOs.Group;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.DTOs.Location;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.DTOs.Organization;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.DTOs.Tag;
using Explore.Application.DTOs.Tenant;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.DTOs.User;
using Explore.Application.DTOs.Webhooks;
using Explore.Domain;

namespace Explore.Application.Authorization;

/// <summary>
/// Central catalog of resource descriptors for all DTO types participating in HATEOAS authorization.
/// <para>
/// Usage in link policies:
/// <code>
/// .RequirePermission(AuthorizationActions.Update, ResourceDescriptors.Event, dto)
/// </code>
/// replaces manual dictionary construction with type-safe, centralized metadata extraction.
/// </para>
/// <para>
/// Descriptors marked "piggybacks on tenant" use <see cref="ResourceKinds.Tenant"/> because
/// their commands authorize as the parent tenant resource, not as their own resource kind.
/// </para>
/// <para>
/// Every descriptor publishes a closed <see cref="IAuthorizationFacts"/> record. A descriptor must not
/// publish a fact field that grants authority the resource does not already have: adding an identifier
/// such as <c>userId</c> or <c>groupId</c> activates a derived role and is a permission widening.
/// </para>
/// </summary>
public static class ResourceDescriptors
{
    #region Core resources with unique resource kinds

    public static readonly ResourceDescriptor<EventDto> Event = new(
        ResourceKinds.Event,
        dto => dto.Id.ToString(),
        EventFacts,
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventDto> EventOrganizerClaimForEvent = new(
        ResourceKinds.EventOrganizerClaim,
        dto => dto.Id.ToString(),
        EventFacts,
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    /// <summary>List DTO variant for collection item-level permission checks.</summary>
    public static readonly ResourceDescriptor<EventListDto> EventList = new(
        ResourceKinds.Event,
        dto => dto.Id.ToString(),
        EventListFacts,
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<Explore.Domain.Event> EventAuthorizationTarget = new(
        ResourceKinds.Event,
        eventEntity => eventEntity.Id.ToString(),
        EventAuthorizationTargetFacts,
        eventEntity => new AuthorizationScope(TenantId: eventEntity.TenantId.ToString()));

    public static readonly ResourceDescriptor<OrganizationDto> Organization = new(
        ResourceKinds.Organization,
        dto => dto.Id.ToString(),
        dto => new OrganizationAuthorizationFacts(dto.TenantId, dto.Id),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    /// <summary>List DTO variant for collection item-level permission checks.</summary>
    public static readonly ResourceDescriptor<OrganizationListDto> OrganizationList = new(
        ResourceKinds.Organization,
        dto => dto.Id.ToString(),
        dto => new OrganizationAuthorizationFacts(dto.TenantId, dto.Id),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<OrganizationTenantEvidenceDto> OrganizationTenantEvidence = new(
        ResourceKinds.Organization,
        dto => dto.OrganizationId.ToString(),
        dto => new OrganizationAuthorizationFacts(dto.TenantId, dto.OrganizationId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<OrganizationTenantEvidenceDto> OrganizationTenantEvidenceDocument = new(
        ResourceKinds.StorageObject,
        dto => dto.DocumentStorageObjectId.ToString(),
        dto => new PersistedStorageObjectAuthorizationFacts(
            dto.TenantId,
            dto.DocumentStorageObjectId,
            StorageObjectVisibilities.PrivateOwner,
            StorageObjectLifecycleStates.Active,
            dto.DocumentCreatedBy,
            null,
            null),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<TenantDto> Tenant = new(
        ResourceKinds.Tenant,
        dto => dto.Id.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.Id),
        dto => new AuthorizationScope(TenantId: dto.Id.ToString()));

    public static readonly ResourceDescriptor<TenantBrandingSettingsDocumentDto> TenantBrandingSettingsDocument = new(
        ResourceKinds.TenantSetting,
        dto => $"{dto.SourceScopeId}:{dto.DocumentKey}",
        dto => new TenantSettingAuthorizationFacts(dto.SourceScopeId, dto.DocumentKey, dto.IsLockedByInstance),
        dto => new AuthorizationScope(TenantId: dto.SourceScopeId.ToString()));

    public static readonly ResourceDescriptor<TenantDirectoryOperatorIdentityDocumentDto>
        TenantDirectoryOperatorIdentityDocument = new(
            ResourceKinds.TenantSetting,
            dto => $"{dto.SourceScopeId}:{dto.DocumentKey}",
            dto => new TenantSettingAuthorizationFacts(
                dto.SourceScopeId,
                dto.DocumentKey,
                IsLockedByInstance: false),
            dto => new AuthorizationScope(TenantId: dto.SourceScopeId.ToString()));

    public static readonly ResourceDescriptor<TenantUserRoleGrantDto> TenantUserRoleGrant = new(
        ResourceKinds.TenantUserRoleGrant,
        dto => dto.Id.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<SupportAccessSessionDto> SupportAccessSession = new(
        ResourceKinds.SupportAccessSession,
        dto => dto.Id.ToString(),
        dto => new SupportAccessSessionAuthorizationFacts(
            dto.TargetTenantId,
            dto.Id,
            dto.ActorUserId,
            dto.ModeName,
            dto.StatusName),
        dto => new AuthorizationScope(TenantId: dto.TargetTenantId.ToString()));

    public static readonly ResourceDescriptor<SupportAccessAuditEventDto> SupportAccessAuditEvent = new(
        ResourceKinds.SupportAccessSession,
        dto => dto.SupportAccessSessionId.ToString(),
        dto => new SupportAccessSessionAuthorizationFacts(
            dto.TargetTenantId,
            dto.SupportAccessSessionId,
            null,
            null,
            null),
        dto => new AuthorizationScope(TenantId: dto.TargetTenantId.ToString()));

    public static readonly ResourceDescriptor<WebhookConsumerDto> WebhookConsumer = new(
        ResourceKinds.Webhook,
        dto => dto.Id.ToString(),
        WebhookConsumerFacts,
        dto => new AuthorizationScope(
            TenantId: dto.TenantId?.ToString(),
            OrganizationId: dto.OrganizationId?.ToString()));

    public static readonly ResourceDescriptor<WebhookEndpointDto> WebhookEndpoint = new(
        ResourceKinds.Webhook,
        dto => dto.Id.ToString(),
        WebhookEndpointFacts,
        dto => new AuthorizationScope(
            TenantId: dto.TenantId?.ToString(),
            OrganizationId: dto.OwnerKindId == (int)WebhookConsumerKind.Organization
                ? dto.OwnerId.ToString()
                : null));

    public static readonly ResourceDescriptor<WebhookMessageDto> WebhookMessage = new(
        ResourceKinds.Webhook,
        dto => dto.Id.ToString(),
        dto => WebhookDeliveryOwnerFacts(dto.OwnerKindId, dto.OwnerId, dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<WebhookDeliveryAttemptDto> WebhookDeliveryAttempt = new(
        ResourceKinds.Webhook,
        dto => dto.Id.ToString(),
        dto => WebhookDeliveryOwnerFacts(dto.OwnerKindId, dto.OwnerId, dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    // Provider publications and bulk replay operations carry no owner-kind evidence, so the local
    // evaluator authorizes them as tenant-scoped webhook administration. Keep that shape explicit.
    public static readonly ResourceDescriptor<WebhookProviderPublicationDto> WebhookProviderPublication = new(
        ResourceKinds.Webhook,
        dto => dto.Id.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<WebhookBulkReplayOperationDto> WebhookBulkReplayOperation = new(
        ResourceKinds.Webhook,
        dto => dto.Id.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<ActorSubscriptionDto> ActorSubscription = new(
        ResourceKinds.ActorSubscription,
        dto => dto.Id.ToString(),
        dto => new PersonalResourceAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<ActorSubscriptionListDto> ActorSubscriptionList = new(
        ResourceKinds.ActorSubscription,
        dto => dto.Id.ToString(),
        dto => new PersonalResourceAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<AiConversationDto> AiConversation = new(
        ResourceKinds.AiConversation,
        dto => dto.Id.ToString(),
        dto => new PersonalResourceAuthorizationFacts(dto.TenantId, dto.UserId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<AiConversationSummaryDto> AiConversationSummary = new(
        ResourceKinds.AiConversation,
        dto => dto.Id.ToString(),
        dto => new PersonalResourceAuthorizationFacts(dto.TenantId, dto.UserId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventSessionDto> EventSession = new(
        ResourceKinds.EventSession,
        dto => dto.Id.ToString(),
        dto => new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId, dto.Id),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventSessionListDto> EventSessionList = new(
        ResourceKinds.EventSession,
        dto => dto.Id.ToString(),
        dto => new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId, dto.Id),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventSessionGroupDto> EventSessionGroup = new(
        ResourceKinds.EventSessionGroup,
        dto => dto.Id.ToString(),
        dto => new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventSessionGroupListDto> EventSessionGroupList = new(
        ResourceKinds.EventSessionGroup,
        dto => dto.Id.ToString(),
        dto => new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventDayDto> EventDay = new(
        ResourceKinds.EventDay,
        dto => dto.Id.ToString(),
        dto => new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventAgendaItemDto> EventAgendaItem = new(
        ResourceKinds.EventAgendaItem,
        dto => dto.Id.ToString(),
        dto => new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<LocationRoomDto> LocationRoom = new(
        ResourceKinds.LocationRoom,
        dto => dto.Id.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventPublicActionDto> EventPublicAction = new(
        ResourceKinds.Event,
        dto => dto.EventId.ToString(),
        dto => new EventAuthorizationFacts(
            dto.TenantId,
            dto.EventId,
            dto.EventActorId,
            dto.EventActorUserId,
            dto.EventActorOrganizationId,
            dto.EventActorGroupId,
            dto.EventOrganizerActorId,
            null,
            null,
            null,
            dto.EventProvenanceTypeCode ?? dto.EventProvenanceTypeId.ToString(),
            dto.EventSubmittedByUserId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventOrganizerClaimDto> EventOrganizerClaim = new(
        ResourceKinds.EventOrganizerClaim,
        dto => dto.EventId.ToString(),
        EventOrganizerClaimFacts,
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventSessionAgendaItemDto> EventSessionAgendaItem = new(
        ResourceKinds.EventSessionAgendaItem,
        dto => dto.Id.ToString(),
        dto => new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId, dto.EventSessionId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventSessionSpeakerDto> EventSessionSpeaker = new(
        ResourceKinds.EventSession,
        dto => dto.EventSessionId.ToString(),
        dto => new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId, dto.EventSessionId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventSessionSpeakerListDto> EventSessionSpeakerList = new(
        ResourceKinds.EventSession,
        dto => dto.EventSessionId.ToString(),
        dto => new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId, dto.EventSessionId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<CategoryDto> Category = new(
        ResourceKinds.Category,
        dto => dto.Id.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<TagDto> Tag = new(
        ResourceKinds.Tag,
        dto => dto.Id.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<LocationDto> Location = new(
        ResourceKinds.Location,
        dto => dto.Id.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<LocationListDto> LocationList = new(
        ResourceKinds.Location,
        dto => dto.Id.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<AddressSuggestionDto> AddressSuggestion = new(
        ResourceKinds.Location,
        dto => dto.LocationId.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<StorageObjectDto> StorageObject = new(
        ResourceKinds.StorageObject,
        dto => dto.Id.ToString(),
        dto => new PersistedStorageObjectAuthorizationFacts(
            dto.TenantId,
            dto.Id,
            dto.Visibility,
            dto.LifecycleState,
            null,
            null,
            null),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<OrganizationMemberDto> OrganizationMember = new(
        ResourceKinds.OrganizationMember,
        dto => dto.Id.ToString(),
        dto => new OrganizationMemberAuthorizationFacts(dto.TenantId, dto.OrganizationId, dto.Id, dto.UserId),
        dto => new AuthorizationScope(
            TenantId: dto.TenantId.ToString(),
            OrganizationId: dto.OrganizationId.ToString()));

    // OrganizationReviewDto carries no tenant identifier; the local evaluator resolves the ambient
    // tenant and the org-scoped rule uses organizationId. Keep the tenant slot empty rather than guessing.
    public static readonly ResourceDescriptor<OrganizationReviewDto> OrganizationReview = new(
        ResourceKinds.OrganizationReview,
        dto => dto.Id.ToString(),
        dto => new OrganizationReviewAuthorizationFacts(Guid.Empty, dto.OrganizationId, dto.UserId),
        dto => new AuthorizationScope(OrganizationId: dto.OrganizationId.ToString()));

    // Group detail deliberately omits groupId: publishing it would grant the group-admin derived role
    // on the detail resource, which is a permission widening. GroupList already publishes it today.
    public static readonly ResourceDescriptor<GroupDto> Group = new(
        ResourceKinds.Group,
        dto => dto.Id.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    /// <summary>List DTO variant for collection item-level permission checks.</summary>
    public static readonly ResourceDescriptor<GroupListDto> GroupList = new(
        ResourceKinds.Group,
        dto => dto.Id.ToString(),
        dto => new GroupAuthorizationFacts(dto.TenantId, dto.Id),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<GroupMemberDto> GroupMember = new(
        ResourceKinds.GroupMember,
        dto => dto.Id.ToString(),
        dto => new GroupMemberAuthorizationFacts(Guid.Empty, dto.GroupId, null, dto.UserId));

    // UserDto exposes no tenant or owner authority today; publishing userId would grant the
    // actor-user-owner derived role, so only the actor identity is declared.
    public static readonly ResourceDescriptor<UserDto> User = new(
        ResourceKinds.User,
        dto => dto.Id.ToString(),
        dto => new UserAuthorizationFacts(Guid.Empty, null, dto.ActorId));

    public static readonly ResourceDescriptor<CustomPropertyDefinitionDto> CustomPropertyDefinition = new(
        ResourceKinds.CustomPropertyDefinition,
        dto => dto.Id.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EmailDispatchStatusDto> EmailDispatchStatus = new(
        ResourceKinds.EmailDispatch,
        dto => dto.OutboxId.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<IncomingWebhookEffectStatusDto> IncomingWebhookEffectStatus = new(
        ResourceKinds.Webhook,
        dto => dto.EffectOutboxId.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    #endregion

    #region Sub-resources piggybacking on parent tenant authorization

    // These use ResourceKinds.Tenant because their commands authorize via
    // [AuthorizeResource(ResourceKinds.Tenant, ...)], not their own resource kind.
    // This aligns HATEOAS link authorization with command-level authorization.

    /// <summary>Piggybacks on tenant authorization. Commands use [AuthorizeResource(ResourceKinds.Tenant, ...)].</summary>
    public static readonly ResourceDescriptor<EventTemplateDto> EventTemplate = new(
        ResourceKinds.Tenant,
        dto => dto.TenantId.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    public static readonly ResourceDescriptor<EventTemplateListDto> EventTemplateList = new(
        ResourceKinds.Tenant,
        dto => dto.TenantId.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    /// <summary>Piggybacks on tenant authorization. Commands use [AuthorizeResource(ResourceKinds.Tenant, ...)].</summary>
    public static readonly ResourceDescriptor<EventSessionTemplateDto> EventSessionTemplate = new(
        ResourceKinds.Tenant,
        dto => dto.TenantId.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    /// <summary>Piggybacks on tenant authorization. Commands use [AuthorizeResource(ResourceKinds.Tenant, ...)].</summary>
    public static readonly ResourceDescriptor<EventCustomPropertyDefinitionDto> EventCustomPropertyDefinition = new(
        ResourceKinds.Tenant,
        dto => dto.TenantId.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    /// <summary>Piggybacks on tenant authorization. Commands use [AuthorizeResource(ResourceKinds.Tenant, ...)].</summary>
    public static readonly ResourceDescriptor<EventSessionCustomPropertyDefinitionDto> EventSessionCustomPropertyDefinition = new(
        ResourceKinds.Tenant,
        dto => dto.TenantId.ToString(),
        dto => new TenantScopedAuthorizationFacts(dto.TenantId),
        dto => new AuthorizationScope(TenantId: dto.TenantId.ToString()));

    #endregion

    private static EventAuthorizationFacts EventFacts(EventDto dto) => new(
        dto.TenantId,
        dto.Id,
        dto.ActorId,
        dto.ActorUserId,
        dto.ActorOrganizationId,
        dto.ActorGroupId,
        dto.OrganizerActorId,
        dto.OrganizerActorUserId,
        dto.OrganizerActorOrganizationId,
        dto.OrganizerActorGroupId,
        dto.ProvenanceTypeCode ?? dto.ProvenanceTypeId.ToString(),
        dto.SubmittedByUserId);

    private static EventAuthorizationFacts EventListFacts(EventListDto dto) => new(
        dto.TenantId,
        dto.Id,
        dto.ActorId,
        dto.ActorUserId,
        dto.ActorOrganizationId,
        dto.ActorGroupId,
        null,
        null,
        null,
        null,
        null,
        null);

    private static EventAuthorizationFacts EventAuthorizationTargetFacts(Explore.Domain.Event eventEntity) => new(
        eventEntity.TenantId,
        eventEntity.Id,
        eventEntity.ActorId,
        eventEntity.Actor?.UserId,
        eventEntity.Actor?.OrganizationId,
        eventEntity.Actor?.GroupId,
        eventEntity.OrganizerActorId,
        eventEntity.OrganizerActor?.UserId,
        eventEntity.OrganizerActor?.OrganizationId,
        eventEntity.OrganizerActor?.GroupId,
        eventEntity.EventProvenanceType?.MasterCode ?? eventEntity.EventProvenanceTypeId.ToString(),
        eventEntity.SubmittedByUserId);

    private static EventOrganizerClaimAuthorizationFacts EventOrganizerClaimFacts(EventOrganizerClaimDto dto) => new(
        dto.TenantId,
        dto.EventId,
        dto.Id,
        dto.ClaimantActorId,
        dto.ClaimantActorUserId,
        dto.ClaimantActorOrganizationId,
        dto.ClaimantActorGroupId,
        dto.StatusCode ?? dto.StatusId.ToString());

    private static WebhookOwnershipAuthorizationFacts WebhookConsumerFacts(WebhookConsumerDto dto) => new(
        (WebhookConsumerKind)dto.ConsumerKindId,
        dto.OwnerId,
        dto.TenantId,
        dto.InstanceId,
        dto.OrganizationId,
        dto.GroupId,
        dto.OwnerUserId);

    private static WebhookOwnershipAuthorizationFacts WebhookEndpointFacts(WebhookEndpointDto dto)
    {
        var kind = (WebhookConsumerKind)dto.OwnerKindId;
        return new WebhookOwnershipAuthorizationFacts(
            kind,
            dto.OwnerId,
            dto.TenantId,
            dto.InstanceId,
            kind == WebhookConsumerKind.Organization ? dto.OwnerId : null,
            kind == WebhookConsumerKind.Group ? dto.OwnerId : null,
            kind == WebhookConsumerKind.User ? dto.OwnerId : null);
    }

    private static WebhookOwnershipAuthorizationFacts WebhookDeliveryOwnerFacts(
        int ownerKindId,
        Guid ownerId,
        Guid sourceTenantId)
    {
        var kind = (WebhookConsumerKind)ownerKindId;
        var isInstanceOwned = kind == WebhookConsumerKind.Instance;
        return new WebhookOwnershipAuthorizationFacts(
            kind,
            ownerId,
            isInstanceOwned ? null : sourceTenantId,
            isInstanceOwned ? ownerId : null,
            kind == WebhookConsumerKind.Organization ? ownerId : null,
            kind == WebhookConsumerKind.Group ? ownerId : null,
            kind == WebhookConsumerKind.User ? ownerId : null);
    }
}
