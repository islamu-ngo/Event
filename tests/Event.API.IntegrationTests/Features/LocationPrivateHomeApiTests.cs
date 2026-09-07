using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Location;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class LocationPrivateHomeApiTests
{
    private const string ConsentVersion = "private-home-test-v1";

    [Test]
    public async Task Operations_KeepExactAuthenticatedRouteHeaderAndConsentMetadata()
    {
        (string Method, string Template, string RouteName)[] contracts =
        [
            (nameof(LocationController.ClassifyAsPrivateHome), "{id:guid}/private-home", RouteNames.ClassifyLocationAsPrivateHome),
            (nameof(LocationController.AcceptPrivateHomeOwnership), "{id:guid}/private-home/ownership", RouteNames.AcceptPrivateHomeOwnership)
        ];

        var violations = new List<string>();
        foreach ((string methodName, string template, string routeName) in contracts)
        {
            MethodInfo method = typeof(LocationController).GetMethod(methodName)
                ?? throw new InvalidOperationException($"Missing action {methodName}.");
            HttpPostAttribute? route = method.GetCustomAttribute<HttpPostAttribute>();
            ParameterInfo body = method.GetParameters().Single(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() is not null);
            ParameterInfo header = method.GetParameters().Single(parameter => parameter.GetCustomAttribute<FromHeaderAttribute>() is not null);

            if (method.GetCustomAttribute<AuthorizeAttribute>() is null)
                violations.Add($"{methodName}:authorize");
            if (route?.Template != template || route.Name != routeName)
                violations.Add($"{methodName}:route");
            if (body.ParameterType != typeof(PrivateHomeOwnershipConsentDto))
                violations.Add($"{methodName}:body");
            if (header.GetCustomAttribute<FromHeaderAttribute>()?.Name != "If-Match")
                violations.Add($"{methodName}:header");
        }

        await Assert.That(violations).IsEmpty();
        await Assert.That(typeof(PrivateHomeOwnershipConsentDto).GetProperties().Select(property => property.Name))
            .IsEquivalentTo(["ConsentAcknowledged", "ConsentVersion"]);
    }

    [Test]
    [Arguments("private-home")]
    [Arguments("private-home/ownership")]
    public async Task Write_WithoutAuthentication_ReturnsUnauthorized(string suffix)
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Route(Guid.CreateVersion7(), suffix))
        {
            Content = JsonContent.Create(AffirmativeConsent())
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{Guid.CreateVersion7():D}\"");

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Arguments("private-home", null)]
    [Arguments("private-home", "unquoted")]
    [Arguments("private-home/ownership", null)]
    [Arguments("private-home/ownership", "unquoted")]
    public async Task Write_WithMissingOrInvalidIfMatch_ReturnsBadRequest(string suffix, string? ifMatch)
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        using var request = AuthenticatedRequest(HttpMethod.Post, Route(Guid.CreateVersion7(), suffix), Guid.CreateVersion7());
        request.Content = JsonContent.Create(AffirmativeConsent());
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    [Arguments("private-home")]
    [Arguments("private-home/ownership")]
    public async Task Write_WithInvalidConsent_RejectsBeforeLocationLookup(string suffix)
    {
        Guid locationId = Guid.CreateVersion7();
        Guid stamp = Guid.CreateVersion7();
        ILocationRepository repository = Substitute.For<ILocationRepository>();
        await using var baseFactory = new AuthenticatedWebApplicationFactory();
        await using WebApplicationFactory<Program> factory = WithRepository(baseFactory, repository);
        using HttpClient client = factory.CreateClient();
        using var request = AuthenticatedRequest(HttpMethod.Post, Route(locationId, suffix), Guid.CreateVersion7());
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        request.Content = JsonContent.Create(new PrivateHomeOwnershipConsentDto(false, ConsentVersion));

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await repository.DidNotReceive().GetById(Arg.Any<Guid>());
    }

    [Test]
    [Arguments("private-home", false)]
    [Arguments("private-home/ownership", true)]
    public async Task Write_WithCurrentStampAndAffirmativeVersionedConsent_AppliesRequestedOperation(
        string suffix,
        bool transfer)
    {
        Guid locationId = Guid.CreateVersion7();
        Guid stamp = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        Guid previousOwnerId = Guid.CreateVersion7();
        var location = new Location
        {
            Id = locationId,
            FullName = "Private venue",
            Country = "Belgium",
            City = "Brussels",
            TenantId = Guid.CreateVersion7(),
            ConcurrencyStamp = stamp
        };
        if (transfer)
            location.ClassifyAsPrivateHome(previousOwnerId);

        ILocationRepository repository = Substitute.For<ILocationRepository>();
        repository.GetById(locationId).Returns(location);
        await using var baseFactory = new AuthenticatedWebApplicationFactory();
        await using WebApplicationFactory<Program> factory = WithRepository(baseFactory, repository);
        using HttpClient client = factory.CreateClient();
        using var request = AuthenticatedRequest(HttpMethod.Post, Route(locationId, suffix), actorId);
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        request.Content = JsonContent.Create(AffirmativeConsent());

        using HttpResponseMessage response = await client.SendAsync(request);
        BaseCommandResponse<Guid>? body = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body?.IsSuccess).IsTrue();
        await Assert.That(location.LocationKindId).IsEqualTo((int)LocationKindEnum.PrivateHome);
        await Assert.That(location.OwnerUserId).IsEqualTo(actorId);
        await repository.Received(1).Update(location);
    }

    private static WebApplicationFactory<Program> WithRepository(
        AuthenticatedWebApplicationFactory factory,
        ILocationRepository repository)
    {
        IAuthorizationProvider authorizationProvider = Substitute.For<IAuthorizationProvider>();
        authorizationProvider.AuthorizeAsync(
                Arg.Any<AuthorizationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local));
        factory.AuthorizationProviderOverride = authorizationProvider;

        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILocationRepository>();
            services.AddSingleton(repository);
        }));
    }

    private static HttpRequestMessage AuthenticatedRequest(HttpMethod method, string route, Guid userId)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
        return request;
    }

    private static PrivateHomeOwnershipConsentDto AffirmativeConsent() => new(true, ConsentVersion);

    private static string Route(Guid locationId, string suffix) => $"/api/location/{locationId:D}/{suffix}";
}
