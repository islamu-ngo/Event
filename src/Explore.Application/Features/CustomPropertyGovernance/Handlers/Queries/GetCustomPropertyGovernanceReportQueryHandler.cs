using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.DTOs.CustomPropertyGovernance;
using Explore.Application.Features.CustomPropertyGovernance.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Enums;

namespace Explore.Application.Features.CustomPropertyGovernance.Handlers.Queries;

public class GetCustomPropertyGovernanceReportQueryHandler
    : IQueryHandler<GetCustomPropertyGovernanceReportQuery, PaginatedResult<CustomPropertyGovernanceRowDto>>
{
    private readonly ICustomPropertyGovernanceRepository _governanceRepository;
    private readonly ITenantContext _tenantContext;

    public GetCustomPropertyGovernanceReportQueryHandler(
        ICustomPropertyGovernanceRepository governanceRepository,
        ITenantContext tenantContext)
    {
        _governanceRepository = governanceRepository;
        _tenantContext = tenantContext;
    }

    public async Task<PaginatedResult<CustomPropertyGovernanceRowDto>> QueryAsync(
        GetCustomPropertyGovernanceReportQuery request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId != _tenantContext.TenantId || request.TenantId == Guid.Empty)
            throw new AuthorizationException(ResourceKinds.CustomPropertyGovernance, AuthorizationActions.View);

        var (pageNumber, pageSize) = PaginatedResult<CustomPropertyGovernanceRowDto>
            .NormalizeParameters(request.Filter.PageNumber, request.Filter.PageSize);

        var totalEventCount = await _governanceRepository.GetTotalEventCountForTenantAsync(
            request.TenantId,
            cancellationToken);

        var (rows, totalCount) = await _governanceRepository.GetGovernanceRowsAsync(
            request.TenantId,
            request.Filter.EntityScope,
            pageNumber,
            pageSize,
            request.Filter.Recommendation,
            totalEventCount,
            cancellationToken);

        var dtos = new List<CustomPropertyGovernanceRowDto>(rows.Count);

        foreach (var row in rows)
        {
            var recommendation = ComputeRecommendation(row, totalEventCount);

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
            && ((long)row.ActiveInstanceCount * 100 / totalEventCount) >= adoptionThresholdPct;

        if (row.IsModerationRelevant && hasSearchFilter && isWidelyAdopted)
            return PromotionRecommendation.ConsiderLayer1Promotion;

        if (hasModerationOrAnalytics)
            return PromotionRecommendation.ConsiderLayer2Promotion;

        if (hasSearchFilter)
            return PromotionRecommendation.ConsiderProjectionFirst;

        return PromotionRecommendation.None;
    }
}
