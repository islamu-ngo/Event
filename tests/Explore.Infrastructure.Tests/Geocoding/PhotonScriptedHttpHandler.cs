using System.Collections.Concurrent;

namespace Explore.Infrastructure.Tests.Geocoding;

internal sealed class PhotonScriptedHttpHandler(
    params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps)
    : HttpMessageHandler
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _expectedCalls = new();
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _steps = new(steps);
    private readonly Lock _lock = new();
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public IReadOnlyList<Uri> RequestUris { get; } = new List<Uri>();

    public Task ExpectCall(int callNumber)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_expectedCalls.TryAdd(callNumber, signal))
        {
            throw new InvalidOperationException($"Call {callNumber} already has an observer.");
        }

        return signal.Task;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step;
        int callNumber = Interlocked.Increment(ref _callCount);
        lock (_lock)
        {
            ((List<Uri>)RequestUris).Add(request.RequestUri
                ?? throw new InvalidOperationException("Photon request URI was absent."));
            step = _steps.Count > 0
                ? _steps.Dequeue()
                : throw new InvalidOperationException("Photon adapter made an unexpected HTTP call.");
        }

        Task<HttpResponseMessage> response = step(request, cancellationToken);
        if (_expectedCalls.TryRemove(callNumber, out TaskCompletionSource? signal))
        {
            signal.TrySetResult();
        }

        return response;
    }

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(
        System.Net.HttpStatusCode statusCode,
        string content = "{\"type\":\"FeatureCollection\",\"features\":[]}",
        TimeSpan? retryAfter = null)
    {
        return (_, _) =>
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/geo+json")
            };
            if (retryAfter is { } delay)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
            }

            return Task.FromResult(response);
        };
    }

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> ThrowTransport() =>
        (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("transport failure"));

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Stall(
        TaskCompletionSource cancellationObserved)
    {
        return async (_, cancellationToken) =>
        {
            var never = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                return await never.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                throw;
            }
        };
    }
}
