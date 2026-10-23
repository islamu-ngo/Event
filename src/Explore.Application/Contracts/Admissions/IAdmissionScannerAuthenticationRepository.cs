using Explore.Domain;

namespace Explore.Application.Contracts.Admissions;

/// <summary>Resolves an opaque scanner credential without borrowing request-tenant authority.</summary>
public interface IAdmissionScannerAuthenticationRepository
{
    Task<AdmissionScannerCapability?> FindByDigestCandidatesAsync(
        IReadOnlyList<AdmissionScannerCapabilityDigestCandidate> candidates,
        CancellationToken cancellationToken);
}
