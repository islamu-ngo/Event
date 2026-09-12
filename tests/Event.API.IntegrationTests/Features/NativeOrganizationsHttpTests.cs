using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Organization;
using Explore.Application.Exceptions;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Application.Features.Organizations.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeOrganizationsHttpTests
{
    [Test]
    public async Task Writes_PreserveCreatorOwnershipPartialUpdatesConcurrencyAndVoidApproval()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        var userId = await SeedAsync(factory);
        using var client = Client(factory, userId);
        var input = Input("Native organization");
        using (var invalid = await client.PostAsJsonAsync("/api/organization", input with { FullName = "" }))
        {
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        var id = await CreateAsync(client, input);
        var before = await ReadAsync(factory, id);
        await Assert.That(before.Email).IsEqualTo(input.Email);
        await Assert.That(before.Postcode).IsEqualTo("1000");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var participation = await db.OrganizationTenants.AsNoTracking().SingleAsync(item => item.OrganizationId == id);
            await Assert.That(participation.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
            await Assert.That(participation.ApprovalStatusId).IsEqualTo((int)ApprovalStatusEnum.Pending);
            await Assert.That(participation.IsVisible).IsFalse();
            await Assert.That(participation.IsOrganizerEligible).IsFalse();
            var member = await db.OrganizationMembers.AsNoTracking()
                .SingleAsync(item => item.OrganizationTenantId == participation.Id);
            await Assert.That(member.UserId).IsEqualTo(userId);
            await Assert.That(member.RoleId).IsEqualTo((int)RoleEnum.OrgAdmin);
            var actor = await db.Actors.AsNoTracking().Include(item => item.Pii).SingleAsync(item => item.OrganizationId == id);
            await Assert.That(actor.ActorTypeId).IsEqualTo((int)ActorTypeEnum.Organization);
            await Assert.That(actor.Pii.DisplayName).IsEqualTo(input.FullName);
        }
        using (var missingStamp = await client.PatchAsJsonAsync($"/api/organization/{id}",
            new { fullName = new { value = "Rejected" } }))
        {
            await Assert.That(missingStamp.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var empty = await PatchAsync(client, id, new { }, before.ConcurrencyStamp))
        {
            await Assert.That(empty.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var updated = await PatchAsync(client, id,
            new { fullName = new { value = "Renamed organization" }, city = new { value = "Ghent" } },
            before.ConcurrencyStamp))
        {
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(updated)).GetProperty("success").GetBoolean()).IsTrue();
        }
        var changed = await ReadAsync(factory, id);
        await Assert.That(changed.FullName).IsEqualTo("Renamed organization");
        await Assert.That(changed.City).IsEqualTo("Ghent");
        await Assert.That(changed.Email).IsEqualTo(input.Email);
        await Assert.That(changed.ConcurrencyStamp).IsNotEqualTo(before.ConcurrencyStamp);
        using (var stale = await PatchAsync(client, id,
            new { fullName = new { value = "Stale" } }, before.ConcurrencyStamp))
        {
            await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        using (var cleared = await PatchAsync(client, id,
            new { websiteUrl = new { value = new { hasValue = true, value = (string?)null } } },
            changed.ConcurrencyStamp))
        {
            await Assert.That(cleared.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        await Assert.That((await ReadAsync(factory, id)).WebsiteUrl).IsNull();
        using (var invalidStatus = await client.PutAsJsonAsync($"/api/organization/{id}/approval-status",
            new { approvalStatusId = int.MaxValue }))
        {
            await Assert.That(invalidStatus.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var approved = await client.PutAsJsonAsync($"/api/organization/{id}/approval-status",
            new { approvalStatusId = (int)ApprovalStatusEnum.Approved }))
        {
            await Assert.That(approved.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(await approved.Content.ReadAsStringAsync()).IsEmpty();
        }
        using (var scope = factory.Services.CreateScope())
        {
            await Assert.That((await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
                .OrganizationTenants.AsNoTracking().SingleAsync(item => item.OrganizationId == id)).ApprovalStatusId)
                .IsEqualTo((int)ApprovalStatusEnum.Approved);
        }
        using var outsider = Client(factory, Guid.CreateVersion7());
        using (var denied = await outsider.DeleteAsync($"/api/organization/{id}"))
        {
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
        using (var deleted = await client.DeleteAsync($"/api/organization/{id}"))
        {
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }
        using var verification = factory.Services.CreateScope();
        var finalDb = verification.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var retained = await finalDb.Organizations.IgnoreQueryFilters(["SoftDelete"]).AsNoTracking()
            .Include(item => item.Pii).SingleAsync(item => item.Id == id);
        await Assert.That(retained.IsDeleted).IsTrue();
        await Assert.That(retained.Pii.Email).IsEqualTo(input.Email);
        await Assert.That(await finalDb.Organizations.AnyAsync(item => item.Id == id)).IsFalse();
    }

    [Test]
    public async Task Queries_PreserveActorLookupPaginationMembershipAndImageNormalization()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        var userId = await SeedAsync(factory);
        using var client = Client(factory, userId);
        using var anonymous = factory.CreateClient();
        var alphaId = await CreateAsync(client, Input("Alpha"));
        var betaId = await CreateAsync(client, Input("Beta"));
        var objectId = Guid.CreateVersion7();
        var contentPath = $"/api/storageobject/{objectId}/content";
        var publicPath = $"/api/storageobject/{objectId}/public";
        Guid actorId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var actor = await db.Actors.Include(item => item.Pii).SingleAsync(item => item.OrganizationId == alphaId);
            actorId = actor.Id;
            actor.Pii.ProfilePictureUri = contentPath;
            var beta = await db.Actors.Include(item => item.Pii).SingleAsync(item => item.OrganizationId == betaId);
            beta.Pii.ProfilePictureUri = "private/raw-key";
            await db.SaveChangesAsync();
        }
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                .SetTenant(PlatformDefaults.DefaultTenantId);
            var detail = scope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetOrganizationDetailsRequest, OrganizationDto?>>();
            var list = scope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetOrganizationListRequest, PaginatedResult<OrganizationListDto>>>();
            var mine = scope.ServiceProvider.GetRequiredService<
                IQueryHandler<GetMyOrganizationsRequest, PaginatedResult<OrganizationListDto>>>();
            var byActor = await detail.QueryAsync(new GetOrganizationDetailsRequest(actorId), default)
                ?? throw new InvalidOperationException("Expected the actor's organization.");
            await Assert.That(byActor.Id).IsEqualTo(alphaId);
            await Assert.That(byActor.ActorProfilePictureUri).IsEqualTo(publicPath);
            await Assert.That(await detail.QueryAsync(new GetOrganizationDetailsRequest(Guid.CreateVersion7()), default)).IsNull();
            var page = await list.QueryAsync(new GetOrganizationListRequest { PageNumber = 1, PageSize = 1 }, default);
            await Assert.That(page.TotalCount).IsEqualTo(2);
            await Assert.That(page.Items.Single().Id).IsEqualTo(alphaId);
            var own = await mine.QueryAsync(new GetMyOrganizationsRequest { UserId = userId.ToString() }, default);
            await Assert.That(own.Items.Select(item => item.Id)).IsEquivalentTo(new[] { alphaId, betaId });
            await Assert.That(own.Items.All(item => item.CurrentUserRoleId == (int)RoleEnum.OrgAdmin)).IsTrue();
            await Assert.That(own.Items.Single(item => item.Id == betaId).ActorProfilePictureUri).IsNull();
            await Assert.That((await mine.QueryAsync(new GetMyOrganizationsRequest { UserId = "invalid" }, default)).Items).IsEmpty();
        }
        using var response = await anonymous.GetAsync($"/api/organization/{actorId}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        await Assert.That(body.GetProperty("id").GetGuid()).IsEqualTo(alphaId);
        await Assert.That(body.GetProperty("actorProfilePictureUri").GetString()).IsEqualTo(publicPath);
        await Assert.That(body.TryGetProperty("members", out _)).IsFalse();
        await Assert.That(body.TryGetProperty("userEmail", out _)).IsFalse();
        using var ownResponse = await client.GetAsync("/api/organization/my");
        await Assert.That(ownResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await JsonAsync(ownResponse)).GetProperty("_embedded").GetProperty("items")
            .EnumerateArray().Select(item => item.GetProperty("id").GetGuid()))
            .IsEquivalentTo(new[] { alphaId, betaId });
        using var unauthenticated = await anonymous.GetAsync("/api/organization/my");
        await Assert.That(unauthenticated.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var verifyScope = factory.Services.CreateScope();
        await Assert.That((await verifyScope.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .Actors.AsNoTracking().Include(item => item.Pii).SingleAsync(item => item.Id == actorId))
            .Pii.ProfilePictureUri).IsEqualTo(contentPath);
    }

    [Test]
    public async Task NativeAuthorization_PreservesPreCreateFactsAndProtectsVoidApproval()
    {
        var checks = new ConcurrentQueue<AuthorizationRequest>();
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider
            {
                CheckPredicate = request =>
                {
                    checks.Enqueue(request);
                    return request.Action == AuthorizationActions.Create;
                }
            }
        };
        var userId = await SeedAsync(factory);
        using var client = Client(factory, userId);
        var id = await CreateAsync(client, Input("Protected organization"));
        var before = await ReadAsync(factory, id);
        await Assert.That(checks.Any(request => request.ResourceKind == ResourceKinds.Organization
            && request.ResourceId == CreateOrganizationCommand.PreCreateResourceId
            && request.Facts is PreCreateAuthorizationFacts)).IsTrue();
        using var scope = factory.Services.CreateScope();
        var approval = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateOrganizationApprovalStatusCommand>>();
        var update = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<UpdateOrganizationCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(async () => await approval.ExecuteAsync(new UpdateOrganizationApprovalStatusCommand
        {
            OrganizationId = id, ApprovalStatusDto = new() { ApprovalStatusId = (int)ApprovalStatusEnum.Approved }
        }, default)).Throws<AuthorizationException>();
        await Assert.That(async () => await update.ExecuteAsync(new UpdateOrganizationCommand
        {
            OrganizationId = id, UserId = userId.ToString(), ExpectedConcurrencyStamp = before.ConcurrencyStamp,
            UpdateOrganizationDto = new() { FullName = new() { Value = "Denied" } }
        }, default)).Throws<AuthorizationException>();
        var after = await ReadAsync(factory, id);
        await Assert.That(after.FullName).IsEqualTo(before.FullName);
        await Assert.That(after.ConcurrencyStamp).IsEqualTo(before.ConcurrencyStamp);
        await Assert.That((await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .OrganizationTenants.AsNoTracking().SingleAsync(item => item.OrganizationId == id)).ApprovalStatusId)
            .IsEqualTo((int)ApprovalStatusEnum.Pending);
    }

    private static CreateOrganizationDto Input(string name) => new()
    {
        FullName = name, WebsiteUrl = "https://organization.example.test", Email = "office@example.test",
        Country = "BE", City = "Brussels", Postcode = 1000, Address = "Square 1"
    };

    private static HttpClient Client(AuthenticatedWebApplicationFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Organization user", ("internal_user_id", userId.ToString("D"))));
        client.DefaultRequestHeaders.Add("Prefer", "return=minimal");
        return client;
    }

    private static async Task<Guid> SeedAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        db.Tenants.Add(new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId, Slug = "native-organizations", FullName = "Native organizations",
            TenantStatusId = status.Id, TenantStatus = status
        });
        var userId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = userId, Pii = new() { Email = "organization-user@example.test", FirstName = "Organization", LastName = "User" }
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static async Task<Guid> CreateAsync(HttpClient client, CreateOrganizationDto dto)
    {
        using var response = await client.PostAsJsonAsync("/api/organization", dto);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var body = await JsonAsync(response);
        await Assert.That(body.GetProperty("success").GetBoolean()).IsTrue();
        var id = body.GetProperty("id").GetGuid();
        var location = response.Headers.Location ?? throw new InvalidOperationException("Expected a Location header.");
        await Assert.That(new Uri(new Uri("https://integration.test"), location).AbsolutePath)
            .IsEqualTo($"/api/organization/{id}");
        return id;
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body, Guid stamp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/organization/{id}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        return await client.SendAsync(request);
    }

    private static async Task<Organization> ReadAsync(AuthenticatedWebApplicationFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ExploreDbContext>().Organizations
            .AsNoTracking().Include(item => item.Pii).SingleAsync(item => item.Id == id);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}
