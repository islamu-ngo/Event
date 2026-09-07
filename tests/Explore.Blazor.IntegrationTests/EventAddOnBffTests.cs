using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Event.Web.BffHosting.Abstractions;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Explore.Blazor.IntegrationTests;

public sealed class EventAddOnBffTests
{
    private const string CapabilityHeader = "X-Registration-Order-Capability";
    private const string IdempotencyHeader = "Idempotency-Key";

    [Test]
    public async Task PublicReadsForwardCanonicalRoutesAndKeepCapabilitiesOutOfUrlsAndResponses()
    {
        AddOnScope scope = AddOnScope.Create();
        var transport = new RecordingApiTransport(scope);
        await using WebApplicationFactory<Program> factory = CreateFactory(transport);
        using HttpClient client = CreateClient(factory);
        string capability = Guid.CreateVersion7().ToString("N");

        using HttpResponseMessage catalogResponse = await client.GetAsync(scope.CatalogPath);
        using var orderRequest = new HttpRequestMessage(HttpMethod.Get, scope.OrderPath);
        orderRequest.Headers.Add(CapabilityHeader, capability);
        using HttpResponseMessage orderResponse = await client.SendAsync(orderRequest);
        string orderBody = await orderResponse.Content.ReadAsStringAsync();

        await Assert.That(catalogResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(orderResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertPrivateNoStoreAsync(catalogResponse);
        await AssertPrivateNoStoreAsync(orderResponse);
        await Assert.That(orderRequest.RequestUri!.Query).DoesNotContain(capability);
        await Assert.That(orderBody).DoesNotContain(capability);

        ForwardedRequest catalog = transport.Request(HttpMethod.Get, scope.ApiCatalogPath);
        ForwardedRequest order = transport.Request(HttpMethod.Get, scope.ApiOrderPath);
        await Assert.That(catalog.Body).IsEmpty();
        await Assert.That(order.Header(CapabilityHeader)).IsEqualTo(capability);
    }

    [Test]
    public async Task ManagementReadRequiresAuthenticatedBrowserAuthority()
    {
        AddOnScope scope = AddOnScope.Create();
        var transport = new RecordingApiTransport(scope);
        await using WebApplicationFactory<Program> factory = CreateFactory(transport);
        using HttpClient client = CreateClient(factory);

        using HttpResponseMessage response = await client.GetAsync(scope.ManagementPath);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(transport.Requests).IsEmpty();
    }

    [Test]
    public async Task EveryUnsafeRouteFailsClosedWithoutAntiforgery()
    {
        AddOnScope scope = AddOnScope.Create();
        var transport = new RecordingApiTransport(scope);
        await using WebApplicationFactory<Program> factory = CreateFactory(transport);
        using HttpClient client = CreateClient(factory);
        string authentication = TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7());

        foreach (AddOnWrite write in AddOnWrite.All)
        {
            using HttpRequestMessage request = write.CreateRequest(scope);
            request.Headers.Add(TestAuthHandler.AuthHeaderName, authentication);

            using HttpResponseMessage response = await client.SendAsync(request);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }

        await Assert.That(transport.Requests).IsEmpty();
    }

    [Test]
    public async Task EveryUnsafeRouteFailsClosedWithoutAuthenticatedBrowserAuthority()
    {
        AddOnScope scope = AddOnScope.Create();
        var transport = new RecordingApiTransport(scope);
        await using WebApplicationFactory<Program> factory = CreateFactory(transport);
        using HttpClient client = CreateClient(factory);
        BrowserSession session = await IssueBrowserSessionAsync(client);

        foreach (AddOnWrite write in AddOnWrite.All)
        {
            using HttpRequestMessage request = write.CreateRequest(scope);
            AddBrowserSession(request, session);

            using HttpResponseMessage response = await client.SendAsync(request);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        await Assert.That(transport.Requests).IsEmpty();
    }

    [Test]
    public async Task AuthenticatedBrowserWithoutServerTokenFailsClosedAtTheApiBoundary()
    {
        AddOnScope scope = AddOnScope.Create();
        var transport = new RecordingApiTransport(scope);
        await using WebApplicationFactory<Program> factory = CreateFactory(transport);
        using HttpClient client = CreateClient(factory);
        string authentication = TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7());
        BrowserSession session = await IssueBrowserSessionAsync(client, authentication);
        using HttpRequestMessage request = AddOnWrite.Reserve.CreateRequest(scope);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, authentication);
        AddBrowserSession(request, session);

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertPrivateNoStoreAsync(response);
        ForwardedRequest rejected = transport.Request(HttpMethod.Post, scope.ApiOrderPath);
        await Assert.That(rejected.Authorization).IsNull();
        await Assert.That(transport.AuthorizedUnsafeRequests).IsEmpty();
    }

