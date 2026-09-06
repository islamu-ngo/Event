// ABOUTME: Tenant-scoped physical place with optional PII and an irreversible privacy lifecycle.
// ABOUTME: Owns consent-backed Private Home identity, erasure tombstones, rooms, audit, and concurrency.

using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public class Location : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    public const string ErasedPrivateVenueLabel = "Private venue";
    private LocationPii? _pii;
    private string? _fullName;
    private string? _country;
    private string? _city;

    public Guid Id { get; set; }
    public required string FullName
    {
        get => _fullName ?? string.Empty;
        set => SetFullName(GuardErasedValue(_fullName, value, nameof(FullName)));
    }

    public string DisplaySortKey { get; private set; } = string.Empty;
    public short DisplaySortKeyVersion { get; private set; }

    public required string Country
    {
        get => _country ?? string.Empty;
        set => _country = GuardErasedValue(_country, value, nameof(Country));
    }

    public required string City
    {
        get => _city ?? string.Empty;
        set => _city = GuardErasedValue(_city, value, nameof(City));
    }

    public LocationPii? Pii
    {
        get => _pii;
        private set
        {
            if (value is not null && LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Erased)
            {
                throw new InvalidOperationException("PII cannot be attached to an erased Location. Create a replacement Location instead.");
            }

            if (value is not null
                && LocationKindId == (int)LocationKindEnum.PrivateHome
                && OwnerUserId is null)
            {
                throw new InvalidOperationException("Active Private Home PII requires an owner.");
            }

            if (value is null
                && _pii is not null
                && LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Active)
            {
                throw new InvalidOperationException("Active PII cannot be cleared without the Location erasure transition.");
            }

            if (value is not null
                && Id != Guid.Empty
                && value.LocationId != Guid.Empty
                && value.LocationId != Id)
            {
                throw new InvalidOperationException("Location PII must belong to the same Location aggregate.");
            }

            _pii = value;
            if (LocationPrivacyStateId != (int)LocationPrivacyStateEnum.Erased)
            {
                LocationPrivacyStateId = value is null
                    ? (int)LocationPrivacyStateEnum.NotProvided
                    : (int)LocationPrivacyStateEnum.Active;
            }
        }
    }

    [NotMapped]
    public string? Address => Pii?.Address;

    [NotMapped]
    public string? Postcode => Pii?.Postcode;

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public GeoCoordinate? GetCoordinate() => Pii?.GetCoordinate();

    public string? Timezone { get; set; }

    [ForeignKey(nameof(LocationKind))]
    public int LocationKindId { get; private set; } = (int)LocationKindEnum.Unclassified;
    public LocationKind? LocationKind { get; private set; }

    [ForeignKey(nameof(LocationPrivacyState))]
    public int LocationPrivacyStateId { get; private set; } = (int)LocationPrivacyStateEnum.NotProvided;
    public LocationPrivacyState? LocationPrivacyState { get; private set; }

    public int AddressSourceId { get; private set; } = (int)LocationAddressSourceEnum.UnknownLegacy;

    [NotMapped]
    public LocationAddressSourceEnum AddressSource => (LocationAddressSourceEnum)AddressSourceId;

    public LocationAddressSource? AddressSourceLookup { get; private set; }

    public int AddressVisibilityId { get; private set; } = (int)LocationAddressVisibilityEnum.Quarantined;

    [NotMapped]
    public LocationAddressVisibilityEnum AddressVisibility => (LocationAddressVisibilityEnum)AddressVisibilityId;

    public LocationAddressVisibility? AddressVisibilityLookup { get; private set; }

    public Guid? AddressOrganizationId { get; private set; }
    public OrganizationTenant? AddressOrganizationTenant { get; private set; }

    [ForeignKey(nameof(OwnerUser))]
    public Guid? OwnerUserId { get; private set; }
    public User? OwnerUser { get; private set; }
    public DateTime? PiiErasedAtUtc { get; private set; }
    public LocationPrivacyErasureReasonEnum? PiiErasureReason { get; private set; }

    public ICollection<LocationRoom> Rooms { get; set; } = new List<LocationRoom>();

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public Guid ConcurrencyStamp { get; set; }

    private void AttachPii(LocationPii pii)
    {
        ArgumentNullException.ThrowIfNull(pii);
        pii.AssociateWith(this);
        Pii = pii;
    }

    public bool SetManualAddress(string address, string postcode) =>
        SetAddress(address, postcode, coordinate: null, LocationAddressSourceEnum.Manual);

    public bool SetProviderAddress(string address, string postcode, GeoCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return SetAddress(address, postcode, coordinate, LocationAddressSourceEnum.ProviderSelection);
    }

    private bool SetAddress(
        string address,
        string postcode,
        GeoCoordinate? coordinate,
        LocationAddressSourceEnum source)
    {
        EnsureNotErased();
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(postcode);

        if (_pii is not null
            && string.Equals(_pii.Address, address, StringComparison.Ordinal)
            && string.Equals(_pii.Postcode, postcode, StringComparison.Ordinal)
            && _pii.GetCoordinate() == coordinate)
        {
            return _pii.EnsureCurrentAddressSubstringKey();
        }

        bool replacesExistingBundle = _pii is not null;
        if (_pii is null)
        {
            AttachPii(LocationPii.Create(address, postcode, coordinate));
        }
        else
        {
            _pii.SetAddress(address, postcode, coordinate);
        }

        AddressSourceId = (int)source;
        AddressSourceLookup = null;
        AddressVisibilityId = (int)LocationAddressVisibilityEnum.Quarantined;
        AddressVisibilityLookup = null;
        AddressOrganizationId = null;
        AddressOrganizationTenant = null;
        if (replacesExistingBundle)
        {
            ConcurrencyStamp = Guid.CreateVersion7();
        }
        return true;
    }

    public void ApplyAddressGovernance(
        Guid actorId,
        LocationAddressSourceEnum source,
        LocationAddressVisibilityEnum visibility,
        Guid? addressOrganizationId) =>
        ApplyAddressGovernanceCore(actorId, source, visibility, addressOrganizationId, changedAtUtc: null);

    public void ApplyAddressGovernanceWithAudit(
        Guid actorId,
        LocationAddressSourceEnum source,
        LocationAddressVisibilityEnum visibility,
        Guid? addressOrganizationId,
        DateTime changedAtUtc) =>
        ApplyAddressGovernanceCore(actorId, source, visibility, addressOrganizationId, changedAtUtc);

    private void ApplyAddressGovernanceCore(
        Guid actorId,
        LocationAddressSourceEnum source,
        LocationAddressVisibilityEnum visibility,
        Guid? addressOrganizationId,
        DateTime? changedAtUtc)
    {
        if (actorId == Guid.Empty)
        {
            throw new ArgumentException("Actor id is required.", nameof(actorId));
        }
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }
        if (!Enum.IsDefined(visibility))
        {
            throw new ArgumentOutOfRangeException(nameof(visibility));
        }
        if (addressOrganizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization id cannot be empty.", nameof(addressOrganizationId));
        }
        if (changedAtUtc is { } changedAt
            && (changedAt == default || changedAt.Kind != DateTimeKind.Utc))
        {
            throw new ArgumentException("Governance time must be a non-default UTC value.", nameof(changedAtUtc));
        }

        EnsureNotErased();
        if (visibility == LocationAddressVisibilityEnum.TenantApproved
            && LocationKindId == (int)LocationKindEnum.PrivateHome)
        {
            throw new InvalidOperationException("Private Home addresses cannot be tenant-approved.");
        }
        if (visibility is LocationAddressVisibilityEnum.Quarantined
                or LocationAddressVisibilityEnum.CreatorPrivate
            && addressOrganizationId.HasValue)
        {
            throw new ArgumentException(
                "Quarantined and creator-private addresses cannot have an organization scope.",
                nameof(addressOrganizationId));
        }
        if (visibility == LocationAddressVisibilityEnum.OrganizationScoped
            && !addressOrganizationId.HasValue)
        {
            throw new ArgumentException(
                "Organization-scoped addresses require an organization id.",
                nameof(addressOrganizationId));
        }
        if (visibility == LocationAddressVisibilityEnum.TenantApproved)
        {
            EnsureCurrentDerivedKeys();
        }

        AddressSourceId = (int)source;
        AddressSourceLookup = null;
        AddressVisibilityId = (int)visibility;
        AddressVisibilityLookup = null;
        AddressOrganizationId = addressOrganizationId;
        AddressOrganizationTenant = null;
        CreatedBy ??= actorId;
        if (changedAtUtc is { } auditedAt)
        {
            UpdatedAt = auditedAt;
            UpdatedBy = actorId;
            ConcurrencyStamp = Guid.CreateVersion7();
        }
    }

    public bool PromoteAddressToTenantApproved(Guid actorId, DateTime changedAtUtc)
    {
        if (actorId == Guid.Empty)
        {
            throw new ArgumentException("Actor id is required.", nameof(actorId));
        }
        if (changedAtUtc == default || changedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Promotion time must be a non-default UTC value.", nameof(changedAtUtc));
        }

        EnsureNotErased();
        if (LocationKindId == (int)LocationKindEnum.PrivateHome)
        {
            throw new InvalidOperationException("Private Home addresses cannot be tenant-approved.");
        }
        if (LocationPrivacyStateId != (int)LocationPrivacyStateEnum.Active || Pii is null)
        {
            throw new InvalidOperationException("Only an active address can be tenant-approved.");
        }
        if (AddressVisibilityId != (int)LocationAddressVisibilityEnum.Quarantined
            && AddressVisibilityId != (int)LocationAddressVisibilityEnum.CreatorPrivate
            && AddressVisibilityId != (int)LocationAddressVisibilityEnum.OrganizationScoped
            && AddressVisibilityId != (int)LocationAddressVisibilityEnum.TenantApproved)
        {
            throw new InvalidOperationException("The current address visibility cannot be tenant-approved.");
        }

        bool keysChanged = EnsureCurrentDerivedKeys();
        if (AddressVisibilityId == (int)LocationAddressVisibilityEnum.TenantApproved && !keysChanged)
        {
            return false;
        }

        AddressVisibilityId = (int)LocationAddressVisibilityEnum.TenantApproved;
        AddressVisibilityLookup = null;
        UpdatedAt = changedAtUtc;
        UpdatedBy = actorId;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    public void ClassifyAsPrivateHome(Guid currentUserId)
    {
        if (currentUserId == Guid.Empty)
        {
            throw new ArgumentException("Current user id is required.", nameof(currentUserId));
        }

        EnsureNotErased();
        if (AddressVisibilityId == (int)LocationAddressVisibilityEnum.TenantApproved)
        {
            throw new InvalidOperationException("Tenant-approved addresses cannot be classified as Private Home.");
        }
        if (LocationKindId == (int)LocationKindEnum.PrivateHome)
        {
            if (OwnerUserId == currentUserId)
            {
                return;
            }

            if (OwnerUserId.HasValue)
            {
                throw new InvalidOperationException("Private Home ownership changes require explicit owner consent.");
            }
        }

        LocationKindId = (int)LocationKindEnum.PrivateHome;
        OwnerUserId = currentUserId;
        OwnerUser = null;
    }

    public void ClassifyAs(LocationKindEnum kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == LocationKindEnum.PrivateHome)
        {
            throw new ArgumentException("Private Home classification requires the current owner.", nameof(kind));
        }

        EnsureNotErased();
        if (LocationKindId == (int)LocationKindEnum.PrivateHome && Pii is not null)
        {
            throw new InvalidOperationException("An active Private Home cannot be reclassified to bypass ownership and erasure.");
        }

        LocationKindId = (int)kind;
        OwnerUserId = null;
        OwnerUser = null;
    }

    public void TransferPrivateHomeOwnership(LocationOwnershipConsent consent)
    {
        ArgumentNullException.ThrowIfNull(consent);
        EnsureNotErased();

        if (LocationKindId != (int)LocationKindEnum.PrivateHome)
        {
            throw new InvalidOperationException("Only Private Home ownership can be transferred.");
        }

        if (consent.NewOwnerUserId == Guid.Empty
            || consent.ConsentedByUserId != consent.NewOwnerUserId
            || consent.ConsentedAtUtc == default
            || string.IsNullOrWhiteSpace(consent.ConsentVersion))
        {
            throw new ArgumentException("The new owner must provide explicit versioned consent.", nameof(consent));
        }

        OwnerUserId = consent.NewOwnerUserId;
        OwnerUser = null;
        UpdatedAt = consent.ConsentedAtUtc.ToUniversalTime();
        UpdatedBy = consent.ConsentedByUserId;
    }

    public void EraseOwnedPii(DateTime erasedAtUtc, LocationPrivacyErasureReasonEnum reason)
    {
        if (LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Erased)
        {
            return;
        }

        if (LocationKindId != (int)LocationKindEnum.PrivateHome)
        {
            throw new InvalidOperationException("Owned PII erasure applies only to Private Home locations.");
        }

        if (erasedAtUtc == default)
        {
            throw new ArgumentException("Erasure timestamp is required.", nameof(erasedAtUtc));
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        var priorOwnerId = OwnerUserId;
        if (Rooms.Any(room => room.Id == Guid.Empty)
            || Rooms.Select(room => room.Id).Distinct().Count() != Rooms.Count)
        {
            throw new InvalidOperationException("Every room requires a unique persisted id before privacy erasure.");
        }

        _pii = null;
        OwnerUserId = null;
        OwnerUser = null;
        AddressVisibilityId = (int)LocationAddressVisibilityEnum.Quarantined;
        AddressVisibilityLookup = null;
        AddressOrganizationId = null;
        AddressOrganizationTenant = null;
        LocationPrivacyStateId = (int)LocationPrivacyStateEnum.Erased;
        PiiErasedAtUtc = erasedAtUtc.ToUniversalTime();
        PiiErasureReason = reason;
        SetFullName(ErasedPrivateVenueLabel);
        _city = string.Empty;

        foreach (var room in Rooms)
        {
            room.TombstoneForPrivacyErasure(PiiErasedAtUtc.Value, priorOwnerId);
        }

        UpdatedAt = PiiErasedAtUtc;
        UpdatedBy = priorOwnerId;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private void SetFullName(string value)
    {
        string displaySortKey = LocationTextNormalization.Normalize(value);
        _fullName = value;
        DisplaySortKey = displaySortKey;
        DisplaySortKeyVersion = LocationTextNormalization.CurrentRevision;
    }

    internal bool HasCurrentDerivedKeys()
    {
        if (LocationPrivacyStateId != (int)LocationPrivacyStateEnum.Active || Pii is null)
        {
            return false;
        }

        string currentDisplaySortKey = LocationTextNormalization.Normalize(FullName);
        string currentAddressSubstringKey = LocationTextNormalization.Normalize(Pii.Address);
        return DisplaySortKeyVersion == LocationTextNormalization.CurrentRevision
            && string.Equals(DisplaySortKey, currentDisplaySortKey, StringComparison.Ordinal)
            && Pii.HasCurrentAddressSubstringKey(currentAddressSubstringKey);
    }

    private bool EnsureCurrentDerivedKeys()
    {
        if (LocationPrivacyStateId != (int)LocationPrivacyStateEnum.Active || Pii is null)
        {
            throw new InvalidOperationException("Current derived keys require active address PII.");
        }

        string currentDisplaySortKey = LocationTextNormalization.Normalize(FullName);
        string currentAddressSubstringKey = LocationTextNormalization.Normalize(Pii.Address);
        bool displayChanged = DisplaySortKeyVersion != LocationTextNormalization.CurrentRevision
            || !string.Equals(DisplaySortKey, currentDisplaySortKey, StringComparison.Ordinal);
        bool addressChanged = !Pii.HasCurrentAddressSubstringKey(currentAddressSubstringKey);

        if (displayChanged)
        {
            DisplaySortKey = currentDisplaySortKey;
            DisplaySortKeyVersion = LocationTextNormalization.CurrentRevision;
        }
        if (addressChanged)
        {
            Pii.SetCurrentAddressSubstringKey(currentAddressSubstringKey);
        }

        return displayChanged || addressChanged;
    }

    private void EnsureNotErased()
    {
        if (LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Erased)
        {
            throw new InvalidOperationException("An erased Location cannot be changed or resurrected.");
        }
    }

    private string GuardErasedValue(string? currentValue, string value, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (LocationPrivacyStateId != (int)LocationPrivacyStateEnum.Erased)
        {
            return value;
        }

        var expectedErasedValue = propertyName switch
        {
            nameof(FullName) => ErasedPrivateVenueLabel,
            nameof(City) => string.Empty,
            _ => currentValue
        };
        if (expectedErasedValue is not null
            && !string.Equals(expectedErasedValue, value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{propertyName} cannot be changed after Location erasure.");
        }

        return value;
    }
}
