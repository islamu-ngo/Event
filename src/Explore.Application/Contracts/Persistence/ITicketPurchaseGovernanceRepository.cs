using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ITicketPurchaseGovernanceRepository
{
    Task<TicketPurchasePolicyVersion?> GetPolicyVersionAsync(
        Guid tenantId,
        Guid eventId,
        Guid policyVersionId,
        CancellationToken cancellationToken);

    Task<TicketPurchasePolicyVersion?> GetCurrentPolicyVersionAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task AddPolicyVersionAsync(
        TicketPurchasePolicyVersion policy,
        CancellationToken cancellationToken);

    Task<TicketPurchaseReservationResult> ReserveAsync(
        TicketPurchasePolicyVersion policy,
        TicketPurchaseReservationRequest request,
        CancellationToken cancellationToken);
}
