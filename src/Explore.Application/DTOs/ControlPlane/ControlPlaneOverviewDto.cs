namespace Explore.Application.DTOs.ControlPlane;

public sealed record ControlPlaneOverviewDto
{
    public string Version { get; init; } = string.Empty;
    public string DeploymentMode { get; init; } = string.Empty;
    public string? PublicOrigin { get; init; }
    public string? AdminOrigin { get; init; }
    public string? InstanceBaseDomain { get; init; }
    public int TotalTenantCount { get; init; }
    public int ActiveTenantCount { get; init; }
    public IReadOnlyList<ControlPlaneTenantStatusCountDto> TenantStatusCounts { get; init; } = [];
    public IReadOnlyList<ControlPlaneProviderSummaryDto> ProviderSummaries { get; init; } = [];
    public IReadOnlyList<ControlPlaneWarningDto> Warnings { get; init; } = [];
}

public sealed record ControlPlaneTenantStatusCountDto
{
    public string Status { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed record ControlPlaneProviderSummaryDto
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool Configured { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Message { get; init; }
}

public sealed record ControlPlaneWarningDto
{
    public string Code { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Remediation { get; init; }
}
