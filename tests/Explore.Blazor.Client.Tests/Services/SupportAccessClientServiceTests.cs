using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Blazor.Client.Contracts.Services.SupportAccess;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class SupportAccessClientServiceTests
{
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

    private readonly SupportAccessClientService _service;

    public SupportAccessClientServiceTests()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler((request, token) => _handler(request, token)))
        {
            BaseAddress = new Uri("https://bff.test")
        };

        _service = new SupportAccessClientService(
            new BffClient(httpClient, Substitute.For<Microsoft.JSInterop.IJSRuntime>()),
            Substitute.For<ILogger<SupportAccessClientService>>());
    }

    [Test]
    public async Task GetSessionsAsync_WithHalLinks_PreservesCollectionAndItemAffordances()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        string? capturedPath = null;

        _handler = (request, _) =>
        {
            capturedPath = request.RequestUri?.PathAndQuery;
            return Task.FromResult(JsonResponse(new HalCollectionResourceOfSupportAccessSessionDto
            {
                TotalCount = 1,
                PageSize = 25,
                _links = new Dictionary<string, HalLink>
                {
                    ["start"] = new() { Href = "/api/support-access/sessions", Method = "POST", Title = "Start support access" }
                },
                _embedded = new HalCollectionEmbeddedOfSupportAccessSessionDto
                {
                    Items =
                    [
                        HalLinkTestFactory.WithLinks(new HalResourceOfSupportAccessSessionDto
                        {
                            Id = sessionId,
                            TargetTenantId = tenantId,
                            IsActive = true,
                            AllowsWrites = true,
                            ModeName = "Write",
                            StatusName = "Active"
                        },
            new HalLinkTestLink("audit-events", "/audit", "GET", "Audit events"),
            new HalLinkTestLink("force-stop", "/force-stop", "POST", "Force-stop"))
                    ]
                }
            }));
        };

        var result = await _service.GetSessionsAsync(tenantId, 25);

        await Assert.That(capturedPath).IsEqualTo($"/bff/support-access/tenants/{tenantId:D}/sessions?limit=25");
        var collection = result.Data!;
        var item = collection._embedded!.Items!.Single();
        await Assert.That(collection._links!.ContainsKey("start")).IsTrue();
        await Assert.That(collection._embedded.Items.Count).IsEqualTo(1);
        await Assert.That(item._links!.ContainsKey("audit-events")).IsTrue();
        await Assert.That(item._links.ContainsKey("force-stop")).IsTrue();
        await Assert.That(item._links.ContainsKey("stop")).IsFalse();
    }

    [Test]
    public async Task GetSessionsAsync_WithoutHalLinks_DoesNotExposeAffordances()
    {
        var tenantId = Guid.NewGuid();

        _handler = (_, _) => Task.FromResult(JsonResponse(new HalCollectionResourceOfSupportAccessSessionDto
        {
            TotalCount = 1,
            _embedded = new HalCollectionEmbeddedOfSupportAccessSessionDto
            {
                Items =
                [
                    new HalResourceOfSupportAccessSessionDto
                    {
                        Id = Guid.NewGuid(),
                        TargetTenantId = tenantId,
                        IsActive = true,
                        StatusName = "Active"
                    }
                ]
            }
        }));

        var result = await _service.GetSessionsAsync(tenantId, 100);

        var collection = result.Data!;
        var item = collection._embedded!.Items!.Single();
        await Assert.That(collection._links?.ContainsKey("start") == true).IsFalse();
        await Assert.That(item._links?.ContainsKey("audit-events") == true).IsFalse();
        await Assert.That(item._links?.ContainsKey("force-stop") == true).IsFalse();
        await Assert.That(item._links?.ContainsKey("stop") == true).IsFalse();
    }

    [Test]
    public async Task ForceStopAsync_UsesBffEndpointAndSendsEndReason()
    {
        var sessionId = Guid.NewGuid();
        var endReason = "Emergency revocation after operator escalation.";
        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        string? capturedBody = null;

        _handler = async (request, token) =>
        {
            capturedMethod = request.Method;
            capturedPath = request.RequestUri?.PathAndQuery;
            capturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(token);

            return JsonResponse(new HalResourceOfSupportAccessSessionDto
            {
                Id = sessionId,
                TargetTenantId = Guid.NewGuid(),
                IsActive = false,
                StatusName = "Revoked"
            });
        };

        var result = await _service.ForceStopAsync(sessionId, endReason);
        var body = JsonSerializer.Deserialize<ForceStopSupportAccessSessionRequestDto>(
            capturedBody!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await Assert.That(result.Success).IsTrue();
        await Assert.That(capturedMethod).IsEqualTo(HttpMethod.Post);
        await Assert.That(capturedPath).IsEqualTo($"/bff/support-access/sessions/{sessionId:D}/force-stop");
        await Assert.That(body?.EndReasonText).IsEqualTo(endReason);
    }

    [Test]
    public async Task StartAsync_WithHalDataEnvelope_MapsSessionAndSetsActiveState()
    {
        var sessionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        _handler = (_, _) => Task.FromResult(JsonResponse(new
        {
            data = new
            {
                id = sessionId,
                targetTenantId = tenantId,
                modeName = "ReadOnly",
                statusName = "Active",
                allowsWrites = false,
                isActive = true
            },
            _links = new Dictionary<string, object>
            {
                ["self"] = new { href = "/api/support-access/current", method = "GET" },
                ["stop"] = new { href = $"/api/support-access/sessions/{sessionId:D}/stop", method = "POST" }
            }
        }));

        var result = await _service.StartAsync(new StartSupportAccessSessionRequestDto
        {
            TargetTenantId = tenantId,
            DurationMinutes = 30,
            ReasonCode = "customer_support",
            ReasonText = "Investigating support ticket.",
            TicketReference = "SUP-1"
        });

        await Assert.That(result.Success).IsTrue();
        await Assert.That(_service.IsActive).IsTrue();
        await Assert.That(_service.CurrentSession?.Id).IsEqualTo(sessionId);
        await Assert.That(_service.CurrentSession?.TargetTenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task StartAsync_WithApiSerializedHalResource_MapsSessionAndSetsActiveState()
    {
        var sessionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        _handler = (_, _) => Task.FromResult(JsonResponse(new HalResourceOfSupportAccessSessionDto
        {
            Id = sessionId,
            TargetTenantId = tenantId,
            ModeName = "ReadOnly",
            StatusName = "Active",
            AllowsWrites = false,
            IsActive = true,
            ExpiresAtUtc = TestTime.UtcNow.AddMinutes(30),
            _links = new Dictionary<string, HalLink>
            {
                ["self"] = new() { Href = "/api/support-access/current", Method = "GET" },
                ["stop"] = new()
                {
                    Href = $"/api/support-access/sessions/{sessionId:D}/stop",
                    Method = "POST"
                }
            }
        }));

        var result = await _service.StartAsync(new StartSupportAccessSessionRequestDto
        {
            TargetTenantId = tenantId,
            DurationMinutes = 30,
            ReasonCode = "customer_support",
            ReasonText = "Investigating support ticket.",
            TicketReference = "SUP-1"
        });

        await Assert.That(result.Success).IsTrue();
        await Assert.That(_service.IsActive).IsTrue();
        await Assert.That(_service.CurrentSession?.Id).IsEqualTo(sessionId);
        await Assert.That(_service.CurrentSession?.TargetTenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task StartAsync_WhenBffRejectsStart_ReportsStartFailure()
    {
        _handler = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                title = "Invalid support access request",
                detail = "Mode is invalid."
            })
        });

        var result = await _service.StartAsync(new StartSupportAccessSessionRequestDto
        {
            TargetTenantId = Guid.NewGuid(),
            DurationMinutes = 30,
            ReasonCode = "customer_support",
            ReasonText = "Investigating support ticket.",
            TicketReference = "SUP-1"
        });

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.ErrorMessage).IsEqualTo("Support access could not be started.");
        await Assert.That(_service.LastError).IsEqualTo("Support access could not be started.");
        await Assert.That(_service.CurrentSession).IsNull();
    }

    private static HttpResponseMessage JsonResponse<T>(T payload) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(payload)
    };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
