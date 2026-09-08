using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Explore.Domain;

namespace Explore.Application.Services;

public static class SafeRasterContentPolicy
{
    private static readonly byte[] JpegSignature = [0xff, 0xd8, 0xff];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private static readonly byte[] Vp8KeyFrameSignature = [0x9d, 0x01, 0x2a];

    public static bool TryNormalizeMimeType(
        string? value,
        [NotNullWhen(true)] out string? normalizedMimeType)
    {
        normalizedMimeType = null;
        string? candidate = value?.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        candidate = candidate.ToLowerInvariant();
        if (candidate is not (
            "image/jpeg" or
            "image/png" or
            "image/gif" or
            "image/webp" or
            "image/avif"))
        {
            return false;
        }

        normalizedMimeType = candidate;
        return true;
    }

    public static bool IsBrowserImageMimeType(string? value) =>
        TryNormalizeMimeType(value, out string? mimeType)
        && mimeType is not "image/avif";

    public static bool MatchesExtension(string? mimeType, string? extension)
    {
        if (!TryNormalizeMimeType(mimeType, out string? normalizedMimeType))
        {
            return false;
        }

        string? normalizedExtension = extension?.Trim().TrimStart('.').ToLowerInvariant();
        return normalizedMimeType switch
        {
            "image/jpeg" => normalizedExtension is "jpg" or "jpeg" or "jpe",
            "image/png" => normalizedExtension is "png",
            "image/gif" => normalizedExtension is "gif",
            "image/webp" => normalizedExtension is "webp",
            "image/avif" => normalizedExtension is "avif",
            _ => false
        };
    }

    public static bool MatchesContainer(ReadOnlySpan<byte> bytes, string? mimeType) =>
        TryNormalizeMimeType(mimeType, out string? normalizedMimeType)
        && normalizedMimeType switch
        {
            "image/jpeg" => IsJpegContainer(bytes),
            "image/png" => IsPngContainer(bytes),
            "image/gif" => IsGifContainer(bytes),
            "image/webp" => IsWebpContainer(bytes),
            "image/avif" => IsAvifContainer(bytes),
            _ => false
        };

    public static bool IsSafeRasterMetadata(string? contentType, string? extension) =>
        MatchesExtension(contentType, extension);

    public static bool IsImagePurpose(string? purpose) =>
        purpose is StorageObjectPurposes.LegacyImage
            or StorageObjectPurposes.ProfileImage
            or StorageObjectPurposes.EventImage;

    public static bool IsValidAccessMetadata(
        string? contentType,
        string? extension,
        string? purpose,
        string? visibility)
    {
        bool imagePurpose = IsImagePurpose(purpose);
        bool safeRaster = IsSafeRasterMetadata(contentType, extension);

        return (!imagePurpose || safeRaster)
            && (visibility != StorageObjectVisibilities.PublicImage || imagePurpose && safeRaster);
    }

    public static bool IsSafePublicImageMetadata(StorageObject? storageObject) =>
        storageObject is
        {
            IsDeleted: false,
            LifecycleState: StorageObjectLifecycleStates.Active,
            Visibility: StorageObjectVisibilities.PublicImage
        }
        && IsValidAccessMetadata(
            storageObject.ContentType,
            storageObject.Extension,
            storageObject.Purpose,
            storageObject.Visibility);

    public static bool IsEligibleImageReference(StorageObject? storageObject, Guid tenantId) =>
        tenantId != Guid.Empty
        && storageObject?.TenantId == tenantId
        && IsSafePublicImageMetadata(storageObject);

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
            if (payloadLength > int.MaxValue
                || payloadLength > bytes.Length - offset - 12)
            {
                return false;
            }

            ReadOnlySpan<byte> chunkType = bytes.Slice(offset + 4, 4);
            ReadOnlySpan<byte> payload = bytes.Slice(offset + 8, (int)payloadLength);
            offset += checked(12 + (int)payloadLength);
            if (!seenHeader)
            {
                if (!chunkType.SequenceEqual("IHDR"u8)
                    || !IsPngHeader(payload))
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
                return payload.IsEmpty
                    && seenImageData
                    && offset == bytes.Length;
            }