    [Test]
    public async Task HostingAdapterResolvesTokenStoredForCircuitSubjectPurpose()
    {
        AddOnScope scope = AddOnScope.Create();
        Guid userId = Guid.CreateVersion7();
        string accessToken = CreateAccessToken(userId);
        var transport = new RecordingApiTransport(scope);
        await using WebApplicationFactory<Program> factory = CreateFactory(transport);
        using var serviceScope = factory.Services.CreateScope();
        var store = serviceScope.ServiceProvider.GetRequiredService<ICircuitTokenStore>();
        store.Store(CircuitKey(userId), sessionId: null, accessToken);
        var context = new DefaultHttpContext
        {
            RequestServices = serviceScope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", userId.ToString())],
                CookieAuthenticationDefaults.AuthenticationScheme))
        };
        var provider = serviceScope.ServiceProvider.GetRequiredService<IEventBffAccessTokenProvider>();

        var resolved = await provider.ResolveAccessTokenAsync(context, CancellationToken.None);

        await Assert.That(resolved).IsEqualTo(accessToken);
    }

    [Test]
    public async Task AuthenticatedWritesForwardCanonicalRoutesHeadersAndJsonPayloads()
    {
        AddOnScope scope = AddOnScope.Create();
        Guid userId = Guid.CreateVersion7();
        string accessToken = CreateAccessToken(userId);
        var transport = new RecordingApiTransport(scope);
        await using WebApplicationFactory<Program> factory = CreateFactory(transport);
        using HttpClient client = CreateClient(factory);
        factory.Services.GetRequiredService<ICircuitTokenStore>().Store(
            CircuitKey(userId),
            sessionId: null,
            accessToken);
        string authentication = TestAuthHandler.CreateAuthHeaderValue(userId);
        BrowserSession session = await IssueBrowserSessionAsync(client, authentication);

        foreach (AddOnWrite write in AddOnWrite.All)
        {
            using HttpRequestMessage request = write.CreateRequest(scope);
            request.Headers.Add(TestAuthHandler.AuthHeaderName, authentication);
            AddBrowserSession(request, session);

            using HttpResponseMessage response = await client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await AssertPrivateNoStoreAsync(response);
            await Assert.That(request.RequestUri!.Query).DoesNotContain(scope.Capability);
            await Assert.That(body).DoesNotContain(scope.Capability);
        }

        ForwardedRequest draft = transport.Request(HttpMethod.Post, $"{scope.ApiManagementPath}/draft");
        ForwardedRequest item = transport.Request(HttpMethod.Post, $"{scope.ApiManagementPath}/items");
        ForwardedRequest publish = transport.Request(HttpMethod.Post, $"{scope.ApiManagementPath}/publish");
        ForwardedRequest retire = transport.Request(HttpMethod.Post, $"{scope.ApiManagementPath}/retire");
        ForwardedRequest reserve = transport.Request(HttpMethod.Post, scope.ApiOrderPath);
        ForwardedRequest fulfill = transport.Request(
            HttpMethod.Post,
            $"{scope.ApiOrderPath}/{scope.LineId}/fulfillment");
        ForwardedRequest refund = transport.Request(
            HttpMethod.Post,
            $"{scope.ApiOrderPath}/{scope.LineId}/refunds");

        foreach (ForwardedRequest forwarded in transport.Requests)
        {
            await Assert.That(forwarded.Authorization).IsEqualTo($"Bearer {accessToken}");
            await Assert.That(forwarded.Header(IdempotencyHeader)).IsEqualTo(scope.IdempotencyKey);
        }

        await Assert.That(Json(draft).GetProperty("currencyCode").GetString()).IsEqualTo("EUR");
        await Assert.That(Json(item).GetProperty("name").GetString()).IsEqualTo("Prayer mat");
        await Assert.That(Json(item).GetProperty("unitPriceMinor").GetInt64()).IsEqualTo(1500);
        await Assert.That(Json(item).GetProperty("inventoryCapacity").GetInt32()).IsEqualTo(20);
        await Assert.That(publish.Body).IsEmpty();
        await Assert.That(retire.Body).IsEmpty();
        await Assert.That(Json(reserve).GetProperty("catalogId").GetGuid()).IsEqualTo(scope.CatalogId);
        await Assert.That(
                Json(reserve).GetProperty("selections")[0].GetProperty("catalogItemId").GetGuid())
            .IsEqualTo(scope.CatalogItemId);
        await Assert.That(Json(reserve).GetProperty("selections")[0].GetProperty("quantity").GetInt32())
            .IsEqualTo(2);
        await Assert.That(refund.JsonProperty("quantity").GetInt32()).IsEqualTo(1);

        foreach (ForwardedRequest forwarded in new[] { reserve, fulfill, refund })
        {
            await Assert.That(forwarded.Header(CapabilityHeader)).IsEqualTo(scope.Capability);
        }
    }

    [Test]
    public async Task ServerHeldAccessTokenIsForwardedWhileBrowserCredentialsAreContainedAndSanitized()
    {
        AddOnScope scope = AddOnScope.Create();
        Guid userId = Guid.CreateVersion7();
        string accessToken = CreateAccessToken(userId);
        var transport = new RecordingApiTransport(scope);
        await using WebApplicationFactory<Program> factory = CreateFactory(transport);
        using HttpClient client = CreateClient(factory);
        factory.Services.GetRequiredService<ICircuitTokenStore>().Store(
            CircuitKey(userId),
            sessionId: null,
            accessToken);
        string authentication = TestAuthHandler.CreateAuthHeaderValue(userId);
        BrowserSession session = await IssueBrowserSessionAsync(client, authentication);
        using HttpRequestMessage request = AddOnWrite.Reserve.CreateRequest(scope);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, authentication);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer browser-forged-token");
        request.Headers.TryAddWithoutValidation("X-Setup-Secret", "browser-forged-secret");
        request.Headers.TryAddWithoutValidation("X-Tenant-Slug", "browser-forged-tenant");
        AddBrowserSession(request, session);

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertPrivateNoStoreAsync(response);
        await Assert.That(body).DoesNotContain(accessToken);
        await Assert.That(body).DoesNotContain("browser-forged-token");
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
        foreach (string cookie in session.SetCookies)
        {
            await Assert.That(cookie).DoesNotContain(accessToken);
            await Assert.That(
                    cookie.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)
                    || cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase))
                .IsTrue();
        }

        ForwardedRequest forwarded = transport.Request(HttpMethod.Post, scope.ApiOrderPath);
        await Assert.That(forwarded.Authorization).IsEqualTo($"Bearer {accessToken}");
        await Assert.That(forwarded.Header("X-Setup-Secret")).IsNull();
        await Assert.That(forwarded.Header("X-Tenant-Slug"))
            .IsNull()
            .Because("event-id routes provide no authoritative tenant slug hint");
        await Assert.That(forwarded.Header("Cookie")).IsNull();
        await Assert.That(forwarded.Header(CapabilityHeader)).IsEqualTo(scope.Capability);
    }

    private static JsonElement Json(ForwardedRequest request) =>
        JsonDocument.Parse(request.Body).RootElement.Clone();

    private static WebApplicationFactory<Program> CreateFactory(RecordingApiTransport transport) =>
        new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<IEventAddOnCatalogClient, EventAddOnCatalogClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => transport);
                services.AddHttpClient<IEventAddOnManagementClient, EventAddOnManagementClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => transport);
                services.AddHttpClient<IRegistrationOrderAddOnClient, RegistrationOrderAddOnClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => transport);
            }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private static async Task AssertPrivateNoStoreAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    private static async Task<BrowserSession> IssueBrowserSessionAsync(
        HttpClient client,
        string? authentication = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/status");
        if (authentication is not null)
        {
            request.Headers.Add(TestAuthHandler.AuthHeaderName, authentication);
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string[] cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        string antiforgery = cookies.First(value =>
            value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        int end = antiforgery.IndexOf(';');
        string token = Uri.UnescapeDataString(antiforgery["XSRF-TOKEN=".Length..end]);
        string cookieHeader = string.Join("; ", cookies.Select(value => value.Split(';', 2)[0]));
        return new BrowserSession(token, cookieHeader, cookies);
    }

    private static void AddBrowserSession(HttpRequestMessage request, BrowserSession session)
    {
        request.Headers.Add("Cookie", session.CookieHeader);
        request.Headers.Add("X-CSRF-TOKEN", session.AntiforgeryToken);
    }

    private static string CreateAccessToken(Guid userId) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: [new Claim("sub", userId.ToString())],
            expires: DateTime.UtcNow.AddMinutes(30)));

    private static string CircuitKey(Guid userId) =>
        new EventBffOpaqueIdentity(
            CookieAuthenticationDefaults.AuthenticationScheme,
            userId.ToString(),
            EventBffOpaqueIdentityPurpose.CircuitSubject,
            EventBffOpaqueIdentitySource.ProviderSubject).PartitionKey;

    private sealed record BrowserSession(
        string AntiforgeryToken,
        string CookieHeader,
        IReadOnlyList<string> SetCookies);

    private sealed record ForwardedRequest(
        HttpMethod Method,
        string Path,
        string? Authorization,
        IReadOnlyDictionary<string, string> Headers,
        string Body)
    {
        public string? Header(string name) =>
            Headers.TryGetValue(name, out string? value) ? value : null;

        public JsonElement JsonProperty(string name) => JsonDocument.Parse(Body).RootElement
            .GetProperty(name)
            .Clone();
    }

    private sealed class RecordingApiTransport(AddOnScope scope) : HttpMessageHandler
    {
        private readonly List<ForwardedRequest> _requests = [];

        public IReadOnlyList<ForwardedRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToArray();
                }
            }
        }

        public IReadOnlyList<ForwardedRequest> AuthorizedUnsafeRequests
        {
            get
            {
                lock (_requests)
                {
                    return _requests
                        .Where(request => request.Method != HttpMethod.Get)
                        .Where(request => request.Authorization?.StartsWith(
                            "Bearer ",
                            StringComparison.Ordinal) == true)
                        .ToArray();
                }
            }
        }

        public ForwardedRequest Request(HttpMethod method, string path)
        {
            lock (_requests)
            {
                return _requests.Single(request =>
                    request.Method == method
                    && request.Path.Equals(path, StringComparison.Ordinal));
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers
                .Where(header => !header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    header => header.Key,
                    header => string.Join(",", header.Value),
                    StringComparer.OrdinalIgnoreCase);
            var forwarded = new ForwardedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                headers,
                body);
            lock (_requests)
            {
                _requests.Add(forwarded);
            }

            if (forwarded.Method != HttpMethod.Get
                && forwarded.Authorization?.StartsWith(
                    "Bearer ",
                    StringComparison.Ordinal) != true)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    RequestMessage = request,
                    Content = new StringContent(
                        "{}",
                        Encoding.UTF8,
                        "application/problem+json"),
                };
            }

            bool orderResponse = forwarded.Path.Contains(
                "/registration-orders/",
                StringComparison.Ordinal);
            string responseBody = orderResponse
                ? JsonSerializer.Serialize(new
                {
                    registrationOrderId = scope.RegistrationOrderId,
                    currencyCode = "EUR",
                    addOnTotalMinor = 3000,
                    grandTotalMinor = 9000,
                })
                : JsonSerializer.Serialize(new
                {
                    id = scope.CatalogId,
                    versionNumber = 1,
                    currencyCode = "EUR",
                    items = Array.Empty<object>(),
                });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record AddOnWrite(
        string Suffix,
        Func<AddOnScope, HttpContent?> Content,
        bool SendsCapability)
    {
        public static readonly AddOnWrite Reserve = new(
            "reserve",
            value => JsonContent.Create(new ReserveEventAddOnsRequest
            {
                CatalogId = value.CatalogId,
                Selections =
                [
                    new EventAddOnSelectionRequest
                    {
                        CatalogItemId = value.CatalogItemId,
                        Quantity = 2,
                    },
                ],
            }),
            true);

        public static readonly IReadOnlyList<AddOnWrite> All =
        [
            new("management/draft", _ => JsonContent.Create(
                new CreateEventAddOnCatalogDraftRequest { CurrencyCode = "EUR" }), false),
            new("management/items", _ => JsonContent.Create(
                new ManageEventAddOnCatalogItemRequest
                {
                    Name = "Prayer mat",
                    UnitPriceMinor = 1500,
                    InventoryCapacity = 20,
                    FulfillmentDisclosure = "Collect on site",
                    RefundDisclosure = "Refundable until start",
                }), false),
            new("management/publish", _ => null, false),
            new("management/retire", _ => null, false),
            Reserve,
            new("line/fulfillment", _ => null, true),
            new("line/refunds", _ => JsonContent.Create(
                new RefundEventAddOnRequest { Quantity = 1 }), true),
        ];

        public HttpRequestMessage CreateRequest(AddOnScope scope)
        {
            string path = Suffix switch
            {
                "reserve" => scope.OrderPath,
                "line/fulfillment" => $"{scope.OrderPath}/{scope.LineId}/fulfillment",
                "line/refunds" => $"{scope.OrderPath}/{scope.LineId}/refunds",
                _ => $"{scope.CatalogPath}/{Suffix}",
            };
            var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = Content(scope),
            };
            request.Headers.Add(IdempotencyHeader, scope.IdempotencyKey);
            if (SendsCapability)
            {
                request.Headers.Add(CapabilityHeader, scope.Capability);
            }

            return request;
        }
    }

    private sealed record AddOnScope(
        Guid EventId,
        Guid RegistrationOrderId,
        Guid LineId,
        Guid CatalogId,
        Guid CatalogItemId,
        string IdempotencyKey,
        string Capability)
    {
        public string CatalogPath => $"/bff/events/{EventId}/add-ons";
        public string ManagementPath => $"{CatalogPath}/management";
        public string OrderPath =>
            $"/bff/events/{EventId}/registration-orders/{RegistrationOrderId}/add-ons";
        public string ApiCatalogPath => $"/api/events/{EventId}/add-ons";
        public string ApiManagementPath => $"{ApiCatalogPath}/management";
        public string ApiOrderPath =>
            $"/api/events/{EventId}/registration-orders/{RegistrationOrderId}/add-ons";

        public static AddOnScope Create() => new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7().ToString("N"),
            Guid.CreateVersion7().ToString("N"));
    }
}
