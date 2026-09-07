using System.Net;
using System.Text;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Tests.Services.Http;

public sealed class ApiClientExecutorTests
{
    private readonly ApiClientExecutor _executor = new();

    [Test]
    public async Task ReadJsonAsync_WithSuccessResponse_ReturnsValue()
    {
        using var response = JsonResponse(HttpStatusCode.OK, """{"name":"Aisha","count":3}""");

        var result = await _executor.ReadJsonAsync<SampleDto>(_ => Task.FromResult(response), "sample service");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsNotNull();
        await Assert.That(result.Value!.Name).IsEqualTo("Aisha");
        await Assert.That(result.Value.Count).IsEqualTo(3);
        await Assert.That(result.Problem).IsNull();
        await Assert.That(result.Exception).IsNull();
    }

    [Test]
    public async Task ReadJsonAsync_WithProblemDetailsResponse_ReturnsProblemFailure()
    {
        using var response = JsonResponse(
            HttpStatusCode.BadRequest,
            """{"status":400,"title":"Invalid request","detail":"Name is required","traceId":"trace-123"}""");

        var result = await _executor.ReadJsonAsync<SampleDto>(_ => Task.FromResult(response), "sample service");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(result.Problem).IsNotNull();
        await Assert.That(result.Problem!.Title).IsEqualTo("Invalid request");
        await Assert.That(result.Problem.Detail).IsEqualTo("Name is required");
        await Assert.That(result.Problem.TraceId).IsEqualTo("trace-123");
        await Assert.That(result.Value).IsNull();
    }

    [Test]
    public async Task ReadJsonAsync_WithSendException_ReturnsExceptionFailure()
    {
        var exception = new HttpRequestException("network unavailable");

        var result = await _executor.ReadJsonAsync<SampleDto>(_ => Task.FromException<HttpResponseMessage>(exception), "sample service");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Exception).IsSameReferenceAs(exception);
        await Assert.That(result.ErrorMessage).IsEqualTo("network unavailable");
        await Assert.That(result.Problem).IsNull();
    }

    [Test]
    public async Task ReadJsonAsync_WithCancellation_PropagatesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () =>
                await _executor.ReadJsonAsync<SampleDto>(
                    token => Task.FromCanceled<HttpResponseMessage>(token),
                    "sample service",
                    cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task SendAsync_WithNoContentSuccess_ReturnsSuccess()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);

        var result = await _executor.SendAsync(_ => Task.FromResult(response), "sample command");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Problem).IsNull();
        await Assert.That(result.Exception).IsNull();
    }

    [Test]
    public async Task SendAsync_WithProblemDetailsResponse_ReturnsProblemFailure()
    {
        using var response = JsonResponse(
            HttpStatusCode.Conflict,
            """{"status":409,"title":"Conflict","detail":"Already exists"}"""
        );

        var result = await _executor.SendAsync(_ => Task.FromResult(response), "sample command");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(result.Problem).IsNotNull();
        await Assert.That(result.Problem!.Title).IsEqualTo("Conflict");
        await Assert.That(result.Problem.Detail).IsEqualTo("Already exists");
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed record SampleDto(string Name, int Count);
}
