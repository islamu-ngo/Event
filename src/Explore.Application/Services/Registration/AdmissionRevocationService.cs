using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionRevocationService(
    IAdmissionRevocationRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAdmissionRevocationService
{
    public const string RefundReconciledReason = "RefundReconciled";
    public const string OrderCancellationReason = "OrderCancellation";

    public Task<AdmissionRevocationResult> ReconcileAsync(
        AdmissionRevocationRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request))
        {
            return Task.FromResult(Result(AdmissionRevocationOutcome.InvalidRequest));
        }

        return unitOfWork.ExecuteInTransactionAsync(
            token => ReconcileInCurrentTransactionAsync(request, token), cancellationToken);
    }

    // The caller retains the order and admission fences and owns commit/rollback.
    public async Task<AdmissionRevocationResult> ReconcileInCurrentTransactionAsync(
        AdmissionRevocationRequest request,
        CancellationToken token)
    {
        if (!IsValidRequest(request))
        {
            return Result(AdmissionRevocationOutcome.InvalidRequest);
        }

        AdmissionRevocationContext? context = await repository.LoadAsync(request, token);
        if (context is null ||
            context.TenantId != request.TenantId ||
            context.RegistrationOrderId != request.RegistrationOrderId)
        {
            return Result(AdmissionRevocationOutcome.NotFound);
        }

        if (!IsValidAllocationSet(request))
        {
            return new AdmissionRevocationResult(
                AdmissionRevocationOutcome.InvalidAllocation,
                [],
                context.Tickets.Select(ticket => ticket.Id).Order().ToArray());
        }

        DateTime appliedAt = timeProvider.GetUtcNow().UtcDateTime;
        var revoked = new List<Guid>();
        var preserved = new List<Guid>();
        foreach (AdmissionTicket ticket in context.Tickets.OrderBy(value => value.Id))
        {
            bool targeted = request.Reason == OrderCancellationReason ||
                IsFullyRefunded(ticket, request.RefundAllocations, appliedAt);
            if (targeted)
            {
                if (request.Reason == OrderCancellationReason &&
                    !IsTerminal((AdmissionTicketStatusEnum)ticket.AdmissionTicketStatusId))
                {
                    ticket.Cancel(appliedAt);
                }
                revoked.Add(ticket.Id);
            }
            else
            {
                preserved.Add(ticket.Id);
            }
        }

        return await repository.ApplyAsync(
            new AdmissionRevocationPersistenceRequest(
                request.TenantId,
                request.RegistrationOrderId,
                revoked,
                preserved),
            token);
    }

    private static bool IsValidRequest(AdmissionRevocationRequest? request) =>
        request is not null &&
        request.TenantId != Guid.Empty &&
        request.RegistrationOrderId != Guid.Empty &&
        request.Reason is RefundReconciledReason or OrderCancellationReason &&
        (request.Reason != OrderCancellationReason || request.RefundAllocations.Count == 0);

    private static bool IsValidAllocationSet(AdmissionRevocationRequest request) =>
        request.RefundAllocations
            .Select(fact => fact.OrderLineId)
            .Distinct()
            .Count() == request.RefundAllocations.Count &&
        request.RefundAllocations.All(fact =>
            fact.OrderLineId != Guid.Empty &&
            fact.RefundedMinor >= 0 &&
            fact.RelevantLineTotalMinor >= 0 &&
            (!fact.IsAdmissionLine ||
             fact.RefundedMinor <= fact.RelevantLineTotalMinor));

    private static bool IsFullyRefunded(
        AdmissionTicket ticket,
        IReadOnlyList<AdmissionRefundAllocationFact> facts,
        DateTime appliedAt)
    {
        AdmissionRefundAllocationFact? fact = facts.SingleOrDefault(value =>
            value.IsAdmissionLine &&
            value.OrderLineId == ticket.RegistrationOrderLineId);
        if (fact is null)
        {
            return false;
        }

        ticket.ApplyRefundAllocations(
            [AdmissionRefundLineAllocation.Create(
                ticket.RegistrationTicketAssignmentId,
                ticket.RegistrationOrderLineId,
                true,
                fact.RelevantLineTotalMinor,
                fact.RefundedMinor)],
            appliedAt);
        return fact.RelevantLineTotalMinor > 0 &&
            fact.RefundedMinor == fact.RelevantLineTotalMinor;
    }

    private static bool IsTerminal(AdmissionTicketStatusEnum status) => status is
        AdmissionTicketStatusEnum.Revoked or
        AdmissionTicketStatusEnum.Cancelled or
        AdmissionTicketStatusEnum.Transferred or
        AdmissionTicketStatusEnum.Expired;

    private static AdmissionRevocationResult Result(AdmissionRevocationOutcome outcome) =>
        new(outcome, [], []);
}