            if (IsPngCriticalChunk(chunkType)
                && !chunkType.SequenceEqual("PLTE"u8))
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
        chunkType.Length == 4
        && chunkType[0] is >= (byte)'A' and <= (byte)'Z';

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
        return componentCount > 0
            && payload.Length == 6 + (3 * componentCount);
    }

    private static bool IsJpegScanHeader(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 6)
        {
            return false;
        }

        int componentCount = payload[0];
        return componentCount > 0
            && payload.Length == 1 + (2 * componentCount) + 3;
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

    private static bool TrySkipGifColorTable(
        ReadOnlySpan<byte> bytes,
        ref int offset,
        byte packedFields)
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
            if (!TryReadWebpChunk(bytes, ref offset, out ReadOnlySpan<byte> chunkType, out ReadOnlySpan<byte> payload))
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

        return seenImage
            && offset == bytes.Length
            && animationDeclared == seenAnimation;
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
        if (payloadLength > int.MaxValue
            || payloadLength > bytes.Length - offset)
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

    private static bool IsAvifContainer(ReadOnlySpan<byte> bytes)
    {
        int offset = 0;
        bool seenFileType = false;
        bool seenMetadata = false;
        bool seenMediaData = false;
        while (offset < bytes.Length)
        {
            if (!TryReadIsoBox(bytes, ref offset, out ReadOnlySpan<byte> boxType, out ReadOnlySpan<byte> payload))
            {
                return false;
            }

            if (!seenFileType)
            {
                if (!boxType.SequenceEqual("ftyp"u8) || !IsAvifFileType(payload))
                {
                    return false;
                }

                seenFileType = true;
                continue;
            }

            if (boxType.SequenceEqual("meta"u8))
            {
                if (seenMetadata || !IsAvifMetadata(payload))
                {
                    return false;
                }

                seenMetadata = true;
            }
            else if (boxType.SequenceEqual("mdat"u8))
            {
                if (seenMediaData || payload.IsEmpty)
                {
                    return false;
                }

                seenMediaData = true;
            }
        }

        return seenFileType && seenMetadata && seenMediaData;
    }

    private static bool IsAvifFileType(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8 || payload.Length % 4 != 0)
        {
            return false;
        }

        if (IsAvifBrand(payload[..4]))
        {
            return true;
        }

        for (int offset = 8; offset < payload.Length; offset += 4)
        {
            if (IsAvifBrand(payload.Slice(offset, 4)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAvifMetadata(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4 || payload[0] != 0)
        {
            return false;
        }

        int offset = 4;
        bool seenHandler = false;
        bool seenPrimaryItem = false;
        bool seenLocation = false;
        bool seenItemInfo = false;
        bool seenProperties = false;
        while (offset < payload.Length)
        {
            if (!TryReadIsoBox(
                    payload,
                    ref offset,
                    out ReadOnlySpan<byte> boxType,
                    out ReadOnlySpan<byte> childPayload))
            {
                return false;
            }

            seenHandler |= boxType.SequenceEqual("hdlr"u8) && !childPayload.IsEmpty;
            seenPrimaryItem |= boxType.SequenceEqual("pitm"u8) && !childPayload.IsEmpty;
            seenLocation |= boxType.SequenceEqual("iloc"u8) && !childPayload.IsEmpty;
            seenItemInfo |= boxType.SequenceEqual("iinf"u8) && !childPayload.IsEmpty;
            seenProperties |= boxType.SequenceEqual("iprp"u8) && !childPayload.IsEmpty;
        }

        return seenHandler
            && seenPrimaryItem
            && seenLocation
            && seenItemInfo
            && seenProperties;
    }

    private static bool TryReadIsoBox(
        ReadOnlySpan<byte> bytes,
        ref int offset,
        out ReadOnlySpan<byte> boxType,
        out ReadOnlySpan<byte> payload)
    {
        boxType = default;
        payload = default;
        if (bytes.Length - offset < 8)
        {
            return false;
        }

        int boxStart = offset;
        uint compactSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
        boxType = bytes.Slice(offset + 4, 4);
        int headerLength = 8;
        ulong boxSize = compactSize;
        if (compactSize == 1)
        {
            if (bytes.Length - offset < 16)
            {
                return false;
            }

            headerLength = 16;
            boxSize = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(offset + 8, 8));
        }

        if (boxSize < (ulong)headerLength
            || boxSize > int.MaxValue
            || boxSize > (ulong)(bytes.Length - boxStart))
        {
            return false;
        }

        payload = bytes.Slice(boxStart + headerLength, (int)boxSize - headerLength);
        offset = boxStart + (int)boxSize;
        return true;
    }

    private static bool IsAvifBrand(ReadOnlySpan<byte> brand) =>
        brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8);
}
