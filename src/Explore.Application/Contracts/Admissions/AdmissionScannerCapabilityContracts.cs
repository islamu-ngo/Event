using Explore.Domain;

namespace Explore.Application.Contracts.Admissions;

public enum AdmissionScannerCapabilityIssueOutcome
{
    Issued = 1,
    AlreadyIssued = 2,
    Rejected = 3
}

public enum AdmissionScannerCapabilityRevocationOutcome
{
    Revoked = 1,
    Rejected = 2
}

public sealed record AdmissionScannerCapabilityIssueRequest(
    Guid IssueRequestId,
    Guid TenantId,
    Guid EventId,
    Guid TargetId,
    IReadOnlyList<AdmissionCheckInAction> Actions,
    string DeviceLabel,
    DateTimeOffset ExpiresAtUtc,
    Guid IssuedByActorId);

public sealed record AdmissionScannerCapabilityReadRequest(
    Guid TenantId,
    Guid ScannerCapabilityId);

public sealed record AdmissionScannerCapabilityRevokeRequest(
    Guid TenantId,
    Guid EventId,
    Guid ScannerCapabilityId,
    Guid RevokedByActorId,
    string Reason);

public sealed record AdmissionScannerCapabilityMaterialRequest(
    Guid IssueRequestId,
    Guid TenantId,
    Guid ScannerCapabilityId);

public sealed record AdmissionScannerCapabilityDigestCandidatesRequest(string Capability)
{
    public override string ToString() =>
        "AdmissionScannerCapabilityDigestCandidatesRequest(<redacted>)";
}

public sealed record AdmissionScannerCapabilityDigestCandidate(
    int KeyVersion,
    string LookupDigest)
{
    public override string ToString() =>
        $"AdmissionScannerCapabilityDigestCandidate(keyVersion={KeyVersion}, <redacted>)";
}

public sealed record AdmissionScannerCapabilityDigestCandidates(
    IReadOnlyList<AdmissionScannerCapabilityDigestCandidate> Candidates)
{
    public override string ToString() =>
        $"AdmissionScannerCapabilityDigestCandidates(count={Candidates.Count}, <redacted>)";
}

public static class AdmissionScannerCapabilityDigestDomain
{
    public const string Purpose = "AdmissionScannerCapability/v1";
}

public sealed class AdmissionScannerCapabilityDigestOptions
{
    public const string SectionName = "Admissions:ScannerCapabilityDigest";
    public const int MaximumKeyVersions = 8;

    public int ActiveKeyVersion { get; set; } = 1;
    public int[] RetainedKeyVersions { get; set; } = [];

    public void Validate()
    {
        int[] versions = RetainedKeyVersions.Prepend(ActiveKeyVersion).ToArray();
        if (ActiveKeyVersion < 1 || RetainedKeyVersions.Any(version => version < 1) ||
            versions.Length > MaximumKeyVersions || versions.Distinct().Count() != versions.Length)
        {
            throw new InvalidOperationException(
                $"Scanner digest key versions must be positive, unique, and at most {MaximumKeyVersions}.");
        }
    }
}

public sealed record AdmissionScannerCapabilityMaterial
{
    public AdmissionScannerCapabilityMaterial(
        string plaintextCapability,
        string lookupDigest,
        int keyVersion)
    {
        PlaintextCapability = plaintextCapability;
        LookupDigest = lookupDigest;
        KeyVersion = keyVersion;
    }

    public string PlaintextCapability { get; }
    public string LookupDigest { get; }
    public int KeyVersion { get; }

    public override string ToString() =>
        $"AdmissionScannerCapabilityMaterial(keyVersion={KeyVersion}, <redacted>)";
}

public sealed record AdmissionScannerCapabilityDescriptor(
    Guid ScannerCapabilityId,
    Guid TenantId,
    Guid EventId,
    Guid TargetId,
    IReadOnlyList<AdmissionCheckInAction> Actions,
    string DeviceLabel,
    DateTimeOffset ExpiresAtUtc,
    bool Revoked,
    string MaskedCapability)
{
    public DateTimeOffset? RevokedAtUtc { get; init; }
}

public sealed record AdmissionScannerCapabilityStoreResult(
    bool Created,
    AdmissionScannerCapability Capability)
{
    public bool Rejected { get; init; }
}

public sealed record AdmissionScannerCapabilityIssuedResult
{
    public AdmissionScannerCapabilityIssuedResult(
        AdmissionScannerCapabilityIssueOutcome outcome,
        Guid scannerCapabilityId,
        string? plaintextCapability,
        AdmissionScannerCapabilityDescriptor? descriptor)
    {
        Outcome = outcome;
        ScannerCapabilityId = scannerCapabilityId;
        PlaintextCapability = plaintextCapability;
        Descriptor = descriptor;
    }

    public AdmissionScannerCapabilityIssueOutcome Outcome { get; }
    public Guid ScannerCapabilityId { get; }
    public string? PlaintextCapability { get; }
    public AdmissionScannerCapabilityDescriptor? Descriptor { get; }

    public override string ToString() =>
        $"AdmissionScannerCapabilityIssuedResult(outcome={Outcome}, id={ScannerCapabilityId}, <redacted>)";
}

public sealed record AdmissionScannerCapabilityRevocationResult(
    AdmissionScannerCapabilityRevocationOutcome Outcome,
    Guid ScannerCapabilityId);

public interface IAdmissionScannerCapabilityMaterialService
{
    Task<AdmissionScannerCapabilityMaterial> IssueAsync(
        AdmissionScannerCapabilityMaterialRequest request,
        CancellationToken cancellationToken);

    Task<AdmissionScannerCapabilityDigestCandidates> DigestCandidatesAsync(
        AdmissionScannerCapabilityDigestCandidatesRequest request,
        CancellationToken cancellationToken);
}

public interface IAdmissionScannerCapabilityRepository
{
    Task<AdmissionScannerCapabilityStoreResult> StoreAsync(
        AdmissionScannerCapability capability,
        CancellationToken cancellationToken);

    Task<AdmissionScannerCapability?> GetAsync(
        Guid tenantId,
        Guid scannerCapabilityId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdmissionScannerCapability>> ListAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task<AdmissionTarget?> FindPlatformManagedTargetAsync(
        Guid tenantId,
        Guid eventId,
        Guid targetId,
        CancellationToken cancellationToken);

    Task<AdmissionScannerCapability?> FindByDigestCandidatesAsync(
        IReadOnlyList<AdmissionScannerCapabilityDigestCandidate> candidates,
        CancellationToken cancellationToken);

    Task<AdmissionScannerCapability> UpdateAsync(
        AdmissionScannerCapability capability,
        CancellationToken cancellationToken);
}
