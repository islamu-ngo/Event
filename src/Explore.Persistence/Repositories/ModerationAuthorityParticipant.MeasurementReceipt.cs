using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationAuthorityParticipant
{
    /// <summary>Requalifies one accepted authentic assertion; it never certifies measured conformance.</summary>
    public async Task<ModerationMeasurementReceiptObservation?> AcquireConsumerMeasurementReceiptAsync(
        Guid transactionId, ModerationActivationBinding binding, string consumerId, Guid receiptId,
        DateTimeOffset observedAt, CancellationToken token)
    {
        var acquired = _acquisition;
        if (acquired is null || acquired.Transaction.TransactionId != transactionId
            || acquired.Rule?.Definition.Rule != binding.Rule)
            return null;
        using var evidence = BorrowConsumerEvidence(acquired);
        return await evidence.AcquireMeasurementAsync(binding, consumerId, receiptId, observedAt, token);
    }
}
