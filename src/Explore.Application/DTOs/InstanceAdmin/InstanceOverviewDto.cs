namespace Explore.Application.DTOs.InstanceAdmin;

public sealed record InstanceOverviewDto
{
    public string Version { get; init; } = string.Empty;
    public string DeploymentMode { get; init; } = string.Empty;
    public string? PublicOrigin { get; init; }
    public string? AdminOrigin { get; init; }
    public string? InstanceBaseDomain { get; init; }
    public int TotalTenantCount { get; init; }
    public int ActiveTenantCount { get; init; }
    public IReadOnlyList<InstanceTenantStatusCountDto> TenantStatusCounts { get; init; } = [];
    public IReadOnlyList<InstanceProviderSummaryDto> ProviderSummaries { get; init; } = [];
    public IReadOnlyList<InstanceWarningDto> Warnings { get; init; } = [];
}

public sealed record InstanceTenantStatusCountDto
{
    public string Status { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed record InstanceProviderSummaryDto
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool Configured { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Message { get; init; }
}

public sealed record InstanceWarningDto
{
    public string Code { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Remediation { get; init; }
}
