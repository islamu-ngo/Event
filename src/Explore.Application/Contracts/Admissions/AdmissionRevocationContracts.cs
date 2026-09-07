using Explore.Domain;

namespace Explore.Application.Contracts.Admissions;

public enum AdmissionRevocationOutcome
{
    Applied,
    InvalidRequest,
    InvalidAllocation,
    NotFound
}

public sealed record AdmissionRefundAllocationFact(
    Guid OrderLineId,
    bool IsAdmissionLine,
    long RefundedMinor,
    long RelevantLineTotalMinor);

public sealed record AdmissionRevocationRequest
{
    public AdmissionRevocationRequest(
        Guid tenantId,
        Guid registrationOrderId,
        string reason,
        IReadOnlyList<AdmissionRefundAllocationFact> refundAllocations)
    {
        TenantId = tenantId;
        RegistrationOrderId = registrationOrderId;
        Reason = reason;
        RefundAllocations = refundAllocations?.ToArray()
            ?? throw new ArgumentNullException(nameof(refundAllocations));
    }

    public Guid TenantId { get; }
    public Guid RegistrationOrderId { get; }
    public string Reason { get; }
    public IReadOnlyList<AdmissionRefundAllocationFact> RefundAllocations { get; }
}

public sealed record AdmissionRevocationContext
{
    public AdmissionRevocationContext(
        Guid tenantId,
        Guid registrationOrderId,
        IReadOnlyList<AdmissionTicket> tickets)
    {
        TenantId = tenantId;
        RegistrationOrderId = registrationOrderId;
        Tickets = tickets?.ToArray() ?? throw new ArgumentNullException(nameof(tickets));
    }

    public Guid TenantId { get; }
    public Guid RegistrationOrderId { get; }
    public IReadOnlyList<AdmissionTicket> Tickets { get; }
}

public sealed record AdmissionRevocationPersistenceRequest
{
    public AdmissionRevocationPersistenceRequest(
        Guid tenantId,
        Guid registrationOrderId,
        IReadOnlyList<Guid> revokedTicketIds,
        IReadOnlyList<Guid> preservedTicketIds)
    {
        TenantId = tenantId;
        RegistrationOrderId = registrationOrderId;
        RevokedTicketIds = revokedTicketIds?.ToArray()
            ?? throw new ArgumentNullException(nameof(revokedTicketIds));
        PreservedTicketIds = preservedTicketIds?.ToArray()
            ?? throw new ArgumentNullException(nameof(preservedTicketIds));
    }

    public Guid TenantId { get; }
    public Guid RegistrationOrderId { get; }
    public IReadOnlyList<Guid> RevokedTicketIds { get; }
    public IReadOnlyList<Guid> PreservedTicketIds { get; }
}

public sealed record AdmissionRevocationResult
{
    public AdmissionRevocationResult(
        AdmissionRevocationOutcome outcome,
        IReadOnlyList<Guid> revokedTicketIds,
        IReadOnlyList<Guid> preservedTicketIds)
    {
        Outcome = outcome;
        RevokedTicketIds = revokedTicketIds?.ToArray()
            ?? throw new ArgumentNullException(nameof(revokedTicketIds));
        PreservedTicketIds = preservedTicketIds?.ToArray()
            ?? throw new ArgumentNullException(nameof(preservedTicketIds));
    }

    public AdmissionRevocationOutcome Outcome { get; }
    public IReadOnlyList<Guid> RevokedTicketIds { get; }
    public IReadOnlyList<Guid> PreservedTicketIds { get; }
}

public interface IAdmissionRevocationService
{
    Task<AdmissionRevocationResult> ReconcileAsync(
        AdmissionRevocationRequest request,
        CancellationToken cancellationToken);
}

public interface IAdmissionRefundRevocationService
{
    Task<AdmissionRevocationResult?> ReconcileSucceededAsync(
        Guid tenantId,
        Guid refundAttemptId,
        CancellationToken cancellationToken);
}

public interface IAdmissionEventCancellationService
{
    Task<int> ReconcileAsync(
        Guid sourceMessageId,
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);
}

public interface IAdmissionRevocationRepository
{
    Task<AdmissionRevocationContext?> LoadAsync(
        AdmissionRevocationRequest request,
        CancellationToken cancellationToken);

    Task<AdmissionRevocationResult> ApplyAsync(
        AdmissionRevocationPersistenceRequest request,
        CancellationToken cancellationToken);
}

public interface IAdmissionEventCancellationRepository
{
    Task<IReadOnlyList<Guid>> ListRevocableOrderIdsAsync(
        Guid tenantId,
        Guid eventId,
        int batchSize,
        CancellationToken cancellationToken);

    Task ScheduleContinuationAsync(
        Guid sourceMessageId,
        Guid tenantId,
        Guid eventId,
        DateTime createdAt,
        CancellationToken cancellationToken);
}
