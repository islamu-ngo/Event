using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.CustomPropertyGovernance.Handlers.Queries;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Features.CustomPropertyGovernance.Queries;

public class GetCustomPropertyGovernanceReportQueryHandlerTests
{
    // ── Promotion Recommendation Matrix Tests ──────────────────────────────

    [Test]
    public async Task ComputeRecommendation_NoneOfThe4Questions_ReturnsNone()
    {
        var row = CreateRow(isSearchable: false, isFilterable: false,
            isModerationRelevant: false, isAnalyticsRelevant: false, instanceCount: 0);

        var result = GetCustomPropertyGovernanceReportQueryHandler.ComputeRecommendation(row, 100);

        await Assert.That(result).IsEqualTo(PromotionRecommendation.None);
    }

    [Test]
    public async Task ComputeRecommendation_IsSearchable_ReturnsConsiderProjectionFirst()
    {
        var row = CreateRow(isSearchable: true, isFilterable: false,
            isModerationRelevant: false, isAnalyticsRelevant: false, instanceCount: 5);

        var result = GetCustomPropertyGovernanceReportQueryHandler.ComputeRecommendation(row, 100);

        await Assert.That(result).IsEqualTo(PromotionRecommendation.ConsiderProjectionFirst);
    }

    [Test]
    public async Task ComputeRecommendation_IsFilterable_ReturnsConsiderProjectionFirst()
    {
        var row = CreateRow(isSearchable: false, isFilterable: true,
            isModerationRelevant: false, isAnalyticsRelevant: false, instanceCount: 5);

        var result = GetCustomPropertyGovernanceReportQueryHandler.ComputeRecommendation(row, 100);

        await Assert.That(result).IsEqualTo(PromotionRecommendation.ConsiderProjectionFirst);
    }

    [Test]
    public async Task ComputeRecommendation_IsModerationRelevant_ReturnsConsiderLayer2()
    {
        var row = CreateRow(isSearchable: false, isFilterable: false,
            isModerationRelevant: true, isAnalyticsRelevant: false, instanceCount: 5);

        var result = GetCustomPropertyGovernanceReportQueryHandler.ComputeRecommendation(row, 100);

        await Assert.That(result).IsEqualTo(PromotionRecommendation.ConsiderLayer2Promotion);
    }

    [Test]
    public async Task ComputeRecommendation_IsAnalyticsRelevant_ReturnsConsiderLayer2()
    {
        var row = CreateRow(isSearchable: false, isFilterable: false,
            isModerationRelevant: false, isAnalyticsRelevant: true, instanceCount: 5);

        var result = GetCustomPropertyGovernanceReportQueryHandler.ComputeRecommendation(row, 100);

        await Assert.That(result).IsEqualTo(PromotionRecommendation.ConsiderLayer2Promotion);
    }

    [Test]
    public async Task ComputeRecommendation_ModerationAndSearchAndWidelyAdopted_ReturnsConsiderLayer1()
    {
        var row = CreateRow(isSearchable: true, isFilterable: false,
            isModerationRelevant: true, isAnalyticsRelevant: false, instanceCount: 40);

        var result = GetCustomPropertyGovernanceReportQueryHandler.ComputeRecommendation(row, 100);

        await Assert.That(result).IsEqualTo(PromotionRecommendation.ConsiderLayer1Promotion);
    }

    [Test]
    public async Task ComputeRecommendation_ModerationAndFilterAndWidelyAdopted_ReturnsConsiderLayer1()
    {
        var row = CreateRow(isSearchable: false, isFilterable: true,
            isModerationRelevant: true, isAnalyticsRelevant: false, instanceCount: 35);

        var result = GetCustomPropertyGovernanceReportQueryHandler.ComputeRecommendation(row, 100);

        await Assert.That(result).IsEqualTo(PromotionRecommendation.ConsiderLayer1Promotion);
    }

    [Test]
    public async Task ComputeRecommendation_ModerationAndSearchButNotWidelyAdopted_ReturnsConsiderLayer2()
    {
        var row = CreateRow(isSearchable: true, isFilterable: false,
            isModerationRelevant: true, isAnalyticsRelevant: false, instanceCount: 20);

        var result = GetCustomPropertyGovernanceReportQueryHandler.ComputeRecommendation(row, 100);

        await Assert.That(result).IsEqualTo(PromotionRecommendation.ConsiderLayer2Promotion);
    }

    [Test]
    public async Task ComputeRecommendation_ExactlyAtThreshold_ReturnsConsiderLayer1()
    {
        var row = CreateRow(isSearchable: true, isFilterable: false,
            isModerationRelevant: true, isAnalyticsRelevant: false, instanceCount: 30);

        var result = GetCustomPropertyGovernanceReportQueryHandler.ComputeRecommendation(row, 100);

        await Assert.That(result).IsEqualTo(PromotionRecommendation.ConsiderLayer1Promotion);
    }

    [Test]
    public async Task ComputeRecommendation_ZeroTotalEvents_NeverReturnsLayer1()
    {
        var row = CreateRow(isSearchable: true, isFilterable: false,
            isModerationRelevant: true, isAnalyticsRelevant: false, instanceCount: 10);

        var result = GetCustomPropertyGovernanceReportQueryHandler.ComputeRecommendation(row, 0);

        await Assert.That(result).IsEqualTo(PromotionRecommendation.ConsiderLayer2Promotion);
    }

    [Test]
    public async Task ComputeRecommendation_ModerationPrecedesProjection()
    {
        var row = CreateRow(isSearchable: true, isFilterable: true,
            isModerationRelevant: true, isAnalyticsRelevant: false, instanceCount: 5);

        var result = GetCustomPropertyGovernanceReportQueryHandler.ComputeRecommendation(row, 100);

        await Assert.That(result).IsEqualTo(PromotionRecommendation.ConsiderLayer2Promotion);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static GovernanceDefinitionRow CreateRow(
        bool isSearchable = false,
        bool isFilterable = false,
        bool isExportable = false,
        bool isModerationRelevant = false,
        bool isAnalyticsRelevant = false,
        ExposureLevel exposureLevel = ExposureLevel.Public,
        int instanceCount = 0)
    {
        return new GovernanceDefinitionRow(
            TenantId: Guid.NewGuid(),
            Namespace: "tenant.custom",
            Key: $"test-{Guid.NewGuid():N}",
            DisplayName: "Test Definition",
            EntityScope: "Event",
            PropertyType: PropertyType.Text,
            ExposureLevel: exposureLevel,
            IsSearchable: isSearchable,
            IsFilterable: isFilterable,
            IsExportable: isExportable,
            IsModerationRelevant: isModerationRelevant,
            IsAnalyticsRelevant: isAnalyticsRelevant,
            IsSystemOwned: false,
            ActiveInstanceCount: instanceCount,
            LastUsedAt: DateTime.UtcNow);
    }
}
