using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.Exceptions;
using Explore.Application.Features.LocationRooms.Requests.Commands;
using Explore.Application.Features.LocationRooms.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<RealRuntimeApiFixture>(Shared = SharedType.PerClass)]
[NotInParallel("ApiTestFixture")]
public sealed class NativeLocationRoomHttpTests(RealRuntimeApiFixture fixture)
{
    [Test]
    public async Task Writes_PreserveParentTenantConcurrencyAndGroupedNullableUpdates()
    {
        await fixture.ResetDatabaseAsync();
        var factory = fixture.Factory;
        var data = await SeedAsync(factory);
        using var client = Client(factory);
        foreach (var input in new[]
        {
            new CreateLocationRoomDto { LocationId = data.First.Id, Name = "" },
            new CreateLocationRoomDto { LocationId = data.Foreign.Id, Name = "Foreign parent" },
            new CreateLocationRoomDto { LocationId = data.First.Id, Name = "Invalid capacity", Capacity = -1 }
        })
        {
            using var rejected = await client.PostAsJsonAsync("/api/locationroom", input);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using var created = await client.PostAsJsonAsync("/api/locationroom", new CreateLocationRoomDto
        {
            LocationId = data.First.Id, Name = "Main room", Slug = "main", Capacity = 20, SortOrder = 2
        });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var room = await ReadAsync(factory, id);
        await Assert.That(room.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(room.LocationId).IsEqualTo(data.First.Id);
        using (var missingStamp = await client.PatchAsJsonAsync($"/api/locationroom/{id}",
            new { name = new { value = "Rejected" } }))
        {
            await Assert.That(missingStamp.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var foreign = await PatchAsync(client, id,
            new { location = new { locationId = data.Foreign.Id } }, room.ConcurrencyStamp))
        {
            await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var updated = await PatchAsync(client, id,
            new
            {
                name = new { value = "Updated room" },
                slug = new { value = new { hasValue = true, value = (string?)null } },
                capacity = new { value = new { hasValue = true, value = (int?)null } }
            }, room.ConcurrencyStamp))
        {
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(updated)).GetProperty("success").GetBoolean()).IsTrue();
        }
        var changed = await ReadAsync(factory, id);
        await Assert.That(changed.Name).IsEqualTo("Updated room");
        await Assert.That(changed.Slug).IsNull();
        await Assert.That(changed.Capacity).IsNull();
        await Assert.That(changed.SortOrder).IsEqualTo(2);
        await Assert.That(changed.ConcurrencyStamp).IsNotEqualTo(room.ConcurrencyStamp);
        using (var stale = await PatchAsync(client, id,
            new { name = new { value = "Stale" } }, room.ConcurrencyStamp))
        {
            await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        using (var moved = await PatchAsync(client, id,
            new { location = new { locationId = data.Second.Id } }, changed.ConcurrencyStamp))
        {
            await Assert.That(moved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        await Assert.That((await ReadAsync(factory, id)).LocationId).IsEqualTo(data.Second.Id);
        using var deleted = await client.DeleteAsync($"/api/locationroom/{id}");
        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using var scope = factory.Services.CreateScope();
        var retained = await scope.ServiceProvider.GetRequiredService<ExploreDbContext>().LocationRooms
            .IgnoreQueryFilters(["SoftDelete"]).AsNoTracking().SingleAsync(item => item.Id == id);
        await Assert.That(retained.IsDeleted).IsTrue();
    }

    [Test]
    public async Task Reads_PreservePrivateNoStoreOrderingAndTenantBoundaries()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        var data = await SeedAsync(factory);
        var first = NewRoom(data.First, "First", 1);
        var second = NewRoom(data.First, "Second", 2);
        var foreign = NewRoom(data.Foreign, "Foreign", 0);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.LocationRooms.AddRange(first, second, foreign);
            await db.SaveChangesAsync();
        }
        using var client = Client(factory);
        using var anonymous = factory.CreateClient();
        using var denied = await anonymous.GetAsync($"/api/locationroom/{first.Id}");
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var list = await client.GetAsync($"/api/locationroom/by-location/{data.First.Id}");
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(list.Headers.CacheControl?.NoStore).IsEqualTo((bool?)true);
        await Assert.That(list.Headers.CacheControl?.Private).IsEqualTo((bool?)true);
        var items = (await JsonAsync(list)).GetProperty("_embedded").GetProperty("items").EnumerateArray().ToArray();
        await Assert.That(items.Select(item => item.GetProperty("id").GetGuid())
            .SequenceEqual(new[] { first.Id, second.Id })).IsTrue();
        using var detail = await client.GetAsync($"/api/locationroom/{first.Id}");
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(detail.Headers.CacheControl?.NoStore).IsEqualTo((bool?)true);
        var body = await JsonAsync(detail);
        await Assert.That(body.GetProperty("name").GetString()).IsEqualTo("First");
        foreach (var property in new[] { "address", "postcode", "coordinate", "ownerUserId", "location" })
        {
            await Assert.That(body.TryGetProperty(property, out _)).IsFalse();
        }
        using var crossTenant = await client.GetAsync($"/api/locationroom/{foreign.Id}");
        await Assert.That(crossTenant.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var readScope = factory.Services.CreateScope();
        readScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
            .SetTenant(PlatformDefaults.DefaultTenantId);
        var rooms = readScope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetLocationRoomsByLocationRequest, List<LocationRoomListDto>>>();
        var roomDetail = readScope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetLocationRoomDetailRequest, LocationRoomDto?>>();
        await Assert.That(await rooms.QueryAsync(new GetLocationRoomsByLocationRequest
        {
            LocationId = data.Foreign.Id, TenantId = PlatformDefaults.DefaultTenantId
        }, default)).IsEmpty();
        await Assert.That(await roomDetail.QueryAsync(new GetLocationRoomDetailRequest
        {
            Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId
        }, default)).IsNull();
    }

    [Test]
    public async Task NativeAuthorization_DeniesBeforeCreatingOrUpdatingRooms()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        var data = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        var create = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<CreateLocationRoomCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(async () => await create.ExecuteAsync(new CreateLocationRoomCommand
        {
            LocationRoomDto = new() { LocationId = data.First.Id, Name = "Denied room" }
        }, default)).Throws<AuthorizationException>();
        var update = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<UpdateLocationRoomCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(async () => await update.ExecuteAsync(new UpdateLocationRoomCommand
        {
            LocationRoomId = Guid.CreateVersion7(), ExpectedConcurrencyStamp = Guid.CreateVersion7(),
            UpdateLocationRoomDto = new() { Name = new() { Value = "Denied update" } }
        }, default)).Throws<AuthorizationException>();
        await Assert.That(await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .LocationRooms.CountAsync()).IsEqualTo(0);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
        client.DefaultRequestHeaders.Add("Prefer", "return=minimal");
        return client;
    }

    private static async Task<SeedData> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId, Slug = "native-rooms", FullName = "Native rooms",
            TenantStatusId = status.Id, TenantStatus = status
        };
        var foreignTenant = new Tenant
        {
            Id = Guid.CreateVersion7(), Slug = "foreign-rooms", FullName = "Foreign rooms",
            TenantStatusId = status.Id, TenantStatus = status
        };
        var first = NewLocation(tenant, "First venue");
        var second = NewLocation(tenant, "Second venue");
        var foreign = NewLocation(foreignTenant, "Foreign venue");
        db.Locations.AddRange(first, second, foreign);
        await db.SaveChangesAsync();
        return new SeedData(first, second, foreign);
    }

    private static Location NewLocation(Tenant tenant, string name)
    {
        var location = new Location
        {
            Id = Guid.CreateVersion7(), FullName = name, Country = "BE", City = "Brussels",
            TenantId = tenant.Id, Tenant = tenant
        };
        location.SetManualAddress("Private street 1", "1000");
        return location;
    }

    private static LocationRoom NewRoom(Location location, string name, int order) => new()
    {
        Id = Guid.CreateVersion7(), LocationId = location.Id, Location = null!,
        TenantId = location.TenantId, Tenant = null!, Name = name, SortOrder = order
    };

    private static async Task<LocationRoom> ReadAsync(WebApplicationFactory<Program> factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ExploreDbContext>().LocationRooms
            .AsNoTracking().SingleAsync(room => room.Id == id);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body, Guid stamp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/locationroom/{id}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private sealed record SeedData(Location First, Location Second, Location Foreign);
}
