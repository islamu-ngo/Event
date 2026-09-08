using System.Buffers.Binary;
using System.Text;

namespace Explore.Blazor.Client.Services;

public static class ImageUploadClientPolicy
{
    private static readonly byte[] JpegSignature = [0xff, 0xd8, 0xff];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Vp8KeyFrameSignature = [0x9d, 0x01, 0x2a];

    public const long DefaultMaxImageFileSizeBytes = 5 * 1024 * 1024;
    public const string DefaultAcceptedImageFormats = ".jpg,.jpeg,.png,.gif,.webp";

    public const string UnsupportedImageTypeMessage = "Select a JPG, PNG, GIF, or WebP image.";
    public const string OversizedImageMessagePrefix = "Image must be";
    public const string ReadFailureMessage = "Failed to read the selected image. Try another file.";
    public const string PreviewFailureMessage = "Failed to generate an image preview.";
    public const string ProcessingFailureMessage = "An error occurred while processing the image.";
    public const string GenericUploadFailureMessage = "Image upload failed. Try again or choose another image.";
    public const string NoImageDataMessage = "No image data was provided.";
    public const string UploadSessionUnavailableMessage = "Failed to get an upload session. Please check your authentication and try again.";
    public const string UploadProxyFailureMessage = "Failed to upload image to storage. Please check your connection and try again.";
    public const string MetadataFailureMessage = "Failed to save image metadata. Please try again.";
    public const string MetadataBuildFailureMessage = "Failed to build storage metadata for uploaded image.";
    public const string StorageUploadCompletedWithoutMetadataMessage = "Storage upload completed without metadata.";
    public const string DirectUploadBrowserUnavailableMessage = "Browser uploads require a server-issued upload session.";

