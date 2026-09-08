using System.Collections.Immutable;
using Explore.Domain;

namespace Explore.Application.Contracts.PrivacyErasure;

public sealed record PrivacyErasureRetentionRequest
{
    public PrivacyErasureRetentionRequest(
        DateTime asOfUtc,
        int batchSize,
        IEnumerable<long> heldAuthoritySequences)
    {
        if (asOfUtc == default || asOfUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Retention evaluation time must be a non-default UTC value.", nameof(asOfUtc));
        }

        if (batchSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        ArgumentNullException.ThrowIfNull(heldAuthoritySequences);
        ImmutableHashSet<long> held = heldAuthoritySequences.ToImmutableHashSet();
        if (held.Any(sequence => sequence <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(heldAuthoritySequences));
        }

        AsOfUtc = asOfUtc;
        BatchSize = batchSize;
        HeldAuthoritySequences = held;
    }

    public DateTime AsOfUtc { get; }
    public int BatchSize { get; }
    public ImmutableHashSet<long> HeldAuthoritySequences { get; }
}

public sealed record PrivacyErasureRetentionEvaluation(
    int EligibleCount,
    int HeldCount,
    long CurrentFloorSequence,
    long ProjectedFloorSequence);

public sealed record PrivacyErasureCompactionResult(
    int DeletedCount,
    int PseudonymizedCount,
    PrivacyErasureAuthorityState State);

public interface IPrivacyErasureAuthorityMaintenance
{
    Task<PrivacyErasureRetentionEvaluation> EvaluateRetentionAsync(
        PrivacyErasureRetentionRequest request,
        CancellationToken cancellationToken = default);

    Task<PrivacyErasureCompactionResult> CompactExpiredIntentsAsync(
        PrivacyErasureRetentionRequest request,
        CancellationToken cancellationToken = default);
}
