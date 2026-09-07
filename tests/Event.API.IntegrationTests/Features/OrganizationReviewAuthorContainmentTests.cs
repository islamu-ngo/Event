using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("OrganizationReviewAuthorContainment")]
public sealed class OrganizationReviewAuthorContainmentTests : IAsyncDisposable
{
    private readonly ContractApiFixture _fixture = new();

    public OrganizationReviewAuthorContainmentTests()
    {
        _fixture.InitializeAsync().GetAwaiter().GetResult();
    }

    [Test]
    public async Task Create_WithAuthenticatedUser_PersistsAuthenticatedUserId()
    {
        await using AsyncServiceScope scope = _fixture.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var scenario = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(dbContext);
        var eventScenario = await EventScenarioSeed.SeedPublishedEventAsync(
            dbContext,
            scenario.OrganizationActorId,
            scenario.TenantId,
            "Review Author Containment Baseline Event");

        using var request = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/organizationreview", scenario.UserId);
        request.Content = JsonContent.Create(new
        {
            scenario.OrganizationId,
            ProgramId = eventScenario.EventId,
            ReviewerName = "Real reviewer",
            Rating = 5,
            Comment = "Authenticated user id must win."
        });

        using var response = await _fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        OrganizationReview persisted = await dbContext.OrganizationReviews
            .SingleAsync(review => review.Comment == "Authenticated user id must win.");
        await Assert.That(persisted.UserId).IsEqualTo(scenario.UserId);
        await Assert.That(persisted.CreatedBy).IsEqualTo(scenario.UserId);
        await Assert.That(persisted.UpdatedBy).IsEqualTo(scenario.UserId);
        await Assert.That(persisted.TenantId).IsEqualTo(scenario.TenantId);
    }

    [Test]
    public async Task Create_WhenBodyContainsDifferentUserId_RejectsRemovedBodyUserIdAndPersistsNoReview()
    {
        await using AsyncServiceScope scope = _fixture.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var scenario = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(dbContext);
        var eventScenario = await EventScenarioSeed.SeedPublishedEventAsync(
            dbContext,
            scenario.OrganizationActorId,
            scenario.TenantId,
            "Review Author Containment Spoof Event");

        var hostileUserId = Guid.CreateVersion7();
        using var hostileRequest = _fixture.CreateAuthenticatedRequest(HttpMethod.Post, "/api/organizationreview", scenario.UserId);
        hostileRequest.Content = JsonContent.Create(new
        {
            scenario.OrganizationId,
            ProgramId = eventScenario.EventId,
            UserId = hostileUserId,
            ReviewerName = "Hostile reviewer",
            Rating = 5,
            Comment = "Hostile user id must not win."
        });

        using var hostileResponse = await _fixture.Client.SendAsync(hostileRequest);

        await Assert.That(hostileResponse.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await dbContext.OrganizationReviews.AnyAsync(review => review.Comment == "Hostile user id must not win."))
            .IsFalse();
    }

    [Test]
    public async Task Create_WithoutAuthentication_RemainsDenied()
    {
        using var response = await _fixture.Client.PostAsJsonAsync("/api/organizationreview", new
        {
            OrganizationId = Guid.CreateVersion7(),
            ProgramId = Guid.CreateVersion7(),
            ReviewerName = "Anonymous reviewer",
            Rating = 5,
            Comment = "Anonymous review must be denied."
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }
}
