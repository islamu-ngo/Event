using Explore.Application.Services.Moderation;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationAuthorityParticipant
{
    /// <summary>Requalifies installed artifacts on the original acquisition without a conformance verdict.</summary>
    public async Task<ModerationCapabilitySourceObservation?> AcquireConsumerInstallationAsync(
        Guid transactionId, ModerationActivationBinding binding, string consumerId,
        DateTimeOffset observedAt, CancellationToken token)
    {
        var acquired = _acquisition;
        if (acquired is null || acquired.Transaction.TransactionId != transactionId
            || acquired.Rule?.Definition.Rule != binding.Rule)
            return null;
        using var evidence = BorrowConsumerEvidence(acquired);
        return await evidence.AcquireInstallationAsync(binding, consumerId, observedAt, token);
    }

    private ModerationConsumerEvidenceSession BorrowConsumerEvidence(Acquisition acquired) =>
        new(context, acquired.Transaction, acquired.Source, acquired.Policy,
            token => CurrentAsync(acquired, token), publications, inventory,
            consumerSources, measurementReceipts, clock,
            (publication, decision, observedAt, token) => AcquireProfileConfigurationAsync(
                acquired, acquired.Transaction.TransactionId, publication, decision, acquired.Grant, observedAt, token));
}
