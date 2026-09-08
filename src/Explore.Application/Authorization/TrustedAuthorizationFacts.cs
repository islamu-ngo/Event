using Explore.Domain;

namespace Explore.Application.Authorization;

/// <summary>
/// Facts for instance-scoped resources whose only authority zone is the instance itself
/// (instance settings, platform namespaces, ATProto federation records).
/// </summary>
public sealed record InstanceScopedAuthorizationFacts : IAuthorizationFacts
{
    public static readonly InstanceScopedAuthorizationFacts Instance = new();
}

/// <summary>Facts that distinguish the privileged whole-instance manifest export operation.</summary>
public sealed record ConfigurationManifestExportAuthorizationFacts : IAuthorizationFacts;

/// <summary>
/// Facts for a create check evaluated before the aggregate row exists. There is nothing to load, so
/// these facts pass through resource resolution untouched and carry the pre-create lifecycle phase that
/// the create rules match on.
/// </summary>
public sealed record PreCreateAuthorizationFacts(
    Guid TenantId,
    Guid? ParentEventId = null,
    Guid? OrganizationId = null,
    Guid? GroupId = null) : IAuthorizationFacts;

/// <summary>Facts for resources whose authority is the owning tenant and nothing narrower.</summary>
public sealed record TenantScopedAuthorizationFacts(Guid TenantId) : IAuthorizationFacts;

/// <summary>Facts for hierarchical tenant settings, including document identity and instance lock state.</summary>
public sealed record TenantSettingAuthorizationFacts(
    Guid TenantId,
    string? DocumentKey = null,
    bool? IsLockedByInstance = null) : IAuthorizationFacts;

/// <summary>Facts for a persisted organization resource.</summary>
public sealed record OrganizationAuthorizationFacts(
    Guid TenantId,
    Guid? OrganizationId = null) : IAuthorizationFacts;

public sealed record OrganizationMemberAuthorizationFacts(
    Guid TenantId,
    Guid OrganizationId,
    Guid? MemberId,
    Guid? UserId) : IAuthorizationFacts;

public sealed record OrganizationReviewAuthorizationFacts(
    Guid TenantId,
    Guid OrganizationId,
    Guid? UserId) : IAuthorizationFacts;

public sealed record GroupAuthorizationFacts(
    Guid TenantId,
    Guid GroupId,
    Guid? OrganizationId = null) : IAuthorizationFacts;

public sealed record GroupMemberAuthorizationFacts(
    Guid TenantId,
    Guid GroupId,
    Guid? OrganizationId,
    Guid? UserId) : IAuthorizationFacts;

/// <summary>Facts for an event aggregate, including its creating and organizing actor authority.</summary>
public sealed record EventAuthorizationFacts(
    Guid TenantId,
    Guid EventId,
    Guid ActorId,
    Guid? UserId,
    Guid? OrganizationId,
    Guid? GroupId,
    Guid? OrganizerActorId,
    Guid? OrganizerUserId,
    Guid? OrganizerOrganizationId,
    Guid? OrganizerGroupId,
    string? ProvenanceType,
    Guid? SubmittedByUserId) : IAuthorizationFacts;

/// <summary>
/// Facts for resources owned by an event but authorized through it: sessions, session groups,
/// days, agenda items, speakers, and registration forms in their pre-create phase.
/// </summary>
public sealed record EventScopedAuthorizationFacts(
    Guid TenantId,
    Guid EventId,
    Guid? EventSessionId = null) : IAuthorizationFacts;

public sealed record EventOrganizerClaimAuthorizationFacts(
    Guid TenantId,
    Guid EventId,
    Guid ClaimId,
    Guid ClaimantActorId,
    Guid? ClaimantUserId,
    Guid? ClaimantOrganizationId,
    Guid? ClaimantGroupId,
    string Status) : IAuthorizationFacts;

/// <summary>Facts for a registration order, whose account holder may act on their own order.</summary>
public sealed record RegistrationOrderAuthorizationFacts(
    Guid TenantId,
    Guid EventId,
    Guid? AccountUserId) : IAuthorizationFacts;

public sealed record ContactShareAuthorizationFacts(
    Guid TenantId,
    Guid OrganizationId) : IAuthorizationFacts;

public sealed record SupportAccessSessionAuthorizationFacts(
    Guid TenantId,
    Guid? SessionId,
    Guid? ActorUserId,
    string? Mode,
    string? Status) : IAuthorizationFacts;

/// <summary>Facts for a persisted storage object whose content visibility drives read access.</summary>
public sealed record PersistedStorageObjectAuthorizationFacts(
    Guid TenantId,
    Guid StorageObjectId,
    string Visibility,
    string LifecycleState,
    Guid? CreatedBy,
    string? OwningResourceKind,
    Guid? OwningResourceId) : IAuthorizationFacts;

/// <summary>
/// Facts for storage-object collection scopes, where no single object exists to inspect and
/// authorization falls back to the owning tenant.
/// </summary>
public sealed record StorageObjectCollectionAuthorizationFacts(Guid TenantId) : IAuthorizationFacts;

/// <summary>
/// Facts for a platform user resource. <see cref="UserId"/> is deliberately optional: publishing it
/// grants the actor-user-owner derived role, so a descriptor may only set it where that authority
/// already exists today. Widening it requires separate approval.
/// </summary>
public sealed record UserAuthorizationFacts(
    Guid TenantId,
    Guid? UserId,
    Guid? ActorId = null) : IAuthorizationFacts;

public sealed record ActorAuthorizationFacts(
    Guid TenantId,
    Guid ActorId,
    Guid? UserId = null) : IAuthorizationFacts;

/// <summary>
/// Facts for personal, tenant-owned resources whose handlers enforce owner identity
/// (notifications, actor subscriptions, AI conversations).
/// </summary>
public sealed record PersonalResourceAuthorizationFacts(
    Guid TenantId,
    Guid? UserId = null) : IAuthorizationFacts;

public sealed record CustomPropertyProjectionAuthorizationFacts(
    Guid TenantId,
    Guid? EventId = null,
    Guid? EventSessionId = null) : IAuthorizationFacts;

public sealed record WebhookOwnershipAuthorizationFacts(
    WebhookConsumerKind Kind,
    Guid OwnerId,
    Guid? TenantId,
    Guid? InstanceId,
    Guid? OrganizationId,
    Guid? GroupId,
    Guid? UserId) : IAuthorizationFacts
{
    public static WebhookOwnershipAuthorizationFacts From(WebhookOwnershipScope ownership) => new(
        ownership.Kind,
        ownership.OwnerId,
        ownership.TenantId,
        ownership.InstanceId,
        ownership.OrganizationId,
        ownership.GroupId,
        ownership.UserId);
}
