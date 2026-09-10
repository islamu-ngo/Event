
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Contracts.Persistence;

public sealed record AnonymousCancellationContext(
    RegistrationOrder Order,
    AnonymousCancellationEvidence Evidence,
    IReadOnlyList<AdmissionTicket> Tickets,
    IReadOnlyList<RegistrationInventoryHold> Holds)
{
    // Consumed identities remain stable when cancellation changes their status to Released.
    public IEnumerable<RegistrationInventoryHold> ConsumedHolds => Holds.Where(hold => hold.ConsumedAt is not null);

    public bool IsEligible => AnonymousCancellationRules.IsEligible(Order, Evidence) &&
        (Holds.Count == 0 || ConsumedHolds.Any()) &&
        Holds.All(hold => !hold.IsDeleted &&
            (IsExpiredHistory(hold) ||
             hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Consumed &&
             hold.ConsumedAt is not null && hold.ReleasedAt is null));

    public bool IsCompleted => AnonymousCancellationRules.IsCancelled(Order, Evidence) &&
        (Holds.Count == 0 || ConsumedHolds.Any()) &&
        Holds.All(hold => !hold.IsDeleted &&
            (IsExpiredHistory(hold) ||
             hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Released &&
             hold.ConsumedAt is not null && hold.ReleasedAt == Order.CancelledAt)) &&
        Tickets.All(ticket => ticket.AdmissionTicketStatusId is
            (int)Explore.Domain.Enums.AdmissionTicketStatusEnum.Cancelled or
            (int)Explore.Domain.Enums.AdmissionTicketStatusEnum.Revoked or
            (int)Explore.Domain.Enums.AdmissionTicketStatusEnum.Transferred or
            (int)Explore.Domain.Enums.AdmissionTicketStatusEnum.Expired);

    private static bool IsExpiredHistory(RegistrationInventoryHold hold) =>
        hold.RegistrationInventoryHoldStatusId == (int)RegistrationInventoryHoldStatusEnum.Expired &&
        hold.ConsumedAt is null && hold.ReleasedAt >= hold.ExpiresAt;
}

public interface IAnonymousCancellationRepository
{
    // Requires the real order-row and event fences already held by P09 authority. Reloads after promise CAS.
    Task<AnonymousCancellationContext> LoadInCurrentTransactionAsync(
        Guid tenantId, Guid eventId, Guid orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ReleaseConsumedInCurrentTransactionAsync(
        AnonymousCancellationContext context, DateTime releasedAt, CancellationToken cancellationToken);
}
