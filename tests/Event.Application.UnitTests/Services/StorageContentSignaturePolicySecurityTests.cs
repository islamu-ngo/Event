using System.Text;
using Explore.Application.Services;
using Explore.Domain;

namespace Event.Application.UnitTests.Services;

public sealed class StorageContentSignaturePolicySecurityTests
{
    // Generated with ImageMagick 7.1.2-27; `file` confirms progressive JPEG and animated WebP,
    // and `magick identify` confirms the WebP contains two independently decoded 2x2 frames.
    private static readonly byte[] ValidJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAgAAAQABAAD//gAQTGF2YzYyLjI4LjEwMgD/2wBDAAgEBAQEBAUFBQUFBQYGBgYGBgYGBgYGBgYHBwcICAgHBwcGBgcHCAgICAkJCQgICAgJCQoKCgwMCwsODg4RERT/xABMAAEBAAAAAAAAAAAAAAAAAAAABgEBAQAAAAAAAAAAAAAAAAAABgcQAQAAAAAAAAAAAAAAAAAAAAARAQAAAAAAAAAAAAAAAAAAAAD/wAARCAACAAIDASIAAhEAAxEA/9oADAMBAAIRAxEAPwCLAE1/f//Z");
    private static readonly byte[] ProgressiveJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wgARCAACAAIDAREAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAAB//EABUBAQEAAAAAAAAAAAAAAAAAAAYI/9oADAMBAAIQAxAAAAE5C1T/AP/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAQUCf//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQMBAT8Bf//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQIBAT8Bf//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEABj8Cf//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAT8hf//aAAwDAQACAAMAAAAQ/wD/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/EH//xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/EH//xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/EH//2Q==");
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAEElEQVR4nGP8ywACLGCSAQANEQED1LYyQAAAAABJRU5ErkJggg==");
    private static readonly byte[] ValidGif = Convert.FromBase64String(
        "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
    private static readonly byte[] ValidWebp = Convert.FromBase64String(
        "UklGRhwAAABXRUJQVlA4TA8AAAAvAUAAAAcQ9Y/+ByKi/wEA");
    private static readonly byte[] ValidAnimatedWebp = Convert.FromBase64String(
        "UklGRsAAAABXRUJQVlA4WAoAAAACAAAAAQAAAQAAQU5JTQYAAAD/////AABBTk1GSAAAAAAAAAAAAAEAAAEAAGQAAAJWUDggMAAAANABAJ0BKgIAAgACADQloAJ0ugH4AAOwAP7wxAv/ILlhdcjX/yA/5Af8gP/48gAAAEFOTUZEAAAAAAAAAAAAAQAAAQAAZAAAAFZQOCAsAAAAlAEAnQEqAgACAAAANCWgAnS6AAOYAP75k2//kB//kB//kB//ID/iF3sgMAA=");
    private static readonly byte[] ValidAvif = Convert.FromBase64String(
        "AAAAIGZ0eXBhdmlmAAAAAGF2aWZtaWYxbWlhZk1BMUIAAAD5bWV0YQAAAAAAAAAvaGRscgAAAAAAAAAAcGljdAAAAAAAAAAAAAAAAFBpY3R1cmVIYW5kbGVyAAAAAA5waXRtAAAAAAABAAAAHmlsb2MAAAAARAAAAQABAAAAAQAAASEAAAAbAAAAKGlpbmYAAAAAAAEAAAAaaW5mZQIAAAAAAQAAYXYwMUNvbG9yAAAAAGppcHJwAAAAS2lwY28AAAAUaXNwZQAAAAAAAAACAAAAAgAAABBwaXhpAAAAAAMICAgAAAAMYXYxQ4EADAAAAAATY29scm5jbHgAAgACAAIAAAAAF2lwbWEAAAAAAAAAAQABBAECgwQAAAAjbWRhdAoFGAA2wCAyEhgAAABQAABAA1Lt5xf080WmIA==");

    [Test]
    [Arguments("image/jpeg", "jpg")]
    [Arguments("image/png", "png")]
    [Arguments("image/gif", "gif")]
    [Arguments("image/webp", "webp")]
    [Arguments("image/avif", "avif")]
    public async Task InspectAsync_SafeRasterWithActiveTail_FailsClosed(string mimeType, string extension)
    {
        byte[] bytes =
        [
            .. RasterBytes(mimeType),
            .. Encoding.UTF8.GetBytes("<svg><script>alert(1)</script></svg>")
        ];

        StorageContentInspectionResult result = await StorageContentSignaturePolicy.InspectAsync(
            new MemoryStream(bytes),
            mimeType,
            extension,
            bytes.Length,
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    [Arguments("image/jpeg", "jpg", "image/png")]
    [Arguments("image/png", "png", "image/gif")]
    [Arguments("image/gif", "gif", "image/webp")]
    [Arguments("image/webp", "webp", "image/avif")]
    [Arguments("image/avif", "avif", "image/svg+xml")]
    public async Task InspectAsync_MimeSpoofedContainer_FailsClosed(
        string declaredMimeType,
        string declaredExtension,
        string actualMimeType)
    {
        byte[] bytes = RasterBytes(actualMimeType);

        StorageContentInspectionResult result = await StorageContentSignaturePolicy.InspectAsync(
            new MemoryStream(bytes),
            declaredMimeType,
            declaredExtension,
            bytes.Length,
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task InspectAsync_TruncatedPng_FailsClosed()
    {
        byte[] bytes = ValidPng[..8];

        StorageContentInspectionResult result = await StorageContentSignaturePolicy.InspectAsync(
            new MemoryStream(bytes),
            "image/png",
            "png",
            bytes.Length,
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task InspectAsync_ParameterizedImageMime_FailsClosed()
    {
        StorageContentInspectionResult result = await StorageContentSignaturePolicy.InspectAsync(
            new MemoryStream(ValidPng),
            "image/png; charset=binary",
            "png",
            ValidPng.Length,
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task SafeRasterPolicy_AcceptsFiveExactMimeExtensionAndContainerPairs()
    {
        (string Mime, string Extension, byte[] Bytes)[] fixtures =
        [
            ("image/jpeg", ".jpeg", ValidJpeg),
            ("image/png", "png", ValidPng),
            ("image/gif", "gif", ValidGif),
            ("image/webp", "webp", ValidWebp),
            ("image/avif", "avif", ValidAvif)
        ];

        foreach ((string mime, string extension, byte[] bytes) in fixtures)
        {
            await Assert.That(SafeRasterContentPolicy.TryNormalizeMimeType(mime, out string? normalized)).IsTrue();
            await Assert.That(normalized).IsEqualTo(mime);
            await Assert.That(SafeRasterContentPolicy.MatchesExtension(mime, extension)).IsTrue();
            await Assert.That(SafeRasterContentPolicy.MatchesContainer(bytes, mime)).IsTrue();
        }
    }

    [Test]
    public async Task SafeRasterPolicy_RejectsParameterizedMalformedAndActiveMimeTypes()
    {
        string?[] rejected =
        [
            null,
            "",
            "image/*",
            "image/svg+xml",
            "image/bmp",
            "text/html",
            "image/png; charset=binary",
            "image/png, image/jpeg"
        ];

        foreach (string? mime in rejected)
        {
            await Assert.That(SafeRasterContentPolicy.TryNormalizeMimeType(mime, out _)).IsFalse();
        }
    }

    [Test]
    public async Task SafeRasterPolicy_RejectsMismatchedTruncatedAndTrailingContainers()
    {
        await Assert.That(SafeRasterContentPolicy.MatchesContainer(ValidPng, "image/jpeg")).IsFalse();
        await Assert.That(SafeRasterContentPolicy.MatchesContainer(ValidJpeg[..^1], "image/jpeg")).IsFalse();
        await Assert.That(SafeRasterContentPolicy.MatchesContainer(ValidPng[..8], "image/png")).IsFalse();
        await Assert.That(SafeRasterContentPolicy.MatchesContainer([.. ValidGif, 0x00], "image/gif")).IsFalse();
        await Assert.That(SafeRasterContentPolicy.MatchesContainer([.. ValidWebp, 0x00], "image/webp")).IsFalse();
        await Assert.That(SafeRasterContentPolicy.MatchesContainer(ValidAvif[..^1], "image/avif")).IsFalse();
    }

    [Test]
    public async Task SafeRasterPolicy_AcceptsStructurallyFramedAnimatedWebp()
    {
        await Assert.That(
            SafeRasterContentPolicy.MatchesContainer(ValidAnimatedWebp, "image/webp")).IsTrue();
    }

    [Test]
    public async Task SafeRasterPolicy_AcceptsProgressiveJpeg()
    {
        await Assert.That(
            SafeRasterContentPolicy.MatchesContainer(ProgressiveJpeg, "image/jpeg")).IsTrue();
    }

    [Test]
    public async Task SafeRasterPolicy_RequiresSafePublicImageMetadataAndTenant()
    {
        Guid tenantId = Guid.CreateVersion7();
        StorageObject storageObject = new()
        {
            FileType = null!,
            Uri = "",
            Provider = StorageProviders.Local,
            FullName = "image.png",
            SafeDisplayName = "image.png",
            Extension = "png",
            ContentType = "image/png",
            Visibility = StorageObjectVisibilities.PublicImage,
            Purpose = StorageObjectPurposes.EventImage,
            LifecycleState = StorageObjectLifecycleStates.Active,
            TenantId = tenantId,
            Tenant = null!
        };

        await Assert.That(SafeRasterContentPolicy.IsSafePublicImageMetadata(storageObject)).IsTrue();
        await Assert.That(SafeRasterContentPolicy.IsEligibleImageReference(storageObject, tenantId)).IsTrue();
        await Assert.That(SafeRasterContentPolicy.IsEligibleImageReference(storageObject, Guid.CreateVersion7())).IsFalse();

        storageObject.Extension = "svg";
        await Assert.That(SafeRasterContentPolicy.IsSafePublicImageMetadata(storageObject)).IsFalse();
    }

    [Test]
    public async Task InspectAsync_NonRasterNonSeekableContent_ReplaysOnlyInspectedPrefix()
    {
        byte[] bytes = "%PDF-1.7\nbody"u8.ToArray();

        StorageContentInspectionResult result = await StorageContentSignaturePolicy.InspectAsync(
            new NonSeekableReadStream(bytes),
            "application/pdf",
            "pdf",
            bytes.Length,
            CancellationToken.None);
        using var captured = new MemoryStream();
        await result.Content.CopyToAsync(captured);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(captured.ToArray()).IsEquivalentTo(bytes);
    }

    private static byte[] RasterBytes(string mimeType) =>
        mimeType switch
        {
            "image/jpeg" => ValidJpeg,
            "image/png" => ValidPng,
            "image/gif" => ValidGif,
            "image/webp" => ValidWebp,
            "image/avif" => ValidAvif,
            "image/svg+xml" => "<svg><script>alert(1)</script></svg>"u8.ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(mimeType))
        };

    private sealed class NonSeekableReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override void Flush()
        {
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
