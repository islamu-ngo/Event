using System.Linq.Expressions;
using Explore.Domain;

namespace Explore.Application.Specifications.Events;

/// <summary>
/// Static factory for creating Tech aspect filter specifications.
/// Each filter targets <see cref="EventTechAspect"/> properties via the Event navigation property.
/// These filters are only composed when the Tech module is enabled for the current tenant.
/// </summary>
/// <remarks>
/// Usage (module-conditional):
/// <code>
/// if (await moduleService.IsModuleEnabledAsync(tenantId, "Mod_Tech"))
/// {
///     spec = spec.And(TechAspectFilter.SkillLevel(Domain.SkillLevel.Advanced))
///                .And(TechAspectFilter.RequiresLaptop());
/// }
/// </code>
/// </remarks>
public sealed class TechAspectFilter : IFilterSpecification<Event>
{
    public Expression<Func<Event, bool>> Predicate { get; }
    public TechAspectFilterType FilterType { get; }
    public EventFilterOperand Operand { get; }

    private TechAspectFilter(TechAspectFilterType type, Expression<Func<Event, bool>> predicate,
        EventFilterOperand? operand = null)
    {
        FilterType = type;
        Predicate = predicate;
        Operand = operand ?? new();
    }

    /// <summary>
    /// Filters events by required skill level.
    /// </summary>
    public static TechAspectFilter SkillLevel(SkillLevel skillLevel) =>
        new(TechAspectFilterType.SkillLevel, e => e.TechAspect != null && e.TechAspect.SkillLevel == skillLevel,
            new(Integer: (int)skillLevel));

    /// <summary>
    /// Filters events that are coding competitions.
    /// </summary>
    public static TechAspectFilter IsCodingCompetition() =>
        new(TechAspectFilterType.IsCodingCompetition, e => e.TechAspect != null && e.TechAspect.IsCodingCompetition);

    /// <summary>
    /// Filters events that are hackathons (have a hackathon track defined).
    /// </summary>
    public static TechAspectFilter IsHackathon() =>
        new(TechAspectFilterType.IsHackathon, e => e.TechAspect != null && e.TechAspect.HackathonTrack != null);

    /// <summary>
    /// Filters events by hackathon track name (case-insensitive contains).
    /// </summary>
    public static TechAspectFilter HackathonTrack(string trackName) =>
        new(TechAspectFilterType.HackathonTrack, e => e.TechAspect != null &&
                 e.TechAspect.HackathonTrack != null &&
                 e.TechAspect.HackathonTrack.Contains(trackName), new(Text: trackName));

    /// <summary>
    /// Filters events that require a laptop.
    /// </summary>
    public static TechAspectFilter RequiresLaptop() =>
        new(TechAspectFilterType.RequiresLaptop, e => e.TechAspect != null && e.TechAspect.RequiresLaptop);

    /// <summary>
    /// Filters events by tech stack tags (case-insensitive contains search).
    /// Searches within the comma-separated TechStackTags field.
    /// </summary>
    public static TechAspectFilter TechStack(string techTag) =>
        new(TechAspectFilterType.TechStack, e => e.TechAspect != null &&
                 e.TechAspect.TechStackTags != null &&
                 e.TechAspect.TechStackTags.Contains(techTag), new(Text: techTag));

    /// <summary>
    /// Filters events that have a prize pool (prize pool amount is not null and greater than 0).
    /// </summary>
    public static TechAspectFilter HasPrizePool() =>
        new(TechAspectFilterType.HasPrizePool, e => e.TechAspect != null && e.TechAspect.PrizePool != null && e.TechAspect.PrizePool > 0);
}

public enum TechAspectFilterType
{
    SkillLevel, IsCodingCompetition, IsHackathon, HackathonTrack, RequiresLaptop, TechStack, HasPrizePool
}
