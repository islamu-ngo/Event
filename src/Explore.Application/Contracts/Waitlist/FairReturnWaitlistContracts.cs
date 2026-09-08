using Explore.Domain;

namespace Explore.Application.Contracts.Waitlist;

public sealed record FairReturnAllocationRequest(
    Guid TenantId,
    Guid EventId,
    Guid FairReturnSupplyPolicyId,
    Guid EventWaitlistOfferId,
    Guid ExistingCapacityHoldId,
    Guid SourceBindingId,
    DateTime AllocatedAtUtc);

public sealed record FairReturnWithdrawalRequest(
    Guid TenantId,
    Guid EventId,
    Guid FairReturnSupplyUnitId,
    DateTime WithdrawnAtUtc);

public sealed record FairReturnSubstitutionRequest(
    Guid TenantId,
    Guid EventId,
    Guid FairReturnSourceBindingId,
    Guid ReplacementSupplyUnitId,
    DateTime SubstitutedAtUtc);

public sealed record WaitlistOfferExpiryRequest(
    Guid TenantId,
    Guid EventId,
    Guid EventWaitlistOfferId,
    DateTime ExpiredAtUtc);

public sealed record WaitlistReplacementFinalizeRequest(
    Guid TenantId,
    Guid EventId,
    Guid EventWaitlistOfferId,
    DateTime FinalizedAtUtc);

public sealed record FairReturnWaitlistResult(
    FairReturnOutcome Outcome,
    FairReturnSupplyUnit? Supply,
    EventWaitlistEntry? Entry,
    EventWaitlistOffer? Offer,
    FairReturnSourceBinding? Binding);

public sealed record FairReturnWaitlistAccessContext(
    RegistrationOrder Order,
    RegistrationOrderLine Line,
    EventWaitlistEntry? Entry,
    EventWaitlistOffer? Offer,
    FairReturnSupplyUnit? Supply,
    FairReturnSourceBinding? Binding,
    FairReturnSupplyPolicy? Policy,
    TicketPurchaseOperation? PurchaseOperation,
    long Position);

public interface IFairReturnWaitlistRepository
{
    Task<FairReturnWaitlistAccessContext?>
        GetAccessAsync(
            Guid tenantId,
            Guid eventId,
            Guid registrationOrderId,
            Guid registrationOrderLineId,
            CancellationToken cancellationToken);

    Task<EventWaitlistEntry> EnqueueAsync(
        EventWaitlistEntry entry,
        CancellationToken cancellationToken);

    Task<EventWaitlistEntry?> LeaveAsync(
        Guid tenantId,
        Guid eventId,
        Guid registrationOrderLineId,
        DateTime withdrawnAtUtc,
        CancellationToken cancellationToken);

    Task<bool> HasReplacementSettlementAsync(
        Guid tenantId,
        Guid fairReturnSourceBindingId,
        CancellationToken cancellationToken);

    Task<FairReturnWaitlistResult> AllocateAsync(
        FairReturnAllocationRequest request,
        CancellationToken cancellationToken);

    Task<FairReturnWaitlistResult> WithdrawAsync(
        FairReturnWithdrawalRequest request,
        CancellationToken cancellationToken);

    Task<FairReturnWaitlistResult> SubstituteAsync(
        FairReturnSubstitutionRequest request,
        CancellationToken cancellationToken);

    Task<FairReturnWaitlistResult> ExpireOfferAsync(
        WaitlistOfferExpiryRequest request,
        CancellationToken cancellationToken);

    Task<FairReturnWaitlistResult>
        FinalizeReplacementAsync(
            WaitlistReplacementFinalizeRequest request,
            CancellationToken cancellationToken);
}
