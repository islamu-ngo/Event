using Explore.Domain;

namespace Explore.Application.Features.Events.Moderation;

public sealed record EventHeavyRedactionGraph(
    Event Event,
    IReadOnlyList<EventSession> Sessions,
    IReadOnlyList<EventDay> Days,
    IReadOnlyList<EventAgendaItem> AgendaItems,
    IReadOnlyList<EventSessionAgendaItem> SessionAgendaItems,
    IReadOnlyList<EventSessionGroup> SessionGroups,
    IReadOnlyList<EventCustomPropertyDefinition> EventCustomPropertyDefinitions,
    IReadOnlyList<EventCustomPropertyProjection> EventCustomPropertyProjections,
    IReadOnlyList<EventSessionCustomPropertyDefinition> SessionCustomPropertyDefinitions,
    IReadOnlyList<EventSessionCustomPropertyProjection> SessionCustomPropertyProjections,
    IReadOnlyList<StorageObject> ImageStorageObjects);
