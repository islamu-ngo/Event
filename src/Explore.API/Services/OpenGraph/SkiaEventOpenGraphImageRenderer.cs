using System.Buffers;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using Explore.Application.Features.Events.OpenGraph;
using Explore.Application.Features.Events.Requests.Queries;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Explore.API.Services.OpenGraph;

public sealed class SkiaEventOpenGraphImageRenderer : IEventOpenGraphImageRenderer
{
    private const int CanvasWidth = 1200;
    private const int CanvasHeight = 630;
    private const int ArtworkLeft = 745;
    private const int ArtworkTop = 35;
    private const int ArtworkWidth = 420;
    private const int ArtworkHeight = 560;
    private const float ArtworkRadius = 24f;
    private const float ArtworkShadowOffset = 8f;
    private const float ArtworkShadowExpansion = 4f;
    private const int ContentLeft = 64;
    private const int ContentRight = 681;
    private const int BrandBaseline = 92;
    private const int DateBadgeTop = 132;
    private const int DateBadgeHeight = 52;
    private const int DateBadgeHorizontalPadding = 24;
    private const int TitleFirstBaseline = 272;
    private const int TitleLineHeight = 76;
    private const int MaximumTitleLines = 4;
    private const int MaximumTextLength = 512;
    private const float BrandFontSize = 28f;
    private const float DateFontSize = 22f;
    private const float TitleFontSize = 62f;
    private const float BackgroundAccentRadius = 220f;
    private const int ReadBufferSize = 81_920;
    private const int MaximumEncodedArtworkBytes = 5 * 1024 * 1024;
    private const int MaximumDecodedDimension = 8192;
    private const long MaximumDecodedPixels = 24L * 1024 * 1024;
    private const long MaximumArtworkDecodePixels = 4L * ArtworkWidth * ArtworkHeight;
    private const string DefaultTitle = "Event";
    private const string DefaultBrand = "Event";
    private const string FontResourceSuffix = "NotoSansArabic[wdth,wght].ttf";

