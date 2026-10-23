using System.Net;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    private sealed class UnavailableInstanceKeyPolicyTransport : HttpMessageHandler
    {
        private int requests;
        internal int Requests => Volatile.Read(ref requests);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Version = new Version(2, 0),
                Content = new ByteArrayContent([])
            });
        }
    }
}
