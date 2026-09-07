using Explore.Application.Contracts.Admissions;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionEventCancellationService(
    IAdmissionEventCancellationRepository repository,
    IAdmissionRevocationService revocationService,
    TimeProvider timeProvider) : IAdmissionEventCancellationService
{
    private const int BatchSize = 100;

    public async Task<int> ReconcileAsync(
        Guid sourceMessageId,
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        if (sourceMessageId == Guid.Empty || tenantId == Guid.Empty || eventId == Guid.Empty)
        {
            throw new ArgumentException("Event admission cancellation requires tenant and event.");
        }

        IReadOnlyList<Guid> orderIds = await repository.ListRevocableOrderIdsAsync(
            tenantId, eventId, BatchSize, cancellationToken);
        int reconciled = 0;
        foreach (Guid orderId in orderIds)
        {
            AdmissionRevocationResult result = await revocationService.ReconcileAsync(
                new AdmissionRevocationRequest(
                    tenantId,
                    orderId,
                    AdmissionRevocationService.OrderCancellationReason,
                    []),
                cancellationToken);
            if (result.Outcome != AdmissionRevocationOutcome.Applied)
            {
                throw new InvalidOperationException(
                    "Event cancellation admission revocation did not converge.");
            }
            reconciled = checked(reconciled + result.RevokedTicketIds.Count);
        }

        if (orderIds.Count == BatchSize)
        {
            await repository.ScheduleContinuationAsync(
                sourceMessageId,
                tenantId,
                eventId,
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
        }
        return reconciled;
    }
}
