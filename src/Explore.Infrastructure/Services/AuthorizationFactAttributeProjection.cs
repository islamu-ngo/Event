using Explore.Application.Authorization;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Translates a trusted <see cref="IAuthorizationFacts"/> record into the attribute map consumed by the
/// Local evaluator and sent to the Cerbos PDP as <c>request.resource.attr.*</c>.
/// <para>
/// The mapping is exhaustive over the closed fact catalog: an unmapped fact type yields <c>null</c>, which
/// every provider treats as "no trusted facts" and therefore denies. Callers cannot inject attributes.
/// </para>
/// </summary>
public static class AuthorizationFactAttributeProjection
{
    public static Dictionary<string, object>? ToAttributes(IAuthorizationFacts? facts) => facts switch
    {
        InstanceScopedAuthorizationFacts => [],
        ConfigurationManifestExportAuthorizationFacts => new(StringComparer.Ordinal)
        {
            ["configurationManifestExport"] = true
        },
        PreCreateAuthorizationFacts value => PreCreate(value),
        TenantScopedAuthorizationFacts value => TenantScoped(value),
        TenantSettingAuthorizationFacts value => TenantSetting(value),
        OrganizationAuthorizationFacts value => Organization(value),
        OrganizationMemberAuthorizationFacts value => OrganizationMember(value),
        OrganizationReviewAuthorizationFacts value => OrganizationReview(value),
        GroupAuthorizationFacts value => Group(value),
        GroupMemberAuthorizationFacts value => GroupMember(value),
        EventAuthorizationFacts value => Event(value),
        EventScopedAuthorizationFacts value => EventScoped(value),
        EventOrganizerClaimAuthorizationFacts value => EventOrganizerClaim(value),
        RegistrationOrderAuthorizationFacts value => RegistrationOrder(value),
        ContactShareAuthorizationFacts value => ContactShare(value),
        SupportAccessSessionAuthorizationFacts value => SupportAccessSession(value),
        PersistedStorageObjectAuthorizationFacts value => StorageObject(value),
        StorageObjectCollectionAuthorizationFacts value => TenantOnly(value.TenantId),
        UserAuthorizationFacts value => User(value),
        ActorAuthorizationFacts value => Actor(value),
        PersonalResourceAuthorizationFacts value => PersonalResource(value),
        CustomPropertyProjectionAuthorizationFacts value => CustomPropertyProjection(value),
        StorageUploadIntentFacts value => StorageUploadIntent(value),
        WebhookOwnershipAuthorizationFacts value => Webhook(value),
        _ => null
    };

    private static Dictionary<string, object> TenantScoped(TenantScopedAuthorizationFacts value) =>
        TenantOnly(value.TenantId);

    private static Dictionary<string, object> PreCreate(PreCreateAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        attributes["authorizationPhase"] = AuthorizationPhases.PreCreate;
        Add(attributes, "eventId", value.ParentEventId);
        Add(attributes, "organizationId", value.OrganizationId);
        Add(attributes, "groupId", value.GroupId);
        return attributes;
    }

