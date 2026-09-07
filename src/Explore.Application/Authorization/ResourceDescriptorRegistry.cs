namespace Explore.Application.Authorization;

using Explore.Application.DTOs.Actor;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.DTOs.Ai;
using Explore.Application.DTOs.Category;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.DTOs.EventDay;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.DTOs.Group;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.DTOs.Location;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.DTOs.Notification;
using Explore.Application.DTOs.Organization;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.DTOs.Tag;
using Explore.Application.DTOs.Tenant;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.DTOs.User;
using Explore.Application.DTOs.Webhooks;

/// <summary>
/// Maps DTO types to their corresponding resource kind identifiers for authorization.
/// Centralizes the convention: each DTO family maps to a single resource kind string.
/// Lives in the Application layer so both API (HATEOAS) and Application (pipeline behaviors) can reference it.
/// </summary>
public static class ResourceDescriptorRegistry
{
    private static readonly IReadOnlyDictionary<Type, string> ResourceKinds = new Dictionary<Type, string>
    {
        // Organization
        [typeof(OrganizationDto)] = global::Explore.Application.Authorization.ResourceKinds.Organization,
        [typeof(OrganizationListDto)] = global::Explore.Application.Authorization.ResourceKinds.Organization,
        [typeof(OrganizationTenantEvidenceDto)] = global::Explore.Application.Authorization.ResourceKinds.Organization,

        // Group
        [typeof(GroupDto)] = global::Explore.Application.Authorization.ResourceKinds.Group,
        [typeof(GroupListDto)] = global::Explore.Application.Authorization.ResourceKinds.Group,

        // Group Member
        [typeof(GroupMemberDto)] = global::Explore.Application.Authorization.ResourceKinds.GroupMember,
        [typeof(GroupMemberListDto)] = global::Explore.Application.Authorization.ResourceKinds.GroupMember,

        // Tenant Settings
        [typeof(TenantBrandingSettingsDocumentDto)] = global::Explore.Application.Authorization.ResourceKinds.TenantSetting,
        [typeof(TenantDirectoryOperatorIdentityDocumentDto)] =
            global::Explore.Application.Authorization.ResourceKinds.TenantSetting,

        // Event
        [typeof(EventDto)] = global::Explore.Application.Authorization.ResourceKinds.Event,
        [typeof(EventListDto)] = global::Explore.Application.Authorization.ResourceKinds.Event,

        [typeof(EventAgendaItemDto)] = global::Explore.Application.Authorization.ResourceKinds.EventAgendaItem,
        [typeof(EventAgendaItemListDto)] = global::Explore.Application.Authorization.ResourceKinds.EventAgendaItem,

        [typeof(EventDayDto)] = global::Explore.Application.Authorization.ResourceKinds.EventDay,
        [typeof(EventDayListDto)] = global::Explore.Application.Authorization.ResourceKinds.EventDay,

        // Tenant
        [typeof(TenantDto)] = global::Explore.Application.Authorization.ResourceKinds.Tenant,
        [typeof(TenantListDto)] = global::Explore.Application.Authorization.ResourceKinds.Tenant,

        // User
        [typeof(UserDto)] = global::Explore.Application.Authorization.ResourceKinds.User,

        // Tenant User Role Grant
        [typeof(TenantUserRoleGrantDto)] = global::Explore.Application.Authorization.ResourceKinds.TenantUserRoleGrant,
        [typeof(TenantUserRoleGrantListDto)] = global::Explore.Application.Authorization.ResourceKinds.TenantUserRoleGrant,

        // Actor Subscription
        [typeof(ActorSubscriptionDto)] = global::Explore.Application.Authorization.ResourceKinds.ActorSubscription,
        [typeof(ActorSubscriptionListDto)] = global::Explore.Application.Authorization.ResourceKinds.ActorSubscription,

        // AI Conversation
        [typeof(AiConversationDto)] = global::Explore.Application.Authorization.ResourceKinds.AiConversation,
        [typeof(AiConversationSummaryDto)] = global::Explore.Application.Authorization.ResourceKinds.AiConversation,

        // Tag
        [typeof(TagDto)] = global::Explore.Application.Authorization.ResourceKinds.Tag,
        [typeof(TagListDto)] = global::Explore.Application.Authorization.ResourceKinds.Tag,

        // Storage Object
        [typeof(StorageObjectDto)] = global::Explore.Application.Authorization.ResourceKinds.StorageObject,
        [typeof(StorageObjectListDto)] = global::Explore.Application.Authorization.ResourceKinds.StorageObject,

        // Organization Review
        [typeof(OrganizationReviewDto)] = global::Explore.Application.Authorization.ResourceKinds.OrganizationReview,

        // Organization Member
        [typeof(OrganizationMemberDto)] = global::Explore.Application.Authorization.ResourceKinds.OrganizationMember,

        // Location
        [typeof(LocationDto)] = global::Explore.Application.Authorization.ResourceKinds.Location,
        [typeof(LocationListDto)] = global::Explore.Application.Authorization.ResourceKinds.Location,
        [typeof(LocationRoomDto)] = global::Explore.Application.Authorization.ResourceKinds.LocationRoom,
        [typeof(LocationRoomListDto)] = global::Explore.Application.Authorization.ResourceKinds.LocationRoom,

        // Event Session
        [typeof(EventSessionDto)] = global::Explore.Application.Authorization.ResourceKinds.EventSession,
        [typeof(EventSessionListDto)] = global::Explore.Application.Authorization.ResourceKinds.EventSession,

        // Event Session Group
        [typeof(EventSessionGroupDto)] = global::Explore.Application.Authorization.ResourceKinds.EventSessionGroup,
        [typeof(EventSessionGroupListDto)] = global::Explore.Application.Authorization.ResourceKinds.EventSessionGroup,

        [typeof(EventTemplateDto)] = global::Explore.Application.Authorization.ResourceKinds.Tenant,
        [typeof(EventTemplateListDto)] = global::Explore.Application.Authorization.ResourceKinds.Tenant,

        // Event Session Agenda Item
        [typeof(EventSessionAgendaItemDto)] = global::Explore.Application.Authorization.ResourceKinds.EventSessionAgendaItem,
        [typeof(EventSessionAgendaItemListDto)] = global::Explore.Application.Authorization.ResourceKinds.EventSessionAgendaItem,

        [typeof(EventSessionSpeakerDto)] = global::Explore.Application.Authorization.ResourceKinds.EventSession,
        [typeof(EventSessionSpeakerListDto)] = global::Explore.Application.Authorization.ResourceKinds.EventSession,

        [typeof(EventOrganizerClaimDto)] = global::Explore.Application.Authorization.ResourceKinds.EventOrganizerClaim,

        // Category
        [typeof(CategoryDto)] = global::Explore.Application.Authorization.ResourceKinds.Category,
        [typeof(CategoryListDto)] = global::Explore.Application.Authorization.ResourceKinds.Category,

        // Custom Property Definition
        [typeof(CustomPropertyDefinitionDto)] = global::Explore.Application.Authorization.ResourceKinds.CustomPropertyDefinition,
        [typeof(CustomPropertyDefinitionListDto)] = global::Explore.Application.Authorization.ResourceKinds.CustomPropertyDefinition,

        // Notification
        [typeof(NotificationDto)] = global::Explore.Application.Authorization.ResourceKinds.Notification,
        [typeof(NotificationListDto)] = global::Explore.Application.Authorization.ResourceKinds.Notification,

        // Email Dispatch
        [typeof(EmailDispatchStatusDto)] = global::Explore.Application.Authorization.ResourceKinds.EmailDispatch,
        [typeof(IncomingWebhookEffectStatusDto)] = global::Explore.Application.Authorization.ResourceKinds.Webhook,

        // Actor
        [typeof(ActorDto)] = global::Explore.Application.Authorization.ResourceKinds.Actor,
        [typeof(ActorListDto)] = global::Explore.Application.Authorization.ResourceKinds.Actor
    };

    /// <summary>
    /// Resolves the resource kind for a given DTO type.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no mapping exists for the given type.</exception>
    public static string ResolveResourceKind(Type resourceType)
    {
        if (ResourceKinds.TryGetValue(resourceType, out var resourceKind))
            return resourceKind;

        throw new InvalidOperationException($"No resource kind mapping configured for type '{resourceType.Name}'.");
    }
}
