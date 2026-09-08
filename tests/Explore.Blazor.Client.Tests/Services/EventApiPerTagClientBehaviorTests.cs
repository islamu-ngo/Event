// ABOUTME: Verifies operation-specific transport hooks on representative per-tag NSwag clients.
// ABOUTME: Covers explicit idempotency context and one-time guest capability response capture.

using System.Net;
using System.Text;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class EventApiPerTagClientBehaviorTests
{
    [Test]
    public async Task EventLifecycleClientCreateWrapperSendsTheCallerIdempotencyKey()
    {
        var terminal = new CapturingHandler(_ => JsonResponse(
            HttpStatusCode.Created,
            """{"success":true,"id":"00000000-0000-0000-0000-000000000001"}"""));
        using var httpClient = CreateClient(terminal);
        var client = new EventLifecycleClient(httpClient);

        await client.CreateEventWithIdempotencyKeyAsync(
            new CreateEventDraftRequestDto(),
            "create-event-key");

        await Assert.That(terminal.Request!.Headers.GetValues("Idempotency-Key").Single())
            .IsEqualTo("create-event-key");
    }

    [Test]
    public async Task GuestOrderClientWrapperCapturesCapabilityWithoutExposingItInTheDto()
    {
        var terminal = new CapturingHandler(_ =>
        {
            var response = JsonResponse(HttpStatusCode.Created, "{}");
            response.Headers.Add("X-Registration-Order-Capability", "opaque-capability");
            return response;
        });
        using var httpClient = CreateClient(terminal);
        var client = new GuestRegistrationOrderClient(httpClient);

        var result = await client.StartGuestRegistrationOrderWithCapabilityAsync(
            Guid.CreateVersion7(),
            new StartRegistrationOrderRequest(),
            Guid.CreateVersion7().ToString("N"), Guid.NewGuid().ToString("N"), "0000000000000000");

        await Assert.That(result.HasCapability).IsTrue();
        await Assert.That(terminal.Request!.Headers.Contains("Idempotency-Key")).IsTrue();
    }

    private static HttpClient CreateClient(HttpMessageHandler terminal) =>
        new(new EventApiBehaviorMessageHandler { InnerHandler = terminal })
        {
            BaseAddress = new Uri("https://example.test/")
        };

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(responseFactory(request));
        }
    }
}
