using System.Net;
using System.Reflection;
using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Explore.Blazor.IntegrationTests;

public sealed class TicketTransferBffTests
{
    [Test]
    public async Task TransferEndpointFamilyIsMappedExplicitly()
    {
        Type? endpoints = typeof(Program).Assembly.GetType(
            "Explore.Blazor.Extensions." +
            "BffTicketTransferEndpoints");
        await Assert.That(endpoints).IsNotNull();
        await Assert.That(endpoints!.GetMethod(
                "MapTicketTransferEndpoints",
                BindingFlags.Public
                | BindingFlags.Static))
            .IsNotNull();
    }

    [Test]
    public async Task CapabilityReadUsesGeneratedHeaderWithoutUrlEcho()
    {
        TransferScope scope = TransferScope.Create();
        string capability =
            Guid.CreateVersion7().ToString("N");
        ITicketTransferClient api =
            Substitute.For<ITicketTransferClient>();
        api.GetTicketTransferAsync(
                scope.EventId,
                scope.TicketId,
                scope.TransferId,
                capability,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfTicketTransferDto
            {
                Id = scope.TransferId,
                AdmissionTicketId = scope.TicketId,
                StatusCode = "OFFERED",
                SupportCode =
                    "recipient_action_required",
                TransferHop = 1,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CredentialGeneration = 1,
                _links = new Dictionary<string, HalLink>(),
            });
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = CreateClient(factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            scope.Path);
        request.Headers.TryAddWithoutValidation(
            "X-Ticket-Transfer-Capability",
            capability);
        request.Headers.TryAddWithoutValidation(
            "X-Tenant-Id",
            Guid.CreateVersion7().ToString("D"));

        using HttpResponseMessage response =
            await client.SendAsync(request);
        string body =
            await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.OK);
        await api.Received(1).GetTicketTransferAsync(
            scope.EventId,
            scope.TicketId,
            scope.TransferId,
            capability,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
        await Assert.That(
                request.RequestUri!.Query)
            .DoesNotContain(capability);
        await Assert.That(body)
            .DoesNotContain(capability);
        await AssertPrivateAsync(response);
    }

    [Test]
    public async Task UnauthenticatedTransferWritesRequireCookieAuthority()
    {
        TransferScope scope = TransferScope.Create();
        ITicketTransferClient api =
            Substitute.For<ITicketTransferClient>();
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = CreateClient(factory);

        foreach ((HttpMethod method, string path) in
                 scope.Writes)
        {
            using var request = new HttpRequestMessage(
                method,
                path)
            {
                Content = JsonContent.Create(new
                {
                    recipientParticipantId =
                        Guid.CreateVersion7(),
                }),
            };
            using HttpResponseMessage response =
                await client.SendAsync(request);
            await Assert.That(response.StatusCode)
                .IsEqualTo(HttpStatusCode.Unauthorized);
            await AssertPrivateAsync(response);
        }
        _ = api.DidNotReceiveWithAnyArgs()
            .OfferTicketTransferAsync(
                default,
                default,
                default,
                default,
                default);
    }

    [Test]
    public async Task AuthenticatedWriteRequiresAntiforgeryBeforeApiCall()
    {
        TransferScope scope = TransferScope.Create();
        ITicketTransferClient api =
            Substitute.For<ITicketTransferClient>();
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = CreateClient(factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            scope.RootPath);
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(
                Guid.CreateVersion7()));

