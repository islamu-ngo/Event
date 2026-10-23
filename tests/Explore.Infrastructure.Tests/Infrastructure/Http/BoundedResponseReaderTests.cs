using System.Net;
using System.Text;
using Explore.Infrastructure.Http;

namespace Explore.Infrastructure.Tests.Infrastructure.Http;

public sealed class BoundedResponseReaderTests
{
    [Test]
    public async Task ExactByteLimitSucceedsAndUnknownLengthReadsOnlyOneOverflowByte()
    {
        byte[] payload = Encoding.UTF8.GetBytes("""{"value":"abc"}""");
        using var exact = new ByteArrayContent(payload);
        await Assert.That((await BoundedResponseReader.ReadJsonAsync(exact, payload.Length)).Length)
            .IsEqualTo(payload.Length);

        var stream = new ObservedStream(Encoding.UTF8.GetBytes("""{"value":"abcdef"}"""));
        using var overflow = new StreamContent(stream);
        var error = await Assert.ThrowsAsync<ProviderResponseRejectedException>(
            () => BoundedResponseReader.ReadJsonAsync(overflow, payload.Length));
        await Assert.That(error!.Category).IsEqualTo("response_too_large");
        await Assert.That(stream.BytesRead).IsEqualTo(payload.Length + 1);
        await Assert.That(stream.Disposed).IsTrue();
    }

    [Test]
    public async Task DeclaredOversizeRejectsBeforeAcquiringTheBody()
    {
        using var content = new UnreadableContent();
        content.Headers.ContentLength = 17;
        var error = await Assert.ThrowsAsync<ProviderResponseRejectedException>(
            () => BoundedResponseReader.ReadJsonAsync(content, 16));
        await Assert.That(error!.Category).IsEqualTo("response_too_large");
        await Assert.That(content.Acquired).IsFalse();
    }

    [Test]
    public async Task DishonestSmallLengthDoesNotDisableStreamOverflowDetection()
    {
        var stream = new ObservedStream(Encoding.UTF8.GetBytes("""{"unknown":"overflow"}"""));
        using var content = new StreamContent(stream);
        content.Headers.ContentLength = 1;
        await Assert.ThrowsAsync<ProviderResponseRejectedException>(() =>
            BoundedResponseReader.ReadJsonAsync(content, 8));
        await Assert.That(stream.BytesRead).IsEqualTo(9);
        await Assert.That(stream.Disposed).IsTrue();
    }

    [Test]
    [Arguments("""{"unknown":"12345"}""", 32, 256, 4, "response_scalar_limit")]
    [Arguments("[1,2,3]", 32, 2, 256, "response_item_limit")]
    [Arguments("[[[0]]]", 2, 256, 256, "response_depth_limit")]
    [Arguments("{", 32, 256, 256, "response_invalid_json")]
    public async Task UnknownFieldsAndStructureUseTheSameBudget(
        string json, int depth, int items, int scalar, string category)
    {
        using var content = new StringContent(json);
        var error = await Assert.ThrowsAsync<ProviderResponseRejectedException>(() =>
            BoundedResponseReader.ReadJsonAsync(content, 1024, depth, items, scalar));
        await Assert.That(error!.Category).IsEqualTo(category);
        await Assert.That(error.Message.Contains(json, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task EncodedContentIsNotSilentlyExpanded()
    {
        using var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentEncoding.Add("gzip");
        var error = await Assert.ThrowsAsync<ProviderResponseRejectedException>(
            () => BoundedResponseReader.ReadJsonAsync(content, 16));
        await Assert.That(error!.Category).IsEqualTo("response_encoding_denied");
    }

    [Test]
    public async Task CallerCancellationPropagatesAndClosesTheStream()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new ObservedStream(Encoding.UTF8.GetBytes("{}"), cancellation);
        using var content = new StreamContent(stream);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            BoundedResponseReader.ReadJsonAsync(content, 16, cancellationToken: cancellation.Token));
        await Assert.That(stream.Disposed).IsTrue();
    }

    private sealed class ObservedStream(byte[] bytes, CancellationTokenSource? cancellation = null)
        : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellation?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            int count = Read(buffer.Span);
            BytesRead += count;
            return ValueTask.FromResult(count);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class UnreadableContent : HttpContent
    {
        public bool Acquired { get; private set; }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new InvalidOperationException("The rejected body must not be acquired.");
        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            Acquired = true;
            throw new InvalidOperationException("The rejected body must not be acquired.");
        }
    }
}
