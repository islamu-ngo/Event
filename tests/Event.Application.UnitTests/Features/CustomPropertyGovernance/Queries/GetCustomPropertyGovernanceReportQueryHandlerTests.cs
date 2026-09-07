using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyGovernance;
using Explore.Application.Features.CustomPropertyGovernance.Handlers.Queries;
using Explore.Application.Features.CustomPropertyGovernance.Requests.Queries;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Features.CustomPropertyGovernance.Queries;

public class GetCustomPropertyGovernanceReportQueryHandlerTests
{
    private readonly ICustomPropertyGovernanceRepository _governanceRepo;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly GetCustomPropertyGovernanceReportQueryHandler _handler;

    public GetCustomPropertyGovernanceReportQueryHandlerTests()
    {
        _governanceRepo = Substitute.For<ICustomPropertyGovernanceRepository>();
        _quotaResolver = Substitute.For<ICustomPropertyQuotaResolver>();
        _handler = new GetCustomPropertyGovernanceReportQueryHandler(_governanceRepo, _quotaResolver);
    }

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

    // ── Handler Integration Tests ──────────────────────────────────────────

    [Test]
    public async Task Handle_ReturnsPagedResults()
    {
        var tenantId = Guid.NewGuid();
        var rows = new List<GovernanceDefinitionRow>
        {
            CreateRow(isSearchable: true, instanceCount: 10),
            CreateRow(isSearchable: false, isModerationRelevant: true, instanceCount: 5),
        };

        _governanceRepo
            .GetGovernanceRowsAsync(tenantId, Arg.Any<string?>(), 1, 20, Arg.Any<CancellationToken>())
            .Returns((rows, 2));
        _governanceRepo
            .GetTotalEventCountForTenantAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(100);

        var query = new GetCustomPropertyGovernanceReportQuery
        {
            TenantId = tenantId,
            Filter = new GovernanceReportFilterDto()
        };

        var result = await _handler.Handle(query, CancellationToken.None);

        await Assert.That(result.Items.Count).IsEqualTo(2);
        await Assert.That(result.Items[0].Recommendation).IsEqualTo(PromotionRecommendation.ConsiderProjectionFirst);
        await Assert.That(result.Items[1].Recommendation).IsEqualTo(PromotionRecommendation.ConsiderLayer2Promotion);
    }

    [Test]
    public async Task Handle_WithRecommendationFilter_FiltersResults()
    {
        var tenantId = Guid.NewGuid();
        var rows = new List<GovernanceDefinitionRow>
        {
            CreateRow(isSearchable: true, instanceCount: 10),
            CreateRow(isSearchable: false, isModerationRelevant: true, instanceCount: 5),
            CreateRow(isSearchable: false, instanceCount: 0),
        };

        _governanceRepo
            .GetGovernanceRowsAsync(tenantId, Arg.Any<string?>(), 1, 20, Arg.Any<CancellationToken>())
            .Returns((rows, 3));
        _governanceRepo
            .GetTotalEventCountForTenantAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(100);

        var query = new GetCustomPropertyGovernanceReportQuery
        {
            TenantId = tenantId,
            Filter = new GovernanceReportFilterDto { Recommendation = PromotionRecommendation.ConsiderProjectionFirst }
        };

        var result = await _handler.Handle(query, CancellationToken.None);

        await Assert.That(result.Items.Count).IsEqualTo(1);
        await Assert.That(result.Items[0].Recommendation).IsEqualTo(PromotionRecommendation.ConsiderProjectionFirst);
    }

    [Test]
    public async Task Handle_MapsExposureAndGovernanceFlags()
    {
        var tenantId = Guid.NewGuid();
        var rows = new List<GovernanceDefinitionRow>
        {
            CreateRow(
                isSearchable: true,
                isFilterable: true,
                isExportable: true,
                isModerationRelevant: true,
                isAnalyticsRelevant: true,
                exposureLevel: ExposureLevel.TenantAdminOnly,
                instanceCount: 12)
        };

        _governanceRepo
            .GetGovernanceRowsAsync(tenantId, Arg.Any<string?>(), 1, 20, Arg.Any<CancellationToken>())
            .Returns((rows, 1));
        _governanceRepo
            .GetTotalEventCountForTenantAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(100);

        var result = await _handler.Handle(
            new GetCustomPropertyGovernanceReportQuery
            {
                TenantId = tenantId,
                Filter = new GovernanceReportFilterDto()
            },
            CancellationToken.None);

        var item = result.Items.Single();
        await Assert.That(item.ExposureLevel).IsEqualTo(ExposureLevel.TenantAdminOnly);
        await Assert.That(item.IsSearchable).IsTrue();
        await Assert.That(item.IsFilterable).IsTrue();
        await Assert.That(item.IsExportable).IsTrue();
        await Assert.That(item.IsModerationRelevant).IsTrue();
        await Assert.That(item.IsAnalyticsRelevant).IsTrue();
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
