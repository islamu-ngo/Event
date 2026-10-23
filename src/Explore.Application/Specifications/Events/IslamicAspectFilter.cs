using System.Linq.Expressions;
using Explore.Domain;

namespace Explore.Application.Specifications.Events;

/// <summary>
/// Static factory for creating Islamic aspect filter specifications.
/// Each filter targets <see cref="EventIslamicAspect"/> properties via the Event navigation property.
/// These filters are only composed when the Islamic module is enabled for the current tenant.
/// </summary>
/// <remarks>
/// Usage (module-conditional):
/// <code>
/// if (await moduleService.IsModuleEnabledAsync(tenantId, "Mod_Islamic"))
/// {
///     spec = spec.And(IslamicAspectFilter.GenderMode(GenderSegregationMode.WomenOnly))
///                .And(IslamicAspectFilter.IncludesQuranRecitation());
/// }
/// </code>
/// </remarks>
public sealed class IslamicAspectFilter : IFilterSpecification<Event>
{
    public Expression<Func<Event, bool>> Predicate { get; }
    public IslamicAspectFilterType FilterType { get; }
    public EventFilterOperand Operand { get; }

    private IslamicAspectFilter(IslamicAspectFilterType type, Expression<Func<Event, bool>> predicate,
        EventFilterOperand? operand = null)
    {
        FilterType = type;
        Predicate = predicate;
        Operand = operand ?? new();
    }

    /// <summary>
    /// Filters events by Islamic aspect madhab (school of jurisprudence).
    /// Requires the Islamic aspect to exist on the event.
    /// </summary>
    public static IslamicAspectFilter Madhab(int madhabId) =>
        new(IslamicAspectFilterType.Madhab, e => e.IslamicAspect != null && e.IslamicAspect.MadhabId == madhabId,
            new(Integer: madhabId));

    /// <summary>
    /// Filters events by gender segregation mode.
    /// </summary>
    public static IslamicAspectFilter GenderMode(GenderSegregationMode genderMode) =>
        new(IslamicAspectFilterType.GenderMode, e => e.IslamicAspect != null && e.IslamicAspect.GenderMode == genderMode,
            new(Integer: (int)genderMode));

    /// <summary>
    /// Filters events by any of the specified gender segregation modes (OR logic).
    /// </summary>
    public static IslamicAspectFilter GenderModes(List<int> genderModeIds)
    {
        genderModeIds = [.. genderModeIds];
        return new(IslamicAspectFilterType.GenderModes,
            e => e.IslamicAspect != null && genderModeIds.Contains((int)e.IslamicAspect.GenderMode),
            new(Integers: [.. genderModeIds]));
    }

    /// <summary>
    /// Filters events that include Quran recitation.
    /// </summary>
    public static IslamicAspectFilter IncludesQuranRecitation() =>
        new(IslamicAspectFilterType.IncludesQuranRecitation, e => e.IslamicAspect != null && e.IslamicAspect.IncludesQuranRecitation);

    /// <summary>
    /// Filters events by reference prayer time for scheduling.
    /// </summary>
    public static IslamicAspectFilter ReferencePrayer(PrayerTime prayerTime) =>
        new(IslamicAspectFilterType.ReferencePrayer, e => e.IslamicAspect != null && e.IslamicAspect.ReferencePrayer == prayerTime,
            new(Integer: (int)prayerTime));

    /// <summary>
    /// Filters events by any of the specified reference prayer times (OR logic).
    /// </summary>
    public static IslamicAspectFilter ReferencePrayers(List<int> prayerTimeIds)
    {
        prayerTimeIds = [.. prayerTimeIds];
        return new(IslamicAspectFilterType.ReferencePrayers, e => e.IslamicAspect != null &&
                 e.IslamicAspect.ReferencePrayer != null &&
                 prayerTimeIds.Contains((int)e.IslamicAspect.ReferencePrayer), new(Integers: [.. prayerTimeIds]));
    }

    /// <summary>
    /// Filters events by primary language of Islamic content.
    /// </summary>
    public static IslamicAspectFilter PrimaryLanguage(int languageId) =>
        new(IslamicAspectFilterType.PrimaryLanguage, e => e.IslamicAspect != null && e.IslamicAspect.PrimaryLanguageId == languageId,
            new(Integer: languageId));

    /// <summary>
    /// Filters events by any of the specified Islamic primary languages (OR logic).
    /// </summary>
    public static IslamicAspectFilter PrimaryLanguages(List<int> languageIds)
    {
        languageIds = [.. languageIds];
        return new(IslamicAspectFilterType.PrimaryLanguages, e => e.IslamicAspect != null &&
                 e.IslamicAspect.PrimaryLanguageId != null &&
                 languageIds.Contains(e.IslamicAspect.PrimaryLanguageId.Value), new(Integers: [.. languageIds]));
    }
}

public enum IslamicAspectFilterType
{
    Madhab, GenderMode, GenderModes, IncludesQuranRecitation, ReferencePrayer, ReferencePrayers,
    PrimaryLanguage, PrimaryLanguages
}
