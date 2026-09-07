using Explore.Domain.Enums;

namespace Explore.Application.DTOs.CustomPropertyGovernance;

public sealed record CustomPropertyGovernanceRowDto
{
    public Guid TenantId { get; init; }
    public string Namespace { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string EntityScope { get; init; } = string.Empty;
    public string PropertyType { get; init; } = string.Empty;
    public ExposureLevel ExposureLevel { get; init; }
    public bool IsSearchable { get; init; }
    public bool IsFilterable { get; init; }
    public bool IsExportable { get; init; }
    public bool IsModerationRelevant { get; init; }
    public bool IsAnalyticsRelevant { get; init; }
    public bool IsSystemOwned { get; init; }
    public int ActiveInstanceCount { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public PromotionRecommendation Recommendation { get; init; }
}
