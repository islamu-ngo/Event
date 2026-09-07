using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.IntegrationTests.Fixtures;
using Explore.Blazor.Services;
using Event.Web.BffHosting.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Explore.Blazor.IntegrationTests.Endpoints;

public sealed class BffStorageUploadProxyTests : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly StorageApiHandler _apiHandler = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();
    private readonly string _authHeader;
    private readonly string _otherAuthHeader;

    public BffStorageUploadProxyTests()
    {
        _authHeader = TestAuthHandler.CreateAuthHeaderValue(
            _userId,
            "Storage Tester",
            (ClaimTypes.Name, "Storage Tester"));
        _otherAuthHeader = TestAuthHandler.CreateAuthHeaderValue(
            _otherUserId,
            "Other Storage Tester",
            (ClaimTypes.Name, "Other Storage Tester"));

        var antiforgery = Substitute.For<IAntiforgery>();
        antiforgery.ValidateRequestAsync(Arg.Any<HttpContext>()).Returns(Task.CompletedTask);

        _factory = new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAntiforgery>();
                services.AddSingleton(antiforgery);
                services.RemoveAll<IStorageObjectClient>();
                services.AddSingleton<IStorageObjectClient>(_ => CreateStorageObjectClient(_apiHandler));
                services.RemoveAll<IOrganizationTenantEvidenceClient>();
                services.AddSingleton<IOrganizationTenantEvidenceClient>(_ => CreateOrganizationTenantEvidenceClient(_apiHandler));
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [Test]
    public async Task UploadSession_WithoutAntiforgeryOrSelfCallToken_ReturnsBadRequest()
    {
        using var factory = CreateStorageBoundaryFactory(new StorageApiHandler());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var request = CreateBoundaryUploadSessionRequest();

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Antiforgery validation failed");
    }

    [Test]
    public async Task UploadProxy_WithoutAntiforgeryOrSelfCallToken_ReturnsBadRequest()
    {
        using var factory = CreateStorageBoundaryFactory(new StorageApiHandler());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var request = CreateBoundaryUploadProxyRequest();

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Antiforgery validation failed");
    }

    [Test]
    public async Task UploadSession_WithValidSelfCallTokenWithoutCsrf_ReturnsOk()
    {
        var apiHandler = new StorageApiHandler();
        using var factory = CreateStorageBoundaryFactory(apiHandler);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var request = CreateBoundaryUploadSessionRequest();
        AddSelfCallToken(factory, client, request, _userId);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(apiHandler.ReserveCallCount).IsEqualTo(1);
    }

    [Test]
    [Arguments("uploadUrl")]
    [Arguments("objectKey")]
    [Arguments("destination")]
    [Arguments("path")]
    public async Task UploadProxy_WithRawDestinationField_ReturnsBadRequest(string fieldName)
    {
        using var request = CreateUploadRequest(fieldName);

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("Raw upload destinations are not accepted");
        await Assert.That(_apiHandler.FinalizeCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task UploadSession_WithPathSegmentFileName_ReturnsBadRequestWithoutCallingApi()
    {
        using var request = CreateUploadSessionRequest("../probe.png", "image/png");

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("simple file name");
        await Assert.That(_apiHandler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task UploadSession_WithReservedDeviceFileName_ReturnsBadRequestWithoutCallingApi()
    {
        using var request = CreateUploadSessionRequest("CON.png", "image/png");

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("reserved device name");
        await Assert.That(_apiHandler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task UploadSession_WithInvalidContentType_ReturnsBadRequestWithoutCallingApi()
    {
        using var request = CreateUploadSessionRequest("probe.png", "not-a-mime-type");

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("JPEG, PNG, GIF, or WebP without parameters");
        await Assert.That(_apiHandler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task OrganizationEvidenceUploadSession_WithPdf_CallsBoundEvidenceApi()
    {
        var organizationId = Guid.CreateVersion7();
        using var request = CreateEvidenceUploadSessionRequest(organizationId);

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(_apiHandler.EvidenceReserveCallCount).IsEqualTo(1);
        await Assert.That(_apiHandler.CapturedReserveRequestBody).Contains("\"contentType\":\"application/pdf\"");
        await Assert.That(_apiHandler.CapturedReserveRequestBody).DoesNotContain("owningResourceId");
    }

    [Test]
    public async Task GenericUploadSession_WithPdf_RemainsRejected()
    {
        using var request = CreateUploadSessionRequest("evidence.pdf", "application/pdf");

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(_apiHandler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task UploadSession_WithMultiSegmentContentType_ReturnsBadRequestWithoutCallingApi()
    {
        using var request = CreateUploadSessionRequest("probe.png", "application/pdf/extra");

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("JPEG, PNG, GIF, or WebP without parameters");
        await Assert.That(_apiHandler.CallCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("image/svg+xml", "probe.svg")]
    [Arguments("image/bmp", "probe.bmp")]
    [Arguments("image/avif", "probe.avif")]
    [Arguments("image/png; charset=binary", "probe.png")]
    [Arguments("image/png", "probe.jpg")]
    [Arguments("image/jpeg", "probe.png")]
    public async Task UploadSession_WithUnsupportedOrMismatchedImageDeclaration_ReturnsBadRequestWithoutCallingApi(
        string contentType,
        string fileName)
    {
        using var request = CreateUploadSessionRequest(fileName, contentType);

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(_apiHandler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task UploadSession_WhenApiReturnsExpiredSession_ReturnsBadGatewayWithoutIssuingBrowserSession()
    {
        _apiHandler.ExpireNextReserveResponse();
        using var request = CreateUploadSessionRequest("probe.png", "image/png");

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("invalid upload session");
        await Assert.That(body).DoesNotContain(_apiHandler.ApiUploadSessionId.ToString());
        await Assert.That(_apiHandler.ReserveCallCount).IsEqualTo(1);
        await Assert.That(_apiHandler.FinalizeCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task UploadProxy_WithContentTypeMismatch_ReturnsBadRequestWithoutUploading()
    {
        var uploadSessionId = await IssueUploadSessionAsync();
        using var request = CreateUploadProxyRequest(uploadSessionId, "image/png", "image/jpeg");

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("content type must match");
        await Assert.That(_apiHandler.FinalizeCallCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("image/svg+xml", "probe.svg")]
    [Arguments("image/bmp", "probe.bmp")]
    [Arguments("image/avif", "probe.avif")]
    [Arguments("image/png; charset=binary", "probe.png")]
    [Arguments("image/png", "probe.jpg")]
    public async Task UploadProxy_WithUnsupportedOrMismatchedImageDeclaration_ReturnsBadRequestWithoutUploading(
        string contentType,
        string fileName)
    {
        var uploadSessionId = await IssueUploadSessionAsync();
        using var request = CreateUploadProxyRequest(uploadSessionId, contentType, contentType, fileName: fileName);

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(_apiHandler.FinalizeCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task UploadProxy_WithReservedDeviceFileName_ReturnsBadRequestWithoutUploading()
    {
        var uploadSessionId = await IssueUploadSessionAsync();
        using var request = CreateUploadProxyRequest(
            uploadSessionId,
            "image/png",
            "image/png",
            fileName: "NUL.png");

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("reserved device name");
        await Assert.That(_apiHandler.FinalizeCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task UploadProxy_WithUnknownSession_ReturnsBadRequestWithoutUploading()
    {
        using var request = CreateUploadProxyRequest("abcdefabcdefabcdefabcdefabcdefab", "image/png", "image/png");

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("server-issued upload session");
        await Assert.That(_apiHandler.FinalizeCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task UploadProxy_WithExpiredCachedSession_ReturnsBadRequestConsumesSessionAndDoesNotUpload()
    {
        const string uploadSessionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        await SeedCachedUploadSessionAsync(uploadSessionId, DateTimeOffset.UtcNow.AddMinutes(-1));
        using var request = CreateUploadProxyRequest(uploadSessionId, "image/png", "image/png");

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("server-issued upload session");
        await Assert.That(_apiHandler.FinalizeCallCount).IsEqualTo(0);

        var cache = _factory.Services.GetRequiredService<IDistributedCache>();
        var payload = await cache.GetStringAsync("storage-upload-session:" + uploadSessionId);
        await Assert.That(payload).IsNull();
    }

    [Test]
    public async Task UploadProxy_WithDifferentUserSession_ReturnsBadRequestWithoutUploading()
    {
        var uploadSessionId = await IssueUploadSessionAsync();
        using var request = CreateUploadProxyRequest(uploadSessionId, "image/png", "image/png", _otherAuthHeader);

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("server-issued upload session");
        await Assert.That(_apiHandler.FinalizeCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task UploadProxy_WithConsumedSession_CannotReuseSession()
    {
        var uploadSessionId = await IssueUploadSessionAsync();

        using var firstRequest = CreateUploadProxyRequest(uploadSessionId, "image/png", "image/png");
        using var firstResponse = await _client.SendAsync(firstRequest);

        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(_apiHandler.FinalizeCallCount).IsEqualTo(1);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        await Assert.That(firstBody).Contains("storageObjectId");

        using var secondRequest = CreateUploadProxyRequest(uploadSessionId, "image/png", "image/png");
        using var secondResponse = await _client.SendAsync(secondRequest);

        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var body = await secondResponse.Content.ReadAsStringAsync();
        await Assert.That(body).Contains("server-issued upload session");
        await Assert.That(_apiHandler.FinalizeCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task UploadSession_WithExpectedSize_CallsProviderNeutralApiSessionEndpoint()
    {
        using var request = CreateUploadSessionRequest("probe.png", "image/png");

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(_apiHandler.ReserveCallCount).IsEqualTo(1);
        await Assert.That(_apiHandler.CapturedReserveRequestBody).Contains("\"expectedSizeBytes\":4");
        await Assert.That(_apiHandler.CapturedReserveRequestBody).IsNotNull();
        await Assert.That(_apiHandler.CapturedReserveRequestBody!.ToLowerInvariant()).DoesNotContain("uploadurl");
        await Assert.That(_apiHandler.CapturedReserveRequestBody.ToLowerInvariant()).DoesNotContain("objectkey");
    }

    [Test]
    [Arguments("photo.jpg", "image/jpeg")]
    [Arguments("photo.jpeg", "image/jpeg")]
    [Arguments("photo.png", "image/png")]
    [Arguments("photo.gif", "image/gif")]
    [Arguments("photo.webp", "image/webp")]
    public async Task UploadSession_WithSafeMatchingImageDeclaration_ReturnsOk(
        string fileName,
        string contentType)
    {
        using var request = CreateUploadSessionRequest(fileName, contentType);

        using var response = await _client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private HttpRequestMessage CreateUploadRequest(string fieldName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/storage/upload-proxy");
        AddBffHeaders(request);

        var uploadUrl = "https://127.0.0.1:1/bucket/key?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Signature=fake";
        var form = new MultipartFormDataContent();
        var uploadUrlContent = new StringContent(uploadUrl);
        form.Add(uploadUrlContent, fieldName);
        var contentTypeContent = new StringContent("image/png");
        form.Add(contentTypeContent, "contentType");

        var fileContent = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "probe.png");
        request.Content = form;

        return request;
    }

    private async Task<string> IssueUploadSessionAsync()
    {
        using var request = CreateUploadSessionRequest("probe.png", "image/png");
        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("uploadSessionId").GetString()!;
    }

    private HttpRequestMessage CreateUploadSessionRequest(string fileName, string contentType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/storage/upload-session");
        AddBffHeaders(request);
        request.Content = JsonContent.Create(new
        {
            fileName,
            contentType,
            expectedSizeBytes = 4L
        });

        return request;
    }

    private HttpRequestMessage CreateEvidenceUploadSessionRequest(Guid organizationId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/bff/organizations/{organizationId:D}/legitimacy-evidence/upload-session");
        AddBffHeaders(request);
        request.Content = JsonContent.Create(new
        {
            fileName = "evidence.pdf",
            contentType = "application/pdf",
            expectedSizeBytes = 4L
        });

        return request;
    }

    private HttpRequestMessage CreateBoundaryUploadSessionRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/storage/upload-session");
        request.Headers.Add(TestAuthHandler.AuthHeaderName, _authHeader);
        request.Content = JsonContent.Create(new
        {
            fileName = "probe.png",
            contentType = "image/png",
            expectedSizeBytes = 4L
        });

        return request;
    }

    private HttpRequestMessage CreateUploadProxyRequest(
        string uploadSessionId,
        string declaredContentType,
        string fileContentType,
        string? authHeader = null,
        string fileName = "probe.png")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/storage/upload-proxy");
        AddBffHeaders(request, authHeader);

        var form = new MultipartFormDataContent();
        form.Add(new StringContent(uploadSessionId), "uploadSessionId");
        form.Add(new StringContent(declaredContentType), "contentType");

        var fileContent = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        fileContent.Headers.TryAddWithoutValidation("Content-Type", fileContentType);
        form.Add(fileContent, "file", fileName);
        request.Content = form;

        return request;
    }

    private HttpRequestMessage CreateBoundaryUploadProxyRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/storage/upload-proxy");
        request.Headers.Add(TestAuthHandler.AuthHeaderName, _authHeader);

        var form = new MultipartFormDataContent();
        form.Add(new StringContent("abcdefabcdefabcdefabcdefabcdefab"), "uploadSessionId");
        form.Add(new StringContent("image/png"), "contentType");

        var fileContent = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "probe.png");
        request.Content = form;

        return request;
    }

    private async Task SeedCachedUploadSessionAsync(string uploadSessionId, DateTimeOffset expiresAtUtc)
    {
        var session = new StorageUploadSession(
            uploadSessionId,
            OwnerKey(_userId),
            _apiHandler.ApiUploadSessionId,
            "image/png",
            4,
            expiresAtUtc);
        var payload = JsonSerializer.Serialize(session, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var cache = _factory.Services.GetRequiredService<IDistributedCache>();
        await cache.SetStringAsync("storage-upload-session:" + uploadSessionId, payload);
    }

    private void AddBffHeaders(HttpRequestMessage request, string? authHeader = null)
    {
        request.Headers.Add("X-CSRF-TOKEN", "test-token");
        request.Headers.Add(TestAuthHandler.AuthHeaderName, authHeader ?? _authHeader);
    }

    private static WebApplicationFactory<Program> CreateStorageBoundaryFactory(StorageApiHandler apiHandler)
    {
        return new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStorageObjectClient>();
                services.AddSingleton<IStorageObjectClient>(_ => CreateStorageObjectClient(apiHandler));
                services.RemoveAll<IOrganizationTenantEvidenceClient>();
                services.AddSingleton<IOrganizationTenantEvidenceClient>(_ => CreateOrganizationTenantEvidenceClient(apiHandler));
            });
        });
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

    private static string OwnerKey(Guid actorUserId)
    {
        CreatePrincipal(actorUserId).TryGetCircuitSubject(out var identity);
        return identity.PartitionKey;
    }

    private static IStorageObjectClient CreateStorageObjectClient(StorageApiHandler apiHandler)
    {
        var httpClient = new HttpClient(apiHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        return new StorageObjectClient(httpClient);
    }

    private static IOrganizationTenantEvidenceClient CreateOrganizationTenantEvidenceClient(StorageApiHandler apiHandler)
    {
        var httpClient = new HttpClient(apiHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        return new OrganizationTenantEvidenceClient(httpClient);
    }

    private sealed class StorageApiHandler : HttpMessageHandler
    {
        private readonly Guid _uploadSessionId = Guid.CreateVersion7();
        private readonly Guid _storageObjectId = Guid.CreateVersion7();
        private DateTime? _nextReserveExpiresAtUtc;
        public int CallCount { get; private set; }
        public int ReserveCallCount { get; private set; }
        public int EvidenceReserveCallCount { get; private set; }
        public int FinalizeCallCount { get; private set; }
        public string? CapturedReserveRequestBody { get; private set; }
        public Guid ApiUploadSessionId => _uploadSessionId;

        public void ExpireNextReserveResponse()
        {
            _nextReserveExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;

            if (request.Method == HttpMethod.Post &&
                string.Equals(request.RequestUri?.AbsolutePath, "/api/storageobject/upload-sessions", StringComparison.Ordinal))
            {
                ReserveCallCount++;
                CapturedReserveRequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                var expiresAtUtc = _nextReserveExpiresAtUtc ?? DateTime.UtcNow.AddMinutes(15);
                _nextReserveExpiresAtUtc = null;

                return JsonResponse(CreateSessionResponse(_uploadSessionId, null, "reserved", expiresAtUtc));
            }

            if (request.Method == HttpMethod.Post
                && request.RequestUri?.AbsolutePath.EndsWith(
                    "/legitimacy-evidence/upload-session",
                    StringComparison.Ordinal) == true)
            {
                EvidenceReserveCallCount++;
                CapturedReserveRequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                return JsonResponse(CreateSessionResponse(
                    _uploadSessionId,
                    null,
                    "reserved",
                    DateTime.UtcNow.AddMinutes(15)));
            }

            if (request.Method == HttpMethod.Put &&
                string.Equals(request.RequestUri?.AbsolutePath, $"/api/storageobject/upload-sessions/{_uploadSessionId}/content", StringComparison.Ordinal))
            {
                FinalizeCallCount++;
                return JsonResponse(CreateSessionResponse(_uploadSessionId, _storageObjectId, "finalized", DateTime.UtcNow.AddMinutes(15)));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string responseJson) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };

        private static string CreateSessionResponse(
            Guid uploadSessionId,
            Guid? storageObjectId,
            string status,
            DateTime expiresAtUtc)
        {
            var storageObjectProperty = storageObjectId.HasValue
                ? $"\"storageObjectId\": \"{storageObjectId.Value}\","
                : string.Empty;

            return $$"""
                {
                  "id": {
                    "id": "{{uploadSessionId}}",
                    "tenantId": "{{Guid.CreateVersion7()}}",
                    "provider": "local",
                    "expectedSizeBytes": 4,
                    "reservedBytes": 4,
                    "contentType": "image/png",
                    "safeDisplayName": "probe.png",
                    "purpose": "legacy_image",
                    "visibility": "public_image",
                    "status": "{{status}}",
                    {{storageObjectProperty}}
                    "expiresAt": "{{expiresAtUtc:O}}",
                    "maxUploadBytes": 10485760,
                    "tenantQuotaBytes": 1073741824,
                    "usedBytes": 0,
                    "totalReservedBytes": 4
                  },
                  "success": true,
                  "message": "ok"
                }
                """;
        }
    }
}
