using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Location;
using Explore.Application.Exceptions;
using Explore.Application.Features.Locations.Requests.Commands;
using Explore.Application.Features.Locations.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeLocationHttpTests
{
    [Test]
    public async Task PrivateHomeTransitions_RequireConsentAndBindOwnershipToTheAuthenticatedActor()
    {
        await using var factory = AllowedFactory();
        var data = await SeedAsync(factory);
        using var owner = Client(factory, data.Owner.Id);
        using var incoming = Client(factory, data.Incoming.Id);
        var input = Input("Private home candidate");
        using (var invalidSelection = await owner.PostAsJsonAsync("/api/location",
            input with { AddressSelectionToken = "invalid-protected-selection" }))
        {
            await Assert.That(invalidSelection.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using var created = await owner.PostAsJsonAsync("/api/location", input);
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var original = await ReadAsync(factory, id);
        await Assert.That(original.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(original.LocationKindId).IsEqualTo((int)LocationKindEnum.Unclassified);
        foreach (var consent in new[]
        {
            new PrivateHomeOwnershipConsentDto(false, "v1"),
            new PrivateHomeOwnershipConsentDto(true, "")
        })
        {
            using var rejected = await ConsentAsync(owner, id, "private-home", consent, original.ConcurrencyStamp);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(rejected.Headers.CacheControl?.NoStore).IsEqualTo((bool?)true);
        }
        using (var missingStamp = await owner.PostAsJsonAsync($"/api/location/{id}/private-home",
            new PrivateHomeOwnershipConsentDto(true, "v1")))
        {
            await Assert.That(missingStamp.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var classified = await ConsentAsync(owner, id, "private-home",
            new(true, "v1"), original.ConcurrencyStamp))
        {
            await Assert.That(classified.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        var home = await ReadAsync(factory, id);
        await Assert.That(home.LocationKindId).IsEqualTo((int)LocationKindEnum.PrivateHome);
        await Assert.That(home.OwnerUserId).IsEqualTo((Guid?)data.Owner.Id);
        await Assert.That(home.ConcurrencyStamp).IsNotEqualTo(original.ConcurrencyStamp);
        using (var takeover = await ConsentAsync(incoming, id, "private-home",
            new(true, "v2"), home.ConcurrencyStamp))
        {
            await Assert.That(takeover.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        await Assert.That((await ReadAsync(factory, id)).OwnerUserId).IsEqualTo((Guid?)data.Owner.Id);
        using (var stale = await ConsentAsync(incoming, id, "private-home/ownership",
            new(true, "v2"), original.ConcurrencyStamp))
        {
            await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        using (var transferred = await ConsentAsync(incoming, id, "private-home/ownership",
            new(true, "v2"), home.ConcurrencyStamp))
        {
            await Assert.That(transferred.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        var accepted = await ReadAsync(factory, id);
        await Assert.That(accepted.OwnerUserId).IsEqualTo((Guid?)data.Incoming.Id);
        await Assert.That(accepted.UpdatedBy).IsEqualTo((Guid?)data.Incoming.Id);
        await Assert.That(accepted.ConcurrencyStamp).IsNotEqualTo(home.ConcurrencyStamp);
        using (var updated = await PatchAsync(incoming, id,
            new { address = new { value = "Updated private street" }, postcode = new { value = "2000" } },
            accepted.ConcurrencyStamp))
        {
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        var changed = await ReadAsync(factory, id);
        await Assert.That(changed.Address).IsEqualTo("Updated private street");
        await Assert.That(changed.OwnerUserId).IsEqualTo((Guid?)data.Incoming.Id);
        await Assert.That(changed.LocationKindId).IsEqualTo((int)LocationKindEnum.PrivateHome);
    }

    [Test]
    public async Task TenantBoundariesAndNoStore_PreserveManagementProjectionWithoutForeignMutation()
    {
        await using var factory = AllowedFactory();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.Owner.Id);
        using var anonymous = factory.CreateClient();
        using var unauthorized = await anonymous.GetAsync("/api/location");
        await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var list = await client.GetAsync("/api/location?PageNumber=1&PageSize=20");
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(list.Headers.CacheControl?.Private).IsEqualTo((bool?)true);
        await Assert.That(list.Headers.CacheControl?.NoStore).IsEqualTo((bool?)true);
        var collection = await JsonAsync(list);
        await Assert.That(collection.GetProperty("totalCount").GetInt32()).IsEqualTo(2);
        await Assert.That(collection.GetProperty("_embedded").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())).IsEquivalentTo(new[] { data.Local.Id, data.Erased.Id });
        using var detail = await client.GetAsync($"/api/location/{data.Local.Id}");
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(detail.Headers.CacheControl?.NoStore).IsEqualTo((bool?)true);
        var body = await JsonAsync(detail);
        await Assert.That(body.GetProperty("fullName").GetString()).IsEqualTo(data.Local.FullName);
        foreach (var property in new[] { "ownerUserId", "pii", "addressVisibilityId", "addressSourceId" })
        {
            await Assert.That(body.TryGetProperty(property, out _)).IsFalse();
        }
        using (var foreignDetail = await client.GetAsync($"/api/location/{data.Foreign.Id}"))
        {
            await Assert.That(foreignDetail.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(foreignDetail.Headers.CacheControl?.NoStore).IsEqualTo((bool?)true);
        }
        using (var foreignUpdate = await PatchAsync(client, data.Foreign.Id,
            new { fullName = new { value = "Foreign overwrite" } }, data.Foreign.ConcurrencyStamp))
        {
            await Assert.That(foreignUpdate.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        foreach (var action in new[] { "private-home", "private-home/ownership" })
        {
            using var rejected = await ConsentAsync(client, data.Foreign.Id, action,
                new(true, "v1"), data.Foreign.ConcurrencyStamp);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        using var foreignDelete = await client.DeleteAsync($"/api/location/{data.Foreign.Id}");
        await Assert.That(foreignDelete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using var missing = await client.GetAsync($"/api/location/{Guid.CreateVersion7()}");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var verification = factory.Services.CreateScope();
        var retained = await verification.ServiceProvider.GetRequiredService<ExploreDbContext>().Locations
            .IgnoreQueryFilters(["Tenant"]).AsNoTracking().SingleAsync(item => item.Id == data.Foreign.Id);
        await Assert.That(retained.FullName).IsEqualTo(data.Foreign.FullName);
        await Assert.That(retained.OwnerUserId).IsNull();
        using var localDelete = await client.DeleteAsync($"/api/location/{data.Local.Id}");
        await Assert.That(localDelete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(await verification.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .Locations.AnyAsync(location => location.Id == data.Local.Id)).IsFalse();
    }

    [Test]
    public async Task ErasedLocations_CannotRegainAddressOrOwnershipThroughNativeRoutes()
    {
        await using var factory = AllowedFactory();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.Owner.Id);
        using (var update = await PatchAsync(client, data.Erased.Id,
            new { address = new { value = "Resurrection attempt" }, postcode = new { value = "3000" } },
            data.Erased.ConcurrencyStamp))
        {
            await Assert.That(update.IsSuccessStatusCode).IsFalse();
        }
        foreach (var action in new[] { "private-home", "private-home/ownership" })
        {
            using var rejected = await ConsentAsync(client, data.Erased.Id, action,
                new(true, "v1"), data.Erased.ConcurrencyStamp);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        var retained = await ReadAsync(factory, data.Erased.Id);
        await Assert.That(retained.LocationPrivacyStateId).IsEqualTo((int)LocationPrivacyStateEnum.Erased);
        await Assert.That(retained.Pii).IsNull();
        await Assert.That(retained.OwnerUserId).IsNull();
        await Assert.That(retained.FullName).IsEqualTo(Location.ErasedPrivateVenueLabel);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativePorts_RejectDeniedAndUnavailableAuthorityBeforeAnyOperation(bool unavailable)
    {
        var authority = Substitute.For<IAuthorizationProvider>();
        authority.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationDecision.Deny(AuthorizationProviderMetadata.Runtime,
                unavailable ? AuthorizationDecisionReasonCodes.ProviderUnavailable : AuthorizationDecisionReasonCodes.Denied));
        await using var factory = new AuthenticatedWebApplicationFactory { AuthorizationProviderOverride = authority };
        var data = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        Func<Task>[] operations =
        [
            async () => await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateLocationCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { LocationDto = Input("Denied"), TenantId = PlatformDefaults.DefaultTenantId }, default),
            async () => await scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateLocationCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { LocationId = data.Local.Id, ExpectedConcurrencyStamp = data.Local.ConcurrencyStamp,
                    UpdateLocationDto = new() { FullName = new() { Value = "Denied" } } }, default),
            async () => await scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteLocationCommand, bool>>()
                .ExecuteAsync(new() { Id = data.Local.Id }, default),
            async () => await scope.ServiceProvider.GetRequiredService<ICommandHandler<ClassifyLocationAsPrivateHomeCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { LocationId = data.Local.Id, ExpectedConcurrencyStamp = data.Local.ConcurrencyStamp,
                    ConsentAcknowledged = true, ConsentVersion = "v1" }, default),
            async () => await scope.ServiceProvider.GetRequiredService<ICommandHandler<AcceptPrivateHomeOwnershipCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { LocationId = data.Local.Id, ExpectedConcurrencyStamp = data.Local.ConcurrencyStamp,
                    ConsentAcknowledged = true, ConsentVersion = "v1" }, default),
            async () => await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetLocationDetailsRequest, LocationDto?>>()
                .QueryAsync(new() { Id = data.Local.Id, TenantId = PlatformDefaults.DefaultTenantId }, default),
            async () => await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetLocationListRequest, PaginatedResult<LocationListDto>>>()
                .QueryAsync(new() { TenantId = PlatformDefaults.DefaultTenantId }, default)
        ];
        foreach (var operation in operations)
        {
            if (unavailable)
                await Assert.That(operation).Throws<AuthorizationProviderUnavailableException>();
            else
                await Assert.That(operation).Throws<AuthorizationException>();
        }
        var retained = await ReadAsync(factory, data.Local.Id);
        await Assert.That(retained.FullName).IsEqualTo(data.Local.FullName);
        await Assert.That(retained.OwnerUserId).IsNull();
        await Assert.That(retained.ConcurrencyStamp).IsEqualTo(data.Local.ConcurrencyStamp);
    }

    private static AuthenticatedWebApplicationFactory AllowedFactory() => new()
    {
        AuthorizationProviderOverride = new StubAuthorizationProvider
        {
            CheckPredicate = request => request.Action != AuthorizationActions.Locations.ApproveTenantAddress
        }
    };

    private static HttpClient Client(AuthenticatedWebApplicationFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
        return client;
    }

    private static CreateLocationDto Input(string name) => new()
    {
        FullName = name,
        Country = "BE",
        City = "Brussels",
        Address = "Private street 1",
        Postcode = "1000"
    };

    private static async Task<SeedData> SeedAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId,
            Slug = "native-locations",
            FullName = "Native locations",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        var foreignTenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            Slug = "foreign-locations",
            FullName = "Foreign locations",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        var owner = NewUser("owner");
        var incoming = NewUser("incoming");
        db.Users.AddRange(owner, incoming);
        var local = NewLocation(tenant, "Managed location");
        var foreign = NewLocation(foreignTenant, "Foreign location");
        var erased = NewLocation(tenant, "Erased location");
        erased.ClassifyAsPrivateHome(owner.Id);
        erased.EraseOwnedPii(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LocationPrivacyErasureReasonEnum.OwnerErasureRequest);
        db.Locations.AddRange(local, foreign, erased);
        db.Set<TenantSetting>().Add(new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            SettingKey = GovernanceSettingKeys.AddressGovernance.CreationMode,
            Value = "\"OpenWithModeration\""
        });
        await db.SaveChangesAsync();
        return new SeedData(owner, incoming, local, foreign, erased);
    }

    private static User NewUser(string name) => new()
    {
        Id = Guid.CreateVersion7(),
        Pii = new() { Email = $"{name}@example.test", FirstName = name, LastName = "User" }
    };

    private static Location NewLocation(Tenant tenant, string name)
    {
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = name,
            Country = "BE",
            City = "Brussels",
            TenantId = tenant.Id,
            Tenant = tenant
        };
        location.SetManualAddress("Private street 1", "1000");
        return location;
    }

    private static async Task<Location> ReadAsync(AuthenticatedWebApplicationFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ExploreDbContext>().Locations
            .AsNoTracking().Include(location => location.Pii).SingleAsync(location => location.Id == id);
    }

    private static async Task<HttpResponseMessage> ConsentAsync(
        HttpClient client, Guid id, string action, PrivateHomeOwnershipConsentDto consent, Guid stamp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/location/{id}/{action}")
        {
            Content = JsonContent.Create(consent)
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body, Guid stamp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/location/{id}")
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

    private sealed record SeedData(User Owner, User Incoming, Location Local, Location Foreign, Location Erased);
}
