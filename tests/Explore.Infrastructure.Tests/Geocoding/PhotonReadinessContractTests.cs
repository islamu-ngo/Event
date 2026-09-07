using System.Net;

namespace Explore.Infrastructure.Tests.Geocoding;

[NotInParallel("PhotonReadinessContract")]
public sealed class PhotonReadinessContractTests
{
    [Test]
    public async Task ProviderNoneIsHealthyWithoutHttpRequest()
    {
        var handler = new PhotonScriptedHttpHandler();
        using var host = GeocodingReadinessContractHost.None(handler);

        ReadinessView result = await host.ProbeAsync();

        await Assert.That(result.Status).IsEqualTo("Healthy");
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task ReachablePhotonUsesOneStatusRequestWithoutQuery()
    {
        string providerRecordCanary = $"record-{Guid.CreateVersion7():N}";
        var handler = new PhotonScriptedHttpHandler(PhotonScriptedHttpHandler.Respond(
            HttpStatusCode.OK,
            $"{{\"status\":\"ready\",\"records\":[\"{providerRecordCanary}\"]}}"));
        Task requestObserved = handler.ExpectCall(1);
        using var host = GeocodingReadinessContractHost.Photon(handler);

        Task<ReadinessView> operation = host.ProbeAsync();
        await requestObserved.WaitAsync(TimeSpan.FromSeconds(2));
        ReadinessView result = await operation.WaitAsync(TimeSpan.FromSeconds(2));
        Uri request = handler.RequestUris.Single();

        await Assert.That(result.Status).IsEqualTo("Healthy");
        await Assert.That(request.AbsolutePath).IsEqualTo("/status");
        await Assert.That(request.Query).IsEmpty();
        await Assert.That(request.AbsolutePath).DoesNotContain("/api");
        await Assert.That(result.ObservableText).DoesNotContain(providerRecordCanary);
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    [Arguments(HttpStatusCode.TooManyRequests)]
    public async Task UnavailableOrLimitedPhotonIsDegradedWithoutSearchOrRetry(
        HttpStatusCode status)
    {
        var handler = new PhotonScriptedHttpHandler(PhotonScriptedHttpHandler.Respond(status));
        Task requestObserved = handler.ExpectCall(1);
        using var host = GeocodingReadinessContractHost.Photon(handler);

        Task<ReadinessView> operation = host.ProbeAsync();
        await requestObserved.WaitAsync(TimeSpan.FromSeconds(2));
        ReadinessView result = await operation.WaitAsync(TimeSpan.FromSeconds(2));
        Uri request = handler.RequestUris.Single();

        await Assert.That(result.Status).IsEqualTo("Degraded");
        await Assert.That(handler.CallCount).IsEqualTo(1);
        await Assert.That(request.AbsolutePath).IsEqualTo("/status");
        await Assert.That(request.Query).IsEmpty();
    }

    [Test]
    public async Task ReadinessOutputContainsNoEndpointQueryTokenAddressOrRecordData()
    {
        string endpointCanary = $"endpoint-{Guid.CreateVersion7():N}.example";
        string queryCanary = $"query-{Guid.CreateVersion7():N}";
        string tokenCanary = $"token-{Guid.CreateVersion7():N}";
        string addressCanary = $"address-{Guid.CreateVersion7():N}";
        string recordCanary = $"record-{Guid.CreateVersion7():N}";
        var handler = new PhotonScriptedHttpHandler(PhotonScriptedHttpHandler.Respond(
            HttpStatusCode.ServiceUnavailable,
            $"{{\"query\":\"{queryCanary}\",\"token\":\"{tokenCanary}\","
            + $"\"address\":\"{addressCanary}\",\"record\":\"{recordCanary}\"}}"));
        Task requestObserved = handler.ExpectCall(1);
        using var host = GeocodingReadinessContractHost.Photon(handler);
        PhotonDeploymentContractHost.SetRequired(
            host.Options,
            new Uri($"https://{endpointCanary}/"),
            "Endpoint",
            "BaseAddress");

        Task<ReadinessView> operation = host.ProbeAsync();
        await requestObserved.WaitAsync(TimeSpan.FromSeconds(2));
        ReadinessView result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        foreach (string canary in new[]
                 {
                     endpointCanary, queryCanary, tokenCanary, addressCanary, recordCanary
                 })
        {
            await Assert.That(result.ObservableText).DoesNotContain(canary);
        }
        foreach (string key in result.DataKeys)
        {
            string normalized = key.Replace("_", string.Empty).ToLowerInvariant();
            await Assert.That(normalized).DoesNotContain("endpoint");
            await Assert.That(normalized).DoesNotContain("query");
            await Assert.That(normalized).DoesNotContain("token");
            await Assert.That(normalized).DoesNotContain("address");
            await Assert.That(normalized).DoesNotContain("record");
        }
    }

    [Test]
    public async Task CallerCancellationStopsSingleStatusRequestAndPropagates()
    {
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PhotonScriptedHttpHandler(
            PhotonScriptedHttpHandler.Stall(cancellationObserved));
        Task requestObserved = handler.ExpectCall(1);
        using var host = GeocodingReadinessContractHost.Photon(handler);
        using var cancellation = new CancellationTokenSource();

        Task<ReadinessView> operation = host.ProbeAsync(cancellation.Token);
        await requestObserved.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(async () => await operation).Throws<OperationCanceledException>();
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task RequestTimeoutDegradesAtConfiguredBoundWithoutRetry()
    {
        const int TimeoutMilliseconds = 750;
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PhotonScriptedHttpHandler(
            PhotonScriptedHttpHandler.Stall(cancellationObserved));
        Task requestObserved = handler.ExpectCall(1);
        using var host = GeocodingReadinessContractHost.Photon(
            handler,
            readinessTimeoutMilliseconds: TimeoutMilliseconds);
        Task timeoutScheduled = host.TimeProvider.ExpectDelay(
            TimeSpan.FromMilliseconds(TimeoutMilliseconds));

        Task<ReadinessView> operation = host.ProbeAsync();
        await requestObserved.WaitAsync(TimeSpan.FromSeconds(2));
        await timeoutScheduled.WaitAsync(TimeSpan.FromSeconds(2));
        host.TimeProvider.Advance(TimeSpan.FromMilliseconds(TimeoutMilliseconds));
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        ReadinessView result = await operation.WaitAsync(TimeSpan.FromSeconds(2));

        await Assert.That(result.Status).IsEqualTo("Degraded");
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }
}
