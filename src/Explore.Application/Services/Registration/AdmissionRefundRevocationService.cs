using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionRefundRevocationService(
    IRefundAttemptRepository refundRepository,
    IAdmissionRevocationService revocationService) : IAdmissionRefundRevocationService
{
    public async Task<AdmissionRevocationResult?> ReconcileSucceededAsync(
        Guid tenantId,
        Guid refundAttemptId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || refundAttemptId == Guid.Empty)
        {
            return null;
        }

        RefundAttempt? current = await refundRepository.GetByIdAsync(
            tenantId, refundAttemptId, cancellationToken);
        if (current?.BuyerRefundSucceededAt is null)
        {
            return null;
        }

        PaidOrderAcceptanceSnapshot? acceptance = await refundRepository.GetAcceptanceAsync(
            tenantId, current.PaidOrderAcceptanceSnapshotId, cancellationToken);
        if (acceptance is null || acceptance.RegistrationOrderId != current.RegistrationOrderId)
        {
            throw new InvalidOperationException("Refund admission authority is incomplete.");
        }

        IReadOnlyList<RefundAttempt> attempts = await refundRepository.GetByPaymentAsync(
            tenantId, current.PaymentAttemptId, cancellationToken);
        RefundLineAllocation[] succeededLines = attempts
            .Where(attempt => attempt.BuyerRefundSucceededAt.HasValue)
            .SelectMany(attempt => attempt.Lines)
            .ToArray();
        AdmissionRefundAllocationFact[] facts = acceptance.Lines
            .OrderBy(line => line.Ordinal)
            .Select(line =>
            {
                long refundedMinor = checked(succeededLines
                    .Where(refund => refund.OrderLineId == line.OrderLineId)
                    .Sum(refund => refund.OrganizerAmountMinor));
                if (refundedMinor > line.LineTotalMinor)
                {
                    throw new InvalidOperationException("Refunded ticket-line authority exceeds accepted value.");
                }
                return new AdmissionRefundAllocationFact(
                    line.OrderLineId,
                    true,
                    refundedMinor,
                    line.LineTotalMinor);
            })
            .ToArray();

        return await revocationService.ReconcileAsync(
            new AdmissionRevocationRequest(
                tenantId,
                current.RegistrationOrderId,
                AdmissionRevocationService.RefundReconciledReason,
                facts),
            cancellationToken);
    }
}
