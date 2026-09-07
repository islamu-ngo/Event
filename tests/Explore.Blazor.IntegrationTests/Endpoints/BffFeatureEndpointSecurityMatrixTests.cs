using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using System.Text.RegularExpressions;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffFeatureEndpointSecurityMatrixTests : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory =
        new BlazorBffWebApplicationFactory();
    private readonly HttpClient _client;
    private readonly string _authHeader =
        TestAuthHandler.CreateAuthHeaderValue(
            Guid.CreateVersion7(),
            "Feature Matrix");

    public BffFeatureEndpointSecurityMatrixTests()
    {
        _client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            });
    }

    [Test]
    public async Task AddOnAndFairReturnRoutesHaveOneFailClosedClassification()
    {
        RouteEndpoint[] endpoints = _factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(IsFeatureRoute)
            .ToArray();
        var failures = new List<string>();

        foreach (IGrouping<string, RouteEndpoint> duplicate in endpoints
            .GroupBy(
                endpoint => $"{HttpMethod(endpoint)} {Route(endpoint)}",
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() != 1))
        {
            failures.Add($"{duplicate.Key}: discovered {duplicate.Count()} times");
        }

        foreach (RouteEndpoint endpoint in endpoints.Where(IsUnsafe))
        {
            bool requiresAuthorization =
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0
                && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;

            if (!requiresAuthorization)
            {
                failures.Add(
                    $"{HttpMethod(endpoint)} {Route(endpoint)}: "
                    + $"authorize={requiresAuthorization}");
                continue;
            }

            using var request = new HttpRequestMessage(
                new System.Net.Http.HttpMethod(HttpMethod(endpoint)),
                Materialize(Route(endpoint)));
            request.Headers.Add(TestAuthHandler.AuthHeaderName, _authHeader);
            if (request.Method != System.Net.Http.HttpMethod.Delete)
            {
                request.Content = JsonContent.Create(new { });
            }

            using HttpResponseMessage response =
                await _client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.BadRequest)
            {
                failures.Add(
                    $"{HttpMethod(endpoint)} {Route(endpoint)}: "
                    + $"missing-antiforgery returned {(int)response.StatusCode}");
            }
        }

        await Assert.That(endpoints.Length).IsEqualTo(15);
        await Assert.That(endpoints.Count(IsUnsafe)).IsEqualTo(11);
        await Assert.That(failures).IsEmpty()
            .Because("unsafe same-origin BFF routes must fail closed");
    }

    private static bool IsFeatureRoute(RouteEndpoint endpoint)
    {
        string route = Route(endpoint);
        return route.StartsWith(
                "/bff/events/{eventId:guid}/add-ons",
                StringComparison.OrdinalIgnoreCase)
            || route.Contains(
                "/registration-orders/{registrationOrderId:guid}/add-ons",
                StringComparison.OrdinalIgnoreCase)
            || route.Contains(
                "/registration-orders/{registrationOrderId:guid}/lines/"
                + "{registrationOrderLineId:guid}/waitlist",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsafe(RouteEndpoint endpoint) =>
        !string.Equals(
            HttpMethod(endpoint),
            System.Net.Http.HttpMethod.Get.Method,
            StringComparison.OrdinalIgnoreCase);

    private static string HttpMethod(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
            .Single()
        ?? string.Empty;

    private static string Route(RouteEndpoint endpoint) =>
        endpoint.RoutePattern.RawText ?? string.Empty;

    private static string Materialize(string route) =>
        Regex.Replace(
            route,
            @"\{[^}]+\}",
            _ => Guid.CreateVersion7().ToString());

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
