using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffSupportAccessEndpointsTests
{
    [Test]
    public async Task ListSessionsForwardsTenantHistoryRequestToApi()
    {
        var tenantId = Guid.NewGuid();
        string? capturedPath = null;

        await using var factory = CreateFactory((request, _) =>
        {
            capturedPath = request.RequestUri?.PathAndQuery;
            return Task.FromResult(JsonResponse(new
            {
                totalCount = 1,
                _links = new { start = new { href = "/api/support-access/sessions", method = "POST" } }
            }));
        });
        using var client = CreateClient(factory);
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/bff/support-access/tenants/{tenantId:D}/sessions?limit=25");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(capturedPath).IsEqualTo($"/api/support-access/tenants/{tenantId:D}/sessions?limit=25");
    }

    [Test]
    public async Task ForceStopWithoutAntiforgeryHeaderReturnsBadRequest()
    {
        await using var factory = new BlazorBffWebApplicationFactory();
        using var client = CreateClient(factory);
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/bff/support-access/sessions/{Guid.NewGuid():D}/force-stop");
        request.Content = JsonContent.Create(new ForceStopSupportAccessSessionRequestDto
        {
            EndReasonText = "Emergency revocation."
        });

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Antiforgery validation failed");
    }

    [Test]
    public async Task StartWhenApiSucceedsStoresSessionAndPreservesFlattenedHalBody()
    {
        var sessionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var startedAtUtc = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var expiresAtUtc = startedAtUtc.AddMinutes(30);
        string? capturedApiBody = null;
        var apiBody = $$"""
        {
          "id": "{{sessionId:D}}",
          "actorUserId": "{{actorUserId:D}}",
          "targetTenantId": "{{tenantId:D}}",
          "statusId": 1,
          "statusName": "Active",
          "modeId": 1,
          "modeName": "ReadOnly",
          "allowsWrites": false,
          "reasonCode": "customer_support",
          "ticketReference": "SUP-E2E-001",
          "startedAtUtc": "{{startedAtUtc:O}}",
          "expiresAtUtc": "{{expiresAtUtc:O}}",
          "isActive": true,
          "_links": {
            "self": { "href": "/api/support-access/tenants/{{tenantId:D}}/sessions", "method": "GET" },
            "stop": { "href": "/api/support-access/sessions/{{sessionId:D}}/stop", "method": "POST" }
          }
        }
        """;
        var store = Substitute.For<IBffSupportAccessSessionStore>();
        store.StoreAsync(
                Arg.Any<ClaimsPrincipal>(),
                Arg.Is<SupportAccessSessionDto>(session =>
                    session.Id == sessionId &&
                    session.TargetTenantId == tenantId &&
                    session.IsActive == true),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BffSupportAccessStoreResult.Stored(new BffSupportAccessSession(
                sessionId,
                actorUserId.ToString("D"),
                null,
                tenantId,
                1,
                false,
                expiresAtUtc))));

        await using var factory = CreateFactory(
            async (request, token) =>
            {
                capturedApiBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(token);

                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(apiBody, Encoding.UTF8, "application/hal+json")
                };
            },
            services =>
            {
                services.RemoveAll<IBffSupportAccessSessionStore>();
                services.AddSingleton(store);
            });
        using var client = CreateClient(factory);
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/bff/support-access/sessions", actorUserId);
        AddSelfCallToken(factory, client, request, actorUserId);
        request.Content = new StringContent(
            $$"""
            {
              "targetTenantId": "{{tenantId:D}}",
              "mode": "ReadOnly",
              "durationMinutes": 30,
              "reasonCode": "customer_support",
              "reasonText": "Investigating support ticket.",
              "ticketReference": "SUP-E2E-001"
            }
            """,
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/hal+json");
        using var document = JsonDocument.Parse(body);
        await Assert.That(document.RootElement.GetProperty("id").GetString()).IsEqualTo(sessionId.ToString("D"));
        await Assert.That(document.RootElement.TryGetProperty("data", out _)).IsFalse();
        await Assert.That(document.RootElement.TryGetProperty("_links", out _)).IsTrue();
        await Assert.That(capturedApiBody).Contains("\"mode\":\"ReadOnly\"");
        await store.Received(1).StoreAsync(
            Arg.Any<ClaimsPrincipal>(),
            Arg.Is<SupportAccessSessionDto>(session =>
                session.Id == sessionId && session.TargetTenantId == tenantId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ForceStopWhenApiSucceedsClearsMatchingCurrentSupportSession()
    {
        var sessionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var endReason = "Emergency revocation after operator escalation.";
        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        string? capturedBody = null;
        var store = Substitute.For<IBffSupportAccessSessionStore>();
        store.ResolveCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BffSupportAccessStoreResult.Stored(new BffSupportAccessSession(
                sessionId,
                actorUserId.ToString(),
                null,
                tenantId,
                1,
                false,
                DateTimeOffset.UtcNow.AddMinutes(30)))));

        await using var factory = CreateFactory(
            async (request, token) =>
            {
                capturedMethod = request.Method;
                capturedPath = request.RequestUri?.PathAndQuery;
                capturedBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(token);

                return JsonResponse(new HalResourceOfSupportAccessSessionDto
                {
                    Id = sessionId,
                    ActorUserId = actorUserId,
                    TargetTenantId = tenantId,
                    StatusName = "Revoked",
                    ModeName = "ReadOnly",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(25),
                    IsActive = false
                });
            },
            services =>
            {
                services.RemoveAll<IBffSupportAccessSessionStore>();
                services.AddSingleton(store);
            });
        using var client = CreateClient(factory);
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/bff/support-access/sessions/{sessionId:D}/force-stop",
            actorUserId);
        AddSelfCallToken(factory, client, request, actorUserId);
        request.Content = JsonContent.Create(new ForceStopSupportAccessSessionRequestDto
        {
            EndReasonText = endReason
        });

        using var response = await client.SendAsync(request);
        var forwardedBody = JsonSerializer.Deserialize<ForceStopSupportAccessSessionRequestDto>(
            capturedBody!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(capturedPath).IsEqualTo($"/api/support-access/sessions/{sessionId:D}/force-stop");
        await Assert.That(forwardedBody?.EndReasonText).IsEqualTo(endReason);
        await store.Received(1).ClearAsync(
            Arg.Is<ClaimsPrincipal>(principal => principal.Identity != null && principal.Identity.IsAuthenticated),
            Arg.Any<CancellationToken>());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        Action<IServiceCollection>? configureServices = null)
    {
        return new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISupportAccessClient>();
                services.AddSingleton<ISupportAccessClient>(_ => CreateSupportAccessClient(handler));
                configureServices?.Invoke(services);
            });
        });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string requestUri,
        Guid? actorUserId = null)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateInstanceAdminHeaderValue(actorUserId ?? Guid.NewGuid()));
        return request;
    }

    private static void AddSelfCallToken(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        HttpRequestMessage request,
        Guid actorUserId)
    {
        var tokenService = factory.Services.GetRequiredService<IBffSelfCallTokenService>();
        var issueContext = new DefaultHttpContext
        {
            RequestServices = factory.Services,
            User = CreatePrincipal(actorUserId)
        };
        using var outboundRequest = new HttpRequestMessage(
            request.Method,
            new Uri(client.BaseAddress!, request.RequestUri!));
        var token = tokenService.Issue(issueContext, outboundRequest)
            ?? throw new InvalidOperationException("Could not issue BFF self-call token for test request.");

        request.Headers.Add(BffSelfCallHeaders.Token, token);
    }

    private static ClaimsPrincipal CreatePrincipal(Guid actorUserId) => new(new ClaimsIdentity(
        [
            new Claim("sub", actorUserId.ToString("D")),
            new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString("D"))
        ],
        "Cookies"));

    private static HttpResponseMessage JsonResponse<T>(T payload) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(payload)
    };

    private static ISupportAccessClient CreateSupportAccessClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("https://api.test/")
        };

        return new SupportAccessClient(httpClient);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
