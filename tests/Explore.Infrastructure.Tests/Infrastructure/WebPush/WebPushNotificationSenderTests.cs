using System.Net;
using Explore.Application.Models;
using Explore.Infrastructure.WebPush;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebPush;

namespace Explore.Infrastructure.Tests.Infrastructure.WebPush;

public sealed class WebPushNotificationSenderTests
{
    [Test]
    public async Task SendAsync_WhenProviderAcceptsRequest_ReturnsSuccessAndSendsEncryptedPayload()
    {
        var keys = VapidHelper.GenerateVapidKeys();
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var sender = CreateSender(handler, keys);
        var subscription = TestSubscription.Create("https://push.example.test/accepted");

        var result = await sender.SendAsync(subscription.Request, CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        var request = handler.Requests[0];
        await Assert.That(request.Headers.Authorization?.Scheme).IsEqualTo("WebPush");
        await Assert.That(request.Headers.Contains("Crypto-Key")).IsTrue();
        await Assert.That(request.Headers.GetValues("TTL").Single()).IsEqualTo("21600");
        await Assert.That(request.Headers.GetValues("Topic").Single()).IsEqualTo("event-updates");
        await Assert.That(request.Headers.GetValues("Urgency").Single()).IsEqualTo("normal");
        await Assert.That(request.Content).IsNotNull();
        var body = await request.Content!.ReadAsByteArrayAsync();
        await Assert.That(body.Length).IsGreaterThan(0);
        await Assert.That(System.Text.Encoding.UTF8.GetString(body)).DoesNotContain(subscription.Request.PayloadJson);
    }

    [Test]
    [Arguments(HttpStatusCode.NotFound)]
    [Arguments(HttpStatusCode.Gone)]
    public async Task SendAsync_WhenProviderReportsStaleSubscription_ReturnsCleanupClassification(HttpStatusCode statusCode)
    {
        var keys = VapidHelper.GenerateVapidKeys();
        var sender = CreateSender(new RecordingMessageHandler(_ => new HttpResponseMessage(statusCode)), keys);

        var result = await sender.SendAsync(TestSubscription.Create("https://push.example.test/stale").Request, CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureKind).IsEqualTo(WebPushSendFailureKind.StaleSubscription);
        await Assert.That(result.StatusCode).IsEqualTo((int)statusCode);
    }

    [Test]
    [Arguments(HttpStatusCode.TooManyRequests)]
    [Arguments(HttpStatusCode.InternalServerError)]
    public async Task SendAsync_WhenProviderReportsRetryableFailure_ReturnsRetryableClassification(HttpStatusCode statusCode)
    {
        var keys = VapidHelper.GenerateVapidKeys();
        var sender = CreateSender(new RecordingMessageHandler(_ => new HttpResponseMessage(statusCode)), keys);

        var result = await sender.SendAsync(TestSubscription.Create("https://push.example.test/retry").Request, CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureKind).IsEqualTo(WebPushSendFailureKind.Retryable);
        await Assert.That(result.StatusCode).IsEqualTo((int)statusCode);
    }

    [Test]
    [Arguments(HttpStatusCode.BadRequest)]
    [Arguments(HttpStatusCode.Unauthorized)]
    [Arguments(HttpStatusCode.Forbidden)]
    public async Task SendAsync_WhenProviderReportsPermanentFailure_DoesNotRequestSubscriptionCleanup(HttpStatusCode statusCode)
    {
        var keys = VapidHelper.GenerateVapidKeys();
        var sender = CreateSender(new RecordingMessageHandler(_ => new HttpResponseMessage(statusCode)), keys);

        var result = await sender.SendAsync(TestSubscription.Create("https://push.example.test/permanent").Request, CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureKind).IsEqualTo(WebPushSendFailureKind.PermanentNonRetryable);
        await Assert.That(result.StatusCode).IsEqualTo((int)statusCode);
    }

    [Test]
    public async Task SendAsync_WhenEndpointResolvesToPrivateAddress_BlocksWithoutHttpSend()
    {
        var keys = VapidHelper.GenerateVapidKeys();
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var safetyPolicy = new WebPushEndpointSafetyPolicy((_, _) =>
            Task.FromResult(new[] { IPAddress.Loopback }));
        var sender = CreateSender(handler, keys, safetyPolicy);

        var result = await sender.SendAsync(
            TestSubscription.Create("https://push.example.test/private").Request,
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureKind).IsEqualTo(WebPushSendFailureKind.PermanentNonRetryable);
        await Assert.That(handler.Requests).IsEmpty();
    }

    private static WebPushNotificationSender CreateSender(
        RecordingMessageHandler handler,
        VapidDetails keys,
        WebPushEndpointSafetyPolicy? safetyPolicy = null)
    {
        var client = new HttpClient(handler);
        return new WebPushNotificationSender(
            Options.Create(new WebPushSettings
            {
                VapidSubject = "mailto:ops@example.test",
                VapidPublicKey = keys.PublicKey,
                VapidPrivateKey = keys.PrivateKey
            }),
            client,
            safetyPolicy ?? new WebPushEndpointSafetyPolicy((_, _) =>
                Task.FromResult(new[] { IPAddress.Parse("203.0.113.10") })),
            NullLogger<WebPushNotificationSender>.Instance);
    }

    private sealed class RecordingMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed record TestSubscription(WebPushSendEnvelope Request)
    {
        public static TestSubscription Create(string endpoint)
        {
            var keys = VapidHelper.GenerateVapidKeys();
            return new TestSubscription(new WebPushSendEnvelope(
                endpoint,
                keys.PublicKey,
                "auth-secret",
                "{\"malicious\":\"<script>alert(1)</script>\"}",
                "correlation-1",
                21600,
                "event-updates",
                WebPushUrgency.Normal));
        }
    }
}
