using Explore.Application.DTOs.EventResource;
using Explore.Application.Features.EventResources.Validators;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Features.EventResources;

public sealed class EventResourceDraftValidatorTests
{
    private static EventResourceDraftDto Draft() => new()
    {
        Title = "Material", Kind = (EventResourceKindEnum)1,
        DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly,
        DeliveryType = EventResourceDeliveryTypeEnum.StoredFile,
        AudienceRules = [new(EventResourceAudienceKindEnum.Public)]
    };

    [Test]
    public async Task PersistedMetadataLimitsAreRejectedBeforePersistence()
    {
        var validator = new EventResourceDraftValidator();
        var invalid = new[]
        {
            Draft() with { Title = new string('x', 501) },
            Draft() with { PublicTitle = new string('x', 501) },
            Draft() with { Description = new string('x', 5001) },
            Draft() with { SensitiveNotes = new string('x', 5001) },
            Draft() with { LanguageCode = new string('x', 36) },
            Draft() with { AccessibilityNote = new string('x', 2001) },
            Draft() with { AudienceRules = [] },
            Draft() with { AudienceRules = [new(EventResourceAudienceKindEnum.Public), new(EventResourceAudienceKindEnum.EventStaff)] },
            Draft() with { Availability = new(StartAnchor: EventResourceAvailabilityAnchorEnum.EventStart) },
            Draft() with { DisclosureMode = EventResourceDisclosureModeEnum.Teaser }
        };
        foreach (var draft in invalid)
            await Assert.That((await validator.ValidateAsync(draft)).IsValid).IsFalse();
    }

    [Test]
    public async Task SupportedDeliveryTypesRemainValidSemanticPlaceholders()
    {
        foreach (var type in Enum.GetValues<EventResourceDeliveryTypeEnum>())
            await Assert.That((await new EventResourceDraftValidator().ValidateAsync(Draft() with { DeliveryType = type })).IsValid).IsTrue();
    }
}
