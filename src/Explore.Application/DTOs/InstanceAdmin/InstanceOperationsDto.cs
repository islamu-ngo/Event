namespace Explore.Application.DTOs.InstanceAdmin;

public sealed record InstanceOperationsDto
{
    public DateTime GeneratedAtUtc { get; init; }
    public IReadOnlyList<InstanceOperationStatusDto> Statuses { get; init; } = [];
    public IReadOnlyList<InstanceWarningDto> Warnings { get; init; } = [];
}

public sealed record InstanceOperationStatusDto
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string? Message { get; init; }
    public IReadOnlyList<InstanceOperationMetricDto> Metrics { get; init; } = [];
}

public sealed record InstanceOperationMetricDto
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public long Value { get; init; }
    public bool IsCapped { get; init; }
}
