using Explore.Domain.ValueObjects;
using FluentValidation;

namespace Explore.Application.DTOs.EventResource.Validators;

public sealed class CreateEventResourceUploadSessionDtoValidator : AbstractValidator<CreateEventResourceUploadSessionDto>
{
    public CreateEventResourceUploadSessionDtoValidator()
    {
        RuleFor(value => value.ExpectedVersion).NotEmpty();
        RuleFor(value => value.ExpectedSizeBytes).GreaterThan(0);
        RuleFor(value => value.ContentType).Must(value => ExtensionFor(value) is not null);
        RuleFor(value => value.SafeDisplayName).NotEmpty().MaximumLength(200)
            .Must(value => !string.IsNullOrWhiteSpace(value) && value == value.Trim()
                && !value.Any(character => char.IsControl(character) || character is '/' or '\\' or ':' or '"' or '<' or '>' or '|')
                && value is not "." and not "..");
        RuleFor(value => value).Must(value => ExtensionFor(value.ContentType) is { } expected
            && (value.Extension is null || value.Extension.TrimStart('.').Equals(expected, StringComparison.OrdinalIgnoreCase))
            && string.Equals(Path.GetExtension(value.SafeDisplayName), "." + expected, StringComparison.OrdinalIgnoreCase));
        RuleFor(value => value.IdempotencyKey).NotEmpty().MaximumLength(128).Matches("^[A-Za-z0-9._:-]+$");
    }

    public static string? ExtensionFor(string? contentType) => contentType switch
    {
        EventResourceGovernancePolicy.PdfMediaType => "pdf",
        EventResourceGovernancePolicy.WordDocumentMediaType => "docx",
        EventResourceGovernancePolicy.PowerPointPresentationMediaType => "pptx",
        _ => null
    };
}
