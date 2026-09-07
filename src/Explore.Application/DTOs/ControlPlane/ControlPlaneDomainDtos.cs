namespace Explore.Application.DTOs.ControlPlane;

public sealed record ControlPlaneDomainOverviewDto
{
    public string? PublicOrigin { get; init; }
    public string? PublicPlatformHost { get; init; }
    public string? InstanceBaseDomain { get; init; }
    public string? WildcardTenantHost { get; init; }
    public string? AdminOrigin { get; init; }
    public string? AdminHost { get; init; }
    public bool AllowTenantCustomDomains { get; init; }
    public bool LockTenantSubdomain { get; init; }
    public bool LockTenantCustomDomain { get; init; }
    public IReadOnlyList<ControlPlaneDnsRecordDto> DnsRecords { get; init; } = [];
    public IReadOnlyList<ControlPlaneWarningDto> Warnings { get; init; } = [];
}

public sealed record ControlPlaneDnsRecordDto
{
    public string Purpose { get; init; } = string.Empty;
    public string RecordType { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public bool Required { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Guidance { get; init; } = string.Empty;
}
