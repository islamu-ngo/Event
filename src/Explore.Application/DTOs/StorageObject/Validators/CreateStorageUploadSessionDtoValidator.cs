using System.Net.Http.Headers;
using Explore.Application.Services;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.DTOs.StorageObject.Validators;

public class CreateStorageUploadSessionDtoValidator : AbstractValidator<CreateStorageUploadSessionDto>
{
    private const int MaxFileNameLength = 500;
    private const int MaxContentTypeLength = 255;
    private const int MaxExtensionLength = 50;
    private const int MaxResourceKindLength = 100;
    private const int MaxIdempotencyKeyLength = 128;

    public CreateStorageUploadSessionDtoValidator()
    {
        RuleFor(x => x.ExpectedSizeBytes)
            .GreaterThan(0).WithMessage("{PropertyName} must be greater than zero");

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage("{PropertyName} is required")
            .MaximumLength(MaxContentTypeLength).WithMessage("{PropertyName} must not exceed 255 characters")
            .Must(NotContainControlCharacters).WithMessage("{PropertyName} must not contain control characters")
            .Must(BeValidContentType).WithMessage("{PropertyName} must be a valid MIME type");

        RuleFor(x => x.OriginalFileName)
            .MaximumLength(MaxFileNameLength).WithMessage("{PropertyName} must not exceed 500 characters")
            .Must(NotContainControlCharacters).WithMessage("{PropertyName} must not contain control characters")
            .Must(NotContainPathSeparators).WithMessage("{PropertyName} must not contain path separators")
            .Must(NotBeDotSegment).WithMessage("{PropertyName} must be a simple file name")
            .Must(NotBeReservedFileName).WithMessage("{PropertyName} must not be a reserved file name");

        RuleFor(x => x.SafeDisplayName)
            .MaximumLength(MaxFileNameLength).WithMessage("{PropertyName} must not exceed 500 characters")
            .Must(NotContainControlCharacters).WithMessage("{PropertyName} must not contain control characters")
            .Must(NotContainPathSeparators).WithMessage("{PropertyName} must not contain path separators")
            .Must(NotBeDotSegment).WithMessage("{PropertyName} must be a simple file name")
            .Must(NotBeReservedFileName).WithMessage("{PropertyName} must not be a reserved file name");

        RuleFor(x => x.Extension)
            .MaximumLength(MaxExtensionLength).WithMessage("{PropertyName} must not exceed 50 characters")
            .Must(NotContainControlCharacters).WithMessage("{PropertyName} must not contain control characters")
            .Must(NotContainPathSeparators).WithMessage("{PropertyName} must not contain path separators")
            .Must(NotBeDotSegment).WithMessage("{PropertyName} must be a simple extension")
            .Must(BeValidExtension).WithMessage("{PropertyName} contains unsupported characters");

        RuleFor(x => x.Purpose)
            .NotEmpty().WithMessage("{PropertyName} is required")
            .Must(value => StorageObjectPurposes.All.Contains(value))
            .WithMessage("{PropertyName} must be a supported storage purpose");

        RuleFor(x => x.Visibility)
            .NotEmpty().WithMessage("{PropertyName} is required")
            .Must(value => StorageObjectVisibilities.All.Contains(value))
            .WithMessage("{PropertyName} must be a supported storage visibility");

        RuleFor(x => x)
            .Must(HaveValidAccessMetadata)
            .WithMessage("Purpose, Visibility, ContentType, and Extension must describe an eligible storage object.");

        RuleFor(x => x.OwningResourceKind)
            .MaximumLength(MaxResourceKindLength).WithMessage("{PropertyName} must not exceed 100 characters")
            .NotEmpty().When(x => x.OwningResourceId.HasValue)
            .WithMessage("{PropertyName} is required when OwningResourceId is provided");

        RuleFor(x => x.OwningResourceId)
            .NotEmpty().When(x => !string.IsNullOrWhiteSpace(x.OwningResourceKind))
            .WithMessage("{PropertyName} is required when OwningResourceKind is provided");

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty().WithMessage("{PropertyName} is required")
            .MaximumLength(MaxIdempotencyKeyLength).WithMessage("{PropertyName} must not exceed 128 characters")
            .Matches("^[A-Za-z0-9._:-]+$").WithMessage("{PropertyName} contains unsupported characters");
    }

    private static bool BeValidContentType(string? value)
    {
        var candidate = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) ||
            !MediaTypeHeaderValue.TryParse(candidate, out var mediaTypeHeader) ||
            string.IsNullOrWhiteSpace(mediaTypeHeader.MediaType))
        {
            return false;
        }

        var mediaTypeParts = mediaTypeHeader.MediaType.Split('/', 2, StringSplitOptions.TrimEntries);
        return mediaTypeParts is [{ Length: > 0 }, { Length: > 0 }] &&
            mediaTypeParts.All(part => !part.Contains("*", StringComparison.Ordinal));
    }

    private static bool NotContainControlCharacters(string? value)
        => value is null || !value.Any(char.IsControl);

    private static bool NotContainPathSeparators(string? value)
        => value is null || (!value.Contains("/", StringComparison.Ordinal) && !value.Contains("\\", StringComparison.Ordinal));

    private static bool NotBeDotSegment(string? value)
    {
        var candidate = value?.Trim();
        return string.IsNullOrWhiteSpace(candidate) || candidate is not "." and not "..";
    }

    private static bool NotBeReservedFileName(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return true;
        }

        var stem = candidate.Split('.', 2)[0];
        return !ReservedFileNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    private static bool BeValidExtension(string? value)
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

    private static bool HaveValidAccessMetadata(CreateStorageUploadSessionDto dto) =>
        SafeRasterContentPolicy.IsValidAccessMetadata(
            dto.ContentType,
            ResolveExtension(dto),
            dto.Purpose,
            dto.Visibility);

    private static string? ResolveExtension(CreateStorageUploadSessionDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.Extension))
        {
            return dto.Extension;
        }

        string? fileName = dto.OriginalFileName?.Trim();
        int dotIndex = fileName?.LastIndexOf('.') ?? -1;
        return dotIndex >= 0 && dotIndex < fileName!.Length - 1
            ? fileName[(dotIndex + 1)..]
            : null;
    }

    private static readonly string[] ReservedFileNames =
    [
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
    ];
}
