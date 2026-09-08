using System.Buffers.Binary;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Services.OpenGraph;
using Explore.Application.Features.Events.OpenGraph;
using Explore.Application.Features.Events.Requests.Queries;
using SkiaSharp;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Fast)]
public sealed class SkiaEventOpenGraphImageRendererTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private const int CanvasWidth = 1200;
    private const int CanvasHeight = 630;
    private const int ArtworkLeft = 745;
    private const int ArtworkTop = 35;
    private const int ArtworkWidth = 420;
    private const int ArtworkHeight = 560;

    private readonly SkiaEventOpenGraphImageRenderer _renderer = new();

    [Test]
    public async Task RenderAsyncProducesStandardPngAndStrongSha256Etag()
    {
        var result = await RenderAsync(title: "Community Iftar");

        await Assert.That(result.PngBytes.Span[..8].SequenceEqual(PngSignature)).IsTrue();
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.PngBytes.Span.Slice(16, 4))).IsEqualTo(CanvasWidth);
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.PngBytes.Span.Slice(20, 4))).IsEqualTo(CanvasHeight);

        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(result.PngBytes.Span));
        await Assert.That(result.ETag).IsEqualTo($"\"{expectedHash}\"");
        await Assert.That(result.ETag.StartsWith("W/", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    [Test]
    public async Task RenderAsyncIsDeterministicForIdenticalInput()
    {
        var first = await RenderAsync(title: "Deterministic Event");
        var second = await RenderAsync(title: "Deterministic Event");

        await Assert.That(second.PngBytes.ToArray()).IsEquivalentTo(
            first.PngBytes.ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(second.ETag).IsEqualTo(first.ETag);
    }

    [Test]
    public async Task FallbackGradientDependsOnTrimmedTitleAndBlankUsesEventSeed()
    {
        var alpha = await RenderAsync(title: "  Alpha Gathering  ");
        var trimmedAlpha = await RenderAsync(title: "Alpha Gathering");
        var beta = await RenderAsync(title: "Beta Gathering");
        var blank = await RenderAsync(title: "   ");
        var eventSeed = await RenderAsync(title: "event");

        await Assert.That(HashArtworkPanel(alpha.PngBytes)).IsEqualTo(HashArtworkPanel(trimmedAlpha.PngBytes));
        await Assert.That(HashArtworkPanel(alpha.PngBytes)).IsNotEqualTo(HashArtworkPanel(beta.PngBytes));
        await Assert.That(HashArtworkPanel(blank.PngBytes)).IsEqualTo(HashArtworkPanel(eventSeed.PngBytes));
    }

    [Test]
    public async Task MissingInvalidUnsupportedAndOversizedArtworkUseSameFallback()
    {
        var fallback = await RenderAsync(title: "Fallback Event");
        using var invalid = new MemoryStream("not an image"u8.ToArray());
        var invalidResult = await RenderAsync("Fallback Event", invalid, "image/png");
        using var validPng = new MemoryStream(CreateSolidPng(40, 40, SKColors.Red));
        var unsupportedResult = await RenderAsync("Fallback Event", validPng, "image/svg+xml");
        using var oversized = new MemoryStream(new byte[(5 * 1024 * 1024) + 1]);
        var oversizedResult = await RenderAsync("Fallback Event", oversized, "image/png");

        var expectedPanel = HashArtworkPanel(fallback.PngBytes);
        await Assert.That(HashArtworkPanel(invalidResult.PngBytes)).IsEqualTo(expectedPanel);
        await Assert.That(HashArtworkPanel(unsupportedResult.PngBytes)).IsEqualTo(expectedPanel);
        await Assert.That(HashArtworkPanel(oversizedResult.PngBytes)).IsEqualTo(expectedPanel);
    }

    [Test]
    public async Task NonSeekableOversizedArtwork_UsesSameGradientFallbackWithoutDecoding()
    {
        const int maximumEncodedArtworkBytes = 5 * 1024 * 1024;
        var title = "Non-Seekable Oversized Event";
        var fallback = await RenderAsync(title);
        var validPng = CreateSolidPng(40, 40, SKColors.Red);
        var oversizedEncodedData = new byte[maximumEncodedArtworkBytes + 1];
        validPng.CopyTo(oversizedEncodedData, 0);

        using (var decodeStream = new NonSeekableReadStream(oversizedEncodedData))
        {
            using var decoded = await SkiaEventOpenGraphImageRenderer.TryDecodeArtworkAsync(
                decodeStream,
                "image/png",
                CancellationToken.None);

            await Assert.That(decoded).IsNull();
        }

        using var artwork = new NonSeekableReadStream(oversizedEncodedData);
        var result = await RenderAsync(title, artwork, "image/png");

        await Assert.That(HashArtworkPanel(result.PngBytes)).IsEqualTo(HashArtworkPanel(fallback.PngBytes));
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.PngBytes.Span.Slice(16, 4))).IsEqualTo(CanvasWidth);
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.PngBytes.Span.Slice(20, 4))).IsEqualTo(CanvasHeight);
    }

    [Test]
    public async Task TryDecodeArtworkAsync_LargeJpeg_UsesCodecScaleWithinArtworkPixelCeiling()
    {
        var jpegBytes = CreateStripedJpeg(
            width: 2000,
            height: 1500,
            vertical: true,
            firstBoundary: 500,
            secondBoundary: 1500);
        using var artwork = new MemoryStream(jpegBytes);

        using var decoded = await SkiaEventOpenGraphImageRenderer.TryDecodeArtworkAsync(
            artwork,
            "image/jpeg",
            CancellationToken.None);

        await Assert.That(decoded).IsNotNull();
        await Assert.That(decoded!.Width).IsGreaterThanOrEqualTo(ArtworkWidth);
        await Assert.That(decoded.Height).IsGreaterThanOrEqualTo(ArtworkHeight);
        await Assert.That(((long)decoded.Width * decoded.Height)).IsLessThanOrEqualTo(4L * ArtworkWidth * ArtworkHeight);

        using var renderArtwork = new MemoryStream(jpegBytes);
        var result = await RenderAsync("Large JPEG Event", renderArtwork, "image/jpeg");

        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.PngBytes.Span.Slice(16, 4))).IsEqualTo(CanvasWidth);
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.PngBytes.Span.Slice(20, 4))).IsEqualTo(CanvasHeight);
    }

    [Test]
    public async Task TryDecodeArtworkAsync_LargePng_UsesGradientFallbackWhenCodecCannotScale()
    {
        var fallback = await RenderAsync(title: "Large PNG Event");
        using var artwork = new MemoryStream(CreateSolidPng(4000, 3000, SKColors.Red));

        using var decoded = await SkiaEventOpenGraphImageRenderer.TryDecodeArtworkAsync(
            artwork,
            "image/png",
            CancellationToken.None);

        await Assert.That(decoded).IsNull();

        using var renderArtwork = new MemoryStream(CreateSolidPng(4000, 3000, SKColors.Red));
        var result = await RenderAsync("Large PNG Event", renderArtwork, "image/png");

        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.PngBytes.Span.Slice(16, 4))).IsEqualTo(CanvasWidth);
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(result.PngBytes.Span.Slice(20, 4))).IsEqualTo(CanvasHeight);
        await Assert.That(HashArtworkPanel(result.PngBytes)).IsEqualTo(HashArtworkPanel(fallback.PngBytes));
    }

    [Test]
    public async Task PortraitArtworkUsesCenteredCoverCrop()
    {
        using var artwork = new MemoryStream(CreateStripedPng(
            width: 300,
            height: 600,
            vertical: false,
            firstBoundary: 100,
            secondBoundary: 500));

        var result = await RenderAsync("Portrait Event", artwork, "image/png");
        using var bitmap = SKBitmap.Decode(result.PngBytes.ToArray());

        await AssertPanelCenterLineIsGreen(bitmap, vertical: true);
    }

    [Test]
    public async Task LandscapeArtworkUsesCenteredCoverCrop()
    {
        using var artwork = new MemoryStream(CreateStripedPng(
            width: 800,
            height: 400,
            vertical: true,
            firstBoundary: 250,
            secondBoundary: 550));

        var result = await RenderAsync("Landscape Event", artwork, "image/png");
        using var bitmap = SKBitmap.Decode(result.PngBytes.ToArray());

        await AssertPanelCenterLineIsGreen(bitmap, vertical: false);
    }

    [Test]
    public async Task FormatDateRangeUsesInvariantBoundedBadgeText()
    {
        await Assert.That(SkiaEventOpenGraphImageRenderer.FormatDateRange(null, null)).IsEqualTo("DATE TO BE ANNOUNCED");
        await Assert.That(SkiaEventOpenGraphImageRenderer.FormatDateRange(
                new DateOnly(2026, 1, 5),
                new DateOnly(2026, 1, 5))).IsEqualTo("JAN 5, 2026");
        await Assert.That(SkiaEventOpenGraphImageRenderer.FormatDateRange(
                new DateOnly(2026, 1, 5),
                new DateOnly(2026, 1, 7))).IsEqualTo("JAN 5–7, 2026");
        await Assert.That(SkiaEventOpenGraphImageRenderer.FormatDateRange(
                new DateOnly(2026, 1, 31),
                new DateOnly(2026, 2, 2))).IsEqualTo("JAN 31 – FEB 2, 2026");
        await Assert.That(SkiaEventOpenGraphImageRenderer.FormatDateRange(
                new DateOnly(2026, 12, 31),
                new DateOnly(2027, 1, 1))).IsEqualTo("DEC 31, 2026 – JAN 1, 2027");

        await Task.CompletedTask;
    }

    [Test]
    public async Task EmbeddedFontLoadsWithoutHostFontDiscovery()
    {
        using var typeface = SkiaEventOpenGraphImageRenderer.LoadEmbeddedTypeface();

        await Assert.That(typeface.FamilyName).Contains("Noto Sans Arabic");
        await Assert.That(typeface.GetGlyphs("فعاليات".AsSpan()).All(glyph => glyph != 0)).IsTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task ArabicBrandAndTitleRenderNonblankShapedText()
    {
        var arabic = await RenderAsync(
            title: "أمسية ثقافية للمجتمع",
            brandDisplayName: "فعاليات إسلامو");
        var blank = await RenderAsync(title: "", brandDisplayName: "");

        await Assert.That(HashTextRegion(arabic.PngBytes)).IsNotEqualTo(HashTextRegion(blank.PngBytes));
        await Assert.That(CountDarkPixels(arabic.PngBytes)).IsGreaterThan(500);
    }

    private Task<EventOpenGraphImageRenderResult> RenderAsync(
        string title,
        Stream? artwork = null,
        string? contentType = null,
        string brandDisplayName = "ISLAMU Event")
    {
        return _renderer.RenderAsync(
            new EventOpenGraphImageRenderRequest(
                title,
                new DateOnly(2026, 1, 5),
                new DateOnly(2026, 1, 7),
                brandDisplayName,
                artwork,
                contentType),
            CancellationToken.None);
    }

    private static string HashArtworkPanel(ReadOnlyMemory<byte> pngBytes)
        => HashRegion(pngBytes, ArtworkLeft, ArtworkTop, ArtworkWidth, ArtworkHeight);

    private static string HashTextRegion(ReadOnlyMemory<byte> pngBytes)
        => HashRegion(pngBytes, 48, 48, 640, 534);

    private static string HashRegion(ReadOnlyMemory<byte> pngBytes, int left, int top, int width, int height)
    {
        using var bitmap = SKBitmap.Decode(pngBytes.ToArray());
        var pixels = new byte[width * height * 4];
        var offset = 0;
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                pixels[offset++] = color.Red;
                pixels[offset++] = color.Green;
                pixels[offset++] = color.Blue;
                pixels[offset++] = color.Alpha;
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(pixels));
    }

    private static int CountDarkPixels(ReadOnlyMemory<byte> pngBytes)
    {
        using var bitmap = SKBitmap.Decode(pngBytes.ToArray());
        var count = 0;
        for (var y = 48; y < 582; y++)
        {
            for (var x = 48; x < 688; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.Red < 90 && color.Green < 90 && color.Blue < 90)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static async Task AssertPanelCenterLineIsGreen(SKBitmap bitmap, bool vertical)
    {
        var points = vertical
            ? new[]
            {
                new SKPointI(ArtworkLeft + (ArtworkWidth / 2), ArtworkTop + 20),
                new SKPointI(ArtworkLeft + (ArtworkWidth / 2), ArtworkTop + (ArtworkHeight / 2)),
                new SKPointI(ArtworkLeft + (ArtworkWidth / 2), ArtworkTop + ArtworkHeight - 20)
            }
            : new[]
            {
                new SKPointI(ArtworkLeft + 20, ArtworkTop + (ArtworkHeight / 2)),
                new SKPointI(ArtworkLeft + (ArtworkWidth / 2), ArtworkTop + (ArtworkHeight / 2)),
                new SKPointI(ArtworkLeft + ArtworkWidth - 20, ArtworkTop + (ArtworkHeight / 2))
            };

        foreach (var point in points)
        {
            var color = bitmap.GetPixel(point.X, point.Y);
            await Assert.That(color.Green).IsGreaterThan((byte)240);
            await Assert.That(color.Red).IsLessThan((byte)15);
            await Assert.That(color.Blue).IsLessThan((byte)15);
        }
    }

    private static byte[] CreateSolidPng(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        return EncodePng(bitmap);
    }

    private static byte[] CreateStripedPng(
        int width,
        int height,
        bool vertical,
        int firstBoundary,
        int secondBoundary)
        => CreateStripedImage(width, height, vertical, firstBoundary, secondBoundary, SKEncodedImageFormat.Png);

    private static byte[] CreateStripedJpeg(
        int width,
        int height,
        bool vertical,
        int firstBoundary,
        int secondBoundary)
        => CreateStripedImage(width, height, vertical, firstBoundary, secondBoundary, SKEncodedImageFormat.Jpeg);

    private static byte[] CreateStripedImage(
        int width,
        int height,
        bool vertical,
        int firstBoundary,
        int secondBoundary,
        SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var position = vertical ? x : y;
                bitmap.SetPixel(
                    x,
                    y,
                    position < firstBoundary
                        ? SKColors.Red
                        : position < secondBoundary
                            ? SKColors.Lime
                            : SKColors.Blue);
            }
        }

        return EncodeImage(bitmap, format);
    }

    private static byte[] EncodePng(SKBitmap bitmap)
        => EncodeImage(bitmap, SKEncodedImageFormat.Png);

    private static byte[] EncodeImage(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        return data.ToArray();
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableReadStream(byte[] bytes)
            => _inner = new MemoryStream(bytes, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
