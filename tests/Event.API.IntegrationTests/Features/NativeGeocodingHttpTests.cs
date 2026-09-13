using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure.Geocoding;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Geocoding;
using Explore.Application.Exceptions;
using Explore.Application.Features.Geocoding.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Explore.Persistence;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeGeocodingHttpTests
{
    [Test]
    public async Task SuggestionCommand_IssuesOpaqueTokensBoundToTrustedContext()
    {
        var selection = new ProtectedAddressSelection
        {
            DisplayName = "Synthetic venue", Address = "Synthetic street", Postcode = "1000",
            City = "Brussels", Country = "BE", Latitude = 50.85, Longitude = 4.35,
            Attribution = "Synthetic attribution",
            Provenance = new() { Provider = "Photon", ProviderRecordId = "synthetic:record-1" }
        };
        var local = Substitute.For<ILocalAddressSuggestionQuery>();
        local.SearchAsync(Arg.Any<LocalAddressSuggestionCriteria>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LocalAddressSuggestion>());
        var gateway = Substitute.For<IAddressSuggestionProviderGateway>();
        gateway.SearchAsync(Arg.Any<AddressGeocoderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AddressGeocoderResult([selection], AddressProviderOutcome.Ready));
        await using var baseFactory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider(),
            AdditionalConfiguration =
            {
                ["Geocoding:Provider"] = "Photon",
                ["Geocoding:Endpoint"] = "https://photon.example.test",
                ["Geocoding:Language"] = "en",
                ["Geocoding:CountryCodes:0"] = "BE",
                ["Geocoding:DatasetVersion"] = "synthetic-v1"
            }
        };
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILocalAddressSuggestionQuery>();
                services.RemoveAll<IAddressSuggestionProviderGateway>();
                services.RemoveAll<IAddressSelectionProtector>();
                services.AddScoped(_ => local);
                services.AddScoped(_ => gateway);
                services.AddSingleton<IAddressSelectionProtector,
                    Explore.Infrastructure.Geocoding.DataProtectionAddressSelectionProtector>();
            });
        });
        var data = await SeedAsync(factory);
        using (var configuredScope = factory.Services.CreateScope())
        {
            await Assert.That(configuredScope.ServiceProvider.GetRequiredService<IAddressSelectionProtector>().GetType())
                .IsEqualTo(typeof(Explore.Infrastructure.Geocoding.DataProtectionAddressSelectionProtector));
        }
        using var client = Client(factory, data.UserId);
        var organizationId = Guid.CreateVersion7();
        using var response = await client.PostAsJsonAsync("/api/geocoding/address-suggestions",
            new AddressSuggestionsRequestDto
            {
                SearchText = "synthetic venue", Limit = 3, OrganizationId = organizationId,
                LocationId = data.Location.Id, ExpectedConcurrencyStamp = data.Location.ConcurrencyStamp
            });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo((bool?)true);
        await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo((bool?)true);
        var body = await JsonAsync(response);
        var suggestion = body.GetProperty("_embedded").GetProperty("items").EnumerateArray().Single();
        string token = suggestion.GetProperty("selectionToken").GetString()
            ?? throw new InvalidOperationException("Expected a protected selection token.");
        await Assert.That(token).DoesNotContain(selection.Address);
        await Assert.That(token).DoesNotContain(selection.Provenance.ProviderRecordId!);
        foreach (var property in new[] { "latitude", "longitude", "providerRecordId", "provenance" })
        {
            await Assert.That(suggestion.TryGetProperty(property, out _)).IsFalse();
        }
        using var scope = factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IAddressSelectionProtector>();
        var context = new AddressSelectionContext
        {
            TenantId = PlatformDefaults.DefaultTenantId, ActorId = data.UserId, OrganizationId = organizationId,
            Purpose = AddressSelectionPurpose.UpdateLocation,
            Target = new() { LocationId = data.Location.Id, ExpectedConcurrencyStamp = data.Location.ConcurrencyStamp },
            ConfigurationFingerprint = protector.ConfigurationFingerprint
        };
        var valid = await protector.UnprotectAsync(token, context, default);
        await Assert.That(valid.IsSuccess).IsTrue();
        await Assert.That(valid.Selection).IsEqualTo(selection);
        AddressSelectionContext[] invalidContexts =
        [
            context with { TenantId = Guid.CreateVersion7() },
            context with { ActorId = Guid.CreateVersion7() },
            context with { OrganizationId = Guid.CreateVersion7() },
            context with { Purpose = AddressSelectionPurpose.CreateLocation },
            context with { Target = context.Target with { LocationId = Guid.CreateVersion7() } },
            context with { Target = context.Target with { ExpectedConcurrencyStamp = Guid.CreateVersion7() } },
            context with { ConfigurationFingerprint = "different-configuration" }
        ];
        foreach (var invalid in invalidContexts)
        {
            await Assert.That((await protector.UnprotectAsync(token, invalid, default)).IsSuccess).IsFalse();
        }
    }

    [Test]
    public async Task Promotion_UsesNativePortAndPreservesTenantApprovalAndConcurrency()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.UserId);
        using (var missingStamp = await client.PostAsync($"/api/location/{data.Location.Id}/address-approval", null))
        {
            await Assert.That(missingStamp.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        using (var promotion = await PromoteAsync(client, data.Location.Id, data.Location.ConcurrencyStamp))
        {
            await Assert.That(promotion.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await JsonAsync(promotion)).GetProperty("success").GetBoolean()).IsTrue();
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var changed = await db.Locations.AsNoTracking().Include(location => location.Pii)
            .SingleAsync(location => location.Id == data.Location.Id);
        await Assert.That(changed.AddressVisibilityId).IsEqualTo((int)LocationAddressVisibilityEnum.TenantApproved);
        await Assert.That(changed.AddressSourceId).IsEqualTo((int)LocationAddressSourceEnum.Manual);
        await Assert.That(changed.OwnerUserId).IsNull();
        await Assert.That(changed.ConcurrencyStamp).IsNotEqualTo(data.Location.ConcurrencyStamp);
        using (var stale = await PromoteAsync(client, data.Location.Id, data.Location.ConcurrencyStamp))
        {
            await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        }
        using var foreign = await PromoteAsync(client, data.Foreign.Id, data.Foreign.ConcurrencyStamp);
        await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task NativeAuthority_DeniesIssuanceAndPromotionBeforeEffects()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        var data = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var suggestions = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<CreateAddressSuggestionsCommand, AddressSuggestionsResponseDto>>();
        var promotion = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<PromoteLocationAddressCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(async () => await suggestions.ExecuteAsync(new(PlatformDefaults.DefaultTenantId,
            new() { SearchText = "denied", Limit = 3 }), default)).Throws<AuthorizationException>();
        await Assert.That(async () => await promotion.ExecuteAsync(new()
        {
            LocationId = data.Location.Id, ExpectedConcurrencyStamp = data.Location.ConcurrencyStamp
        }, default)).Throws<AuthorizationException>();
        var unchanged = await scope.ServiceProvider.GetRequiredService<ExploreDbContext>().Locations
            .AsNoTracking().SingleAsync(location => location.Id == data.Location.Id);
        await Assert.That(unchanged.AddressVisibilityId).IsEqualTo(data.Location.AddressVisibilityId);
        await Assert.That(unchanged.ConcurrencyStamp).IsEqualTo(data.Location.ConcurrencyStamp);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
        return client;
    }

    private static async Task<SeedData> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId, Slug = "native-geocoding", FullName = "Native geocoding",
            TenantStatusId = status.Id, TenantStatus = status
        };
        var foreignTenant = new Tenant
        {
            Id = Guid.CreateVersion7(), Slug = "foreign-geocoding", FullName = "Foreign geocoding",
            TenantStatusId = status.Id, TenantStatus = status
        };
        var userId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = userId, Pii = new() { Email = "geocoding@example.test", FirstName = "Geocoding", LastName = "User" }
        });
        var location = NewLocation(tenant, "Synthetic location");
        var foreign = NewLocation(foreignTenant, "Foreign location");
        db.Locations.AddRange(location, foreign);
        await db.SaveChangesAsync();
        return new SeedData(userId, location, foreign);
    }

    private static Location NewLocation(Tenant tenant, string name)
    {
        var location = new Location
        {
            Id = Guid.CreateVersion7(), FullName = name, Country = "BE", City = "Brussels",
            TenantId = tenant.Id, Tenant = tenant
        };
        location.SetManualAddress("Synthetic street", "1000");
        return location;
    }

    private static async Task<HttpResponseMessage> PromoteAsync(HttpClient client, Guid id, Guid stamp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/location/{id}/address-approval");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private sealed record SeedData(Guid UserId, Location Location, Location Foreign);
}
