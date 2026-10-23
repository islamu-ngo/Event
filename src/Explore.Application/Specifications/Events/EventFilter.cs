using System.Linq.Expressions;
using System.Collections.Immutable;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Specifications.Events;

/// <summary>Finite semantic inputs retained independently of the LINQ translation.</summary>
public sealed record EventFilterOperand(
    int? Integer = null,
    ImmutableArray<int> Integers = default,
    Guid? Identifier = null,
    string? Text = null,
    DateOnly? Date = null);

/// <summary>
/// Static factory for creating event filter specifications.
/// Each filter translates to a database-level WHERE clause via EF Core expression trees.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// var query = new EventQuerySpecification()
///     .And(EventFilter.Category(categoryId))
///     .And(EventFilter.SearchTerm("workshop"))
///     .And(EventFilter.DateFrom(DateOnly.FromDateTime(DateTime.Today)));
/// </code>
/// </remarks>
public sealed class EventFilter : IFilterSpecification<Event>
{
    public EventFilterType FilterType { get; }

    public Expression<Func<Event, bool>> Predicate { get; }
    public EventFilterOperand Operand { get; }

    private EventFilter(EventFilterType filterType, Expression<Func<Event, bool>> predicate,
        EventFilterOperand? operand = null)
    {
        FilterType = filterType;
        Predicate = predicate;
        Operand = operand ?? new();
    }

    /// <summary>
    /// Filters events by event type.
    /// </summary>
    public static EventFilter EventType(int eventTypeId) =>
        new(EventFilterType.EventType, e => e.EventTypeId == eventTypeId, new(Integer: eventTypeId));

    /// <summary>
    /// Filters events by any of the specified event types (OR logic).
    /// </summary>
    public static EventFilter EventTypes(List<int> eventTypeIds)
    {
        eventTypeIds = [.. eventTypeIds];
        return new(EventFilterType.EventTypes, e => e.EventTypeId != null && eventTypeIds.Contains(e.EventTypeId.Value),
            new(Integers: [.. eventTypeIds]));
    }

    /// <summary>
    /// Filters events by event format (online, in-person, hybrid).
    /// </summary>
    public static EventFilter Format(int formatId) =>
        new(EventFilterType.Format, e => e.EventFormatId == formatId, new(Integer: formatId));

    /// <summary>
    /// Filters events by any of the specified formats (OR logic).
    /// </summary>
    public static EventFilter Formats(List<int> formatIds)
    {
        formatIds = [.. formatIds];
        return new(EventFilterType.Formats, e => formatIds.Contains(e.EventFormatId), new(Integers: [.. formatIds]));
    }

    /// <summary>
    /// Filters events by madhab.
    /// </summary>
    public static EventFilter Madhab(int madhabId) =>
        new(EventFilterType.Madhab, e => e.MadhabId == madhabId, new(Integer: madhabId));

    /// <summary>
    /// Filters events by any of the specified madhabs (OR logic).
    /// </summary>
    public static EventFilter Madhabs(List<int> madhabIds)
    {
        madhabIds = [.. madhabIds];
        return new(EventFilterType.Madhabs, e => e.MadhabId != null && madhabIds.Contains(e.MadhabId.Value),
            new(Integers: [.. madhabIds]));
    }

    /// <summary>
    /// Filters events by audience gender.
    /// </summary>
    public static EventFilter AudienceGender(int audienceGenderId) =>
        new(EventFilterType.AudienceGender, e => e.AudienceGenderId == audienceGenderId, new(Integer: audienceGenderId));

    /// <summary>
    /// Filters events by any of the specified audience genders (OR logic).
    /// </summary>
    public static EventFilter AudienceGenders(List<int> audienceGenderIds)
    {
        audienceGenderIds = [.. audienceGenderIds];
        return new(EventFilterType.AudienceGenders, e => e.AudienceGenderId != null && audienceGenderIds.Contains(e.AudienceGenderId.Value),
            new(Integers: [.. audienceGenderIds]));
    }

    /// <summary>
    /// Filters events by audience age.
    /// </summary>
    public static EventFilter AudienceAge(int audienceAgeId) =>
        new(EventFilterType.AudienceAge, e => e.AudienceAgeId == audienceAgeId, new(Integer: audienceAgeId));

    /// <summary>
    /// Filters events by any of the specified audience ages (OR logic).
    /// </summary>
    public static EventFilter AudienceAges(List<int> audienceAgeIds)
    {
        audienceAgeIds = [.. audienceAgeIds];
        return new(EventFilterType.AudienceAges, e => e.AudienceAgeId != null && audienceAgeIds.Contains(e.AudienceAgeId.Value),
            new(Integers: [.. audienceAgeIds]));
    }

