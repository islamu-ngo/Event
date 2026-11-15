using System.Collections.Immutable;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Discovers bounded admission metadata only. Candidates are hints; recovery must
/// reacquire the exact tenant operation and verify its current generation.
/// </summary>
public interface IVisitorEnrollmentRecoveryReader
{
    const int MaximumCandidates = 100;

    Task<ImmutableArray<VisitorEnrollmentRecoveryCandidate>> ReadCandidatesAsync(
        DateTime nowUtc, int maximumCandidates, CancellationToken cancellationToken = default);
}

/// <summary>No identity, intent digest, credential or membership evidence leaves discovery.</summary>
public sealed record VisitorEnrollmentRecoveryCandidate(
    Guid Id,
    Guid TenantId,
    VisitorEnrollmentStage Stage,
    long Generation,
    DateTime ExpiresAt);
