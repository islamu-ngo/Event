using System.Net;
using System.Net.Http.Json;
using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Explore.Blazor.IntegrationTests;

public sealed class TicketPurchaseGovernanceBffTests
{
    [Test]
    public async Task AuthenticatedPurchaseRequiresAntiforgeryBeforeApiCall()
    {
        ITicketPurchaseClient api = Substitute.For<ITicketPurchaseClient>();
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        using HttpRequestMessage request = PurchaseRequest(
            guest: false,
            guestCapability: null);
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(
                Guid.CreateVersion7()));

        using HttpResponseMessage response =
            await client.SendAsync(request);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.BadRequest);
        await api.DidNotReceiveWithAnyArgs()
            .ReserveAuthenticatedPurchaseAuthorityAsync(
                default,
                default,
                default!,
                default,
                default,
                default,
                default);
    }

    [Test]
    public async Task AuthenticatedPurchaseRequiresCookieAuthority()
    {
        ITicketPurchaseClient api = Substitute.For<ITicketPurchaseClient>();
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        BrowserSession session =
            await IssueBrowserSessionAsync(client);
        using HttpRequestMessage request = PurchaseRequest(
            guest: false,
            guestCapability: null);
        AddBrowserSession(request, session);

        using HttpResponseMessage response =
            await client.SendAsync(request);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertPrivateNoStore(response);
    }

    [Test]
    public async Task AuthenticatedPurchaseCreatesServerIdempotencyAndIgnoresTenantSpoof()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        Guid purchaserActorId = Guid.CreateVersion7();
        ITicketPurchaseClient api = SuccessfulApi(orderId);
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        string authentication =
            TestAuthHandler.CreateAuthHeaderValue(
                Guid.CreateVersion7());
        BrowserSession session =
            await IssueBrowserSessionAsync(
                client,
                authentication);
        using HttpRequestMessage request = PurchaseRequest(
            guest: false,
            guestCapability: null,
            eventId,
            orderId,
            purchaserActorId);
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            authentication);
        request.Headers.Add(
            "X-Tenant-Id",
            Guid.CreateVersion7().ToString("D"));
        AddBrowserSession(request, session);

        using HttpResponseMessage response =
            await client.SendAsync(request);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.OK);
        await AssertPrivateNoStore(response);
        await api.Received(1)
            .ReserveAuthenticatedPurchaseAuthorityAsync(
                eventId,
                orderId,
                Arg.Is<string>(value =>
                    IsCanonicalOperationKey(value)),
                null,
                null,
                Arg.Is<ReserveTicketPurchaseRequest>(body =>
                    body.AccessMode == 1
                    && body.RequestedPurchaserActorId ==
                        purchaserActorId),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GuestPurchaseForwardsCapabilityWithoutEchoingIt()
    {
        const string capability =
            "opaque-guest-purchase-capability";
        Guid eventId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        ITicketPurchaseClient api = SuccessfulApi(orderId);
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        BrowserSession session =
            await IssueBrowserSessionAsync(client);
        using HttpRequestMessage request = PurchaseRequest(
            guest: true,
            capability,
            eventId,
            orderId,
            requestedPurchaserActorId:
                Guid.CreateVersion7());
        AddBrowserSession(request, session);

        using HttpResponseMessage response =
            await client.SendAsync(request);
        string payload =
            await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.OK);
        await AssertPrivateNoStore(response);
        await Assert.That(payload).DoesNotContain(capability);
        await api.Received(1)
            .ReserveGuestPurchaseAuthorityAsync(
                eventId,
                orderId,
                Arg.Is<string>(value =>
                    IsCanonicalOperationKey(value)),
                capability,
                null,
                null,
                Arg.Is<ReserveTicketPurchaseRequest>(body =>
                    body.AccessMode == 3
                    && body.RequestedPurchaserActorId == null),
                Arg.Any<CancellationToken>());
    }

    private static HttpRequestMessage PurchaseRequest(
        bool guest,
        string? guestCapability,
        Guid? eventId = null,
        Guid? orderId = null,
        Guid? requestedPurchaserActorId = null)
    {
        Guid resolvedEventId =
            eventId ?? Guid.CreateVersion7();
        Guid resolvedOrderId =
            orderId ?? Guid.CreateVersion7();
        string path =
            $"/bff/events/{resolvedEventId:D}/registration-orders/" +
            (guest
                ? $"guest/{resolvedOrderId:D}/purchase-authority"
                : $"{resolvedOrderId:D}/purchase-authority");
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            path)
        {
            Content = JsonContent.Create(
                new ReserveTicketPurchaseRequest
                {
                    AccessMode = 3,
                    RequestedPurchaserActorId =
                        requestedPurchaserActorId,
                }),
        };
        if (!string.IsNullOrWhiteSpace(guestCapability))
        {
            request.Headers.Add(
                "X-Registration-Order-Capability",
                guestCapability);
        }
        return request;
    }

    private static ITicketPurchaseClient SuccessfulApi(
        Guid orderId)
    {
        ITicketPurchaseClient api =
            Substitute.For<ITicketPurchaseClient>();
        var response =
            new HalResourceOfTicketPurchaseGovernanceResource
            {
                OrderId = orderId,
                AccessMode = 1,
                SupportsHardCrossOrderCeiling = true,
                EnforcementScopeCode = "stable-authority",
            };
        api.ReserveAuthenticatedPurchaseAuthorityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<ReserveTicketPurchaseRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        api.ReserveGuestPurchaseAuthorityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<ReserveTicketPurchaseRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        return api;
    }

    private static async Task<BrowserSession>
        IssueBrowserSessionAsync(
            HttpClient client,
            string? authentication = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/auth/status");
        if (authentication is not null)
        {
            request.Headers.Add(
                TestAuthHandler.AuthHeaderName,
                authentication);
        }

        using HttpResponseMessage response =
            await client.SendAsync(request);
        string[] cookies =
            response.Headers.GetValues("Set-Cookie").ToArray();
        string antiforgery = cookies.First(value =>
            value.StartsWith(
                "XSRF-TOKEN=",
                StringComparison.Ordinal));
        int end = antiforgery.IndexOf(';');
        string token = Uri.UnescapeDataString(
            antiforgery["XSRF-TOKEN=".Length..end]);
        string cookieHeader = string.Join(
            "; ",
            cookies.Select(value =>
                value.Split(';', 2)[0]));
        return new BrowserSession(token, cookieHeader);
    }

    private static void AddBrowserSession(
        HttpRequestMessage request,
        BrowserSession session)
    {
        request.Headers.Add(
            "Cookie",
            session.CookieHeader);
        request.Headers.Add(
            "X-CSRF-TOKEN",
            session.AntiforgeryToken);
    }

    private static async Task AssertPrivateNoStore(
        HttpResponseMessage response)
    {
        await Assert.That(
                response.Headers.CacheControl?.Private)
            .IsTrue();
        await Assert.That(
                response.Headers.CacheControl?.NoStore)
            .IsTrue();
    }

    private static bool IsCanonicalOperationKey(
        string value) =>
        Guid.TryParseExact(value, "N", out _);

    private static WebApplicationFactory<Program>
        CreateFactory(ITicketPurchaseClient api) =>
        new BlazorBffWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITicketPurchaseClient>();
                    services.AddSingleton(api);
                });
            });

    private sealed record BrowserSession(
        string AntiforgeryToken,
        string CookieHeader);
}
