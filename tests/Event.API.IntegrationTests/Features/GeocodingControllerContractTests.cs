using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Collections.Concurrent;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.Application.DTOs.Location;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TUnit.Assertions.Enums;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class GeocodingControllerContractTests
{
    private const string ControllerTypeName = "Explore.API.Controllers.GeocodingController";
    private const string RequestTypeName =
        "Explore.Application.DTOs.Geocoding.AddressSuggestionsRequestDto";
    private const string ActionName = "GetAddressSuggestions";
    private const string RouteName = "GetAddressSuggestions";
    private const string RateLimitPolicyName = "AddressSuggestions";

    [Test]
    public async Task AddressSuggestions_UsesExactPrivateAuthenticatedPostContract()
    {
        Type controller = RequiredApiType(ControllerTypeName);
        MethodInfo action = controller.GetMethod(
            ActionName,
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Missing {ControllerTypeName}.{ActionName}.");
        HttpPostAttribute route = action.GetCustomAttribute<HttpPostAttribute>()
            ?? throw new InvalidOperationException($"{ActionName} must be an HTTP POST action.");
        int[] problemStatuses = action
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Where(attribute => attribute.Type == typeof(ProblemDetails)
                || attribute.Type == typeof(ValidationProblemDetails))
            .Select(attribute => attribute.StatusCode)
            .Order()
            .ToArray();

        await Assert.That(controller.GetCustomAttribute<RouteAttribute>()?.Template)
            .IsEqualTo("api/geocoding");
        await Assert.That(route.Template).IsEqualTo("address-suggestions");
        await Assert.That(route.Name).IsEqualTo(RouteName);
        await Assert.That(GetEffectiveAttribute<AuthorizeAttribute>(controller, action)).IsNotNull();
        await Assert.That(GetEffectiveAttribute<AllowAnonymousAttribute>(controller, action)).IsNull();
        await Assert.That(GetEffectiveAttribute<EndpointClassificationAttribute>(controller, action)?.Class)
            .IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(action.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitPolicyName);
        await Assert.That(action.GetCustomAttribute<ConsumesAttribute>()?.ContentTypes)
            .Contains("application/json");
        await Assert.That(problemStatuses)
            .IsEquivalentTo(
                [
                    StatusCodes.Status400BadRequest,
                    StatusCodes.Status401Unauthorized,
                    StatusCodes.Status403Forbidden,
                    StatusCodes.Status429TooManyRequests
                ],
                CollectionOrdering.Matching);

        ParameterInfo body = action.GetParameters()
            .Single(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() is not null);
        await Assert.That(body.ParameterType.FullName).IsEqualTo(RequestTypeName);
        await Assert.That(action.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(CancellationToken))).IsTrue();
    }

    [Test]
    public async Task AddressSuggestions_RequestBodyContainsTargetBoundIntentButNoAuthorityOrCoordinates()
    {
        Type request = RequiredApplicationType(RequestTypeName);
        string[] properties = request
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(properties)
            .IsEquivalentTo(
                [
                    "ExpectedConcurrencyStamp",
                    "Limit",
                    "LocationId",
                    "OrganizationId",
                    "SearchText"
                ],
                CollectionOrdering.Matching);
        await Assert.That(properties).DoesNotContain("TenantId");
        await Assert.That(properties).DoesNotContain("ActorId");
        await Assert.That(properties).DoesNotContain("UserId");
        await Assert.That(properties).DoesNotContain("Provider");
        await Assert.That(properties).DoesNotContain("Latitude");
        await Assert.That(properties).DoesNotContain("Longitude");
    }

    [Test]
    public async Task AddressSuggestions_WithoutAuthentication_ReturnsUnauthorized()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/geocoding/address-suggestions",
            new
            {
                searchText = "ab",
                limit = 5,
                organizationId = (Guid?)null
            });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task AddressSuggestions_InvalidBody_ReturnsPrivateProblemWithoutEchoingQuery()
    {
        const string QuerySentinel = "exact-query-text-must-not-leak";
        var logs = new CapturingLoggerProvider();
        await using var rootFactory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        await using WebApplicationFactory<Program> factory =
            rootFactory.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                    services.AddSingleton<ILoggerProvider>(logs)));
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/geocoding/address-suggestions");
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
        request.Content = new StringContent(
            $$"""
            {
              "searchText": "{{QuerySentinel}}{{new string('x', 190)}}",
              "limit": 5,
              "organizationId": null
            }
            """,
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(body).DoesNotContain(QuerySentinel);
        await Assert.That(logs.Contains(QuerySentinel)).IsFalse();
    }

    [Test]
    public async Task AddressApproval_UsesExactAuthenticatedConcurrencyContract()
    {
        MethodInfo action = typeof(LocationController).GetMethod(
            nameof(LocationController.ApproveTenantAddress))
            ?? throw new InvalidOperationException("Missing address approval action.");
        HttpPostAttribute route = action.GetCustomAttribute<HttpPostAttribute>()
            ?? throw new InvalidOperationException("Address approval must be an HTTP POST.");
        ParameterInfo ifMatch = action.GetParameters()
            .Single(parameter =>
                parameter.GetCustomAttribute<FromHeaderAttribute>()?.Name == "If-Match");

        await Assert.That(route.Template).IsEqualTo("{id:guid}/address-approval");
        await Assert.That(route.Name).IsEqualTo("ApproveTenantAddress");
        await Assert.That(action.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(GetEffectiveAttribute<AuthorizeAttribute>(
            typeof(LocationController),
            action)).IsNotNull();
        await Assert.That(ifMatch.ParameterType).IsEqualTo(typeof(string));
        await Assert.That(action.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(CancellationToken))).IsTrue();
    }

    private static Type RequiredApiType(string fullName) =>
        typeof(LocationController).Assembly.GetType(fullName, throwOnError: false)
        ?? throw new InvalidOperationException($"Missing API contract type {fullName}.");

    private static Type RequiredApplicationType(string fullName) =>
        typeof(CreateLocationDto).Assembly.GetType(fullName, throwOnError: false)
        ?? throw new InvalidOperationException($"Missing Application contract type {fullName}.");

    private static TAttribute? GetEffectiveAttribute<TAttribute>(
        Type controller,
        MethodInfo action)
        where TAttribute : Attribute =>
        action.GetCustomAttribute<TAttribute>(inherit: true)
        ?? controller.GetCustomAttribute<TAttribute>(inherit: true);

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public ILogger CreateLogger(string categoryName) => this;

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(formatter(state, exception));
            if (exception is not null)
            {
                _entries.Enqueue(exception.ToString());
            }
        }

        public bool Contains(string value) =>
            _entries.Any(entry => entry.Contains(value, StringComparison.Ordinal));

        public void Dispose()
        {
        }
    }
}
