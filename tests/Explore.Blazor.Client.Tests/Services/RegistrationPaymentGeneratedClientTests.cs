using System.Net;
using System.Reflection;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class RegistrationPaymentGeneratedClientTests
{
    [Test]
    public async Task AuthenticatedPaymentMutationsSendGeneratedIdempotencyHeader()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var authClient = new AuthenticatedRegistrationOrderPaymentClient(httpClient);
        var guestClient = new GuestRegistrationOrderPaymentClient(httpClient);
        Guid eventId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();

        await authClient.StartAuthenticatedRegistrationPaymentAsync(
            eventId, orderId, idempotency_Key: "start-key", cancellationToken: CancellationToken.None);
        await authClient.RetryAuthenticatedRegistrationPaymentAsync(
            eventId, orderId, idempotency_Key: "retry-key", cancellationToken: CancellationToken.None);
        await guestClient.StartGuestRegistrationPaymentAsync(
            eventId, orderId, "guest-start-key", "guest-capability", cancellationToken: CancellationToken.None);
        await guestClient.RetryGuestRegistrationPaymentAsync(
            eventId, orderId, "guest-retry-key", "guest-capability", cancellationToken: CancellationToken.None);

        await Assert.That(handler.Requests.Count).IsEqualTo(4);
        await Assert.That(handler.Requests[0].Headers.GetValues("Idempotency-Key").Single()).IsEqualTo("start-key");
        await Assert.That(handler.Requests[1].Headers.GetValues("Idempotency-Key").Single()).IsEqualTo("retry-key");
        await Assert.That(handler.Requests[2].Headers.GetValues("Idempotency-Key").Single()).IsEqualTo("guest-start-key");
        await Assert.That(handler.Requests[3].Headers.GetValues("Idempotency-Key").Single()).IsEqualTo("guest-retry-key");

        foreach (var (interfaceType, methodName) in new (Type, string)[]
                 {
                     (typeof(IAuthenticatedRegistrationOrderPaymentClient), nameof(IAuthenticatedRegistrationOrderPaymentClient.StartAuthenticatedRegistrationPaymentAsync)),
                     (typeof(IAuthenticatedRegistrationOrderPaymentClient), nameof(IAuthenticatedRegistrationOrderPaymentClient.RetryAuthenticatedRegistrationPaymentAsync)),
                     (typeof(IGuestRegistrationOrderPaymentClient), nameof(IGuestRegistrationOrderPaymentClient.StartGuestRegistrationPaymentAsync)),
                     (typeof(IGuestRegistrationOrderPaymentClient), nameof(IGuestRegistrationOrderPaymentClient.RetryGuestRegistrationPaymentAsync))
                 })
        {
            ParameterInfo parameter = interfaceType.GetMethods().Single(method => method.Name == methodName)
                .GetParameters().Single(candidate => candidate.Name == "idempotency_Key");
            await Assert.That(parameter.HasDefaultValue).IsFalse();
            await Assert.That(new NullabilityInfoContext().Create(parameter).ReadState).IsEqualTo(NullabilityState.NotNull);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
