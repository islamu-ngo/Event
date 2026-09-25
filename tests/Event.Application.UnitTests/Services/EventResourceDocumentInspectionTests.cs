using Explore.Application.Services;
using Explore.Domain.ValueObjects;

namespace Event.Application.UnitTests.Services;

[NotInParallel]
public sealed class EventResourceDocumentInspectionTests : IDisposable
{
    private const string Pdf = EventResourceGovernancePolicy.PdfMediaType;
    private readonly string _temporaryDirectory;
    private readonly Dictionary<string, string?> _previousEnvironment;

    public EventResourceDocumentInspectionTests()
    {
        _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"document-inspection-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temporaryDirectory);
        _previousEnvironment = new[] { "TMPDIR", "TMP", "TEMP" }
            .ToDictionary(name => name, Environment.GetEnvironmentVariable);
        foreach (string name in _previousEnvironment.Keys)
            Environment.SetEnvironmentVariable(name, _temporaryDirectory);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _previousEnvironment)
            Environment.SetEnvironmentVariable(name, value);
        Directory.Delete(_temporaryDirectory, recursive: true);
    }

    [Test]
    public async Task NonSeekableShortReadsProduceExactOwnedReadOnlyReplay()
    {
        byte[] bytes = "%PDF-1.7\nresource"u8.ToArray();
        using var input = new InputStream(bytes);
        var result = await EventResourceDocumentInspection.InspectAsync(input, Pdf, ".pdf", bytes.Length, 1024, default);
        await using var replay = result.Content;
        await Assert.That(result.Success).IsTrue();
        await Assert.That(ReferenceEquals(input, replay)).IsFalse();
        await Assert.That(replay.CanSeek).IsTrue();
        await Assert.That(replay.CanWrite).IsFalse();
        using var copy = new MemoryStream();
        await replay.CopyToAsync(copy);
        await Assert.That(copy.ToArray().SequenceEqual(bytes)).IsTrue();
        replay.Position = 0;
        await Assert.That(replay.ReadByte()).IsEqualTo((int)'%');
        await replay.DisposeAsync();
        await Assert.That(input.Disposed).IsFalse();
    }

    [Test]
    public async Task LargeInputUsesBoundedReadsAndReplayCannotChangeWithCallerBytes()
    {
        byte[] bytes = new byte[262_144];
        "%PDF-1.7"u8.CopyTo(bytes);
        using var input = new InputStream(bytes, chunkSize: 65_536);
        var result = await EventResourceDocumentInspection.InspectAsync(input, Pdf, "pdf", bytes.Length, bytes.Length, default);
        await using var replay = result.Content;
        await Assert.That(result.Success).IsTrue();
        await Assert.That(input.MaximumReadRequest <= 65_536).IsTrue();
        bytes[0] = (byte)'X';
        input.Dispose();
        await Assert.That(replay.ReadByte()).IsEqualTo((int)'%');
        await Assert.That(replay.Length).IsEqualTo(262_144L);
    }

    [Test]
    public async Task AlreadyCanceledInputIsNeitherReadNorDisposed()
    {
        using var input = new InputStream("%PDF-1.7\nabc"u8.ToArray());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => EventResourceDocumentInspection.InspectAsync(input, Pdf, "pdf", 12, 1024, cancellation.Token));
        await Assert.That(input.ReadBytes).IsEqualTo(0);
        await Assert.That(input.Disposed).IsFalse();
    }

    [Test]
    [Arguments(EventResourceGovernancePolicy.WordDocumentMediaType, "docx")]
    [Arguments(EventResourceGovernancePolicy.PowerPointPresentationMediaType, "pptx")]
    public async Task ZipSignatureWithoutInspectablePackageNeverReturnsProviderBytes(string mime, string extension)
    {
        using var input = new InputStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        var result = await EventResourceDocumentInspection.InspectAsync(input, mime, extension, 4, 1024, default);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(ReferenceEquals(result.Content, Stream.Null)).IsTrue();
        await Assert.That(input.Disposed).IsFalse();
    }

    [Test]
    [Arguments("text/html", "html", 12L, 1024L)]
    [Arguments(Pdf, "docx", 12L, 1024L)]
    [Arguments(Pdf, "pdf", 0L, 1024L)]
    [Arguments(Pdf, "pdf", 1025L, 1024L)]
    [Arguments(Pdf, "pdf", 12L, 0L)]
    public async Task InvalidReservationRejectsWithoutReading(string mime, string extension, long expected, long maximum)
    {
        using var input = new InputStream("%PDF-1.7\nabc"u8.ToArray());
        var result = await EventResourceDocumentInspection.InspectAsync(input, mime, extension, expected, maximum, default);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(input.ReadBytes).IsEqualTo(0);
        await Assert.That(input.Disposed).IsFalse();
        await Assert.That(ReferenceEquals(result.Content, Stream.Null)).IsTrue();
    }

    [Test]
    [Arguments(8L)]
    [Arguments(99L)]
    public async Task OverflowAndTruncationNeverReturnProviderBytes(long expected)
    {
        using var input = new InputStream("%PDF-1.7\nabc"u8.ToArray());
        var result = await EventResourceDocumentInspection.InspectAsync(input, Pdf, "pdf", expected, 1024, default);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(ReferenceEquals(result.Content, Stream.Null)).IsTrue();
        await Assert.That(input.Disposed).IsFalse();
        await Assert.That(input.ReadBytes <= expected + 1).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RejectionOrReadFailureDeletesOwnedTemporaryFile(bool throwOnRead)
    {
        var before = TemporaryFiles();
        string? ownedPath = null;
        using var input = new InputStream("not a PDF document"u8.ToArray(), () =>
        {
            ownedPath ??= TemporaryFiles().Except(before).SingleOrDefault();
            if (throwOnRead) throw new IOException("input unavailable");
        });
        if (throwOnRead)
            await Assert.ThrowsAsync<IOException>(() => EventResourceDocumentInspection.InspectAsync(input, Pdf, "pdf", 18, 1024, default));
        else
        {
            var result = await EventResourceDocumentInspection.InspectAsync(input, Pdf, "pdf", 18, 1024, default);
            await Assert.That(result.Success).IsFalse();
        }
        await Assert.That(ownedPath is not null).IsTrue();
        await Assert.That(File.Exists(ownedPath)).IsFalse();
        await Assert.That(input.Disposed).IsFalse();
    }

    [Test]
    public async Task CancellationDuringIntakeDeletesTemporaryFileButNotCallerStream()
    {
        var before = TemporaryFiles();
        string? ownedPath = null;
        using var cancellation = new CancellationTokenSource();
        using var input = new InputStream("%PDF-1.7\nabc"u8.ToArray(), () =>
        {
            ownedPath ??= TemporaryFiles().Except(before).SingleOrDefault();
            cancellation.Cancel();
        });
        await Assert.ThrowsAsync<OperationCanceledException>(() => EventResourceDocumentInspection.InspectAsync(input, Pdf, "pdf", 12, 1024, cancellation.Token));
        await Assert.That(ownedPath is not null).IsTrue();
        await Assert.That(File.Exists(ownedPath)).IsFalse();
        await Assert.That(input.Disposed).IsFalse();
    }

    [Test]
    public async Task SuccessKeepsTemporaryFileAliveUntilResultDisposal()
    {
        var before = TemporaryFiles();
        string? ownedPath = null;
        using var input = new InputStream("%PDF-1.7\nabc"u8.ToArray(), () => ownedPath ??= TemporaryFiles().Except(before).SingleOrDefault());
        var result = await EventResourceDocumentInspection.InspectAsync(input, Pdf, "pdf", 12, 1024, default);
        await using var replay = result.Content;
        await Assert.That(result.Success).IsTrue();
        await Assert.That(ownedPath is not null).IsTrue();
        await Assert.That(File.Exists(ownedPath)).IsTrue();
        await replay.DisposeAsync();
        await Assert.That(File.Exists(ownedPath)).IsFalse();
        await Assert.That(input.Disposed).IsFalse();
    }

    private static string[] TemporaryFiles() => Directory.GetFiles(Path.GetTempPath(), "event-resource-inspection-*.tmp");

    private sealed class InputStream(byte[] bytes, Action? onRead = null, int chunkSize = 3) : Stream
    {
        public int ReadBytes { get; private set; }
        public int MaximumReadRequest { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            MaximumReadRequest = Math.Max(MaximumReadRequest, buffer.Length);
            int count = Math.Min(Math.Min(chunkSize, buffer.Length), bytes.Length - ReadBytes);
            bytes.AsMemory(ReadBytes, count).CopyTo(buffer);
            ReadBytes += count;
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
