namespace Explore.Application.DTOs.ControlPlane;

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Explore.Application.Hateoas;

public sealed record ControlPlaneTenantEffectiveConfigurationDto
{
    public Guid TenantId { get; init; }
    public ControlPlaneTenantPlanAssignmentDto? PlanAssignment { get; init; }
    public ControlPlaneTenantPlanAssignmentDto? RollbackAssignment { get; init; }
    public IReadOnlyList<ControlPlaneTenantEffectiveSettingDto> Settings { get; init; } = [];
    public IReadOnlyList<ControlPlaneTenantQuotaUsageDto> Quotas { get; init; } = [];
}

public sealed record ControlPlaneTenantEffectiveSettingDto
{
    private IReadOnlyDictionary<string, HalLink>? _links;

    public string Key { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public int SettingValueTypeId { get; init; }
    public string SettingValueTypeCode { get; init; } = string.Empty;
    public string SettingValueTypeName { get; init; } = string.Empty;
    public string ValueSource { get; init; } = string.Empty;
    public bool IsLocked { get; init; }
    public string? LockSource { get; init; }
    public string? Description { get; init; }
    public bool IsSensitive { get; init; }
    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    [JsonPropertyName("_links")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, HalLink>? Links
    {
        get => _links;
        set => _links = value is null
            ? null
            : new ReadOnlyDictionary<string, HalLink>(new Dictionary<string, HalLink>(value, StringComparer.Ordinal));
    }
}

public sealed record ControlPlaneTenantQuotaUsageDto
{
    public string Key { get; init; } = string.Empty;
    public long Limit { get; init; }
    public long Used { get; init; }
    public long Reserved { get; init; }
    public long Quarantined { get; init; }
    public long Available { get; init; }
    public long ObjectCount { get; init; }
    public string? Provider { get; init; }
    public string Source { get; init; } = string.Empty;
    public DateTime? LastRecalculatedAt { get; init; }
}
