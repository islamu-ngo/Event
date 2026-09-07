using System.Net;
using Explore.Infrastructure.Services.Federation;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoRevocationObserverTests
{
    [Test]
    [Arguments(HttpStatusCode.OK, true)]
    [Arguments(HttpStatusCode.BadRequest, false)]
    [Arguments(HttpStatusCode.ServiceUnavailable, false)]
    public async Task PostRecordsOnlyBoundedStatus(HttpStatusCode statusCode, bool expectedSuccess)
    {
        var observer = new AtprotoRevocationObserver();
        using var invoker = new HttpMessageInvoker(new AtprotoRevocationObserverHandler(
            observer,
            new StubHandler(_ => new HttpResponseMessage(statusCode))));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://issuer.example/oauth/revoke")
        {
            Content = new StringContent("credential-canary")
        };

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        await Assert.That(observer.Attempted).IsTrue();
        await Assert.That(observer.Succeeded).IsEqualTo(expectedSuccess);
    }

    [Test]
    public async Task TransportFailureRecordsFailureAndPreservesTheException()
    {
        var observer = new AtprotoRevocationObserver();
        using var invoker = new HttpMessageInvoker(new AtprotoRevocationObserverHandler(
            observer,
            new StubHandler(_ => throw new HttpRequestException("bounded-test-failure"))));

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "https://issuer.example/oauth/revoke"),
            CancellationToken.None));

        await Assert.That(observer.Attempted).IsTrue();
        await Assert.That(observer.Succeeded).IsFalse();
    }

    [Test]
    public async Task MetadataGetIsNotMisclassifiedAsRevocation()
    {
        var observer = new AtprotoRevocationObserver();
        using var invoker = new HttpMessageInvoker(new AtprotoRevocationObserverHandler(
            observer,
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        using var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://issuer.example/.well-known/oauth-authorization-server"),
            CancellationToken.None);

        await Assert.That(observer.Attempted).IsFalse();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
