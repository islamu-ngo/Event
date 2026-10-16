namespace Explore.API.Middleware;

/// <summary>
/// Defers eligible output within a fixed retention budget. Crossing that budget,
/// starting/flushing the response, or discovering ineligible output drains the
/// prefix once and permanently switches to forwarding. It never owns the destination.
/// </summary>
internal sealed class BoundedResponseCaptureStream(
    Stream destination,
    int limit,
    Func<bool> eligible,
    Func<bool> suppressOutput,
    bool deferFlush = false) : Stream
{
    private MemoryStream? _buffer;
    public bool IsCaptured { get; private set; } = true;
    public ReadOnlyMemory<byte> CapturedBody =>
        _buffer is null ? ReadOnlyMemory<byte>.Empty : _buffer.GetBuffer().AsMemory(0, (int)_buffer.Length);

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _buffer?.Length ?? 0;
    public override long Position
    {
        get => Length;
        set => throw new NotSupportedException();
    }

    private bool CanRetain(int count) =>
        IsCaptured && eligible() && count <= limit - Length;

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (CanRetain(buffer.Length))
        {
            if (!buffer.IsEmpty)
            {
                (_buffer ??= new MemoryStream(limit)).Write(buffer);
            }
            return;
        }

        ForwardPrefix();
        if (!suppressOutput())
        {
            destination.Write(buffer);
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CanRetain(buffer.Length))
        {
            if (!buffer.IsEmpty)
            {
                (_buffer ??= new MemoryStream(limit)).Write(buffer.Span);
            }
            return;
        }

        await ForwardPrefixAsync(cancellationToken);
        if (!suppressOutput())
        {
            await destination.WriteAsync(buffer, cancellationToken);
        }
    }

    private void ForwardPrefix()
    {
        IsCaptured = false;
        MemoryStream? prefix = _buffer;
        _buffer = null;
        using (prefix)
        {
            if (prefix is not null && !suppressOutput())
            {
                destination.Write(prefix.GetBuffer().AsSpan(0, (int)prefix.Length));
            }
        }
    }

    public async Task ForwardPrefixAsync(CancellationToken cancellationToken)
    {
        IsCaptured = false;
        MemoryStream? prefix = _buffer;
        _buffer = null;
        using (prefix)
        {
            if (prefix is not null && !suppressOutput())
            {
                await destination.WriteAsync(prefix.GetBuffer().AsMemory(0, (int)prefix.Length), cancellationToken);
            }
        }
    }

    public override void Flush()
    {
        if (deferFlush && IsCaptured && eligible())
        {
            return;
        }
        ForwardPrefix();
        destination.Flush();
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (deferFlush && IsCaptured && eligible())
        {
            return;
        }
        await ForwardPrefixAsync(cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer?.Dispose();
            _buffer = null;
        }
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
