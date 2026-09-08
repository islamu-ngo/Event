using Explore.Application.Contracts.Admissions;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionScannerAuthenticationService(
    IAdmissionScannerCapabilityMaterialService materialService,
    IAdmissionScannerCapabilityRepository repository,
    TimeProvider timeProvider) : IAdmissionScannerAuthenticationService
{
    private const int MaximumCapabilityLength = 512;
    private const int MaximumDigestLength = 256;

    public async Task<AdmissionScannerAuthenticationResult> AuthenticateAsync(
        AdmissionScannerAuthenticationRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Capability) ||
            request.Capability.Length > MaximumCapabilityLength)
        {
            return AdmissionScannerAuthenticationResult.Failed();
        }

        try
        {
            AdmissionScannerCapabilityDigestCandidates digests =
                await materialService.DigestCandidatesAsync(
                    new AdmissionScannerCapabilityDigestCandidatesRequest(request.Capability),
                    cancellationToken);
            if (!ValidCandidates(digests.Candidates))
            {
                return AdmissionScannerAuthenticationResult.Failed();
            }

            AdmissionScannerCapability? capability = await repository.FindByDigestCandidatesAsync(
                digests.Candidates.ToArray(),
                cancellationToken);
            if (capability is null || !ValidCapability(capability, timeProvider.GetUtcNow().UtcDateTime))
            {
                return AdmissionScannerAuthenticationResult.Failed();
            }

            AdmissionCheckInAction[] actions = ToApplicationActions(capability.Actions);
            return actions.Length > 0
                ? AdmissionScannerAuthenticationResult.Success(
                    capability.Id,
                    capability.TenantId,
                    capability.EventId,
                    capability.AdmissionTargetId,
                    actions)
                : AdmissionScannerAuthenticationResult.Failed();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return AdmissionScannerAuthenticationResult.Failed();
        }
    }

    private static bool ValidCandidates(
        IReadOnlyList<AdmissionScannerCapabilityDigestCandidate>? candidates) =>
        candidates is { Count: > 0 and <= AdmissionScannerCapabilityDigestOptions.MaximumKeyVersions } &&
        candidates.All(candidate =>
            candidate.KeyVersion > 0 &&
            !string.IsNullOrWhiteSpace(candidate.LookupDigest) &&
            candidate.LookupDigest.Length <= MaximumDigestLength) &&
        candidates.Select(candidate => (candidate.KeyVersion, candidate.LookupDigest))
            .Distinct()
            .Count() == candidates.Count;

    private static bool ValidCapability(AdmissionScannerCapability capability, DateTime evaluatedAt)
    {
        const AdmissionScannerCapabilityAction supportedActions =
            AdmissionScannerCapabilityAction.CheckIn | AdmissionScannerCapabilityAction.Undo;
        return capability.Id != Guid.Empty &&
            capability.TenantId != Guid.Empty &&
            capability.EventId != Guid.Empty &&
            capability.AdmissionTargetId != Guid.Empty &&
            capability.Actions != AdmissionScannerCapabilityAction.None &&
            (capability.Actions & ~supportedActions) == 0 &&
            capability.IsActiveAt(evaluatedAt);
    }

    private static AdmissionCheckInAction[] ToApplicationActions(
        AdmissionScannerCapabilityAction actions)
    {
        var result = new List<AdmissionCheckInAction>(2);
        if (actions.HasFlag(AdmissionScannerCapabilityAction.CheckIn))
        {
            result.Add(AdmissionCheckInAction.CheckIn);
        }
        if (actions.HasFlag(AdmissionScannerCapabilityAction.Undo))
        {
            result.Add(AdmissionCheckInAction.Undo);
        }

        return result.ToArray();
    }
}
