using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Waitlist;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Closed account or bearer reads. Caller identities are never accepted as authority.</summary>
public interface IRegistrationPrivateReader
{
    Task<RegistrationPrivateGraph?> ReadAsync(
        RegistrationPrivateRead request, string? capability, CancellationToken cancellationToken);
}

public enum RegistrationPrivateReadKind
{
    AddOns,
    Waitlist,
    Readiness,
    PurchaseOrder,
    Transfer,
    AccountOrder,
    GuestRecovery
}

public sealed record RegistrationPrivateRead(
    RegistrationPrivateReadKind Kind,
    Guid EventId,
    Guid OrderId = default,
    Guid LineId = default,
    Guid ParticipantId = default,
    Guid AssignmentId = default,
    Guid TicketId = default,
    Guid TransferId = default);

/// <summary>Complete selected entities; query handlers retain response and affordance mapping.</summary>
public sealed class RegistrationPrivateGraph
{
    public required RegistrationOrder Order { get; init; }
    public Event? Event { get; init; }
    public Guid? PrincipalId { get; set; }
    public bool CanManageAttendees { get; set; }
    public DateTime? BearerExpiresAt { get; set; }
    public EventAddOnCatalogVersion? AddOnCatalog { get; init; }
    public IReadOnlyList<EventAddOnFulfillment> Fulfillments { get; init; } = [];
    public IReadOnlyList<EventAddOnRefundAllocation> Refunds { get; init; } = [];
    public FairReturnWaitlistAccessContext? Waitlist { get; init; }
    public bool ReplacementSettled { get; init; }
    public PaidCheckoutSaleControl? TenantSaleControl { get; init; }
    public PaidCheckoutSaleControl? EventSaleControl { get; init; }
    public RegistrationParticipant? Participant { get; init; }
    public ParticipantAdmissionEligibility? Eligibility { get; init; }
    public AdmissionTicket? Ticket { get; init; }
    public AdmissionTicketTransferAccessContext? Transfer { get; init; }
}

public interface IRegistrationPrivateReadRepository
{
    Task<RegistrationPrivateGraph?> ReadAsync(
        RegistrationPrivateRead request, CancellationToken cancellationToken);
}

/// <summary>The original scoped reader alone activates admission; persistence only consumes it.</summary>
public interface IRegistrationPrivateReadCustody
{
    RegistrationPrivateReadAdmission RequireActive(RegistrationPrivateRead request);
}

public sealed record RegistrationPrivateReadAdmission(
    Guid TenantId, Guid? PrincipalId, string? CapabilityHash, bool CanManageAttendees, DateTime EvaluatedAt)
{
    public override string ToString() => nameof(RegistrationPrivateReadAdmission);
}
