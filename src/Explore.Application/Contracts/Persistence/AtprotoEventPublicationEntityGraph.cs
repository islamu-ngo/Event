using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record AtprotoEventPublicationEntityGraph(
    Event Event,
    IReadOnlyList<EventLocation> EventLocations,
    IReadOnlyList<EventSession> Sessions,
    IReadOnlyList<EventDay> Days,
    IReadOnlyList<EventSessionGroup> SessionGroups,
    IReadOnlyList<EventSessionGroupSession> SessionGroupSessions,
    IReadOnlyList<EventAgendaItem> AgendaItems,
    IReadOnlyList<EventSessionAgendaItem> SessionAgendaItems,
    IReadOnlyList<EventCategories> Categories,
    IReadOnlyList<EventTags> Tags,
    IReadOnlyList<EventSessionCategory> SessionCategories,
    IReadOnlyList<EventSessionTag> SessionTags,
    IReadOnlyList<EventSessionLanguage> SessionLanguages,
    IReadOnlyList<EventSessionSpeaker> SessionSpeakers,
    IReadOnlyList<EventCustomPropertyDefinition> CustomPropertyDefinitions,
    IReadOnlyList<EventSessionCustomPropertyDefinition> SessionCustomPropertyDefinitions);
