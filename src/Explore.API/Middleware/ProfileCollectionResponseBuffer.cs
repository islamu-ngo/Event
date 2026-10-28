using Microsoft.AspNetCore.Http.Features;

namespace Explore.API.Middleware;

/// <summary>Contains downstream body, pipe, start and completion operations until native commit succeeds.</summary>
public sealed class ProfileCollectionResponseBuffer : IDisposable
{
    private readonly HttpContext _context;
    private readonly IHttpResponseBodyFeature _original;
    private readonly BoundedOutput _buffer = new();
    private readonly StreamResponseBodyFeature _feature;
    private bool _sealed;

    public ProfileCollectionResponseBuffer(HttpContext context)
    {
        _context = context;
        _original = context.Features.Get<IHttpResponseBodyFeature>()
            ?? throw new InvalidOperationException("The response body feature is unavailable.");
        _feature = new StreamResponseBodyFeature(_buffer);
        context.Features.Set<IHttpResponseBodyFeature>(_feature);
    }

    public async Task SealAsync(CancellationToken cancellationToken)
    {
        if (_sealed) return;
        cancellationToken.ThrowIfCancellationRequested();
        await _feature.CompleteAsync();
        cancellationToken.ThrowIfCancellationRequested();
        _sealed = true;
    }

    public async Task DeliverAsync(CancellationToken cancellationToken)
    {
        if (!_sealed) throw new InvalidOperationException("The private profile response must be sealed before commit.");
        cancellationToken.ThrowIfCancellationRequested();
        _context.Features.Set(_original);
        _context.Response.ContentLength = _buffer.Length;
        _buffer.Position = 0;
        await _buffer.CopyToAsync(_original.Stream, cancellationToken);
    }

    public void Dispose()
    {
        _context.Features.Set(_original);
        _buffer.Dispose();
    }

    private sealed class BoundedOutput : MemoryStream
    {
        private void RequireCapacity(int count)
        {
            if (count > 65536 - Position)
                throw new InvalidOperationException("The private profile response exceeds its output limit.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        { RequireCapacity(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { RequireCapacity(buffer.Length); base.Write(buffer); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { RequireCapacity(count); return base.WriteAsync(buffer, offset, count, cancellationToken); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { RequireCapacity(buffer.Length); return base.WriteAsync(buffer, cancellationToken); }
    }
}
