using System.Net;
using System.Text;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class GuestRegistrationOrderCapabilityTests
{
    [Test]
    public async Task StartGuestOrder_CapturesCapabilityOnlyFromResponseHeader()
    {
        var handler = new CapturingHandler();
        var client = new GuestRegistrationOrderClient(new HttpClient(handler) { BaseAddress = new Uri("https://event.test/") });

        var result = await client.StartGuestRegistrationOrderWithCapabilityAsync(
            Guid.CreateVersion7(),
            new StartRegistrationOrderRequest());

        await Assert.That(result.HasCapability).IsTrue();
        await Assert.That(handler.HasIdempotencyKey).IsTrue();
        await Assert.That(handler.RequestUri!.Query).DoesNotContain("capability");
        await Assert.That(handler.RequestBody!).DoesNotContain("capability");
    }

    [Test]
    public async Task StartGuestOrder_WithoutCapabilityHeader_FailsClosed()
    {
        var handler = new CapturingHandler { IncludeCapabilityHeader = false };
        var client = new GuestRegistrationOrderClient(new HttpClient(handler) { BaseAddress = new Uri("https://event.test/") });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.StartGuestRegistrationOrderWithCapabilityAsync(
                Guid.CreateVersion7(),
                new StartRegistrationOrderRequest()));
    }

    [Test]
    public async Task ContinueGuestOrder_AddsRequiredIdempotencyKey()
    {
        var handler = new CapturingHandler();
        var client = new GuestRegistrationOrderClient(new HttpClient(handler) { BaseAddress = new Uri("https://event.test/") });

        await client.ContinueGuestRegistrationOrderAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "opaque-capability",
            body: new ContinueRegistrationOrderRequest());

        await Assert.That(handler.HasIdempotencyKey).IsTrue();
    }

    [Test]
    public async Task ApplyGuestPromotion_SendsSingleExplicitIdempotencyKey()
    {
        var handler = new CapturingHandler();
        var client = new GuestRegistrationOrderClient(new HttpClient(handler) { BaseAddress = new Uri("https://event.test/") });

        await client.ApplyGuestRegistrationOrderPromotionAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7().ToString("N"),
            new PromotionCodeRequest { Code = "GUEST10" },
            "opaque-capability");

        await Assert.That(handler.IdempotencyKeyCount).IsEqualTo(1);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }
        public bool HasIdempotencyKey { get; private set; }
        public int IdempotencyKeyCount { get; private set; }
        public bool IncludeCapabilityHeader { get; init; } = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            HasIdempotencyKey = request.Headers.Contains("Idempotency-Key");
            IdempotencyKeyCount = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Count() : 0;
            var isStart = request.RequestUri?.AbsolutePath.EndsWith("/guest", StringComparison.OrdinalIgnoreCase) == true;
            var response = new HttpResponseMessage(isStart ? HttpStatusCode.Created : HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"0196f4d2-4c53-7000-8000-000000000000\",\"success\":true}", Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
            if (isStart && IncludeCapabilityHeader)
            {
                response.Headers.Add("X-Registration-Order-Capability", "opaque-capability");
            }
            return response;
        }
    }
}
