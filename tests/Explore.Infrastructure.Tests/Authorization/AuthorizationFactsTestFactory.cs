using Explore.Application.Authorization;
using Explore.Domain;

namespace Explore.Infrastructure.Tests.Authorization;

/// <summary>
/// Production code cannot build policy facts from a dictionary — that is the whole point of the typed
/// boundary. The behavioural corpus, however, was written against the attribute names the providers used
/// to read, and those scenarios are still the specification. This factory performs the same translation
/// the trusted resolvers and resource descriptors perform, so a scenario expressed as attributes reaches
/// the provider as the exact fact record production would have supplied.
/// </summary>
internal static class AuthorizationFactsTestFactory
{
    public static IAuthorizationFacts? Create(
        string resourceKind,
        string resourceId,
        IDictionary<string, object>? attributes)
    {
        if (attributes is null)
            return null;

        if (Phase(attributes) is not null)
        {
            return new PreCreateAuthorizationFacts(
                Guid(attributes, "tenantId") ?? System.Guid.Empty,
                Guid(attributes, "eventId"),
                Guid(attributes, "organizationId"),
                Guid(attributes, "groupId"));
        }

        var tenantId = Guid(attributes, "tenantId") ?? System.Guid.Empty;

        return resourceKind switch
        {
            ResourceKinds.InstanceSetting or ResourceKinds.PlatformNamespace or
                ResourceKinds.AtprotoRecord or ResourceKinds.IndexedDid =>
                InstanceScopedAuthorizationFacts.Instance,

            ResourceKinds.TenantSetting => new TenantSettingAuthorizationFacts(
                tenantId,
                Text(attributes, "documentKey"),
                Flag(attributes, "isLockedByInstance")),

            ResourceKinds.Event or ResourceKinds.RegistrationForm => EventFacts(resourceId, tenantId, attributes),

            ResourceKinds.EventOrganizerClaim => ClaimFacts(resourceId, tenantId, attributes),

            ResourceKinds.EventSession or ResourceKinds.EventSessionGroup or
                ResourceKinds.EventSessionAgendaItem or ResourceKinds.EventDay or
                ResourceKinds.EventAgendaItem => new EventScopedAuthorizationFacts(
                    tenantId,
                    Guid(attributes, "eventId") ?? System.Guid.Empty,
                    Guid(attributes, "eventSessionId")),

            ResourceKinds.RegistrationOrder => new RegistrationOrderAuthorizationFacts(
                tenantId,
                Guid(attributes, "eventId") ?? System.Guid.Empty,
                Guid(attributes, "accountUserId")),

            ResourceKinds.Organization => new OrganizationAuthorizationFacts(
                tenantId,
                Guid(attributes, "organizationId")),

            ResourceKinds.OrganizationMember => new OrganizationMemberAuthorizationFacts(
                tenantId,
                Guid(attributes, "organizationId") ?? ParsedId(resourceId),
                Guid(attributes, "memberId"),
                Guid(attributes, "userId")),

            ResourceKinds.OrganizationReview => new OrganizationReviewAuthorizationFacts(
                tenantId,
                Guid(attributes, "organizationId") ?? ParsedId(resourceId),
                Guid(attributes, "userId")),

            ResourceKinds.Group => new GroupAuthorizationFacts(
                tenantId,
                Guid(attributes, "groupId") ?? ParsedId(resourceId),
                Guid(attributes, "organizationId")),

            ResourceKinds.GroupMember => new GroupMemberAuthorizationFacts(
                tenantId,
                Guid(attributes, "groupId") ?? ParsedId(resourceId),
                Guid(attributes, "organizationId"),
                Guid(attributes, "userId")),

            ResourceKinds.EventContactShareConsent => new ContactShareAuthorizationFacts(
                tenantId,
                Guid(attributes, "organizationId") ?? ParsedId(resourceId)),

            ResourceKinds.SupportAccessSession => new SupportAccessSessionAuthorizationFacts(
                tenantId,
                Guid(attributes, "sessionId") ?? ParsedIdOrNull(resourceId),
                Guid(attributes, "actorUserId"),
                Text(attributes, "mode"),
                Text(attributes, "status")),

            ResourceKinds.StorageObject => StorageFacts(resourceId, tenantId, attributes),

            ResourceKinds.Webhook => WebhookFacts(tenantId, attributes),

            ResourceKinds.User => new UserAuthorizationFacts(
                tenantId,
                Guid(attributes, "userId"),
                Guid(attributes, "actorId")),

            ResourceKinds.Actor => new ActorAuthorizationFacts(
                tenantId,
                Guid(attributes, "actorId") ?? ParsedId(resourceId),
                Guid(attributes, "userId")),

            ResourceKinds.Notification or ResourceKinds.ActorSubscription or ResourceKinds.AiConversation =>
                new PersonalResourceAuthorizationFacts(tenantId, Guid(attributes, "userId")),

            ResourceKinds.CustomPropertyProjection => new CustomPropertyProjectionAuthorizationFacts(
                tenantId,
                Guid(attributes, "eventId"),
                Guid(attributes, "eventSessionId")),

            _ => new TenantScopedAuthorizationFacts(tenantId)
        };
    }

