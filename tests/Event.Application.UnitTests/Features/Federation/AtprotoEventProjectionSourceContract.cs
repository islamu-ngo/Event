using System.Collections.Immutable;
using System.Reflection;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Domain;

namespace Event.Application.UnitTests.Features.Federation;

internal static class AtprotoEventProjectionSourceContract
{
    private static readonly ImmutableArray<(Type Type, string Prefix)> SourceTypes =
    [
        (typeof(Explore.Domain.Event), "Event"),
        (typeof(Actor), "Event.Actor"),
        (typeof(ActorPii), "Event.Actor.Pii"),
        (typeof(AtprotoIdentity), "Event.Actor.AtprotoIdentity"),
        (typeof(ActorType), "Event.Actor.ActorType"),
        (typeof(Organization), "Event.Actor.Organization"),
        (typeof(OrganizationPii), "Event.Actor.Organization.Pii"),
        (typeof(Group), "Event.Actor.Group"),
        (typeof(Explore.Domain.EventSeries), "Event.EventSeries"),
        (typeof(VisibilityType), "Event.EventSeries.VisibilityType"),
        (typeof(ActorPii), "Event.EventSeries.Actor.Pii"),
        (typeof(AtprotoIdentity), "Event.EventSeries.Actor.AtprotoIdentity"),
        (typeof(EventType), "Event.EventType"),
        (typeof(AudienceGender), "Event.AudienceGender"),
        (typeof(AudienceAge), "Event.AudienceAge"),
        (typeof(EventFormat), "Event.EventFormat"),
        (typeof(EventStatus), "Event.EventStatus"),
        (typeof(VisibilityType), "Event.VisibilityType"),
        (typeof(Madhab), "Event.Madhab"),
        (typeof(EventRegistrationPolicy), "Event.RegistrationPolicy"),
        (typeof(EventIslamicAspect), "Event.IslamicAspect"),
        (typeof(Madhab), "Event.IslamicAspect.Madhab"),
        (typeof(Language), "Event.IslamicAspect.PrimaryLanguage"),
        (typeof(EventTechAspect), "Event.TechAspect"),
        (typeof(EventLocation), "EventLocation"),
        (typeof(Location), "Location"),
        (typeof(LocationPii), "LocationPii"),
        (typeof(LocationRoom), "LocationRoom"),
        (typeof(EventLocationDisclosureResult), "EventLocationDisclosureResult"),
        (typeof(EventLocationDisclosureValues), "EventLocationDisclosureResult.Values"),
        (typeof(EventDay), "EventDay"),
        (typeof(EventSession), "EventSession"),
        (typeof(EventSessionKind), "EventSession.EventSessionKind"),
        (typeof(EventSessionStatus), "EventSession.EventSessionStatus"),
        (typeof(RegistrationMode), "EventSession.RegistrationMode"),
        (typeof(EventSessionIslamicAspect), "EventSession.IslamicAspect"),
        (typeof(EventSessionGroup), "EventSessionGroup"),
        (typeof(EventSessionGroupSession), "EventSessionGroupSession"),
        (typeof(EventAgendaItem), "EventAgendaItem"),
        (typeof(ScheduleItemKind), "EventAgendaItem.Kind"),
        (typeof(EventSessionAgendaItem), "EventSessionAgendaItem"),
        (typeof(Explore.Domain.EventCategories), "EventCategoryLink"),
        (typeof(Explore.Domain.EventTags), "EventTagLink"),
        (typeof(Category), "Event.Category"),
        (typeof(Category), "Event.Category.Parent"),
        (typeof(Tag), "Event.Tag"),
        (typeof(EventSessionCategory), "EventSessionCategoryLink"),
        (typeof(EventSessionTag), "EventSessionTagLink"),
        (typeof(EventSessionLanguage), "EventSessionLanguageLink"),
        (typeof(Language), "EventSession.Language"),
        (typeof(EventSessionSpeaker), "EventSession.Speaker"),
        (typeof(Actor), "EventSession.Speaker.Actor"),
        (typeof(ActorPii), "EventSession.Speaker.Actor.Pii"),
        (typeof(AtprotoIdentity), "EventSession.Speaker.Actor.AtprotoIdentity"),
        (typeof(EventCustomPropertyDefinition), "EventCustomPropertyDefinition"),
        (typeof(EventCustomPropertyOption), "EventCustomPropertyOption"),
        (typeof(EventCustomPropertyOption), "EventCustomPropertyDefinition.DefaultOption"),
        (typeof(EventCustomPropertyOption), "EventCustomPropertyOption.ParentOption"),
        (typeof(EventCustomPropertyValue), "EventCustomPropertyValue"),
        (typeof(EventCustomPropertyOption), "EventCustomPropertyValue.Option"),
        (typeof(EventSessionCustomPropertyDefinition), "EventSessionCustomPropertyDefinition"),
        (typeof(EventSessionCustomPropertyOption), "EventSessionCustomPropertyOption"),
        (typeof(EventSessionCustomPropertyOption), "EventSessionCustomPropertyDefinition.DefaultOption"),
        (typeof(EventSessionCustomPropertyOption), "EventSessionCustomPropertyOption.ParentOption"),
        (typeof(EventSessionCustomPropertyValue), "EventSessionCustomPropertyValue"),
        (typeof(EventSessionCustomPropertyOption), "EventSessionCustomPropertyValue.Option"),
        (typeof(StorageObject), "StorageObject"),
        (typeof(FileType), "StorageObject.FileType")
    ];

    private static readonly ImmutableArray<string> ConditionalPolicyPaths =
    [
        "Event.ParticipationConfiguration.AdvanceRegistrationObligationId",
        "Event.PublicActions[ExternalRegistration.Active].Url",
        "EventCustomPropertyDefinition.ExposureLevel!=Public",
        "EventSessionCustomPropertyDefinition.ExposureLevel!=Public",
        "StorageObject.Visibility!=public_image",
        "StorageObject.LifecycleState!=active"
    ];

    public static ImmutableArray<string> SourcePaths { get; } =
    [
        .. SourceTypes.SelectMany(source => source.Type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(property => IsScalarSourceType(property.PropertyType))
            .Select(property => $"{source.Prefix}.{property.Name}")),
        .. ConditionalPolicyPaths
    ];

    private static bool IsScalarSourceType(Type propertyType)
    {
        Type valueType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        return valueType.IsEnum
            || valueType.IsPrimitive
            || valueType == typeof(string)
            || valueType == typeof(decimal)
            || valueType == typeof(Guid)
            || valueType == typeof(DateTime)
            || valueType == typeof(DateTimeOffset)
            || valueType == typeof(DateOnly)
            || valueType == typeof(TimeOnly);
    }
}
