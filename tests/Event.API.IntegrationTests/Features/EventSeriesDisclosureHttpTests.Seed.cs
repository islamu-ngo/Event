using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class EventSeriesDisclosureHttpTests
{
    private static async Task<SeedData> SeedAsync(NativeEventSeriesFactory factory)
    {
        using var client = factory.CreateClient();
        using var scope = Scope(factory);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = new TenantBuilder().WithId(PlatformDefaults.DefaultTenantId).WithSlug("series-disclosure").Build();
        var foreignTenant = new TenantBuilder().WithSlug("foreign-series").Build();
        var owner = new UserBuilder().WithEmail($"series-{Guid.CreateVersion7():N}@example.test").Build();
        var admin = new UserBuilder().WithEmail($"admin-{Guid.CreateVersion7():N}@example.test").Build();
        var suspendedOwner = new UserBuilder().WithEmail($"suspended-{Guid.CreateVersion7():N}@example.test").Build();
        var actor = new ActorBuilder().WithUserId(owner.Id).WithDisplayName("Series owner").Build();
        var suspendedActor = new ActorBuilder().WithUserId(suspendedOwner.Id).WithDisplayName("Suspended owner").Build();
        suspendedActor.IsSuspended = true;
        var publicSeries = Series("Public series", true, VisibilityTypeEnum.Public, tenant, actor);
        var privateSeries = Series("Private series", true, VisibilityTypeEnum.Private, tenant, actor);
        var draftSeries = Series("Draft series", false, VisibilityTypeEnum.Public, tenant, actor);
        var foreignSeries = Series("Foreign series", true, VisibilityTypeEnum.Public, foreignTenant, actor);
        var deletedSeries = Series("Deleted series", true, VisibilityTypeEnum.Public, tenant, actor);
        deletedSeries.IsDeleted = true;
        db.AddRange(tenant, foreignTenant, owner, admin, suspendedOwner, actor, suspendedActor,
            publicSeries, privateSeries, draftSeries, foreignSeries, deletedSeries);
        var ownerMembership = Membership(tenant, owner, actor);
        var adminMembership = Membership(tenant, admin);
        db.TenantUsers.AddRange(ownerMembership, adminMembership, Membership(tenant, suspendedOwner, suspendedActor),
            Membership(foreignTenant, owner, actor));
        db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
            TenantUserId = adminMembership.Id, TenantUser = adminMembership,
            RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant
        });
        Guid publicEventId = default;
        foreach (var (title, status, visibility, deleted) in new[]
        {
            ("Public nested event", EventStatusEnum.Published, VisibilityTypeEnum.Public, false),
            ("Draft nested event", EventStatusEnum.Draft, VisibilityTypeEnum.Public, false),
            ("Private nested event", EventStatusEnum.Published, VisibilityTypeEnum.Private, false),
            ("Deleted nested event", EventStatusEnum.Published, VisibilityTypeEnum.Public, true)
        })
        {
            var item = new EventBuilder().WithTitle(title).WithTenantId(tenant.Id).WithActorId(actor.Id)
                .WithStatus(status).WithVisibility(visibility).Build();
            item.EventSeriesId = publicSeries.Id;
            item.IsDeleted = deleted;
            db.Events.Add(item);
            if (title == "Public nested event")
                publicEventId = item.Id;
        }
        var foreignEvent = new EventBuilder().WithTitle("Foreign nested event").WithTenantId(foreignTenant.Id)
            .WithActorId(actor.Id).WithStatus(EventStatusEnum.Published).Build();
        foreignEvent.EventSeriesId = publicSeries.Id;
        var suspendedEvent = new EventBuilder().WithTitle("Suspended actor nested event").WithTenantId(tenant.Id)
            .WithActorId(suspendedActor.Id).WithStatus(EventStatusEnum.Published).Build();
        suspendedEvent.EventSeriesId = publicSeries.Id;
        db.Events.AddRange(foreignEvent, suspendedEvent);
        await db.SaveChangesAsync();
        return new(publicSeries.Id, privateSeries.Id, draftSeries.Id, foreignSeries.Id, deletedSeries.Id,
            admin.Id, owner.Id, foreignTenant.Id, actor.Id, publicEventId, adminMembership.Id);
    }

    private static TenantUser Membership(Tenant tenant, User user, Actor? actor = null) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, UserId = user.Id, User = user,
        ActorId = actor?.Id, Actor = actor, StatusId = (int)TenantUserStatusEnum.Active
    };

    private static EventSeries Series(string title, bool published, VisibilityTypeEnum visibility, Tenant tenant, Actor actor) => new()
    {
        Id = Guid.CreateVersion7(), Title = title, Description = "Initial description", Slug = title.Replace(' ', '-'),
        TenantId = tenant.Id, Tenant = tenant, ActorId = actor.Id, Actor = actor,
        IsPublished = published, VisibilityTypeId = (int)visibility, VisibilityType = null!
    };

    private static IServiceScope Scope(NativeEventSeriesFactory factory, Guid? userId = null, Guid? tenantId = null)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenantId ?? PlatformDefaults.DefaultTenantId);
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = userId is { } id
                ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], "Test"))
                : new ClaimsPrincipal(new ClaimsIdentity())
        };
        return scope;
    }

    private static HttpClient Client(NativeEventSeriesFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
        return client;
    }

    private static string Detail(Guid id) => $"/api/eventseries/{id}";

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(Detail(id));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object patch, Guid stamp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, Detail(id)) { Content = JsonContent.Create(patch) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp}\"");
        return await client.SendAsync(request);
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        // MVC preserves the controller's JSON media type for its own validation/not-found results.
        var mediaType = status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound
            ? "application/json" : "application/problem+json";
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo(mediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("status").GetInt32()).IsEqualTo((int)status);
    }

    private sealed record SeedData(Guid PublicId, Guid PrivateId, Guid DraftId, Guid ForeignId, Guid DeletedId,
        Guid AdminId, Guid OwnerId, Guid ForeignTenantId, Guid ActorId, Guid PublicEventId, Guid AdminMembershipId);
}
