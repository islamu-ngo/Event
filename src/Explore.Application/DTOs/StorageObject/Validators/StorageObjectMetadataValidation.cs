using System.Net.Http.Headers;

namespace Explore.Application.DTOs.StorageObject.Validators;

internal static class StorageObjectMetadataValidation
{
    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    public static bool BeValidRequiredContentType(string? value)
    {
        var candidate = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) ||
            !MediaTypeHeaderValue.TryParse(candidate, out var mediaTypeHeader) ||
            string.IsNullOrWhiteSpace(mediaTypeHeader.MediaType))
        {
            return false;
        }

        var mediaTypeParts = mediaTypeHeader.MediaType.Split('/', StringSplitOptions.TrimEntries);
        return mediaTypeParts is [{ Length: > 0 }, { Length: > 0 }] &&
            mediaTypeParts.All(part => !part.Contains("*", StringComparison.Ordinal));
    }

    public static bool BeValidOptionalContentType(string? value)
        => string.IsNullOrWhiteSpace(value) || BeValidRequiredContentType(value);

    public static bool NotContainControlCharacters(string? value)
        => value is null || !value.Any(char.IsControl);

    public static bool NotContainPathSeparators(string? value)
        => value is null || (!value.Contains("/", StringComparison.Ordinal) && !value.Contains("\\", StringComparison.Ordinal));

    public static bool NotBeDotSegment(string? value)
    {
        var candidate = value?.Trim();
        return string.IsNullOrWhiteSpace(candidate) || candidate is not "." and not "..";
    }

    public static bool NotBeReservedFileName(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return true;
        }

        var stem = candidate.Split('.', 2)[0];
        return !ReservedFileNames.Contains(stem);
    }

    public static bool BeValidExtension(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return true;
        }

        var extension = candidate.StartsWith('.')
            ? candidate[1..]
            : candidate;
        return extension.Length > 0 &&
            extension.All(character => char.IsLetterOrDigit(character) || character is '_' or '-');
    }

    public static bool BeValidSha256HexDigest(string? value)
    {
        var candidate = value?.Trim();
        return string.IsNullOrWhiteSpace(candidate) ||
            candidate.Length == 64 && candidate.All(Uri.IsHexDigit);
    }

    public static bool BeValidObjectKey(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return true;
        }

        if (candidate.Length != value!.Length ||
            candidate.StartsWith('/') ||
            candidate.Contains("\\", StringComparison.Ordinal) ||
            candidate.Contains("?", StringComparison.Ordinal) ||
            candidate.Contains("#", StringComparison.Ordinal) ||
            candidate.Any(char.IsControl))
        {
            return false;
        }

        var segments = candidate.Split('/');
        return segments.All(segment => segment.Length > 0 && segment is not "." and not "..");
    }
}
