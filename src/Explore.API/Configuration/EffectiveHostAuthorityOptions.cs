namespace Explore.API.Configuration;

/// <summary>Deployment authority, deliberately independent of tenant-editable domain reservations.</summary>
public sealed class EffectiveHostAuthorityOptions
{
    public const string SectionName = "EffectiveHostAuthority";

    public string CanonicalOrigin { get; set; } = "https://localhost";
    public string[] InstanceHosts { get; set; } = [];
    public string[] SubdomainBaseDomains { get; set; } = [];
    public TenantDomainOwnershipAttestation[] TenantDomains { get; set; } = [];

    public Uri GetCanonicalOrigin()
    {
        if (!Uri.TryCreate(CanonicalOrigin, UriKind.Absolute, out var origin)
            || (origin.Scheme != Uri.UriSchemeHttps && !(origin.IsLoopback && origin.Scheme == Uri.UriSchemeHttp))
            || origin.UserInfo.Length != 0
            || origin.IdnHost.Length > 253 || Uri.CheckHostName(origin.IdnHost) == UriHostNameType.Unknown
            || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new InvalidOperationException("effective_host_canonical_origin_invalid");
        return origin;
    }

    public bool IsInstanceHost(string host) =>
        string.Equals(GetCanonicalOrigin().IdnHost, host, StringComparison.OrdinalIgnoreCase)
        || InstanceHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        _ = GetCanonicalOrigin();
        if (InstanceHosts.Length > 32 || SubdomainBaseDomains.Length > 32 || TenantDomains.Length > 1024
            || InstanceHosts.Concat(SubdomainBaseDomains).Any(host => !IsExactHost(host))
            || SubdomainBaseDomains.Any(host => !IsInstanceHost(host))
            || TenantDomains.Any(proof => proof is null || !IsExactHost(proof.Host)
                || proof.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(proof.EvidenceReference)
                || proof.EvidenceReference.Length > 256 || proof.VerifiedAt == default
                || proof.ExpiresAt <= proof.VerifiedAt || proof.ExpiresAt - proof.VerifiedAt > TimeSpan.FromHours(24))
            || TenantDomains.Select(proof => proof.Host).Distinct(StringComparer.OrdinalIgnoreCase).Count() != TenantDomains.Length)
            throw new InvalidOperationException("effective_host_authority_invalid");
    }

    private static bool IsExactHost(string host) =>
        !string.IsNullOrWhiteSpace(host) && host.Length <= 253
        && host == host.Trim().TrimEnd('.')
        && Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
}

/// <summary>
/// An operator's short-lived receipt for independently collected ownership proof.
/// Neither a tenant reservation nor a successful DNS resolution issues this receipt.
/// </summary>
public sealed class TenantDomainOwnershipAttestation
{
    public string Host { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string EvidenceReference { get; set; } = string.Empty;

    public bool IsCurrent(string host, Guid tenantId, DateTimeOffset now) =>
        tenantId != Guid.Empty && TenantId == tenantId
        && string.Equals(Host, host, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(EvidenceReference)
        && VerifiedAt <= now && now < ExpiresAt
        && ExpiresAt - VerifiedAt <= TimeSpan.FromHours(24);
}
