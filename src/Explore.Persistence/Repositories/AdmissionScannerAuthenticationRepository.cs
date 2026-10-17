using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Repositories;

public sealed class AdmissionScannerAuthenticationRepository(IGlobalExploreDbContextFactory factory)
    : IAdmissionScannerAuthenticationRepository
{
    public async Task<AdmissionScannerCapability?> FindByDigestCandidatesAsync(
        IReadOnlyList<AdmissionScannerCapabilityDigestCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates is not { Count: > 0 and <= AdmissionScannerCapabilityDigestOptions.MaximumKeyVersions }
            || candidates.Any(candidate => candidate.KeyVersion <= 0
                || string.IsNullOrWhiteSpace(candidate.LookupDigest) || candidate.LookupDigest.Length > 256)
            || candidates.Select(candidate => (candidate.KeyVersion, candidate.LookupDigest))
                .Distinct().Count() != candidates.Count)
            return null;

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await AdmissionScannerAuthenticationQuery.ReadAsync(context, candidates, cancellationToken);
    }
}