        using HttpResponseMessage response =
            await client.SendAsync(request);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.BadRequest);
        _ = api.DidNotReceiveWithAnyArgs()
            .OfferTicketTransferAsync(
                default,
                default,
                default,
                default,
                default);
        await AssertPrivateAsync(response);
    }

    [Test]
    public async Task AuthenticatedActionsUseGeneratedClientAndHeaderOnlyCapability()
    {
        TransferScope scope = TransferScope.Create();
        Guid recipientParticipantId =
            Guid.CreateVersion7();
        string capability =
            Guid.CreateVersion7().ToString("N");
        HalResourceOfTicketTransferDto transfer =
            TransferResource(scope);
        ITicketTransferClient api =
            Substitute.For<ITicketTransferClient>();
        api.OfferTicketTransferAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new TicketTransferOfferResponse
            {
                Transfer = transfer,
                ClaimCapability =
                    Guid.CreateVersion7().ToString("N"),
            });
        api.AcceptTicketTransferAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<AcceptTicketTransferRequest>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(CredentialResponse(transfer));
        api.CancelTicketTransferAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(transfer);
        api.CorrectTicketTransferAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(CredentialResponse(transfer));
        api.ReissueTransferredTicketAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(CredentialResponse(transfer));
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = CreateClient(factory);
        string authentication =
            TestAuthHandler.CreateAuthHeaderValue(
                Guid.CreateVersion7());
        BrowserSession session =
            await IssueBrowserSessionAsync(
                client,
                authentication);

        foreach ((HttpMethod method, string path) in
                 scope.Writes)
        {
            using var request = new HttpRequestMessage(
                method,
                path);
            request.Headers.Add(
                TestAuthHandler.AuthHeaderName,
                authentication);
            AddBrowserSession(request, session);
            if (path.EndsWith(
                    "/accept",
                    StringComparison.Ordinal))
            {
                request.Headers.Add(
                    "X-Ticket-Transfer-Capability",
                    capability);
                request.Content = JsonContent.Create(
                    new AcceptTicketTransferRequest
                    {
                        RecipientParticipantId =
                            recipientParticipantId,
                    });
            }

            using HttpResponseMessage response =
                await client.SendAsync(request);

            await Assert.That(response.StatusCode)
                .IsEqualTo(HttpStatusCode.OK);
            await Assert.That(
                    request.RequestUri!.Query)
                .DoesNotContain(capability);
            await AssertPrivateAsync(response);
        }

        await api.Received(1)
            .OfferTicketTransferAsync(
                scope.EventId,
                scope.TicketId,
                null,
                null,
                Arg.Any<CancellationToken>());
        await api.Received(1)
            .AcceptTicketTransferAsync(
                scope.EventId,
                scope.TicketId,
                scope.TransferId,
                Arg.Is<AcceptTicketTransferRequest>(
                    value =>
                        value.RecipientParticipantId ==
                        recipientParticipantId),
                capability,
                null,
                null,
                Arg.Any<CancellationToken>());
        await api.Received(1)
            .CancelTicketTransferAsync(
                scope.EventId,
                scope.TicketId,
                scope.TransferId,
                null,
                null,
                Arg.Any<CancellationToken>());
        await api.Received(1)
            .CorrectTicketTransferAsync(
                scope.EventId,
                scope.TicketId,
                scope.TransferId,
                null,
                null,
                Arg.Any<CancellationToken>());
        await api.Received(1)
            .ReissueTransferredTicketAsync(
                scope.EventId,
                scope.TicketId,
                scope.TransferId,
                null,
                null,
                Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments(404, HttpStatusCode.NotFound)]
    [Arguments(429, HttpStatusCode.TooManyRequests)]
    [Arguments(500, HttpStatusCode.BadGateway)]
    public async Task DownstreamFailuresMapWithoutEcho(
        int downstreamStatus,
        HttpStatusCode expected)
    {
        TransferScope scope = TransferScope.Create();
        string capability =
            Guid.CreateVersion7().ToString("N");
        ITicketTransferClient api =
            Substitute.For<ITicketTransferClient>();
        api.GetTicketTransferAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfTicketTransferDto>>(
                _ => throw new ApiException(
                    "downstream failure",
                    downstreamStatus,
                    capability,
                    new Dictionary<
                        string,
                        IEnumerable<string>>(),
                    null));
        await using WebApplicationFactory<Program> factory =
            CreateFactory(api);
        using HttpClient client = CreateClient(factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            scope.Path);
        request.Headers.Add(
            "X-Ticket-Transfer-Capability",
            capability);

        using HttpResponseMessage response =
            await client.SendAsync(request);
        string body =
            await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(expected);
        await Assert.That(body).DoesNotContain(capability);
        await AssertPrivateAsync(response);
    }

    private static WebApplicationFactory<Program>
        CreateFactory(ITicketTransferClient api) =>
        new BlazorBffWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITicketTransferClient>();
                    services.AddSingleton(api);
                });
            });

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory) =>
        factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
                BaseAddress =
                    new Uri("https://localhost"),
            });

    private static async Task AssertPrivateAsync(
        HttpResponseMessage response)
    {
        await Assert.That(
                response.Headers.CacheControl?.Private)
            .IsTrue();
        await Assert.That(
                response.Headers.CacheControl?.NoStore)
            .IsTrue();
        await Assert.That(response.Headers.TryGetValues(
                "Referrer-Policy",
                out IEnumerable<string>? values))
            .IsTrue();
        await Assert.That(values)
            .Contains("no-referrer");
    }

    private static HalResourceOfTicketTransferDto
        TransferResource(TransferScope scope) =>
        new()
        {
            Id = scope.TransferId,
            AdmissionTicketId = scope.TicketId,
            StatusCode = "offered",
            SupportCode =
                "recipient_action_required",
            TransferHop = 1,
            ExpiresAt = DateTimeOffset.UtcNow
                .AddHours(1),
            CredentialGeneration = 1,
            _links =
                new Dictionary<string, HalLink>(),
        };

    private static TicketTransferCredentialResponse
        CredentialResponse(
            HalResourceOfTicketTransferDto transfer) =>
        new()
        {
            Transfer = transfer,
            Credential =
                Guid.CreateVersion7().ToString("N"),
        };

    private static async Task<BrowserSession>
        IssueBrowserSessionAsync(
            HttpClient client,
            string authentication)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/auth/status");
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            authentication);
        using HttpResponseMessage response =
            await client.SendAsync(request);
        string[] cookies = response.Headers
            .GetValues("Set-Cookie")
            .ToArray();
        string antiforgery = cookies.First(value =>
            value.StartsWith(
                "XSRF-TOKEN=",
                StringComparison.Ordinal));
        int end = antiforgery.IndexOf(';');
        string token = Uri.UnescapeDataString(
            antiforgery[
                "XSRF-TOKEN=".Length..end]);
        string cookieHeader = string.Join(
            "; ",
            cookies.Select(value =>
                value.Split(';', 2)[0]));
        return new BrowserSession(
            token,
            cookieHeader);
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

    private sealed record BrowserSession(
        string AntiforgeryToken,
        string CookieHeader);

    private sealed record TransferScope(
        Guid EventId,
        Guid TicketId,
        Guid TransferId)
    {
        public string RootPath =>
            $"/bff/events/{EventId}/admission-tickets/" +
            $"{TicketId}/transfers";

        public string Path =>
            $"{RootPath}/{TransferId}";

        public (HttpMethod Method, string Path)[] Writes =>
        [
            (HttpMethod.Post, RootPath),
            (HttpMethod.Post, $"{Path}/accept"),
            (HttpMethod.Delete, Path),
            (HttpMethod.Post, $"{Path}/correction"),
            (HttpMethod.Post, $"{Path}/reissue"),
        ];

        public static TransferScope Create() =>
            new(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7());
    }
}
