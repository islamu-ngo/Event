using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyGovernance;
using Explore.Application.Features.CustomPropertyGovernance.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using MediatR;

namespace Explore.Application.Features.CustomPropertyGovernance.Handlers.Queries;

public class GetCustomPropertyGovernanceReportQueryHandler
    : IRequestHandler<GetCustomPropertyGovernanceReportQuery, PaginatedResult<CustomPropertyGovernanceRowDto>>
{
    private readonly ICustomPropertyGovernanceRepository _governanceRepository;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;

    public GetCustomPropertyGovernanceReportQueryHandler(
        ICustomPropertyGovernanceRepository governanceRepository,
        ICustomPropertyQuotaResolver quotaResolver)
    {
        _governanceRepository = governanceRepository;
        _quotaResolver = quotaResolver;
    }

    public async Task<PaginatedResult<CustomPropertyGovernanceRowDto>> Handle(
        GetCustomPropertyGovernanceReportQuery request,
        CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<CustomPropertyGovernanceRowDto>
            .NormalizeParameters(request.Filter.PageNumber, request.Filter.PageSize);

        var (rows, totalCount) = await _governanceRepository.GetGovernanceRowsAsync(
            request.TenantId,
            request.Filter.EntityScope,
            pageNumber,
            pageSize,
            cancellationToken);

        var totalEventCount = await _governanceRepository.GetTotalEventCountForTenantAsync(
            request.TenantId,
            cancellationToken);

        var dtos = new List<CustomPropertyGovernanceRowDto>(rows.Count);

        foreach (var row in rows)
        {
            var recommendation = ComputeRecommendation(row, totalEventCount);

            if (request.Filter.Recommendation.HasValue && recommendation != request.Filter.Recommendation.Value)
                continue;

            dtos.Add(new CustomPropertyGovernanceRowDto
            {
                TenantId = row.TenantId,
                Namespace = row.Namespace,
                Key = row.Key,
                DisplayName = row.DisplayName,
                EntityScope = row.EntityScope,
                PropertyType = row.PropertyType.ToString(),
                ExposureLevel = row.ExposureLevel,
                IsSearchable = row.IsSearchable,
                IsFilterable = row.IsFilterable,
                IsExportable = row.IsExportable,
                IsModerationRelevant = row.IsModerationRelevant,
                IsAnalyticsRelevant = row.IsAnalyticsRelevant,
                IsSystemOwned = row.IsSystemOwned,
                ActiveInstanceCount = row.ActiveInstanceCount,
                LastUsedAt = row.LastUsedAt,
                Recommendation = recommendation
            });
        }

        return PaginatedResult<CustomPropertyGovernanceRowDto>.Create(
            dtos, totalCount, pageNumber, pageSize);
    }

    /// <summary>
    /// Computes the promotion recommendation using the Atlassian 4-question matrix:
    /// 1. Is it used for search/filter? → ConsiderProjectionFirst
    /// 2. Is it used for moderation/analytics (automation/AI/cross-tenant reporting)? → ConsiderLayer2Promotion
    /// 3. Is it used for moderation AND search AND widely adopted (>30% of events)? → ConsiderLayer1Promotion
    /// </summary>
    public static PromotionRecommendation ComputeRecommendation(
        GovernanceDefinitionRow row,
        int totalEventCount)
    {
        var hasSearchFilter = row.IsSearchable || row.IsFilterable;
        var hasModerationOrAnalytics = row.IsModerationRelevant || row.IsAnalyticsRelevant;
        var adoptionThresholdPct = 30;
        var isWidelyAdopted = totalEventCount > 0
            && (row.ActiveInstanceCount * 100 / totalEventCount) >= adoptionThresholdPct;

        if (row.IsModerationRelevant && hasSearchFilter && isWidelyAdopted)
            return PromotionRecommendation.ConsiderLayer1Promotion;

        if (hasModerationOrAnalytics)
            return PromotionRecommendation.ConsiderLayer2Promotion;

        if (hasSearchFilter)
            return PromotionRecommendation.ConsiderProjectionFirst;

        return PromotionRecommendation.None;
    }
}
