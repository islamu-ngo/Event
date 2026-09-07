using System.Net;

namespace Explore.Infrastructure.Tests.Geocoding;

[NotInParallel("PhotonContract")]
public sealed class PhotonAdapterResilienceContractTests
{
    [Test]
    public async Task SearchAsync_TransientResponses_RetriesAtMostTwiceAfter200And500Milliseconds()
    {
        var handler = new PhotonScriptedHttpHandler(
            PhotonScriptedHttpHandler.Respond(HttpStatusCode.ServiceUnavailable),
            PhotonScriptedHttpHandler.Respond(HttpStatusCode.RequestTimeout),
            PhotonScriptedHttpHandler.Respond(HttpStatusCode.InternalServerError));
        using var host = CreateHost(handler);
        Task firstCall = handler.ExpectCall(1);
        Task secondCall = handler.ExpectCall(2);
        Task thirdCall = handler.ExpectCall(3);
        Task firstDelay = host.TimeProvider.ExpectDelay(TimeSpan.FromMilliseconds(200));
        Task secondDelay = host.TimeProvider.ExpectDelay(TimeSpan.FromMilliseconds(500));

        Task<PhotonSearchOutcome> operation = host.SearchAsync();
        await firstCall.WaitAsync(TimeSpan.FromSeconds(2));
        await firstDelay.WaitAsync(TimeSpan.FromSeconds(2));
        host.TimeProvider.Advance(TimeSpan.FromMilliseconds(199));
        await Assert.That(handler.CallCount).IsEqualTo(1);
        host.TimeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await secondCall.WaitAsync(TimeSpan.FromSeconds(2));
        await secondDelay.WaitAsync(TimeSpan.FromSeconds(2));
        host.TimeProvider.Advance(TimeSpan.FromMilliseconds(499));
        await Assert.That(handler.CallCount).IsEqualTo(2);
        host.TimeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await thirdCall.WaitAsync(TimeSpan.FromSeconds(2));
        PhotonSearchOutcome outcome = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(outcome.Suggestions).IsEmpty();
        await Assert.That(handler.CallCount).IsEqualTo(3);
    }

    [Test]
    public async Task SearchAsync_TransportFailure_RetriesAfterFirstDeterministicDelay()
    {
        var handler = new PhotonScriptedHttpHandler(
            PhotonScriptedHttpHandler.ThrowTransport(),
            PhotonScriptedHttpHandler.Respond(HttpStatusCode.OK));
        using var host = CreateHost(handler);
        Task firstCall = handler.ExpectCall(1);
        Task secondCall = handler.ExpectCall(2);
        Task retryDelay = host.TimeProvider.ExpectDelay(TimeSpan.FromMilliseconds(200));

        Task<PhotonSearchOutcome> operation = host.SearchAsync();
        await firstCall.WaitAsync(TimeSpan.FromSeconds(2));
        await retryDelay.WaitAsync(TimeSpan.FromSeconds(2));
        host.TimeProvider.Advance(TimeSpan.FromMilliseconds(200));
        await secondCall.WaitAsync(TimeSpan.FromSeconds(2));
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(handler.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task SearchAsync_RetryAfterFitsRemainingBudget_UsesBoundedProviderDelay()
    {
        var handler = new PhotonScriptedHttpHandler(
            PhotonScriptedHttpHandler.Respond(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(4)),
            PhotonScriptedHttpHandler.Respond(HttpStatusCode.OK));
        using var host = CreateHost(handler);
        Task firstCall = handler.ExpectCall(1);
        Task secondCall = handler.ExpectCall(2);
        Task retryDelay = host.TimeProvider.ExpectDelay(TimeSpan.FromSeconds(4));

        Task<PhotonSearchOutcome> operation = host.SearchAsync();
        await firstCall.WaitAsync(TimeSpan.FromSeconds(2));
        await retryDelay.WaitAsync(TimeSpan.FromSeconds(2));
        host.TimeProvider.Advance(TimeSpan.FromMilliseconds(3_999));
        await Assert.That(handler.CallCount).IsEqualTo(1);
        host.TimeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await secondCall.WaitAsync(TimeSpan.FromSeconds(2));
        await operation.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(handler.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task SearchAsync_RetryAfterExceedsRemainingBudget_DoesNotRetry()
    {
        var handler = new PhotonScriptedHttpHandler(
            PhotonScriptedHttpHandler.Respond(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(6)));
        using var host = CreateHost(handler);
        Task firstCall = handler.ExpectCall(1);

        Task<PhotonSearchOutcome> operation = host.SearchAsync();
        await firstCall.WaitAsync(TimeSpan.FromSeconds(2));
        PhotonSearchOutcome outcome = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(outcome.Suggestions).IsEmpty();
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_NonRetryableFourHundredResponse_IsTerminal()
    {
        var handler = new PhotonScriptedHttpHandler(
            PhotonScriptedHttpHandler.Respond(HttpStatusCode.UnprocessableEntity));
        using var host = CreateHost(handler);
        Task firstCall = handler.ExpectCall(1);

        Task<PhotonSearchOutcome> operation = host.SearchAsync();
        await firstCall.WaitAsync(TimeSpan.FromSeconds(2));
        PhotonSearchOutcome outcome = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(outcome.Suggestions).IsEmpty();
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_StalledProvider_StopsAtFiveSecondTotalBudgetWithoutRetry()
    {
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PhotonScriptedHttpHandler(PhotonScriptedHttpHandler.Stall(cancellationObserved));
        using var host = CreateHost(handler);
        Task firstCall = handler.ExpectCall(1);
        Task totalBudget = host.TimeProvider.ExpectDelay(TimeSpan.FromSeconds(5));

        Task<PhotonSearchOutcome> operation = host.SearchAsync();
        await firstCall.WaitAsync(TimeSpan.FromSeconds(2));
        await totalBudget.WaitAsync(TimeSpan.FromSeconds(2));
        host.TimeProvider.Advance(TimeSpan.FromSeconds(5));
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        PhotonSearchOutcome outcome = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(outcome.Suggestions).IsEmpty();
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_CallerCancellation_StopsCurrentRequestImmediatelyAndPropagatesCancellation()
    {
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PhotonScriptedHttpHandler(PhotonScriptedHttpHandler.Stall(cancellationObserved));
        using var host = CreateHost(handler);
        Task firstCall = handler.ExpectCall(1);
        using var cancellation = new CancellationTokenSource();

        Task<PhotonSearchOutcome> operation = host.SearchAsync(cancellationToken: cancellation.Token);
        await firstCall.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(async () => await operation).Throws<OperationCanceledException>();
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    private static PhotonAdapterContractHost CreateHost(PhotonScriptedHttpHandler handler) =>
        PhotonAdapterContractHost.Create(
            handler,
            new PhotonManualTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero)),
            new PhotonObservabilityCapture());
}
