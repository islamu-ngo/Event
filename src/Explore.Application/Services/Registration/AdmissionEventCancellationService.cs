using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionEventCancellationService(
    IAdmissionEventCancellationRepository repository,
    IAdmissionRevocationService revocationService,
    IUnitOfWork unitOfWork,
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

        repository.RequireCommittedCancellationScope(tenantId);
        // Do not retain the Event fence while acquiring orders: issuance owns order -> Event.
        IReadOnlyList<Guid> orderIds = await unitOfWork.ExecuteReadCommittedAsync(
            token => repository.ListRevocableOrderIdsAsync(tenantId, eventId, BatchSize, token),
            cancellationToken);
        int reconciled = 0;
        foreach (Guid orderId in orderIds)
        {
            AdmissionRevocationResult result = await revocationService.ReconcileCommittedCancellationAsync(
                new AdmissionCommittedCancellationRequest(tenantId, orderId, eventId, AdmissionCancellationSource.Event),
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
            repository.RequireCommittedCancellationScope(tenantId);
            DateTime createdAt = timeProvider.GetUtcNow().UtcDateTime;
            await unitOfWork.ExecuteReadCommittedAsync(async token =>
            {
                await repository.ScheduleContinuationAsync(sourceMessageId, tenantId, eventId, createdAt, token);
                return true;
            }, cancellationToken);
        }
        return reconciled;
    }
}
