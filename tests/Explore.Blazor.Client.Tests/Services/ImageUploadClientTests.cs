using System.Net;
using System.Net.Http.Json;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class ImageUploadClientTests
{
    private readonly System.Net.Http.IHttpClientFactory _httpClientFactory = Substitute.For<System.Net.Http.IHttpClientFactory>();
    private readonly ILogger<ImageUploadClient> _logger = Substitute.For<ILogger<ImageUploadClient>>();

    [Test]
    public async Task GetUploadUrlAsync_WhenBffSessionUnavailable_ReturnsNullWithoutDirectFallback()
    {
        var client = new ImageUploadClient(
            _httpClientFactory,
            _logger,
            apiClientExecutor: new FailingExecutor());

        var result = await client.GetUploadUrlAsync("test.jpg", "image/jpeg", expectedSizeBytes: 3);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetUploadUrlAsync_WhenBffSessionSucceedsOutsideBrowser_ReturnsUploadSession()
    {
        var client = new ImageUploadClient(
            _httpClientFactory,
            _logger,
            apiClientExecutor: new SuccessfulSessionExecutor());

        var result = await client.GetUploadUrlAsync("direct.jpg", "image/jpeg", expectedSizeBytes: 3);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.UploadSessionId).IsEqualTo("session-1");
        await Assert.That(result.UploadUrl).IsEmpty();
        await Assert.That(result.ObjectKey).IsEmpty();
    }

    [Test]
    public async Task UploadViaBffProxyAsync_WithBytes_PostsMultipartToUploadProxy()
    {
        var storageObjectId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new BffStorageUploadProxyResponse
            {
                StorageObjectId = storageObjectId,
                ViewUrl = $"/api/storageobject/{storageObjectId}/public",
                ContentUrl = $"/api/storageobject/{storageObjectId}/content"
            })
        });
        _httpClientFactory.CreateClient("BffClient").Returns(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://bff.test")
        });
        var client = CreateClient();
        var fileData = new FileUploadData(new byte[] { 1, 2, 3 }, "test.jpg", "image/jpeg");

        var result = await client.UploadViaBffProxyAsync("session-1", fileData);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await Assert.That(result.StorageObjectId).IsEqualTo(storageObjectId);
        await Assert.That(result.ViewUrl).IsEqualTo($"/api/storageobject/{storageObjectId}/public");
        await Assert.That(handler.LastRequest?.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.LastRequest?.RequestUri?.AbsolutePath).IsEqualTo("/bff/storage/upload-proxy");
    }

    [Test]
    public async Task UploadViaBffProxyAsync_WhenBffFails_SanitizesMultipartFileNameAndDoesNotLogRawBody()
    {
        var dangerousFileName = @"..\..\secret<script>.png";
        var rawBody = "provider secret body https://upload.example.com/object?signature=abc";
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(rawBody)
        });
        _httpClientFactory.CreateClient("BffClient").Returns(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://bff.test")
        });
        var client = CreateClient();
        var fileData = new FileUploadData(new byte[] { 1, 2, 3 }, dangerousFileName, "image/png");

        var result = await client.UploadViaBffProxyAsync("session-1", fileData);

        await Assert.That(result).IsNull();
        var requestBody = handler.LastRequestBody ?? string.Empty;
        await Assert.That(requestBody).Contains("secret-script.png");
        await Assert.That(requestBody).DoesNotContain(dangerousFileName);
        _logger.DidNotReceive().Log(
            Arg.Any<LogLevel>(),
            Arg.Any<EventId>(),
            Arg.Is<object>(state => LogStateContains(state, dangerousFileName) || LogStateContains(state, rawBody)),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    private ImageUploadClient CreateClient()
    {
        return new ImageUploadClient(_httpClientFactory, _logger, apiClientExecutor: new FailingExecutor());
    }

    private sealed class FailingExecutor : IApiClientExecutor
    {
        public Task<ApiResult<T>> ReadJsonAsync<T>(
            Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
            string serviceName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApiResult<T>.Failure(new InvalidOperationException("BFF unavailable")));
        }

        public Task<ApiResult> SendAsync(
            Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
            string serviceName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApiResult.Failure(new InvalidOperationException("BFF unavailable")));
        }
    }

    private sealed class SuccessfulSessionExecutor : IApiClientExecutor
    {
        public Task<ApiResult<T>> ReadJsonAsync<T>(
            Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
            string serviceName,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(BffStorageUploadSessionResponse))
            {
                var response = new BffStorageUploadSessionResponse
                {
                    UploadSessionId = "session-1",
                    ObjectKey = "images/session.jpg",
                    ViewUrl = "https://cdn.example.com/images/session.jpg",
                    ExpiresInMinutes = 60
                };

                return Task.FromResult(ApiResult<T>.Success((T)(object)response));
            }

            return Task.FromResult(ApiResult<T>.Failure(new InvalidOperationException("Unexpected executor request")));
        }

        public Task<ApiResult> SendAsync(
            Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
            string serviceName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApiResult.Failure(new InvalidOperationException("Unexpected executor request")));
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }

    private static bool LogStateContains(object? state, string value)
    {
        return state.ToString()?.Contains(value, StringComparison.Ordinal) == true;
    }
}