    /// <summary>
    /// Filters events by event status.
    /// </summary>
    public static EventFilter Status(int statusId) =>
        new(EventFilterType.Status, e => e.EventStatusId == statusId, new(Integer: statusId));

    /// <summary>
    /// Filters events by any of the specified statuses (OR logic).
    /// </summary>
    public static EventFilter Statuses(List<int> statusIds)
    {
        statusIds = [.. statusIds];
        return new(EventFilterType.Statuses, e => statusIds.Contains(e.EventStatusId), new(Integers: [.. statusIds]));
    }

    /// <summary>
    /// Filters events by visibility type.
    /// </summary>
    public static EventFilter Visibility(int visibilityTypeId) =>
        new(EventFilterType.Visibility, e => e.VisibilityTypeId == visibilityTypeId, new(Integer: visibilityTypeId));

    /// <summary>
    /// Filters events by actor (organizer).
    /// </summary>
    public static EventFilter Actor(Guid actorId) =>
        new(EventFilterType.Actor, e => e.ActorId == actorId, new(Identifier: actorId));

    /// <summary>
    /// Searches events by title, card description, or long content (case-insensitive).
    /// Translates to SQL ILIKE/LIKE for database-level search.
    /// </summary>
    public static EventFilter SearchTerm(string searchTerm) =>
        new(EventFilterType.SearchTerm, e => e.Title.Contains(searchTerm) ||
                 (e.Description != null && e.Description.Contains(searchTerm)) ||
                 (e.Content != null && e.Content.Contains(searchTerm)), new(Text: searchTerm));

    /// <summary>
    /// Filters events with first session date on or after the specified date.
    /// </summary>
    public static EventFilter DateFrom(DateOnly dateFrom) =>
        new(EventFilterType.DateFrom, e => e.FirstSessionDate != null && e.FirstSessionDate >= dateFrom, new(Date: dateFrom));

    /// <summary>
    /// Filters events with first session date on or before the specified date.
    /// </summary>
    public static EventFilter DateTo(DateOnly dateTo) =>
        new(EventFilterType.DateTo, e => e.FirstSessionDate != null && e.FirstSessionDate <= dateTo, new(Date: dateTo));

    /// <summary>
    /// Filters events that have a specific category assigned.
    /// Requires a subquery on EventCategories — use <see cref="EventSubqueryFilter"/> instead.
    /// </summary>
    /// <remarks>
    /// Category and Tag filtering require access to junction tables (EventCategory, EventTag)
    /// which are not directly navigable from the Event entity. These filters are applied
    /// at the repository level using DbContext-aware subqueries via <see cref="EventSubqueryFilter"/>.
    /// </remarks>
    public static EventFilter Free() =>
        new(EventFilterType.Free, e =>
            e.ParticipationConfiguration != null
            && e.ParticipationConfiguration.ParticipationHandlingModeId == (int)ParticipationHandlingModeEnum.PlatformManaged
            && e.TicketCatalogVersions.Any(catalog =>
                !catalog.IsDeleted
                && catalog.TicketCatalogStatusId == (int)TicketCatalogStatusEnum.Published
                && catalog.TicketTypes.Any(ticketType =>
                    !ticketType.IsDeleted
                    && (ticketType.TicketPricingModeId == (int)TicketPricingModeEnum.Free
                        || ((ticketType.TicketPricingModeId == (int)TicketPricingModeEnum.Donation
                                || ticketType.TicketPricingModeId == (int)TicketPricingModeEnum.PayWhatYouCan)
                            && (!ticketType.MinimumPriceMinor.HasValue || ticketType.MinimumPriceMinor == 0))
                        || (ticketType.TicketPricingModeId == (int)TicketPricingModeEnum.SlidingScale
                            && ticketType.MinimumPriceMinor == 0)))));

    /// <summary>
    /// Filters events to only those publicly visible in discovery listings.
    /// Excludes Draft, Moderated, and Archived statuses, and Unlisted/Private/MembersOnly visibility types.
    /// Shows only Public visibility events with Published, Cancelled, or Completed status.
    /// </summary>
    public static EventFilter PubliclyDiscoverable() =>
        new(EventFilterType.PubliclyDiscoverable, e => e.VisibilityTypeId == (int)VisibilityTypeEnum.Public
                 && e.EventStatusId != (int)EventStatusEnum.Draft
                 && e.EventStatusId != (int)EventStatusEnum.Moderated
                 && e.EventStatusId != (int)EventStatusEnum.Archived);
}

public enum EventFilterType
{
    EventType,
    EventTypes,
    Format,
    Formats,
    Madhab,
    Madhabs,
    AudienceGender,
    AudienceGenders,
    AudienceAge,
    AudienceAges,
    Status,
    Statuses,
    Visibility,
    Actor,
    SearchTerm,
    DateFrom,
    DateTo,
    Free,
    PubliclyDiscoverable
}
