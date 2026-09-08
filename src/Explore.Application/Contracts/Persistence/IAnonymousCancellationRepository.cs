// ABOUTME: Loads exact cancellation evidence under retained issuance, readiness, ticket and target fences.
// ABOUTME: Releases only the exact consumed inventory identities authorized by the cancelled aggregate.

using Explore.Domain;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Contracts.Persistence;

public sealed record AnonymousCancellationContext(
    RegistrationOrder Order,
    AnonymousCancellationEvidence Evidence,
    IReadOnlyList<AdmissionTicket> Tickets,
    IReadOnlyList<RegistrationInventoryHold> Holds)
{
    public bool IsEligible => AnonymousCancellationRules.IsEligible(Order, Evidence) &&
        Holds.All(hold => !hold.IsDeleted &&
            hold.RegistrationInventoryHoldStatusId == (int)Explore.Domain.Enums.RegistrationInventoryHoldStatusEnum.Consumed &&
            hold.ConsumedAt is not null && hold.ReleasedAt is null);

    public bool IsCompleted => AnonymousCancellationRules.IsCancelled(Order, Evidence) &&
        Holds.All(hold => !hold.IsDeleted &&
            hold.RegistrationInventoryHoldStatusId == (int)Explore.Domain.Enums.RegistrationInventoryHoldStatusEnum.Released &&
            hold.ConsumedAt is not null && hold.ReleasedAt == Order.CancelledAt) &&
        Tickets.All(ticket => ticket.AdmissionTicketStatusId is
            (int)Explore.Domain.Enums.AdmissionTicketStatusEnum.Cancelled or
            (int)Explore.Domain.Enums.AdmissionTicketStatusEnum.Revoked or
            (int)Explore.Domain.Enums.AdmissionTicketStatusEnum.Transferred or
            (int)Explore.Domain.Enums.AdmissionTicketStatusEnum.Expired);
}

public interface IAnonymousCancellationRepository
{
    // Requires the real order-row and event fences already held by P09 authority. Reloads after promise CAS.
    Task<AnonymousCancellationContext> LoadInCurrentTransactionAsync(
        Guid tenantId, Guid eventId, Guid orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ReleaseConsumedInCurrentTransactionAsync(
        AnonymousCancellationContext context, DateTime releasedAt, CancellationToken cancellationToken);
}