    private static readonly string[] DefaultAllowedImageContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp"
    ];

    private static readonly Dictionary<string, string> ExtensionByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp"
    };

    private static readonly HashSet<string> SafeImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".webp"
    };

    private static readonly HashSet<string> UserSafeUploadMessages = new(StringComparer.Ordinal)
    {
        GenericUploadFailureMessage,
        NoImageDataMessage,
        UploadSessionUnavailableMessage,
        UploadProxyFailureMessage,
        MetadataFailureMessage,
        MetadataBuildFailureMessage,
        StorageUploadCompletedWithoutMetadataMessage,
        DirectUploadBrowserUnavailableMessage
    };

    public static string[] AllowedImageContentTypes => DefaultAllowedImageContentTypes.ToArray();

    public static string? DetectImageContentType(ReadOnlySpan<byte> content)
    {
        if (IsJpegContainer(content))
        {
            return "image/jpeg";
        }

        if (IsPngContainer(content))
        {
            return "image/png";
        }

        if (IsGifContainer(content))
        {
            return "image/gif";
        }

        return IsWebpContainer(content) ? "image/webp" : null;
    }

    public static bool IsAllowedImageContentType(string? contentType)
    {
        return !string.IsNullOrWhiteSpace(contentType)
            && DefaultAllowedImageContentTypes.Contains(contentType.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryValidateImageFile(
        string? fileName,
        string? declaredContentType,
        ReadOnlySpan<byte> content,
        out string contentType)
    {
        contentType = string.Empty;
        if (!IsAllowedImageContentType(declaredContentType))
        {
            return false;
        }

        var normalizedContentType = declaredContentType!.Trim().ToLowerInvariant();
        var detectedContentType = DetectImageContentType(content);
        if (!string.Equals(normalizedContentType, detectedContentType, StringComparison.Ordinal))
        {
            return false;
        }

        var extension = Path.GetExtension(GetLastPathSegment(fileName));
        if (!SafeImageExtensions.Contains(extension))
        {
            return false;
        }

        var expectedExtension = ExtensionByContentType[normalizedContentType];
        if (!string.Equals(extension, expectedExtension, StringComparison.OrdinalIgnoreCase) &&
            !(normalizedContentType == "image/jpeg" &&
              string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        contentType = normalizedContentType;
        return true;
    }

    public static string FormatMaxFileSizeMessage(long maxFileSize)
    {
        return $"{OversizedImageMessagePrefix} {FormatBytes(maxFileSize)} or smaller.";
    }

    public static string FormatBytes(long bytes)
    {
        const double oneMiB = 1024d * 1024d;
        return bytes >= oneMiB
            ? $"{bytes / oneMiB:0.#} MB"
            : $"{bytes} bytes";
    }

    public static string BuildSafeFileName(string? browserFileName, string? contentType)
    {
        var extension = ResolveSafeImageExtension(browserFileName, contentType);
        var baseName = BuildSafeFileNameStem(browserFileName);

        return $"{baseName}{extension}";
    }

    public static string ResolveSafeImageExtension(string? browserFileName, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && ExtensionByContentType.TryGetValue(contentType.Trim(), out var mappedExtension))
        {
            return mappedExtension;
        }

        var lastSegment = GetLastPathSegment(browserFileName);
        var extension = Path.GetExtension(lastSegment);
        return SafeImageExtensions.Contains(extension) ? extension.ToLowerInvariant() : ".jpg";
    }

    public static string GetSizeBucket(long sizeBytes)
    {
        return sizeBytes switch
        {
            <= 0 => "empty",
            <= 1024 * 1024 => "0-1MB",
            <= DefaultMaxImageFileSizeBytes => "1-5MB",
            <= 10 * 1024 * 1024 => "5-10MB",
            _ => ">10MB"
        };
    }

    public static string GetContentTypeBucket(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return "missing";
        }

        var trimmed = contentType.Trim();
        if (IsAllowedImageContentType(trimmed))
        {
            return "allowed-image";
        }

        return trimmed.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? "other-image"
            : "other";
    }

    public static string GetFailureType(Exception exception)
    {
        return exception.GetType().Name;
    }

    public static string ToUserSafeUploadError(string? message)
    {
        var trimmed = message?.Trim();
        return !string.IsNullOrEmpty(trimmed) && UserSafeUploadMessages.Contains(trimmed)
            ? trimmed
            : GenericUploadFailureMessage;
    }

    private static bool IsPngContainer(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith(PngSignature))
        {
            return false;
        }

        int offset = PngSignature.Length;
        bool seenHeader = false;
        bool seenImageData = false;
        bool imageDataEnded = false;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 12)
            {
                return false;
            }

            uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (payloadLength > int.MaxValue || payloadLength > bytes.Length - offset - 12)
            {
                return false;
            }

            ReadOnlySpan<byte> chunkType = bytes.Slice(offset + 4, 4);
            ReadOnlySpan<byte> payload = bytes.Slice(offset + 8, (int)payloadLength);
            offset += checked(12 + (int)payloadLength);
            if (!seenHeader)
            {
                if (!chunkType.SequenceEqual("IHDR"u8) || !IsPngHeader(payload))
                {
                    return false;
                }

                seenHeader = true;
                continue;
            }

            if (chunkType.SequenceEqual("IHDR"u8))
            {
                return false;
            }

            if (chunkType.SequenceEqual("IDAT"u8))
            {
                if (imageDataEnded || payload.IsEmpty)
                {
                    return false;
                }

                seenImageData = true;
                continue;
            }

            imageDataEnded |= seenImageData;
            if (chunkType.SequenceEqual("IEND"u8))
            {
                return payload.IsEmpty && seenImageData && offset == bytes.Length;
            }

            if (IsPngCriticalChunk(chunkType) && !chunkType.SequenceEqual("PLTE"u8))
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsPngHeader(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 13
            || BinaryPrimitives.ReadUInt32BigEndian(payload[..4]) == 0
            || BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)) == 0
            || payload[10] != 0
            || payload[11] != 0
            || payload[12] > 1)
        {
            return false;
        }

        byte bitDepth = payload[8];
        return payload[9] switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 or 6 => bitDepth is 8 or 16,
            _ => false
        };
    }

    private static bool IsPngCriticalChunk(ReadOnlySpan<byte> chunkType) =>
        chunkType.Length == 4 && chunkType[0] is >= (byte)'A' and <= (byte)'Z';

    private static bool IsJpegContainer(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith(JpegSignature))
        {
            return false;
        }

        int offset = 2;
        bool seenFrame = false;
        bool seenScan = false;
        while (offset < bytes.Length)
        {
            if (bytes[offset++] != 0xff)
            {
                return false;
            }

            while (offset < bytes.Length && bytes[offset] == 0xff)
            {
                offset++;
            }

            if (offset >= bytes.Length)
            {
                return false;
            }

            byte marker = bytes[offset++];
            if (marker == 0xd9)
            {
                return seenFrame && seenScan && offset == bytes.Length;
            }

            if (marker is 0x00 or 0x01 or 0xd8
                || marker is >= 0xd0 and <= 0xd7
                || bytes.Length - offset < 2)
            {
                return false;
            }

            int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (segmentLength < 2 || segmentLength > bytes.Length - offset)
            {
                return false;
            }

            ReadOnlySpan<byte> payload = bytes.Slice(offset + 2, segmentLength - 2);
            offset += segmentLength;
            if (IsJpegFrameMarker(marker))
            {
                if (seenFrame || !IsJpegFrameHeader(payload))
                {
                    return false;
                }

                seenFrame = true;
                continue;
            }

            if (marker != 0xda)
            {
                continue;
            }

            if (!seenFrame || !IsJpegScanHeader(payload))
            {
                return false;
            }

            seenScan = true;
            if (!TrySkipJpegEntropy(bytes, ref offset))
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsJpegFrameMarker(byte marker) =>
        marker is >= 0xc0 and <= 0xc3
        or >= 0xc5 and <= 0xc7
        or >= 0xc9 and <= 0xcb
        or >= 0xcd and <= 0xcf;

    private static bool IsJpegFrameHeader(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 9
            || payload[0] == 0
            || BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(1, 2)) == 0
            || BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(3, 2)) == 0)
        {
            return false;
        }

        int componentCount = payload[5];
        return componentCount > 0 && payload.Length == 6 + (3 * componentCount);
    }

    private static bool IsJpegScanHeader(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 6)
        {
            return false;
        }

        int componentCount = payload[0];
        return componentCount > 0 && payload.Length == 1 + (2 * componentCount) + 3;
    }

    private static bool TrySkipJpegEntropy(ReadOnlySpan<byte> bytes, ref int offset)
    {
        while (offset < bytes.Length)
        {
            if (bytes[offset] != 0xff)
            {
                offset++;
                continue;
            }

            int markerStart = offset++;
            while (offset < bytes.Length && bytes[offset] == 0xff)
            {
                offset++;
            }

            if (offset >= bytes.Length)
            {
                return false;
            }

            byte marker = bytes[offset];
            if (marker == 0x00 || marker is >= 0xd0 and <= 0xd7)
            {
                offset++;
                continue;
            }

            offset = markerStart;
            return true;
        }

        return false;
    }

    private static bool IsGifContainer(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 13
            || !(bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)))
        {
            return false;
        }

        ReadOnlySpan<byte> logicalScreen = bytes.Slice(6, 7);
        if (BinaryPrimitives.ReadUInt16LittleEndian(logicalScreen[..2]) == 0
            || BinaryPrimitives.ReadUInt16LittleEndian(logicalScreen.Slice(2, 2)) == 0)
        {
            return false;
        }

        int offset = 13;
        if (!TrySkipGifColorTable(bytes, ref offset, logicalScreen[4]))
        {
            return false;
        }

        bool seenImage = false;
        while (offset < bytes.Length)
        {
            byte blockType = bytes[offset++];
            if (blockType == 0x3b)
            {
                return seenImage && offset == bytes.Length;
            }

            if (blockType == 0x21)
            {
                if (offset >= bytes.Length)
                {
                    return false;
                }

                offset++;
                if (!TrySkipGifSubBlocks(bytes, ref offset, requireData: false))
                {
                    return false;
                }

                continue;
            }

            if (blockType != 0x2c || bytes.Length - offset < 9)
            {
                return false;
            }

            ReadOnlySpan<byte> descriptor = bytes.Slice(offset, 9);
            offset += 9;
            if (BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(4, 2)) == 0
                || BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(6, 2)) == 0
                || !TrySkipGifColorTable(bytes, ref offset, descriptor[8])
                || offset >= bytes.Length
                || bytes[offset++] is < 2 or > 8
                || !TrySkipGifSubBlocks(bytes, ref offset, requireData: true))
            {
                return false;
            }

            seenImage = true;
        }

        return false;
    }

    private static bool TrySkipGifColorTable(ReadOnlySpan<byte> bytes, ref int offset, byte packedFields)
    {
        if ((packedFields & 0x80) == 0)
        {
            return true;
        }

        int tableLength = 3 * (1 << ((packedFields & 0x07) + 1));
        if (tableLength > bytes.Length - offset)
        {
            return false;
        }

        offset += tableLength;
        return true;
    }

    private static bool TrySkipGifSubBlocks(
        ReadOnlySpan<byte> bytes,
        ref int offset,
        bool requireData)
    {
        bool seenData = false;
        while (offset < bytes.Length)
        {
            int blockLength = bytes[offset++];
            if (blockLength == 0)
            {
                return seenData || !requireData;
            }

            if (blockLength > bytes.Length - offset)
            {
                return false;
            }

            seenData = true;
            offset += blockLength;
        }

        return false;
    }

    private static bool IsWebpContainer(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12
            || !bytes[..4].SequenceEqual("RIFF"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) != bytes.Length - 8
            || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        int offset = 12;
        bool seenExtendedHeader = false;
        bool animationDeclared = false;
        bool seenAnimation = false;
        bool seenImage = false;
        while (offset < bytes.Length)
        {
            if (!TryReadWebpChunk(
                    bytes,
                    ref offset,
                    out ReadOnlySpan<byte> chunkType,
                    out ReadOnlySpan<byte> payload))
            {
                return false;
            }

            if (chunkType.SequenceEqual("VP8X"u8))
            {
                if (seenExtendedHeader
                    || seenAnimation
                    || seenImage
                    || payload.Length != 10
                    || (payload[0] & 0xc1) != 0)
                {
                    return false;
                }

                seenExtendedHeader = true;
                animationDeclared = (payload[0] & 0x02) != 0;
                continue;
            }

            if (chunkType.SequenceEqual("ANIM"u8))
            {
                if (!animationDeclared || seenAnimation || seenImage || payload.Length != 6)
                {
                    return false;
                }

                seenAnimation = true;
                continue;
            }

            if (chunkType.SequenceEqual("ANMF"u8))
            {
                if (!seenAnimation || !IsAnimatedWebpFrame(payload))
                {
                    return false;
                }

                seenImage = true;
                continue;
            }

            if (chunkType.SequenceEqual("VP8 "u8))
            {
                if (animationDeclared || seenImage || !IsVp8Payload(payload))
                {
                    return false;
                }

                seenImage = true;
            }
            else if (chunkType.SequenceEqual("VP8L"u8))
            {
                if (animationDeclared || seenImage || !IsVp8LosslessPayload(payload))
                {
                    return false;
                }

                seenImage = true;
            }
            else if (!(chunkType.SequenceEqual("ALPH"u8)
                || chunkType.SequenceEqual("ICCP"u8)
                || chunkType.SequenceEqual("EXIF"u8)
                || chunkType.SequenceEqual("XMP "u8)))
            {
                return false;
            }
        }

        return seenImage && offset == bytes.Length && animationDeclared == seenAnimation;
    }

    private static bool IsAnimatedWebpFrame(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 24 || (payload[15] & 0xfc) != 0)
        {
            return false;
        }

        int offset = 16;
        bool seenImage = false;
        while (offset < payload.Length)
        {
            if (!TryReadWebpChunk(
                    payload,
                    ref offset,
                    out ReadOnlySpan<byte> chunkType,
                    out ReadOnlySpan<byte> framePayload))
            {
                return false;
            }

            if (chunkType.SequenceEqual("ALPH"u8))
            {
                if (seenImage)
                {
                    return false;
                }

                continue;
            }

            if (seenImage
                || !(chunkType.SequenceEqual("VP8 "u8) && IsVp8Payload(framePayload)
                    || chunkType.SequenceEqual("VP8L"u8) && IsVp8LosslessPayload(framePayload)))
            {
                return false;
            }

            seenImage = true;
        }

        return seenImage && offset == payload.Length;
    }

    private static bool TryReadWebpChunk(
        ReadOnlySpan<byte> bytes,
        ref int offset,
        out ReadOnlySpan<byte> chunkType,
        out ReadOnlySpan<byte> payload)
    {
        chunkType = default;
        payload = default;
        if (bytes.Length - offset < 8)
        {
            return false;
        }

        chunkType = bytes.Slice(offset, 4);
        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
        offset += 8;
        if (payloadLength > int.MaxValue || payloadLength > bytes.Length - offset)
        {
            return false;
        }

        payload = bytes.Slice(offset, (int)payloadLength);
        offset += (int)payloadLength;
        if ((payloadLength & 1) != 0)
        {
            if (offset >= bytes.Length || bytes[offset] != 0)
            {
                return false;
            }

            offset++;
        }

        return true;
    }

    private static bool IsVp8Payload(ReadOnlySpan<byte> payload) =>
        payload.Length > 10
        && (payload[0] & 0x01) == 0
        && payload.Slice(3, 3).SequenceEqual(Vp8KeyFrameSignature)
        && (BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2)) & 0x3fff) > 0
        && (BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2)) & 0x3fff) > 0;

    private static bool IsVp8LosslessPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length <= 5 || payload[0] != 0x2f)
        {
            return false;
        }

        uint dimensions = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4));
        return (dimensions >> 29) == 0;
    }

    private static string BuildSafeFileNameStem(string? browserFileName)
    {
        var lastSegment = GetLastPathSegment(browserFileName);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(lastSegment);
        var builder = new StringBuilder(nameWithoutExtension.Length);
        var previousWasSeparator = false;

        foreach (var character in nameWithoutExtension.Normalize(NormalizationForm.FormD))
        {
            if (IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
            }
            else if (character is '.' or '_' or '-')
            {
                AppendSeparator(builder, character, ref previousWasSeparator);
            }
            else if (!previousWasSeparator)
            {
                AppendSeparator(builder, '-', ref previousWasSeparator);
            }
        }

        var safeName = builder.ToString().Trim('.', '_', '-');
        if (safeName.Length > 64)
        {
            safeName = safeName[..64].Trim('.', '_', '-');
        }

        return string.IsNullOrWhiteSpace(safeName) ? "image" : safeName;
    }

    private static string GetLastPathSegment(string? browserFileName)
    {
        if (string.IsNullOrWhiteSpace(browserFileName))
        {
            return "image";
        }

        var normalized = browserFileName.Trim().Replace('\\', '/');
        var lastSeparator = normalized.LastIndexOf('/');
        return lastSeparator >= 0 ? normalized[(lastSeparator + 1)..] : normalized;
    }

    private static void AppendSeparator(StringBuilder builder, char separator, ref bool previousWasSeparator)
    {
        if (builder.Length == 0)
        {
            previousWasSeparator = true;
            return;
        }

        if (previousWasSeparator)
        {
            return;
        }

        builder.Append(separator);
        previousWasSeparator = true;
    }

    private static bool IsAsciiLetterOrDigit(char character)
    {
        return character is >= 'a' and <= 'z'
            || character is >= 'A' and <= 'Z'
            || character is >= '0' and <= '9';
    }
}