    private static readonly SKColor BackgroundColor = new(247, 245, 239);
    private static readonly SKColor BackgroundAccentColor = new(232, 226, 245, 150);
    private static readonly SKColor InkColor = new(31, 30, 28);
    private static readonly SKColor MutedInkColor = new(97, 92, 84);
    private static readonly SKColor BadgeColor = new(232, 226, 245);
    private static readonly SKColor BadgeInkColor = new(75, 58, 122);
    private static readonly SKColor DividerColor = new(218, 213, 203);
    private static readonly SKColor ArtworkShadowColor = new(36, 30, 22, 35);
    private static readonly byte[] EmbeddedFontBytes = ReadEmbeddedFontBytes();
    private static readonly HashSet<string> SupportedArtworkContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/gif",
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public async Task<EventOpenGraphImageRenderResult> RenderAsync(
        EventOpenGraphImageRenderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var title = NormalizeText(request.Title, DefaultTitle);
        var brand = NormalizeText(request.BrandDisplayName, DefaultBrand);
        using var typeface = LoadEmbeddedTypeface();
        using var shaper = new SKShaper(typeface);
        using var artwork = await TryDecodeArtworkAsync(
            request.FeaturedImage,
            request.FeaturedImageContentType,
            cancellationToken);
        using var surface = SKSurface.Create(new SKImageInfo(
                CanvasWidth,
                CanvasHeight,
                SKColorType.Rgba8888,
                SKAlphaType.Opaque))
            ?? throw new InvalidOperationException("SkiaSharp could not create the Open Graph drawing surface.");

        DrawBackground(surface.Canvas);
        DrawTextContent(
            surface.Canvas,
            shaper,
            typeface,
            brand,
            title,
            request.FirstSessionDate,
            request.LastSessionDate);
        DrawArtwork(surface.Canvas, artwork, request.Title);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("SkiaSharp could not encode the Open Graph PNG.");
        var pngBytes = encoded.ToArray();
        var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(pngBytes))}\"";
        return new EventOpenGraphImageRenderResult(pngBytes, etag);
    }

    internal static string FormatDateRange(DateOnly? firstDate, DateOnly? lastDate)
    {
        if (firstDate is null && lastDate is null)
        {
            return "DATE TO BE ANNOUNCED";
        }

        var first = firstDate ?? lastDate!.Value;
        var last = lastDate ?? first;
        if (first == last)
        {
            return FormatDate(first);
        }

        if (first.Year == last.Year && first.Month == last.Month)
        {
            return $"{first.ToString("MMM d", CultureInfo.InvariantCulture)}–{last.Day}, {last.Year}"
                .ToUpperInvariant();
        }

        if (first.Year == last.Year)
        {
            return $"{first.ToString("MMM d", CultureInfo.InvariantCulture)} – {last.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}"
                .ToUpperInvariant();
        }

        return $"{FormatDate(first)} – {FormatDate(last)}";
    }

    internal static SKTypeface LoadEmbeddedTypeface()
    {
        using var data = SKData.CreateCopy(EmbeddedFontBytes);
        return SKTypeface.FromData(data, 0)
            ?? throw new InvalidOperationException("The embedded Noto Sans Arabic font is invalid.");
    }

    private static void DrawBackground(SKCanvas canvas)
    {
        canvas.Clear(BackgroundColor);
        using var accentPaint = new SKPaint
        {
            Color = BackgroundAccentColor,
            IsAntialias = true
        };
        canvas.DrawCircle(ContentLeft, CanvasHeight, BackgroundAccentRadius, accentPaint);

        using var dividerPaint = new SKPaint
        {
            Color = DividerColor,
            IsAntialias = true,
            StrokeWidth = 2f
        };
        canvas.DrawLine(ContentLeft, CanvasHeight - ContentLeft, ContentRight, CanvasHeight - ContentLeft, dividerPaint);
    }

    private static void DrawTextContent(
        SKCanvas canvas,
        SKShaper shaper,
        SKTypeface typeface,
        string brand,
        string title,
        DateOnly? firstDate,
        DateOnly? lastDate)
    {
        var maximumWidth = ContentRight - ContentLeft;
        using var brandFont = CreateFont(typeface, BrandFontSize, embolden: true);
        using var brandPaint = CreatePaint(MutedInkColor);
        var fittedBrand = FitLine(shaper, brand, brandFont, maximumWidth);
        DrawShapedLine(canvas, shaper, fittedBrand, ContentLeft, ContentRight, BrandBaseline, brandFont, brandPaint);

        using var dateFont = CreateFont(typeface, DateFontSize, embolden: true);
        using var datePaint = CreatePaint(BadgeInkColor);
        var dateText = FitLine(
            shaper,
            FormatDateRange(firstDate, lastDate),
            dateFont,
            maximumWidth - (DateBadgeHorizontalPadding * 2));
        var dateWidth = MeasureShapedText(shaper, dateText, dateFont);
        var badgeWidth = Math.Min(
            maximumWidth,
            dateWidth + (DateBadgeHorizontalPadding * 2));
        using var badgePaint = CreatePaint(BadgeColor);
        canvas.DrawRoundRect(
            new SKRect(
                ContentLeft,
                DateBadgeTop,
                ContentLeft + badgeWidth,
                DateBadgeTop + DateBadgeHeight),
            DateBadgeHeight / 2f,
            DateBadgeHeight / 2f,
            badgePaint);
        DrawShapedLine(
            canvas,
            shaper,
            dateText,
            ContentLeft + DateBadgeHorizontalPadding,
            ContentLeft + badgeWidth - DateBadgeHorizontalPadding,
            DateBadgeTop + 35,
            dateFont,
            datePaint);

        using var titleFont = CreateFont(typeface, TitleFontSize, embolden: true);
        using var titlePaint = CreatePaint(InkColor);
        var titleLines = WrapText(shaper, title, titleFont, maximumWidth, MaximumTitleLines);
        for (var index = 0; index < titleLines.Count; index++)
        {
            DrawShapedLine(
                canvas,
                shaper,
                titleLines[index],
                ContentLeft,
                ContentRight,
                TitleFirstBaseline + (index * TitleLineHeight),
                titleFont,
                titlePaint);
        }
    }

    private static void DrawArtwork(SKCanvas canvas, SKBitmap? artwork, string title)
    {
        var bounds = new SKRect(
            ArtworkLeft,
            ArtworkTop,
            ArtworkLeft + ArtworkWidth,
            ArtworkTop + ArtworkHeight);
        using var shadowPaint = CreatePaint(ArtworkShadowColor);
        canvas.DrawRoundRect(
            new SKRect(
                bounds.Left - ArtworkShadowExpansion,
                bounds.Top + ArtworkShadowOffset,
                bounds.Right + ArtworkShadowExpansion,
                bounds.Bottom + ArtworkShadowOffset),
            ArtworkRadius + ArtworkShadowExpansion,
            ArtworkRadius + ArtworkShadowExpansion,
            shadowPaint);

        var saveCount = canvas.Save();
        try
        {
            using var clipBounds = new SKRoundRect(bounds, ArtworkRadius);
            canvas.ClipRoundRect(clipBounds, antialias: true);
            if (artwork is null)
            {
                DrawTitleGradient(canvas, bounds, title);
            }
            else
            {
                DrawCenterCover(canvas, artwork, bounds);
            }
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }

    private static void DrawCenterCover(SKCanvas canvas, SKBitmap artwork, SKRect destination)
    {
        var scale = Math.Max(
            destination.Width / artwork.Width,
            destination.Height / artwork.Height);
        var sourceWidth = destination.Width / scale;
        var sourceHeight = destination.Height / scale;
        var source = new SKRect(
            (artwork.Width - sourceWidth) / 2f,
            (artwork.Height - sourceHeight) / 2f,
            (artwork.Width + sourceWidth) / 2f,
            (artwork.Height + sourceHeight) / 2f);
        using var paint = new SKPaint
        {
            IsAntialias = true
        };
        canvas.DrawBitmap(
            artwork,
            source,
            destination,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
            paint);
    }

    private static void DrawTitleGradient(SKCanvas canvas, SKRect bounds, string title)
    {
        var hash = GetStableTitleHash(title);
        var firstHue = hash % 360;
        var secondHue = (firstHue + 48 + ((hash >> 8) % 120)) % 360;
        var meshHue = (secondHue + 72 + ((hash >> 16) % 90)) % 360;
        var meshX = 20 + ((hash >> 5) % 61);
        var meshY = 15 + ((hash >> 13) % 71);

        using var baseShader = SKShader.CreateLinearGradient(
            new SKPoint(bounds.Left, bounds.Top),
            new SKPoint(bounds.Right, bounds.Bottom),
            [
                SKColor.FromHsl(firstHue, 58f, 70f),
                SKColor.FromHsl(secondHue, 55f, 60f)
            ],
            SKShaderTileMode.Clamp);
        using var basePaint = new SKPaint
        {
            IsAntialias = true,
            Shader = baseShader
        };
        canvas.DrawRect(bounds, basePaint);

        using var firstMeshShader = SKShader.CreateRadialGradient(
            new SKPoint(
                bounds.Left + (bounds.Width * meshX / 100f),
                bounds.Top + (bounds.Height * meshY / 100f)),
            bounds.Height * 0.72f,
            [
                SKColor.FromHsl(meshHue, 64f, 76f, 184),
                SKColor.FromHsl(meshHue, 58f, 64f, 0)
            ],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        using var firstMeshPaint = new SKPaint
        {
            IsAntialias = true,
            Shader = firstMeshShader
        };
        canvas.DrawRect(bounds, firstMeshPaint);

        using var secondMeshShader = SKShader.CreateRadialGradient(
            new SKPoint(
                bounds.Left + (bounds.Width * 0.92f),
                bounds.Top + (bounds.Height * 0.88f)),
            bounds.Height * 0.68f,
            [
                SKColor.FromHsl(secondHue, 65f, 68f, 148),
                SKColor.FromHsl(secondHue, 55f, 58f, 0)
            ],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        using var secondMeshPaint = new SKPaint
        {
            IsAntialias = true,
            Shader = secondMeshShader
        };
        canvas.DrawRect(bounds, secondMeshPaint);
    }

    internal static async Task<SKBitmap?> TryDecodeArtworkAsync(
        Stream? artwork,
        string? contentType,
        CancellationToken cancellationToken)
    {
        if (artwork is null || !IsSupportedContentType(contentType))
        {
            return null;
        }

        try
        {
            if (artwork.CanSeek && artwork.Length - artwork.Position > MaximumEncodedArtworkBytes)
            {
                return null;
            }

            using var encodedStream = new MemoryStream();
            if (!await CopyBoundedAsync(artwork, encodedStream, cancellationToken))
            {
                return null;
            }

            using var encodedData = SKData.CreateCopy(encodedStream.GetBuffer().AsSpan(0, (int)encodedStream.Length));
            using var codec = SKCodec.Create(encodedData);
            if (codec is null || !MatchesContentType(codec.EncodedFormat, contentType))
            {
                return null;
            }

            var info = codec.Info;
            if (info.Width <= 0 ||
                info.Height <= 0 ||
                info.Width > MaximumDecodedDimension ||
                info.Height > MaximumDecodedDimension ||
                (long)info.Width * info.Height > MaximumDecodedPixels)
            {
                return null;
            }

            var desiredScale = Math.Min(
                1f,
                Math.Max(
                    (float)ArtworkWidth / info.Width,
                    (float)ArtworkHeight / info.Height));
            var decodeDimensions = codec.GetScaledDimensions(desiredScale);
            var sourceCoversArtwork = info.Width >= ArtworkWidth && info.Height >= ArtworkHeight;
            while (sourceCoversArtwork &&
                   (decodeDimensions.Width < ArtworkWidth || decodeDimensions.Height < ArtworkHeight) &&
                   desiredScale < 1f)
            {
                desiredScale = Math.Min(1f, desiredScale * 2f);
                decodeDimensions = codec.GetScaledDimensions(desiredScale);
            }

            if (decodeDimensions.Width <= 0 ||
                decodeDimensions.Height <= 0 ||
                (sourceCoversArtwork &&
                    (decodeDimensions.Width < ArtworkWidth || decodeDimensions.Height < ArtworkHeight)) ||
                (long)decodeDimensions.Width * decodeDimensions.Height > MaximumArtworkDecodePixels)
            {
                return null;
            }

            var decodeInfo = info.WithSize(decodeDimensions);
            if (decodeInfo.AlphaType == SKAlphaType.Unpremul)
            {
                decodeInfo = decodeInfo.WithAlphaType(SKAlphaType.Premul);
            }

            return SKBitmap.Decode(codec, decodeInfo);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static async Task<bool> CopyBoundedAsync(
        Stream source,
        MemoryStream destination,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        try
        {
            var totalBytes = 0;
            while (true)
            {
                var remaining = MaximumEncodedArtworkBytes - totalBytes;
                var requested = Math.Min(buffer.Length, remaining + 1);
                var read = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (read == 0)
                {
                    return true;
                }

                totalBytes += read;
                if (totalBytes > MaximumEncodedArtworkBytes)
                {
                    return false;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static List<string> WrapText(
        SKShaper shaper,
        string text,
        SKFont font,
        float maximumWidth,
        int maximumLines)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>(maximumLines);
        var currentLine = string.Empty;

        for (var index = 0; index < words.Length; index++)
        {
            var candidate = currentLine.Length == 0
                ? words[index]
                : $"{currentLine} {words[index]}";
            if (MeasureShapedText(shaper, candidate, font) <= maximumWidth)
            {
                currentLine = candidate;
                continue;
            }

            if (lines.Count == maximumLines - 1)
            {
                var remainder = string.Join(' ', words[index..]);
                var finalLine = currentLine.Length == 0
                    ? remainder
                    : $"{currentLine} {remainder}";
                lines.Add(FitLine(shaper, finalLine, font, maximumWidth));
                return lines;
            }

            if (currentLine.Length > 0)
            {
                lines.Add(FitLine(shaper, currentLine, font, maximumWidth));
                currentLine = words[index];
            }
            else
            {
                lines.Add(FitLine(shaper, words[index], font, maximumWidth));
            }
        }

        if (currentLine.Length > 0 && lines.Count < maximumLines)
        {
            lines.Add(FitLine(shaper, currentLine, font, maximumWidth));
        }

        return lines.Count == 0 ? [DefaultTitle] : lines;
    }

    private static string FitLine(SKShaper shaper, string text, SKFont font, float maximumWidth)
    {
        if (MeasureShapedText(shaper, text, font) <= maximumWidth)
        {
            return text;
        }

        const string ellipsis = "…";
        var elementOffsets = StringInfo.ParseCombiningCharacters(text);
        var low = 0;
        var high = elementOffsets.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var end = middle == elementOffsets.Length ? text.Length : elementOffsets[middle];
            var candidate = text[..end].TrimEnd() + ellipsis;
            if (MeasureShapedText(shaper, candidate, font) <= maximumWidth)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        var fittedEnd = low == elementOffsets.Length ? text.Length : elementOffsets[low];
        return text[..fittedEnd].TrimEnd() + ellipsis;
    }

    private static void DrawShapedLine(
        SKCanvas canvas,
        SKShaper shaper,
        string text,
        float left,
        float right,
        float baseline,
        SKFont font,
        SKPaint paint)
    {
        var rightToLeft = ContainsRightToLeftText(text);
        canvas.DrawShapedText(
            shaper,
            text,
            rightToLeft ? right : left,
            baseline,
            rightToLeft ? SKTextAlign.Right : SKTextAlign.Left,
            font,
            paint);
    }

    private static float MeasureShapedText(SKShaper shaper, string text, SKFont font)
        => shaper.Shape(text, 0f, 0f, font).Width;

    private static SKFont CreateFont(SKTypeface typeface, float size, bool embolden)
        => new()
        {
            Typeface = typeface,
            Size = size,
            Embolden = embolden,
            Edging = SKFontEdging.Antialias,
            Subpixel = true
        };

    private static SKPaint CreatePaint(SKColor color)
        => new()
        {
            Color = color,
            IsAntialias = true
        };

    private static string NormalizeText(string? value, string fallback)
    {
        var normalized = string.Join(
            ' ',
            (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            return fallback;
        }

        return normalized.Length <= MaximumTextLength
            ? normalized
            : normalized[..MaximumTextLength];
    }

    private static bool ContainsRightToLeftText(string text)
        => text.Any(character => character is >= '\u0590' and <= '\u08FF');

    private static uint GetStableTitleHash(string? title)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var character in string.IsNullOrWhiteSpace(title) ? "event" : title.Trim())
        {
            hash ^= character;
            hash = unchecked(hash * prime);
        }

        return hash;
    }

    private static bool IsSupportedContentType(string? contentType)
    {
        var normalized = NormalizeContentType(contentType);
        return normalized is not null && SupportedArtworkContentTypes.Contains(normalized);
    }

    private static bool MatchesContentType(SKEncodedImageFormat format, string? contentType)
        => NormalizeContentType(contentType) switch
        {
            "image/gif" => format == SKEncodedImageFormat.Gif,
            "image/jpeg" => format == SKEncodedImageFormat.Jpeg,
            "image/png" => format == SKEncodedImageFormat.Png,
            "image/webp" => format == SKEncodedImageFormat.Webp,
            _ => false
        };

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var semicolonIndex = contentType.IndexOf(';', StringComparison.Ordinal);
        return (semicolonIndex >= 0 ? contentType[..semicolonIndex] : contentType)
            .Trim()
            .ToLowerInvariant();
    }

    private static string FormatDate(DateOnly date)
        => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();

    private static byte[] ReadEmbeddedFontBytes()
    {
        var assembly = typeof(SkiaEventOpenGraphImageRenderer).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(FontResourceSuffix, StringComparison.Ordinal));
        if (resourceName is null)
        {
            throw new InvalidOperationException("The embedded Noto Sans Arabic font resource was not found.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The embedded Noto Sans Arabic font resource could not be opened.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
