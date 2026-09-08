namespace Explore.Domain;

public sealed class PromotionEligibility
{
    private readonly HashSet<Guid>? _eligibleTicketTypeIds;

    private PromotionEligibility(IEnumerable<Guid>? eligibleTicketTypeIds)
    {
        _eligibleTicketTypeIds = eligibleTicketTypeIds is null ? null : new HashSet<Guid>(eligibleTicketTypeIds);
    }

    public bool IncludesAllTickets => _eligibleTicketTypeIds is null;

    public IReadOnlyCollection<Guid> EligibleTicketTypeIds => _eligibleTicketTypeIds?.ToArray() ?? [];

    public static PromotionEligibility AllTickets() => new(null);

    public static PromotionEligibility ForTicketTypes(IEnumerable<Guid> ticketTypeIds)
    {
        ArgumentNullException.ThrowIfNull(ticketTypeIds);
        Guid[] materializedIds = ticketTypeIds.ToArray();
        if (materializedIds.Length == 0 || materializedIds.Any(static id => id == Guid.Empty) || materializedIds.Distinct().Count() != materializedIds.Length)
        {
            throw new ArgumentException("Promotion eligibility requires distinct ticket type ids.", nameof(ticketTypeIds));
        }

        return new PromotionEligibility(materializedIds);
    }

    public bool IsEligible(Guid ticketTypeId) => ticketTypeId != Guid.Empty && (_eligibleTicketTypeIds is null || _eligibleTicketTypeIds.Contains(ticketTypeId));
}
