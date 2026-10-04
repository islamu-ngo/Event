namespace Explore.Application.DTOs.InstanceAdmin;

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Explore.Application.Hateoas;

public sealed record InstanceTenantPlanListItemDto
{
    public Guid Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int LatestVersionNumber { get; init; }
    public int? PublishedVersionNumber { get; init; }
    public decimal PriceAmount { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public string BillingPeriod { get; init; } = string.Empty;
    public bool IsActiveForProvisioning { get; init; }
}

public sealed record InstanceTenantPlanDetailDto
{
    public Guid Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IReadOnlyList<InstanceTenantPlanVersionDto> Versions { get; init; } = [];
}

public sealed record InstanceTenantPlanVersionDto
{
    private IReadOnlyDictionary<string, HalLink>? _links;

    public Guid Id { get; init; }
    public int VersionNumber { get; init; }
    public int StatusId { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public decimal PriceAmount { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public string BillingPeriod { get; init; } = string.Empty;
    public bool IsActiveForProvisioning { get; init; }
    public IReadOnlyList<InstanceTenantPlanSettingDto> Settings { get; init; } = [];
    public IReadOnlyList<InstanceTenantPlanQuotaDto> Quotas { get; init; } = [];

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

public sealed record InstanceTenantPlanSettingDto
{
    public string Key { get; init; } = string.Empty;
    public string JsonValue { get; init; } = string.Empty;
    public bool IsLocked { get; init; }
}

public sealed record InstanceTenantPlanQuotaDto
{
    public string Key { get; init; } = string.Empty;
    public long Limit { get; init; }
}

public sealed record InstanceTenantPlanAssignmentDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid PlanId { get; init; }
    public string PlanKey { get; init; } = string.Empty;
    public Guid PlanVersionId { get; init; }
    public int VersionNumber { get; init; }
    public int StatusId { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public DateTime AssignedAt { get; init; }
    public Guid? AssignedByUserId { get; init; }
}
