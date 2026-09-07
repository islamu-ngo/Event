// and user, review creation, and proper error handling verification.

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Tests OrganizationReviewService behavior patterns:
/// - Read operations (GetReviewsByOrganizationId, GetReviewsByUserId) return empty collections on error
/// - Write operations (CreateReview) throw on error
/// </summary>
public class OrganizationReviewServiceTests
{
    private readonly IOrganizationReviewClient _apiClient;
    private readonly OrganizationReviewService _service;

    public OrganizationReviewServiceTests()
    {
        _apiClient = Substitute.For<IOrganizationReviewClient>();
        var logger = Substitute.For<ILogger<OrganizationReviewService>>();
        _service = new OrganizationReviewService(_apiClient, logger);
    }

    // ========== Read Operations (return empty on error) ==========

    #region GetReviewsByOrganizationId Tests

    [Test]
    public async Task GetReviewsByOrganizationId_ReturnsReviews_WhenApiSucceeds()
    {
        // Arrange
        var organizationId = Guid.NewGuid();
        var reviews = new List<OrganizationReviewDto>
         {
             new() { Id = Guid.NewGuid(), OrganizationId = organizationId, Rating = 5, Comment = "Excellent!" },
             new() { Id = Guid.NewGuid(), OrganizationId = organizationId, Rating = 4, Comment = "Good" }
         };

        _apiClient.GetOrganizationReviewsByOrganizationAsync(organizationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(reviews);

        // Act
        var result = await _service.GetReviewsByOrganizationId(organizationId);

        // Assert
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetReviewsByOrganizationId_ReturnsEmptyList_WhenApiThrows()
    {
        // Arrange
        var organizationId = Guid.NewGuid();
        _apiClient.GetOrganizationReviewsByOrganizationAsync(organizationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("API Error", 500, null, null, null));

        // Act
        var result = await _service.GetReviewsByOrganizationId(organizationId);

        // Assert
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetReviewsByUserId Tests

    [Test]
    public async Task GetReviewsByUserId_ReturnsReviews_WhenApiSucceeds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var reviews = new List<OrganizationReviewDto>
         {
             new() { Id = Guid.NewGuid(), UserId = userId, Rating = 5, Comment = "Great experience!" },
             new() { Id = Guid.NewGuid(), UserId = userId, Rating = 3, Comment = "Average" }
         };

        _apiClient.GetOrganizationReviewsByUserAsync(userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(reviews);

        // Act
        var result = await _service.GetReviewsByUserId(userId);

        // Assert
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetReviewsByUserId_ReturnsEmptyList_WhenApiThrows()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _apiClient.GetOrganizationReviewsByUserAsync(userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("API Error", 500, null, null, null));

        // Act
        var result = await _service.GetReviewsByUserId(userId);

        // Assert
        await Assert.That(result).IsEmpty();
    }

    #endregion

    // ========== Write Operations (throw on error) ==========

    #region CreateReview Tests

    [Test]
    public async Task CreateReview_ReturnsResponse_WhenApiSucceeds()
    {
        // Arrange
        var dto = new CreateOrganizationReviewDto
        {
            OrganizationId = Guid.NewGuid(),
            Rating = 5,
            Comment = "Excellent organization!"
        };
        var expected = ComponentDataBuilder.SuccessResponse();

        _apiClient.CreateOrganizationReviewAsync(dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(expected);

        // Act
        var result = await _service.CreateReview(dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await _apiClient.Received(1).CreateOrganizationReviewAsync(dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateReview_Throws_WhenApiThrows()
    {
        // Arrange
        var dto = new CreateOrganizationReviewDto
        {
            OrganizationId = Guid.NewGuid(),
            Rating = 5,
            Comment = "Excellent organization!"
        };
        _apiClient.CreateOrganizationReviewAsync(dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Bad Request", 400, null, null, null));

        // Act & Assert
        await Assert.ThrowsAsync<ApiException>(async () => await _service.CreateReview(dto));
    }

    #endregion
}
