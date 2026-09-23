using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Services;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class EventResource : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private readonly List<EventResourceAudienceRule> _audienceRules = [];
    private Guid _tenantId;

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResource));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResource));
    }
    public Guid EventId { get; private set; }
    public Guid? EventSessionId { get; private set; }
    public Guid SessionScopeId { get; private set; }
    public int EventResourceKindId { get; private set; }
    public int EventResourceDeliveryTypeId { get; private set; }
    public int PublicationStateId { get; private set; }
    public int DisclosureModeId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? PublicTitle { get; private set; }
    public string? Description { get; private set; }
    public string? SensitiveNotes { get; private set; }
    public string? LanguageCode { get; private set; }
    public string? AccessibilityNote { get; private set; }
    public int SortOrder { get; private set; }
    public Guid? AccessibleAlternativeEventResourceId { get; private set; }
    public Guid? StorageObjectId { get; private set; }
    public string? ExternalDestinationCiphertext { get; private set; }
    public int? ExternalDestinationProtectionVersion { get; private set; }
    public string? ExternalDestinationSafeOrigin { get; private set; }
    public DateTimeOffset? AvailabilityAbsoluteStartUtc { get; private set; }
    public DateTimeOffset? AvailabilityAbsoluteEndUtc { get; private set; }
    public int? AvailabilityStartAnchorId { get; private set; }
    public long? AvailabilityStartOffsetTicks { get; private set; }
    public int? AvailabilityEndAnchorId { get; private set; }
    public long? AvailabilityEndOffsetTicks { get; private set; }
    public EventResourceAvailability Availability => EventResourceAvailability.Create(
        AvailabilityAbsoluteStartUtc,
        AvailabilityAbsoluteEndUtc,
        (EventResourceAvailabilityAnchorEnum?)AvailabilityStartAnchorId,
        AvailabilityStartOffsetTicks.HasValue ? TimeSpan.FromTicks(AvailabilityStartOffsetTicks.Value) : null,
        (EventResourceAvailabilityAnchorEnum?)AvailabilityEndAnchorId,
        AvailabilityEndOffsetTicks.HasValue ? TimeSpan.FromTicks(AvailabilityEndOffsetTicks.Value) : null);
    public IReadOnlyCollection<EventResourceAudienceRule> AudienceRules => _audienceRules.AsReadOnly();
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }
    public Guid ConcurrencyStamp { get; set; }

    bool ISoftDeletable.IsDeleted
    {
        get => IsDeleted;
        set
        {
            if (IsDeleted && !value)
            {
                throw new InvalidOperationException("A deleted resource cannot be resurrected.");
            }
            IsDeleted = value;
        }
    }
    DateTime? ISoftDeletable.DeletedAt { get => DeletedAt; set => DeletedAt = value; }
    Guid? ISoftDeletable.DeletedBy { get => DeletedBy; set => DeletedBy = value; }

    private EventResource() { }

    public static EventResource CreateDraft(
        Guid id, Guid tenantId, Guid eventId, Guid? sessionId,
        EventResourceMetadata metadata, EventResourceDeliveryTypeEnum deliveryType,
        EventResourceAvailability availability, IEnumerable<EventResourceAudienceRule> audienceRules,
        Guid actorId, DateTime occurredAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || eventId == Guid.Empty || sessionId == Guid.Empty
            || !Enum.IsDefined(deliveryType))
        {
            throw new ArgumentException("Resource ownership and delivery type must be explicit and valid.");
        }
        RequireActorAndTime(actorId, occurredAtUtc);
        ArgumentNullException.ThrowIfNull(availability);
        var resource = new EventResource
        {
            Id = id,
            TenantId = tenantId,
            EventId = eventId,
            EventSessionId = sessionId,
            SessionScopeId = sessionId ?? eventId,
            EventResourceDeliveryTypeId = (int)deliveryType,
            PublicationStateId = (int)EventResourcePublicationStateEnum.Draft,
            CreatedAt = occurredAtUtc,
            CreatedBy = actorId
        };
        resource.ApplyAvailability(availability);
        resource.ApplyMetadata(metadata);
        resource.ReplaceAudienceRules(resource.ValidateAudience(audienceRules));
        return resource;
    }

    public void UpdateMetadata(EventResourceMetadata metadata, Guid expectedStamp, Guid actorId, DateTime occurredAtUtc)
    {
        RequireWritable(expectedStamp, actorId, occurredAtUtc);
        ApplyMetadata(metadata);
        Touch(actorId, occurredAtUtc);
    }

    public void ReplacePolicy(EventResourceAvailability availability,
        IEnumerable<EventResourceAudienceRule> audienceRules, Guid expectedStamp, Guid actorId, DateTime occurredAtUtc)
    {
        RequireWritable(expectedStamp, actorId, occurredAtUtc);
        ArgumentNullException.ThrowIfNull(availability);
        var rules = ValidateAudience(audienceRules);
        ApplyAvailability(availability);
        ReplaceAudienceRules(rules);
        Touch(actorId, occurredAtUtc);
    }

    public Guid? SetStoredFile(Guid storageObjectId, Guid expectedStamp, Guid actorId, DateTime occurredAtUtc)
    {
        RequireWritable(expectedStamp, actorId, occurredAtUtc);
        if (storageObjectId == Guid.Empty)
        {
            throw new ArgumentException("A stored object identity is required.", nameof(storageObjectId));
        }
        var detached = StorageObjectId == storageObjectId ? null : StorageObjectId;
        StorageObjectId = storageObjectId;
        ExternalDestinationCiphertext = null;
        ExternalDestinationProtectionVersion = null;
        ExternalDestinationSafeOrigin = null;
        EventResourceDeliveryTypeId = (int)EventResourceDeliveryTypeEnum.StoredFile;
        Touch(actorId, occurredAtUtc);
        return detached;
    }

    public Guid? SetExternalDestination(string ciphertext, int protectionVersion, string safeOrigin,
        Guid expectedStamp, Guid actorId, DateTime occurredAtUtc)
    {
        RequireWritable(expectedStamp, actorId, occurredAtUtc);
        if (string.IsNullOrWhiteSpace(ciphertext) || protectionVersion < 1
            || !Uri.TryCreate(safeOrigin, UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(origin.UserInfo)
            || origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
        {
            throw new ArgumentException("A protected destination and safe HTTPS origin are required.");
        }
        var detached = StorageObjectId;
        StorageObjectId = null;
        ExternalDestinationCiphertext = ciphertext;
        ExternalDestinationProtectionVersion = protectionVersion;
        ExternalDestinationSafeOrigin = origin.GetLeftPart(UriPartial.Authority);
        EventResourceDeliveryTypeId = (int)EventResourceDeliveryTypeEnum.ExternalLink;
        Touch(actorId, occurredAtUtc);
        return detached;
    }

    public void Publish(EventResourceParentFacts parent, bool payloadSafetySatisfied,
        Guid expectedStamp, Guid actorId, DateTime occurredAtUtc)
    {
        RequireWritable(expectedStamp, actorId, occurredAtUtc);
        if (PublicationStateId != (int)EventResourcePublicationStateEnum.Draft)
        {
            throw new InvalidOperationException("Only a draft can be published.");
        }
        RequirePublishable(parent, payloadSafetySatisfied);
        PublicationStateId = (int)EventResourcePublicationStateEnum.Published;
        Touch(actorId, occurredAtUtc);
    }

    public void Withdraw(Guid expectedStamp, Guid actorId, DateTime occurredAtUtc)
    {
        RequireWritable(expectedStamp, actorId, occurredAtUtc);
        if (PublicationStateId != (int)EventResourcePublicationStateEnum.Published)
        {
            throw new InvalidOperationException("Only a published resource can be withdrawn.");
        }
        PublicationStateId = (int)EventResourcePublicationStateEnum.Withdrawn;
        Touch(actorId, occurredAtUtc);
    }

    public void Republish(EventResourceParentFacts parent, bool payloadSafetySatisfied,
        Guid expectedStamp, Guid actorId, DateTime occurredAtUtc)
    {
        RequireWritable(expectedStamp, actorId, occurredAtUtc);
        if (PublicationStateId != (int)EventResourcePublicationStateEnum.Withdrawn)
        {
            throw new InvalidOperationException("Only a withdrawn resource can be republished.");
        }
        RequirePublishable(parent, payloadSafetySatisfied);
        PublicationStateId = (int)EventResourcePublicationStateEnum.Published;
        Touch(actorId, occurredAtUtc);
    }

    public void Archive(Guid expectedStamp, Guid actorId, DateTime occurredAtUtc)
    {
        RequireWritable(expectedStamp, actorId, occurredAtUtc);
        if (PublicationStateId is not (int)EventResourcePublicationStateEnum.Draft
            and not (int)EventResourcePublicationStateEnum.Withdrawn)
        {
            throw new InvalidOperationException("Withdraw a published resource before archiving it.");
        }
        PublicationStateId = (int)EventResourcePublicationStateEnum.Archived;
        Touch(actorId, occurredAtUtc);
    }

    public Guid? Delete(Guid expectedStamp, Guid actorId, DateTime occurredAtUtc)
    {
        RequireActorAndTime(actorId, occurredAtUtc);
        if (IsDeleted || ConcurrencyStamp != expectedStamp)
        {
            throw new InvalidOperationException("The resource was deleted or changed.");
        }
        var detached = StorageObjectId;
        StorageObjectId = null;
        ExternalDestinationCiphertext = null;
        ExternalDestinationProtectionVersion = null;
        ExternalDestinationSafeOrigin = null;
        IsDeleted = true;
        DeletedAt = occurredAtUtc;
        DeletedBy = actorId;
        Touch(actorId, occurredAtUtc);
        return detached;
    }

    /// <summary>Irreversibly removes delivery and private content with a heavy-redacted parent.</summary>
    public void ApplyParentModeration(string redactedText, Guid? moderatorUserId, DateTime occurredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(redactedText) || moderatorUserId == Guid.Empty
            || occurredAtUtc == default || occurredAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Parent moderation requires safe replacement text and a UTC timestamp.");
        Title = redactedText;
        PublicTitle = redactedText;
        Description = null;
        SensitiveNotes = null;
        AccessibilityNote = null;
        AccessibleAlternativeEventResourceId = null;
        StorageObjectId = null;
        ExternalDestinationCiphertext = null;
        ExternalDestinationProtectionVersion = null;
        ExternalDestinationSafeOrigin = null;
        PublicationStateId = (int)EventResourcePublicationStateEnum.Withdrawn;
        IsDeleted = true;
        DeletedAt = occurredAtUtc;
        DeletedBy = moderatorUserId;
        UpdatedAt = occurredAtUtc;
        UpdatedBy = moderatorUserId;
    }

    public bool HasPublishablePayload() => (EventResourceDeliveryTypeEnum)EventResourceDeliveryTypeId switch
    {
        EventResourceDeliveryTypeEnum.StoredFile => StorageObjectId.HasValue && StorageObjectId != Guid.Empty
            && ExternalDestinationCiphertext is null && ExternalDestinationProtectionVersion is null
            && ExternalDestinationSafeOrigin is null,
        EventResourceDeliveryTypeEnum.ExternalLink => StorageObjectId is null
            && !string.IsNullOrWhiteSpace(ExternalDestinationCiphertext)
            && ExternalDestinationProtectionVersion > 0 && !string.IsNullOrWhiteSpace(ExternalDestinationSafeOrigin),
        _ => false
    };

    private void RequirePublishable(EventResourceParentFacts parent, bool payloadSafetySatisfied)
    {
        if (!payloadSafetySatisfied || !HasPublishablePayload()
            || !EventResourceAccessRules.IsParentEligible(this, parent)
            || !Availability.TryResolve(parent.Schedule, out _, out _))
        {
            throw new InvalidOperationException("Resource publication prerequisites are not satisfied.");
        }
        _ = ValidateAudience(AudienceRules);
    }

    private IReadOnlyCollection<EventResourceAudienceRule> ValidateAudience(IEnumerable<EventResourceAudienceRule> audienceRules)
    {
        ArgumentNullException.ThrowIfNull(audienceRules);
        var rules = audienceRules.ToArray();
        if (rules.Length == 0 || rules.Select(rule => rule.Id).Distinct().Count() != rules.Length
            || rules.Length > 1 && rules.Any(rule => rule.AudienceKindId == (int)EventResourceAudienceKindEnum.Public)
            || rules.Any(rule => !rule.IsValid() || rule.TenantId != TenantId || rule.EventId != EventId
                || rule.EventResourceId != Id
                || EventSessionId.HasValue && rule.EventSessionId.HasValue && rule.EventSessionId != EventSessionId))
        {
            throw new ArgumentException("Resource audience rules must have valid, matching ownership and scopes.");
        }
        foreach (EventResourceAudienceRule rule in rules)
        {
            rule.BindResourceSession(EventSessionId, SessionScopeId);
        }
        return Array.AsReadOnly(rules);
    }

    private void ReplaceAudienceRules(IEnumerable<EventResourceAudienceRule> rules)
    {
        _audienceRules.Clear();
        _audienceRules.AddRange(rules);
    }

    private void ApplyAvailability(EventResourceAvailability availability)
    {
        AvailabilityAbsoluteStartUtc = availability.AbsoluteStartUtc;
        AvailabilityAbsoluteEndUtc = availability.AbsoluteEndUtc;
        AvailabilityStartAnchorId = (int?)availability.StartAnchor;
        AvailabilityStartOffsetTicks = availability.StartOffsetTicks;
        AvailabilityEndAnchorId = (int?)availability.EndAnchor;
        AvailabilityEndOffsetTicks = availability.EndOffsetTicks;
    }

    private void ApplyMetadata(EventResourceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(metadata.Title) || !Enum.IsDefined(metadata.Kind)
            || !Enum.IsDefined(metadata.DisclosureMode) || metadata.SortOrder < 0
            || metadata.AccessibleAlternativeEventResourceId == Guid.Empty || metadata.AccessibleAlternativeEventResourceId == Id
            || metadata.DisclosureMode != EventResourceDisclosureModeEnum.EligibleOnly && string.IsNullOrWhiteSpace(metadata.PublicTitle))
        {
            throw new ArgumentException("Resource metadata and disclosure must be explicit and valid.", nameof(metadata));
        }
        Title = metadata.Title.Trim();
        EventResourceKindId = (int)metadata.Kind;
        DisclosureModeId = (int)metadata.DisclosureMode;
        PublicTitle = metadata.PublicTitle?.Trim();
        Description = metadata.Description;
        SensitiveNotes = metadata.SensitiveNotes;
        LanguageCode = metadata.LanguageCode;
        AccessibilityNote = metadata.AccessibilityNote;
        SortOrder = metadata.SortOrder;
        AccessibleAlternativeEventResourceId = metadata.AccessibleAlternativeEventResourceId;
    }

    private void RequireWritable(Guid expectedStamp, Guid actorId, DateTime occurredAtUtc)
    {
        RequireActorAndTime(actorId, occurredAtUtc);
        if (IsDeleted || PublicationStateId == (int)EventResourcePublicationStateEnum.Archived
            || ConcurrencyStamp != expectedStamp)
        {
            throw new InvalidOperationException("The resource was changed or is no longer editable.");
        }
    }

    private static void RequireActorAndTime(Guid actorId, DateTime occurredAtUtc)
    {
        if (actorId == Guid.Empty || occurredAtUtc == default || occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A responsible actor and a non-default UTC timestamp are required.");
        }
    }

    private void Touch(Guid actorId, DateTime occurredAtUtc)
    {
        UpdatedBy = actorId;
        UpdatedAt = occurredAtUtc;
    }
}

public sealed record EventResourceMetadata
{
    public required string Title { get; init; }
    public required EventResourceKindEnum Kind { get; init; }
    public required EventResourceDisclosureModeEnum DisclosureMode { get; init; }
    public string? PublicTitle { get; init; }
    public string? Description { get; init; }
    public string? SensitiveNotes { get; init; }
    public string? LanguageCode { get; init; }
    public string? AccessibilityNote { get; init; }
    public int SortOrder { get; init; }
    public Guid? AccessibleAlternativeEventResourceId { get; init; }
    public override string ToString() => nameof(EventResourceMetadata);
}
