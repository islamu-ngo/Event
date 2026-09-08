using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Api.IntegrationTests.Builders;

/// <summary>
/// Builds <see cref="Explore.Domain.Event"/> instances for test data seeding.
/// FK IDs default to lookup values seeded by <c>LookupTableSeeder</c>.
/// Requires ActorId and TenantId to be set explicitly.
/// </summary>
public sealed class EventBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _title = "Test Event";
    private string _publicCode = Guid.NewGuid().ToString("N")[..12];
    private string? _subtitle;
    private string? _description;
    private Guid _actorId;
    private Guid _tenantId;
    private int? _eventTypeId;
    private EventStatusEnum _eventStatus = EventStatusEnum.Draft;
    private int _visibilityTypeId = (int)VisibilityTypeEnum.Public;
    private int _eventFormatId = (int)EventFormatEnum.Local;
    private int _eventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated;
    private int? _audienceGenderId;
    private int? _audienceAgeId;
    private DateOnly? _firstSessionDate;
    private DateOnly? _lastSessionDate;

    public EventBuilder WithId(Guid id) { _id = id; return this; }
    public EventBuilder WithTitle(string title) { _title = title; return this; }
    public EventBuilder WithPublicCode(string publicCode) { _publicCode = publicCode; return this; }
    public EventBuilder WithSubtitle(string subtitle) { _subtitle = subtitle; return this; }
    public EventBuilder WithDescription(string description) { _description = description; return this; }
    public EventBuilder WithActorId(Guid actorId) { _actorId = actorId; return this; }
    public EventBuilder WithTenantId(Guid tenantId) { _tenantId = tenantId; return this; }
    public EventBuilder WithEventType(EventTypeEnum type) { _eventTypeId = (int)type; return this; }
    public EventBuilder WithStatus(EventStatusEnum status) { _eventStatus = status; return this; }
    public EventBuilder WithVisibility(VisibilityTypeEnum visibility) { _visibilityTypeId = (int)visibility; return this; }
    public EventBuilder WithFormat(EventFormatEnum format) { _eventFormatId = (int)format; return this; }
    public EventBuilder WithProvenance(EventProvenanceTypeEnum provenance) { _eventProvenanceTypeId = (int)provenance; return this; }
    public EventBuilder WithProvenance(int provenanceTypeId) { _eventProvenanceTypeId = provenanceTypeId; return this; }

    public EventBuilder WithSessionDates(DateOnly first, DateOnly last)
    {
        _firstSessionDate = first;
        _lastSessionDate = last;
        return this;
    }

    public Explore.Domain.Event Build() => new(_eventStatus)
    {
        Id = _id,
        Title = _title,
        PublicCode = _publicCode,
        Subtitle = _subtitle,
        Description = _description,
        ActorId = _actorId,
        Actor = null!,
        TenantId = _tenantId,
        Tenant = null!,
        EventProvenanceTypeId = _eventProvenanceTypeId,
        EventTypeId = _eventTypeId,
        EventStatus = null!,
        VisibilityTypeId = _visibilityTypeId,
        VisibilityType = null!,
        EventFormatId = _eventFormatId,
        EventFormat = null!,
        AudienceGenderId = _audienceGenderId,
        AudienceAgeId = _audienceAgeId,
        FirstSessionDate = _firstSessionDate,
        LastSessionDate = _lastSessionDate,
        ParticipationConfiguration = EventParticipationConfiguration.Create(
            _id,
            _tenantId,
            (int)ParticipationHandlingModeEnum.InformationOnly,
            (int)AdvanceRegistrationObligationEnum.NotApplicable,
            identityAccessModeId: null,
            guestRecoveryPolicy: null,
            DateTime.UtcNow),
        TotalViews = 0,
        ConcurrencyStamp = Guid.NewGuid()
    };
}
