using Explore.Domain.Services.EventResources;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Services;

/// <summary>
/// Inspects a private, bounded snapshot rather than the mutable caller stream. The caller
/// retains ownership of input, which is consumed from its current position (including at
/// most one overflow sentinel byte). Successful Content is an owned, read-only, seekable
/// replay positioned at zero; the provider must write this stream, then the caller must
/// dispose it. Disposal deletes its temporary backing file. Rejection returns Stream.Null
/// and fixed non-content-bearing errors; all owned resources are disposed on failure,
/// I/O exception or cancellation. Cancellation and I/O failures propagate to the caller.
/// PDF acceptance is signature-only; OOXML acceptance is bounded container validation.
/// Neither establishes a Clean verdict or performs malware scanning.
/// </summary>
public static class EventResourceDocumentInspection
{
    public static async Task<StorageContentInspectionResult> InspectAsync(
        Stream content,
        string contentType,
        string? extension,
        long expectedSizeBytes,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        string requiredExtension = contentType switch
        {
            EventResourceGovernancePolicy.PdfMediaType => "pdf",
            EventResourceGovernancePolicy.WordDocumentMediaType => "docx",
            EventResourceGovernancePolicy.PowerPointPresentationMediaType => "pptx",
            _ => ""
        };
        if (requiredExtension.Length == 0)
            return Reject("Reserved document content type is not supported.");
        if (!string.Equals(extension?.Trim().TrimStart('.'), requiredExtension, StringComparison.OrdinalIgnoreCase))
            return Reject("File extension did not match the reserved content type.");
        if (expectedSizeBytes <= 0 || maximumBytes <= 0 || expectedSizeBytes > maximumBytes)
            return Reject("Reserved document length exceeds the permitted bounds.");

        // Disk use never exceeds the reservation. Memory is one 64 KiB intake buffer;
        // the independent Domain container reader imposes its own fixed metadata budgets.
        string path = Path.Combine(Path.GetTempPath(), $"event-resource-inspection-{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 65_536,
            Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var owner = new OwnedReadOnlyStream(path, options);
        FileStream snapshot = owner.Inner;
        byte[] buffer = new byte[65_536];
        long remaining = expectedSizeBytes;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await content.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), cancellationToken);
            if (read == 0) return Reject("Upload length did not match the reservation.");
            await snapshot.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
        if (await content.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
            return Reject("Upload length did not match the reservation.");
        await snapshot.FlushAsync(cancellationToken);
        snapshot.Position = 0;
        if (!EventResourceDocumentPolicy.Matches(snapshot, contentType, maximumBytes, cancellationToken))
            return Reject("Document signature or container was not accepted.");
        cancellationToken.ThrowIfCancellationRequested();
        snapshot.Position = 0;
        return new StorageContentInspectionResult(Stream.Null, []) { Content = owner.TransferOwnership() };
    }

    private static StorageContentInspectionResult Reject(string error) =>
        StorageContentInspectionResult.Failed(Stream.Null, [error]);

    private sealed class OwnedReadOnlyStream : Stream
    {
        private FileStream? _inner;

        public OwnedReadOnlyStream(string path, FileStreamOptions options) => _inner = new FileStream(path, options);
        private OwnedReadOnlyStream(FileStream inner) => _inner = inner;
        public FileStream Inner => _inner ?? throw new ObjectDisposedException(nameof(OwnedReadOnlyStream));

        // Transfer the existing handle, never reopen a path after validation. The scoped
        // owner can then dispose unconditionally without closing the returned replay.
        public Stream TransferOwnership()
        {
            var replay = new OwnedReadOnlyStream(Inner);
            _inner = null;
            return replay;
        }

        public override bool CanRead => _inner?.CanRead == true;
        public override bool CanSeek => _inner?.CanSeek == true;
        public override bool CanWrite => false;
        public override long Length => Inner.Length;
        public override long Position { get => Inner.Position; set => Inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => Inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Inner.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner?.Dispose();
            _inner = null;
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            if (_inner is not null) await _inner.DisposeAsync();
            _inner = null;
            await base.DisposeAsync();
        }
    }
}
