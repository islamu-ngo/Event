using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.IntegrationTests.Endpoints;

/// <summary>
/// Split-host transport evidence. The downstream handler is a controlled API boundary, not the native
/// EventResource controller; native authorization/race behavior is covered by Event.API integration tests.
/// </summary>
public sealed class BffEventResourceDeliveryTests : IAsyncDisposable
{
    private static readonly byte[] DocumentBytes = "%PDF-1.7\nprivate resource\n%%EOF"u8.ToArray();
    private readonly ResourceApiHandler _api = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly string _authHeader;

    public BffEventResourceDeliveryTests()
    {
        _authHeader = TestAuthHandler.CreateAuthHeaderValue(
            _userId,
            "Resource transport user",
            ("test:access_token", CreateForwardableToken()));
        var antiforgery = Substitute.For<IAntiforgery>();
        antiforgery.ValidateRequestAsync(Arg.Any<HttpContext>()).Returns(Task.CompletedTask);

        _factory = new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAntiforgery>();
                services.AddSingleton(antiforgery);
                services.AddHttpClient<IEventResourcesClient, EventResourcesClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _api);
                services.AddHttpClient<IStorageObjectClient, StorageObjectClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _api);
            }));

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
    }

    [Test]
    [Arguments("handout.pdf", "application/pdf")]
    [Arguments("handout.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [Arguments("handout.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    public async Task ResourceUploadUsesSessionAuthorityOpaqueResourceBindingAndRawByteTransport(
        string fileName, string contentType)
    {
        Guid resourceId = Guid.CreateVersion7();
        Guid expectedVersion = Guid.CreateVersion7();
        using var reserve = CreateReserveRequest(resourceId, expectedVersion, fileName: fileName, contentType: contentType);
        AddSession(reserve, _authHeader);
        reserve.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "browser-forged");
        reserve.Headers.Add(EventBffHeaderNames.TenantSlug, "browser-forged");

        using var reserved = await _client.SendAsync(reserve);

        await Assert.That(reserved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await reserved.Content.ReadAsStringAsync());
        string opaqueSession = payload.RootElement.GetProperty("uploadSessionId").GetString()!;
        await Assert.That(opaqueSession).IsNotEqualTo(_api.ApiUploadSessionId.ToString("N"));
        await Assert.That(_api.ReservedResourceId).IsEqualTo(resourceId);
        await Assert.That(_api.ReserveBody).Contains($"\"expectedVersion\":\"{expectedVersion:D}\"");
        await Assert.That(_api.ReserveBody).DoesNotContain("provider");
        await Assert.That(_api.ReserveBody).DoesNotContain("destination");
        await Assert.That(_api.ReserveAuthorization).StartsWith("Bearer ");
        await Assert.That(_api.ReserveAuthorization).DoesNotContain("browser-forged");
        await Assert.That(_api.ReserveTenant).DoesNotContain("browser-forged");

        using var wrongUserUpload = CreateUploadRequest(opaqueSession, fileName, contentType);
        AddSession(wrongUserUpload, TestAuthHandler.CreateAuthHeaderValue(
            Guid.CreateVersion7(),
            "Different resource user",
            ("test:access_token", CreateForwardableToken())));
        using var rejected = await _client.SendAsync(wrongUserUpload);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await rejected.Content.ReadAsStringAsync()).Contains("server-issued upload session");
        await Assert.That(_api.UploadCount).IsEqualTo(0);

        using var upload = CreateUploadRequest(opaqueSession, fileName, contentType);
        AddSession(upload, _authHeader);
        using var uploaded = await _client.SendAsync(upload);

        await Assert.That(uploaded.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(_api.UploadCount).IsEqualTo(1);
        await Assert.That(_api.UploadBytes).IsEquivalentTo(DocumentBytes);
        await Assert.That(_api.UploadContentType).IsEqualTo(contentType);
        string uploadedBody = await uploaded.Content.ReadAsStringAsync();
        await Assert.That(uploadedBody).DoesNotContain(_api.StorageObjectId.ToString());
        await Assert.That(uploadedBody).DoesNotContain("/api/storageobject/");
        using var completed = JsonDocument.Parse(uploadedBody);
        await Assert.That(completed.RootElement.GetProperty("resourceId").GetGuid()).IsEqualTo(resourceId);
    }

    [Test]
    public async Task PrivateDeliveryStreamsThroughActualSplitProxyAndDisposalCancelsUpstream()
    {
        Guid resourceId = Guid.CreateVersion7();
        var producerStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool revoked = false;
        string seenAuthorization = string.Empty;
        string seenTenant = string.Empty;
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.Run(async context =>
        {
            seenAuthorization = context.Request.Headers.Authorization.ToString();
            seenTenant = context.Request.Headers[EventBffHeaderNames.TenantSlug].ToString();
            if (revoked)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.Headers.CacheControl = "private, no-store";
                return;
            }

            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.ContentDisposition = "attachment; filename*=UTF-8''handout.pdf";
            context.Response.ContentType = "application/pdf";
            try
            {
                await context.Response.Body.WriteAsync(DocumentBytes, context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                producerStopped.TrySetResult();
            }
        });
        await upstream.StartAsync();
        string address = upstream.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        using var factory = new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.UseSetting("ExploreApi:BaseUrl", address));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/eventresource/{resourceId:D}/content");
        AddSession(request, _authHeader);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "browser-forged");
        request.Headers.Add(EventBffHeaderNames.TenantSlug, "browser-forged");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        byte[] received = new byte[DocumentBytes.Length];
        await stream.ReadExactlyAsync(received);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(received).IsEquivalentTo(DocumentBytes);
        await Assert.That(response.Headers.CacheControl!.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl.NoStore).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
        await Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single()).IsEqualTo("nosniff");
        await Assert.That(response.Content.Headers.ContentDisposition!.DispositionType).IsEqualTo("attachment");
        await Assert.That(response.Headers.ETag).IsNull();
        await Assert.That(response.Content.Headers.LastModified).IsNull();
        await Assert.That(seenAuthorization).StartsWith("Bearer ");
        await Assert.That(seenAuthorization).DoesNotContain("browser-forged");
        await Assert.That(seenTenant).DoesNotContain("browser-forged");

        response.Dispose();
        await producerStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        revoked = true;
        using var later = new HttpRequestMessage(HttpMethod.Get, $"/api/eventresource/{resourceId:D}/content");
        AddSession(later, _authHeader);
        using var denied = await client.SendAsync(later);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await denied.Content.ReadAsByteArrayAsync()).IsEmpty();
    }

    [Test]
    public async Task ResourceUploadSessionWithoutCsrfIsRejectedBeforeApiTransport()
    {
        using var factory = CreateCsrfBoundaryFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        using var request = CreateReserveRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), includeCsrf: false);
        AddSession(request, _authHeader);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("Antiforgery validation failed");
        await Assert.That(_api.ReserveCount).IsEqualTo(0);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _api.Dispose();
    }

    private WebApplicationFactory<Program> CreateCsrfBoundaryFactory() =>
        new BlazorBffWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<IEventResourcesClient, EventResourcesClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => _api)));

    private static HttpRequestMessage CreateReserveRequest(
        Guid resourceId,
        Guid expectedVersion,
        bool includeCsrf = true,
        string fileName = "handout.pdf",
        string contentType = "application/pdf")
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/bff/event-resources/{resourceId:D}/upload-session")
        {
            Content = JsonContent.Create(new
            {
                expectedVersion,
                fileName,
                contentType,
                expectedSizeBytes = DocumentBytes.LongLength
            })
        };
        if (includeCsrf)
            request.Headers.Add("X-CSRF-TOKEN", "validated-by-test-antiforgery");
        return request;
    }

    private static HttpRequestMessage CreateUploadRequest(string uploadSessionId,
        string fileName = "handout.pdf", string contentType = "application/pdf")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/storage/upload-proxy");
        request.Headers.Add("X-CSRF-TOKEN", "validated-by-test-antiforgery");
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(uploadSessionId), "uploadSessionId");
        form.Add(new StringContent(contentType), "contentType");
        var file = new ByteArrayContent(DocumentBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        request.Content = form;
        return request;
    }

    private static void AddSession(HttpRequestMessage request, string authHeader) =>
        request.Headers.Add(TestAuthHandler.AuthHeaderName, authHeader);

    private static string CreateForwardableToken() =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            expires: DateTime.UtcNow.AddMinutes(10)));

    private sealed class ResourceApiHandler : HttpMessageHandler
    {
        public Guid ApiUploadSessionId { get; } = Guid.CreateVersion7();
        public Guid StorageObjectId { get; } = Guid.CreateVersion7();
        public Guid? ReservedResourceId { get; private set; }
        public string ReserveBody { get; private set; } = string.Empty;
        public string ReserveAuthorization { get; private set; } = string.Empty;
        public string ReserveTenant { get; private set; } = string.Empty;
        public int ReserveCount { get; private set; }
        public int UploadCount { get; private set; }
        public byte[] UploadBytes { get; private set; } = [];
        public string UploadContentType { get; private set; } = string.Empty;
        private string _reservedContentType = "application/pdf";
        private string _reservedFileName = "handout.pdf";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri?.AbsolutePath is { } path
                && path.StartsWith("/api/eventresource/", StringComparison.Ordinal)
                && path.EndsWith("/upload-sessions", StringComparison.Ordinal))
            {
                ReserveCount++;
                ReservedResourceId = Guid.Parse(path.Split('/')[3]);
                ReserveBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var reserved = JsonDocument.Parse(ReserveBody);
                _reservedContentType = reserved.RootElement.GetProperty("contentType").GetString()!;
                _reservedFileName = reserved.RootElement.GetProperty("safeDisplayName").GetString()!;
                ReserveAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
                ReserveTenant = request.Headers.TryGetValues(EventBffHeaderNames.TenantSlug, out var tenants)
                    ? string.Join(',', tenants)
                    : string.Empty;
                return JsonResponse(SessionJson(ApiUploadSessionId, null, "reserved"));
            }

            if (request.Method == HttpMethod.Put
                && request.RequestUri?.AbsolutePath ==
                    $"/api/storageobject/upload-sessions/{ApiUploadSessionId:D}/content")
            {
                UploadCount++;
                UploadBytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                UploadContentType = request.Content.Headers.ContentType?.MediaType ?? string.Empty;
                return JsonResponse(SessionJson(ApiUploadSessionId, StorageObjectId, "finalized"));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        private string SessionJson(Guid sessionId, Guid? storageObjectId, string status)
        {
            string objectProperty = storageObjectId is { } id
                ? $"\"storageObjectId\":\"{id:D}\","
                : string.Empty;
            return $$"""
                {"id":{"id":"{{sessionId:D}}","tenantId":"{{Guid.CreateVersion7():D}}","provider":"local","expectedSizeBytes":{{DocumentBytes.LongLength}},"reservedBytes":{{DocumentBytes.LongLength}},"contentType":"{{_reservedContentType}}","safeDisplayName":"{{_reservedFileName}}","purpose":"event_resource","visibility":"private_owner","status":"{{status}}",{{objectProperty}}"expiresAt":"{{DateTimeOffset.UtcNow.AddMinutes(10):O}}","maxUploadBytes":10485760,"tenantQuotaBytes":1073741824,"usedBytes":0,"totalReservedBytes":{{DocumentBytes.LongLength}}},"success":true,"message":"ok"}
                """;
        }
    }
}
