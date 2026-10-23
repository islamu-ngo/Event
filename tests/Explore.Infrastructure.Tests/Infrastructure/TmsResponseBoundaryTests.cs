using System.Net;
using Explore.Domain.Common.Localization;
using Explore.Infrastructure.Localization;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class TmsResponseBoundaryTests
{
    [Test]
    public async Task ExactLimitRemainsReadableWithoutContentLength()
    {
        using var body = new MemoryStream(new byte[LocalizationBundlePolicy.MaximumBytes]);
        using var client = CreateClient(body);
        using var response = await client.GetAsync("https://tms.invalid/");
        await Assert.That((await response.Content.ReadAsByteArrayAsync()).Length)
            .IsEqualTo(LocalizationBundlePolicy.MaximumBytes);
    }

    [Test]
    public async Task OversizedUnannouncedBodyIsRejectedAndDisposed()
    {
        var body = new MemoryStream(new byte[LocalizationBundlePolicy.MaximumBytes + 1]);
        using var client = CreateClient(body);
        await Assert.That(async () => await client.GetAsync("https://tms.invalid/"))
            .Throws<HttpRequestException>();
        await Assert.That(body.CanRead).IsFalse();
    }

    [Test]
    public async Task ErrorResponseBodiesAreAlsoBounded()
    {
        var body = new MemoryStream(new byte[LocalizationBundlePolicy.MaximumBytes + 1]);
        using var client = CreateClient(body, HttpStatusCode.TooManyRequests);
        await Assert.That(async () => await client.GetAsync("https://tms.invalid/"))
            .Throws<HttpRequestException>();
        await Assert.That(body.CanRead).IsFalse();
    }

    private static HttpClient CreateClient(Stream body, HttpStatusCode status = HttpStatusCode.OK)
        => new(new TmsResponseBoundaryHandler { InnerHandler = new Transport(body, status) });

    private sealed class Transport(Stream body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new UnannouncedContent(body);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    private sealed class UnannouncedContent(Stream body) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => body.CopyToAsync(stream);

        protected override Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken)
            => body.CopyToAsync(stream, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing) body.Dispose();
            base.Dispose(disposing);
        }
    }
}
