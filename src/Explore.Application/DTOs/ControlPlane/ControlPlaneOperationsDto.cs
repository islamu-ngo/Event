namespace Explore.Application.DTOs.ControlPlane;

public sealed record ControlPlaneOperationsDto
{
    public DateTime GeneratedAtUtc { get; init; }
    public IReadOnlyList<ControlPlaneOperationStatusDto> Statuses { get; init; } = [];
    public IReadOnlyList<ControlPlaneWarningDto> Warnings { get; init; } = [];
}

public sealed record ControlPlaneOperationStatusDto
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string? Message { get; init; }
    public IReadOnlyList<ControlPlaneOperationMetricDto> Metrics { get; init; } = [];
}

public sealed record ControlPlaneOperationMetricDto
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public long Value { get; init; }
    public bool IsCapped { get; init; }
}
