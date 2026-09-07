using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventLocation : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private Guid _tenantId;

    private EventLocation()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => SetTenantId(value);
    }
    public Tenant? Tenant { get; private set; }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => SetTenantId(value);
    }

    [ForeignKey(nameof(Event))]
    public Guid EventId { get; private set; }
    public Event? Event { get; private set; }

    [ForeignKey(nameof(Location))]
    public Guid? LocationId { get; private set; }
    public Location? Location { get; private set; }

    public bool ShowVenueName { get; private set; }
    public bool ShowCity { get; private set; }
    public bool ShowCountry { get; private set; }
    public bool ShowRoomName { get; private set; }
    public bool ShowStreetAddress { get; private set; }
    public bool ShowPostcode { get; private set; }
    public bool ShowCoordinates { get; private set; }

    [ForeignKey(nameof(FullDetailsAudience))]
    public int FullDetailsAudienceId { get; private set; }
    public LocationDisclosureAudience? FullDetailsAudience { get; private set; }

    public DateTime? RevealFullDetailsFromUtc { get; private set; }
    public bool NeedsPrivacyReview { get; private set; }
    public bool IsToBeAnnounced { get; private set; }
    public int PolicyVersion { get; private set; }
    public Guid? LastPolicyActorUserId { get; private set; }
    public DateTime? LastPolicyChangedAtUtc { get; private set; }

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
                throw new InvalidOperationException("A detached EventLocation cannot be resurrected.");
            }

            IsDeleted = value;
        }
    }

    DateTime? ISoftDeletable.DeletedAt
    {
        get => DeletedAt;
        set => DeletedAt = value;
    }

    Guid? ISoftDeletable.DeletedBy
    {
        get => DeletedBy;
        set => DeletedBy = value;
    }

    [NotMapped]
    public bool HasValidLocationOrTbaShape => LocationId.HasValue ^ IsToBeAnnounced;

    public bool SatisfiesPublicationVenueRequirement(Location? physicalLocation)
    {
        if (IsDeleted || !HasValidLocationOrTbaShape)
        {
            return false;
        }

        if (IsToBeAnnounced)
        {
            return true;
        }

        return physicalLocation is not null
            && physicalLocation.Id == LocationId
            && physicalLocation.TenantId == TenantId
            && physicalLocation.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Active
            && physicalLocation.Pii is not null
            && !string.IsNullOrWhiteSpace(physicalLocation.Pii.Address)
            && !string.IsNullOrWhiteSpace(physicalLocation.Pii.Postcode);
    }

    public static EventLocation CreatePhysical(
        Guid tenantId,
        Guid eventId,
        Guid locationId,
        Guid actorUserId,
        DateTime createdAtUtc)
    {
        RequireId(locationId, nameof(locationId));
        return Create(tenantId, eventId, locationId, false, actorUserId, createdAtUtc);
    }

    public static EventLocation CreateToBeAnnounced(
        Guid tenantId,
        Guid eventId,
        Guid actorUserId,
        DateTime createdAtUtc)
    {
        return Create(tenantId, eventId, null, true, actorUserId, createdAtUtc);
    }

    internal void AttachLocation(Location location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (IsToBeAnnounced || LocationId != location.Id || TenantId != location.TenantId)
        {
            throw new InvalidOperationException("The physical location must match this EventLocation identity and tenant.");
        }

        Location = location;
    }

    public EventLocationDisclosureAudit CreateInitialDisclosureAudit()
    {
        if (PolicyVersion != 1 || LastPolicyActorUserId is null || LastPolicyChangedAtUtc is null)
        {
            throw new InvalidOperationException("Only a newly created EventLocation can produce its initial disclosure audit.");
        }

        return EventLocationDisclosureAudit.Create(
            TenantId,
            Id,
            LastPolicyActorUserId.Value,
            EventLocationDisclosureFields.None,
            GetDisclosureFields(),
            LocationDisclosureAudienceEnum.Never,
            (LocationDisclosureAudienceEnum)FullDetailsAudienceId,
            null,
            RevealFullDetailsFromUtc,
            0,
            1,
            EventLocationDisclosureAuditReasonEnum.AssociationCreated,
            LastPolicyChangedAtUtc.Value);
    }

    public EventLocationDisclosureAudit ChangeDisclosurePolicy(
        EventLocationDisclosureFields newFields,
        LocationDisclosureAudienceEnum newAudience,
        DateTime? newRevealFullDetailsFromUtc,
        int expectedPolicyVersion,
        Guid actorUserId,
        EventLocationDisclosureAuditReasonEnum reason,
        DateTime changedAtUtc,
        bool? needsPrivacyReview = null)
    {
        RequireId(actorUserId, nameof(actorUserId));
        if (IsDeleted)
        {
            throw new InvalidOperationException("A detached EventLocation disclosure policy cannot be changed.");
        }

        if (expectedPolicyVersion != PolicyVersion)
        {
            throw new InvalidOperationException("The expected EventLocation policy version is stale.");
        }

        if (reason == EventLocationDisclosureAuditReasonEnum.AssociationCreated)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "AssociationCreated is reserved for the initial policy audit.");
        }

        EventLocationDisclosureFields previousFields = GetDisclosureFields();
        var previousAudience = (LocationDisclosureAudienceEnum)FullDetailsAudienceId;
        EventLocationDisclosureAudit audit = EventLocationDisclosureAudit.Create(
            TenantId,
            Id,
            actorUserId,
            previousFields,
            newFields,
            previousAudience,
            newAudience,
            RevealFullDetailsFromUtc,
            newRevealFullDetailsFromUtc,
            PolicyVersion,
            PolicyVersion + 1,
            reason,
            changedAtUtc);

        ShowVenueName = newFields.HasFlag(EventLocationDisclosureFields.VenueName);
        ShowCity = newFields.HasFlag(EventLocationDisclosureFields.City);
        ShowCountry = newFields.HasFlag(EventLocationDisclosureFields.Country);
        ShowRoomName = newFields.HasFlag(EventLocationDisclosureFields.RoomName);
        ShowStreetAddress = newFields.HasFlag(EventLocationDisclosureFields.StreetAddress);
        ShowPostcode = newFields.HasFlag(EventLocationDisclosureFields.Postcode);
        ShowCoordinates = newFields.HasFlag(EventLocationDisclosureFields.Coordinates);
        FullDetailsAudienceId = (int)newAudience;
        RevealFullDetailsFromUtc = newRevealFullDetailsFromUtc;
        NeedsPrivacyReview = needsPrivacyReview ?? NeedsPrivacyReview;
        PolicyVersion++;
        LastPolicyActorUserId = actorUserId;
        LastPolicyChangedAtUtc = changedAtUtc;
        UpdatedAt = changedAtUtc;
        UpdatedBy = actorUserId;
        ConcurrencyStamp = Guid.CreateVersion7();
        return audit;
    }

    public EventLocationDisclosureAudit ApplyGovernanceTightening(
        bool requiresPrivacyReview,
        Guid actorUserId,
        DateTime changedAtUtc)
    {
        EventLocationDisclosureAudit audit = ChangeDisclosurePolicy(
            GetDisclosureFields(),
            (LocationDisclosureAudienceEnum)FullDetailsAudienceId,
            RevealFullDetailsFromUtc,
            PolicyVersion,
            actorUserId,
            EventLocationDisclosureAuditReasonEnum.GovernanceTightening,
            changedAtUtc);

        if (requiresPrivacyReview)
        {
            NeedsPrivacyReview = true;
        }

        return audit;
    }

    public EventLocationDisclosureAudit CompletePrivacyReview(
        Guid actorUserId,
        DateTime changedAtUtc)
    {
        if (!NeedsPrivacyReview)
        {
            throw new InvalidOperationException("EventLocation does not require privacy remediation.");
        }

        if (!SatisfiesPublicationVenueRequirement(Location))
        {
            throw new InvalidOperationException("EventLocation cannot complete privacy remediation without a usable physical location or explicit TBA state.");
        }

        return ChangeDisclosurePolicy(
            GetDisclosureFields(),
            (LocationDisclosureAudienceEnum)FullDetailsAudienceId,
            RevealFullDetailsFromUtc,
            PolicyVersion,
            actorUserId,
            EventLocationDisclosureAuditReasonEnum.PrivacyErasureRemediation,
            changedAtUtc,
            needsPrivacyReview: false);
    }

    public void DetachFinalReference(Guid actorUserId, DateTime deletedAtUtc)
    {
        RequireId(actorUserId, nameof(actorUserId));
        if (deletedAtUtc == default)
        {
            throw new ArgumentException("Deletion timestamp is required.", nameof(deletedAtUtc));
        }

        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedAt = deletedAtUtc.ToUniversalTime();
        DeletedBy = actorUserId;
        UpdatedAt = DeletedAt;
        UpdatedBy = actorUserId;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private static EventLocation Create(
        Guid tenantId,
        Guid eventId,
        Guid? locationId,
        bool isToBeAnnounced,
        Guid actorUserId,
        DateTime createdAtUtc)
    {
        RequireId(tenantId, nameof(tenantId));
        RequireId(eventId, nameof(eventId));
        RequireId(actorUserId, nameof(actorUserId));
        if (createdAtUtc == default)
        {
            throw new ArgumentException("Creation timestamp is required.", nameof(createdAtUtc));
        }

        var created = new EventLocation
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventId = eventId,
            LocationId = locationId,
            IsToBeAnnounced = isToBeAnnounced,
            FullDetailsAudienceId = (int)LocationDisclosureAudienceEnum.Never,
            NeedsPrivacyReview = true,
            PolicyVersion = 1,
            LastPolicyActorUserId = actorUserId,
            LastPolicyChangedAtUtc = createdAtUtc.ToUniversalTime(),
            CreatedAt = createdAtUtc.ToUniversalTime(),
            CreatedBy = actorUserId,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

        if (!created.HasValidLocationOrTbaShape)
        {
            throw new InvalidOperationException("EventLocation requires exactly one of physical Location or TBA.");
        }

        return created;
    }

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }

    private EventLocationDisclosureFields GetDisclosureFields()
    {
        EventLocationDisclosureFields fields = EventLocationDisclosureFields.None;
        fields |= ShowVenueName ? EventLocationDisclosureFields.VenueName : EventLocationDisclosureFields.None;
        fields |= ShowCity ? EventLocationDisclosureFields.City : EventLocationDisclosureFields.None;
        fields |= ShowCountry ? EventLocationDisclosureFields.Country : EventLocationDisclosureFields.None;
        fields |= ShowRoomName ? EventLocationDisclosureFields.RoomName : EventLocationDisclosureFields.None;
        fields |= ShowStreetAddress ? EventLocationDisclosureFields.StreetAddress : EventLocationDisclosureFields.None;
        fields |= ShowPostcode ? EventLocationDisclosureFields.Postcode : EventLocationDisclosureFields.None;
        fields |= ShowCoordinates ? EventLocationDisclosureFields.Coordinates : EventLocationDisclosureFields.None;
        return fields;
    }

    private void SetTenantId(Guid value)
    {
        RequireId(value, nameof(TenantId));
        if (_tenantId != Guid.Empty && _tenantId != value)
        {
            throw new InvalidOperationException("EventLocation tenant identity is immutable.");
        }

        _tenantId = value;
    }
}