    private static IAuthorizationFacts EventFacts(
        string resourceId,
        Guid tenantId,
        IDictionary<string, object> attributes)
    {
        var eventId = Guid(attributes, "eventId") ?? ParsedId(resourceId);
        var actorId = Guid(attributes, "actorId");
        var userId = Guid(attributes, "userId");
        var organizationId = Guid(attributes, "organizationId");
        var groupId = Guid(attributes, "groupId");
        var organizerActorId = Guid(attributes, "organizerActorId");
        var organizerUserId = Guid(attributes, "organizerUserId");
        var organizerOrganizationId = Guid(attributes, "organizerOrganizationId");
        var organizerGroupId = Guid(attributes, "organizerGroupId");
        var provenanceType = Text(attributes, "provenanceType");
        var submittedByUserId = Guid(attributes, "submittedByUserId");

        // A scenario that names any owner at all needs the full authority record: dropping to the scoped
        // record would erase the very ownership the rule under test is meant to weigh.
        var hasAuthority = actorId is not null
            || userId is not null
            || organizationId is not null
            || groupId is not null
            || organizerActorId is not null
            || organizerUserId is not null
            || organizerOrganizationId is not null
            || organizerGroupId is not null
            || provenanceType is not null
            || submittedByUserId is not null;

        return hasAuthority
            ? new EventAuthorizationFacts(
                tenantId,
                eventId,
                actorId ?? System.Guid.Empty,
                userId,
                organizationId,
                groupId,
                organizerActorId,
                organizerUserId,
                organizerOrganizationId,
                organizerGroupId,
                provenanceType,
                submittedByUserId)
            : new EventScopedAuthorizationFacts(tenantId, eventId);
    }

    private static IAuthorizationFacts ClaimFacts(
        string resourceId,
        Guid tenantId,
        IDictionary<string, object> attributes)
    {
        var claimId = Guid(attributes, "claimId") ?? ParsedIdOrNull(resourceId);
        var claimantActorId = Guid(attributes, "claimantActorId");
        var claimantUserId = Guid(attributes, "claimantUserId");
        var claimantOrganizationId = Guid(attributes, "claimantOrganizationId");
        var claimantGroupId = Guid(attributes, "claimantGroupId");

        // Withdrawal is decided on the claimant's own control of the claim, and that control may be held
        // through a user, organization or group. Any one of them is enough to warrant claim facts.
        var hasClaimant = claimantActorId is not null
            || claimantUserId is not null
            || claimantOrganizationId is not null
            || claimantGroupId is not null;

        return claimId is null || !hasClaimant
            ? EventFacts(resourceId, tenantId, attributes)
            : new EventOrganizerClaimAuthorizationFacts(
                tenantId,
                Guid(attributes, "eventId") ?? System.Guid.Empty,
                claimId.Value,
                claimantActorId ?? System.Guid.Empty,
                claimantUserId,
                claimantOrganizationId,
                claimantGroupId,
                Text(attributes, "status") ?? string.Empty);
    }

    private static IAuthorizationFacts StorageFacts(
        string resourceId,
        Guid tenantId,
        IDictionary<string, object> attributes)
    {
        if (string.Equals(Text(attributes, "authorizationScope"), "collection", StringComparison.Ordinal))
            return new StorageObjectCollectionAuthorizationFacts(tenantId);

        var visibility = Text(attributes, "visibility");
        var lifecycleState = Text(attributes, "lifecycleState");

        return visibility is null || lifecycleState is null
            ? new StorageObjectCollectionAuthorizationFacts(tenantId)
            : new PersistedStorageObjectAuthorizationFacts(
                tenantId,
                Guid(attributes, "storageObjectId") ?? ParsedId(resourceId),
                visibility,
                lifecycleState,
                Guid(attributes, "createdBy"),
                Text(attributes, "owningResourceKind"),
                Guid(attributes, "owningResourceId"));
    }

    private static IAuthorizationFacts WebhookFacts(Guid tenantId, IDictionary<string, object> attributes)
    {
        if (!attributes.TryGetValue("ownerKindId", out var rawKind) ||
            !int.TryParse(rawKind?.ToString(), out var ownerKindId) ||
            !Enum.IsDefined(typeof(WebhookConsumerKind), ownerKindId))
        {
            return new TenantScopedAuthorizationFacts(tenantId);
        }

        return new WebhookOwnershipAuthorizationFacts(
            (WebhookConsumerKind)ownerKindId,
            Guid(attributes, "ownerId") ?? System.Guid.Empty,
            tenantId == System.Guid.Empty ? null : tenantId,
            Guid(attributes, "instanceId"),
            Guid(attributes, "organizationId"),
            Guid(attributes, "groupId"),
            Guid(attributes, "userId"));
    }

    private static string? Phase(IDictionary<string, object> attributes) => Text(attributes, "authorizationPhase");

    private static Guid? Guid(IDictionary<string, object> attributes, string name) =>
        attributes.TryGetValue(name, out var value) switch
        {
            true when value is Guid guid && guid != System.Guid.Empty => guid,
            true when System.Guid.TryParse(value?.ToString(), out var parsed) && parsed != System.Guid.Empty => parsed,
            _ => null
        };

    private static string? Text(IDictionary<string, object> attributes, string name) =>
        attributes.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value?.ToString())
            ? value.ToString()
            : null;

    private static bool? Flag(IDictionary<string, object> attributes, string name) =>
        attributes.TryGetValue(name, out var value) && bool.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : null;

    private static Guid ParsedId(string resourceId) =>
        System.Guid.TryParse(resourceId, out var value) ? value : System.Guid.Empty;

    private static Guid? ParsedIdOrNull(string resourceId) =>
        System.Guid.TryParse(resourceId, out var value) ? value : null;
}