    private static Dictionary<string, object> TenantSetting(TenantSettingAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "documentKey", value.DocumentKey);
        if (value.IsLockedByInstance is { } isLocked)
            attributes["isLockedByInstance"] = isLocked;
        return attributes;
    }

    private static Dictionary<string, object> Organization(OrganizationAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "organizationId", value.OrganizationId);
        return attributes;
    }

    private static Dictionary<string, object> OrganizationMember(OrganizationMemberAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "organizationId", value.OrganizationId);
        Add(attributes, "memberId", value.MemberId);
        Add(attributes, "userId", value.UserId);
        return attributes;
    }

    private static Dictionary<string, object> OrganizationReview(OrganizationReviewAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "organizationId", value.OrganizationId);
        Add(attributes, "userId", value.UserId);
        return attributes;
    }

    private static Dictionary<string, object> Group(GroupAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "groupId", value.GroupId);
        Add(attributes, "organizationId", value.OrganizationId);
        return attributes;
    }

    private static Dictionary<string, object> GroupMember(GroupMemberAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "groupId", value.GroupId);
        Add(attributes, "organizationId", value.OrganizationId);
        Add(attributes, "userId", value.UserId);
        return attributes;
    }

    private static Dictionary<string, object> Event(EventAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "eventId", value.EventId);
        Add(attributes, "actorId", value.ActorId);
        Add(attributes, "userId", value.UserId);
        Add(attributes, "organizationId", value.OrganizationId);
        Add(attributes, "groupId", value.GroupId);
        Add(attributes, "organizerActorId", value.OrganizerActorId);
        Add(attributes, "organizerUserId", value.OrganizerUserId);
        Add(attributes, "organizerOrganizationId", value.OrganizerOrganizationId);
        Add(attributes, "organizerGroupId", value.OrganizerGroupId);
        Add(attributes, "provenanceType", value.ProvenanceType);
        Add(attributes, "submittedByUserId", value.SubmittedByUserId);
        return attributes;
    }

    private static Dictionary<string, object> EventScoped(EventScopedAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "eventId", value.EventId);
        Add(attributes, "eventSessionId", value.EventSessionId);
        return attributes;
    }

    private static Dictionary<string, object> EventOrganizerClaim(EventOrganizerClaimAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "eventId", value.EventId);
        Add(attributes, "claimId", value.ClaimId);
        Add(attributes, "claimantActorId", value.ClaimantActorId);
        attributes["status"] = value.Status;
        Add(attributes, "claimantUserId", value.ClaimantUserId);
        Add(attributes, "claimantOrganizationId", value.ClaimantOrganizationId);
        Add(attributes, "claimantGroupId", value.ClaimantGroupId);
        return attributes;
    }

    private static Dictionary<string, object> RegistrationOrder(RegistrationOrderAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "eventId", value.EventId);
        Add(attributes, "accountUserId", value.AccountUserId);
        return attributes;
    }

    private static Dictionary<string, object> ContactShare(ContactShareAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "organizationId", value.OrganizationId);
        return attributes;
    }

    private static Dictionary<string, object> SupportAccessSession(SupportAccessSessionAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "sessionId", value.SessionId);
        Add(attributes, "actorUserId", value.ActorUserId);
        Add(attributes, "mode", value.Mode);
        Add(attributes, "status", value.Status);
        return attributes;
    }

    private static Dictionary<string, object> StorageObject(PersistedStorageObjectAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "storageObjectId", value.StorageObjectId);
        attributes["visibility"] = value.Visibility;
        attributes["lifecycleState"] = value.LifecycleState;
        Add(attributes, "createdBy", value.CreatedBy);
        Add(attributes, "owningResourceKind", value.OwningResourceKind);
        Add(attributes, "owningResourceId", value.OwningResourceId);
        return attributes;
    }

    private static Dictionary<string, object> StorageUploadIntent(StorageUploadIntentFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        attributes["authorizationPhase"] = AuthorizationPhases.PreCreate;
        Add(attributes, "owningResourceKind", value.OwningResourceKind);
        Add(attributes, "owningResourceId", value.OwningResourceId);
        Add(attributes, "organizationId", value.OwningOrganizationId);
        Add(attributes, "userId", value.SubjectUserId);
        return attributes;
    }

    private static Dictionary<string, object> User(UserAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "userId", value.UserId);
        Add(attributes, "actorId", value.ActorId);
        return attributes;
    }

    private static Dictionary<string, object> Actor(ActorAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "actorId", value.ActorId);
        Add(attributes, "userId", value.UserId);
        return attributes;
    }

    private static Dictionary<string, object> PersonalResource(PersonalResourceAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "userId", value.UserId);
        return attributes;
    }

    private static Dictionary<string, object> CustomPropertyProjection(
        CustomPropertyProjectionAuthorizationFacts value)
    {
        var attributes = TenantOnly(value.TenantId);
        Add(attributes, "eventId", value.EventId);
        Add(attributes, "eventSessionId", value.EventSessionId);
        return attributes;
    }

    private static Dictionary<string, object> Webhook(WebhookOwnershipAuthorizationFacts value)
    {
        var attributes = new Dictionary<string, object>
        {
            ["ownerKindId"] = (int)value.Kind,
            ["ownerKind"] = value.Kind.ToString().ToUpperInvariant(),
            ["ownerId"] = value.OwnerId.ToString("D")
        };
        Add(attributes, "tenantId", value.TenantId);
        Add(attributes, "instanceId", value.InstanceId);
        Add(attributes, "organizationId", value.OrganizationId);
        Add(attributes, "groupId", value.GroupId);
        Add(attributes, "userId", value.UserId);
        return attributes;
    }

    private static Dictionary<string, object> TenantOnly(Guid tenantId)
    {
        var attributes = new Dictionary<string, object>(StringComparer.Ordinal);
        if (tenantId != Guid.Empty)
            attributes["tenantId"] = tenantId.ToString("D");
        return attributes;
    }

    private static void Add(Dictionary<string, object> attributes, string key, Guid? value)
    {
        if (value is { } guid && guid != Guid.Empty)
            attributes[key] = guid.ToString("D");
    }

    private static void Add(Dictionary<string, object> attributes, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            attributes[key] = value;
    }
}
