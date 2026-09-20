using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.Exceptions;
using Explore.Application.Features.OrganizationReviews.Commands.CreateOrganizationReview;
using Explore.Application.Features.OrganizationReviews.Queries.GetMyReviews;
using Explore.Application.Features.OrganizationReviews.Queries.GetOrganizationReviews;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeOrganizationReviewHttpTests
{
    [Test]
    public async Task Creation_BindsTrustedUserTenantAndProgramWithoutDisclosingSubmittedIdentity()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        var data = await SeedAsync(factory);
        using var anonymous = factory.CreateClient();
        var input = new CreateOrganizationReviewDto
        {
            OrganizationId = data.Organization.Id,
            ProgramId = data.Program.Id,
            ReviewerName = "Submitted name",
            Rating = 4,
            Comment = "Useful program"
        };
        using var unauthenticated = await anonymous.PostAsJsonAsync("/api/organizationreview", input);
        await Assert.That(unauthenticated.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(data.User.Id));
        using var created = await client.PostAsJsonAsync("/api/organizationreview", input);
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await JsonAsync(created);
        await Assert.That(body.GetProperty("success").GetBoolean()).IsTrue();
        var id = body.GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
                .OrganizationReviews.AsNoTracking().SingleAsync(review => review.Id == id);
            await Assert.That(stored.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
            await Assert.That(stored.UserId).IsEqualTo((Guid?)data.User.Id);
            await Assert.That(stored.CreatedBy).IsEqualTo((Guid?)data.User.Id);
            await Assert.That(stored.UpdatedBy).IsEqualTo((Guid?)data.User.Id);
            await Assert.That(stored.EventId).IsEqualTo(data.Program.Id);
            await Assert.That(stored.ReviewerName).IsEqualTo(input.ReviewerName);
            await Assert.That(stored.Rating).IsEqualTo(input.Rating);
            await Assert.That(stored.IsDeleted).IsFalse();
        }
        foreach (var path in new[]
        {
            $"/api/organizationreview/{data.Organization.Id}",
            $"/api/organizationreview/user/{data.User.Id}"
        })
        {
            using var response = await anonymous.GetAsync(path);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var review = (await JsonAsync(response)).EnumerateArray().Single();
            await Assert.That(review.GetProperty("id").GetGuid()).IsEqualTo(id);
            await Assert.That(review.GetProperty("userFullName").GetString()).IsEqualTo("Trusted Reviewer");
            await Assert.That(review.GetProperty("organizationFullName").GetString()).IsEqualTo("Review organization");
            await Assert.That(review.GetProperty("comment").GetString()).IsEqualTo(input.Comment);
            await Assert.That(review.TryGetProperty("reviewerName", out _)).IsFalse();
            await Assert.That(review.TryGetProperty("tenantId", out _)).IsFalse();
            await Assert.That(review.TryGetProperty("userEmail", out _)).IsFalse();
        }
    }

    [Test]
    public async Task NativeReads_PreserveNewestFirstOrderingAndTenantSoftDeleteFilters()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        var data = await SeedAsync(factory);
        var older = Guid.CreateVersion7();
        var newer = Guid.CreateVersion7();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.OrganizationReviews.AddRange(
                Review(older, data, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                Review(newer, data, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
                new OrganizationReview
                {
                    Id = Guid.CreateVersion7(),
                    OrganizationId = data.Organization.Id,
                    Organization = null!,
                    EventId = data.Program.Id,
                    Event = null!,
                    UserId = data.User.Id,
                    TenantId = data.ForeignTenantId,
                    Tenant = null!,
                    ReviewerName = "Foreign",
                    Rating = 1,
                    CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)
                });
            var deleted = Review(Guid.CreateVersion7(), data, new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc));
            deleted.IsDeleted = true;
            db.OrganizationReviews.Add(deleted);
            await db.SaveChangesAsync();
        }
        using var readScope = factory.Services.CreateScope();
        readScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
            .SetTenant(PlatformDefaults.DefaultTenantId);
        var byOrganization = readScope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetOrganizationReviewsQuery, List<OrganizationReviewDto>>>();
        var byUser = readScope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetMyReviewsQuery, List<OrganizationReviewDto>>>();
        var organizationReviews = await byOrganization.QueryAsync(
            new GetOrganizationReviewsQuery(data.Organization.Id), default);
        var userReviews = await byUser.QueryAsync(new GetMyReviewsQuery(data.User.Id), default);
        await Assert.That(organizationReviews.Select(review => review.Id).SequenceEqual(new[] { newer, older })).IsTrue();
        await Assert.That(userReviews.Select(review => review.Id).SequenceEqual(new[] { newer, older })).IsTrue();
        await Assert.That(await byOrganization.QueryAsync(new GetOrganizationReviewsQuery(Guid.CreateVersion7()), default))
            .IsEmpty();
        await Assert.That(await byUser.QueryAsync(new GetMyReviewsQuery(Guid.CreateVersion7()), default)).IsEmpty();
        var dbRead = readScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(dbRead.ChangeTracker.Entries().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
    }

    [Test]
    public async Task NativeAuthorization_DeniesBeforeCreatingAReview()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        var data = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
            .SetTenant(PlatformDefaults.DefaultTenantId);
        var create = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<CreateOrganizationReviewCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(async () => await create.ExecuteAsync(new CreateOrganizationReviewCommand
        {
            ReviewerUserId = data.User.Id,
            CreateOrganizationReviewDto = new()
            {
                OrganizationId = data.Organization.Id,
                ProgramId = data.Program.Id,
                ReviewerName = "Denied",
                Rating = 4,
                Comment = "Denied"
            }
        }, default)).Throws<AuthorizationException>();
        await Assert.That(await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .OrganizationReviews.CountAsync()).IsEqualTo(0);
    }

    private static OrganizationReview Review(Guid id, SeedData data, DateTime createdAt) => new()
    {
        Id = id,
        OrganizationId = data.Organization.Id,
        Organization = null!,
        EventId = data.Program.Id,
        Event = null!,
        UserId = data.User.Id,
        TenantId = PlatformDefaults.DefaultTenantId,
        Tenant = null!,
        ReviewerName = "Not a public identity",
        Rating = 4,
        Comment = "Public comment",
        CreatedAt = createdAt
    };

    private static async Task<SeedData> SeedAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId,
            Slug = "native-reviews",
            FullName = "Native reviews",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        var foreignTenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            Slug = "foreign-reviews",
            FullName = "Foreign reviews",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        db.Tenants.AddRange(tenant, foreignTenant);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new() { Email = "reviewer@example.test", FirstName = "Trusted", LastName = "Reviewer" }
        };
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new() { FullName = "Review organization" }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Organization,
            ActorType = null!,
            OrganizationId = organization.Id,
            Organization = organization,
            Pii = new() { DisplayName = "Review actor" }
        };
        var program = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = "Review program",
            ActorId = actor.Id,
            Actor = actor,
            TenantId = tenant.Id,
            Tenant = tenant,
            EventStatus = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventFormatId = 1,
            EventFormat = null!
        };
        db.Users.Add(user);
        db.Events.Add(program);
        await db.SaveChangesAsync();
        return new SeedData(organization, program, user, foreignTenant.Id);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private sealed record SeedData(Organization Organization, Explore.Domain.Event Program, User User, Guid ForeignTenantId);
}
