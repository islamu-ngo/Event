using Explore.Domain.CustomProperties;
using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.DTOs.EventSessionCustomProperty.Validators;

public class CreateEventSessionCustomPropertyDefinitionDtoValidator : AbstractValidator<CreateEventSessionCustomPropertyDefinitionDto>
{
    public CreateEventSessionCustomPropertyDefinitionDtoValidator()
    {
        RuleFor(x => x.EventSessionId)
            .NotEmpty().WithMessage("EventSessionId is required.");

        RuleFor(x => x.Namespace)
            .NotEmpty().WithMessage("Namespace is required.")
            .MaximumLength(100).WithMessage("Namespace must not exceed 100 characters.");

        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Key is required.")
            .MaximumLength(100).WithMessage("Key must not exceed 100 characters.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("DisplayName is required.")
            .MaximumLength(200).WithMessage("DisplayName must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.PropertyType)
            .IsInEnum().WithMessage("PropertyType must be a valid value.");

        RuleFor(x => x.ExposureLevel)
            .IsInEnum().WithMessage("ExposureLevel must be a valid value.");

        RuleFor(x => x.RegexPattern)
            .Must(CustomPropertyPatternPolicy.IsValid)
            .WithMessage("RegexPattern must use supported syntax and not exceed 2048 characters.");

        RuleFor(x => x.AllowedUrlSchemes)
            .MaximumLength(500).WithMessage("AllowedUrlSchemes must not exceed 500 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.AllowedUrlSchemes));

        RuleFor(x => x.MaxLength)
            .GreaterThanOrEqualTo(x => x.MinLength ?? 0)
            .When(x => x.MinLength.HasValue && x.MaxLength.HasValue)
            .WithMessage("MaxLength must be greater than or equal to MinLength.");

        RuleFor(x => x.MinLength).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxLength).GreaterThanOrEqualTo(0);

        RuleFor(x => x.DefaultTextValue)
            .Must((dto, value) => value is null || CustomPropertyPatternPolicy.Matches(value.Trim(), dto.RegexPattern, dto.MinLength, dto.MaxLength))
            .When(x => x.PropertyType is PropertyType.Text or PropertyType.Url)
            .WithMessage("DefaultTextValue must satisfy the configured length and pattern.");

        RuleFor(x => x.DefaultNumberValue)
            .Must((dto, value) => !value.HasValue ||
                ((!dto.MinNumber.HasValue || value >= dto.MinNumber) && (!dto.MaxNumber.HasValue || value <= dto.MaxNumber)))
            .WithMessage("DefaultNumberValue must satisfy the configured bounds.");

        RuleFor(x => x.DefaultDateTimeValue)
            .Must((dto, value) => !value.HasValue ||
                ((!dto.MinDateTime.HasValue || value >= dto.MinDateTime) && (!dto.MaxDateTime.HasValue || value <= dto.MaxDateTime)))
            .WithMessage("DefaultDateTimeValue must satisfy the configured bounds.");

        RuleFor(x => x.MaxNumber)
            .GreaterThanOrEqualTo(x => x.MinNumber ?? decimal.Zero)
            .When(x => x.MinNumber.HasValue && x.MaxNumber.HasValue)
            .WithMessage("MaxNumber must be greater than or equal to MinNumber.");

        RuleFor(x => x.MaxDateTime)
            .GreaterThanOrEqualTo(x => x.MinDateTime ?? DateTimeOffset.MinValue)
            .When(x => x.MinDateTime.HasValue && x.MaxDateTime.HasValue)
            .WithMessage("MaxDateTime must be greater than or equal to MinDateTime.");

        RuleFor(x => x.Options)
            .NotNull().WithMessage("Options is required.");

        RuleFor(x => x.Options)
            .Must(options => options is null || options.Count == 0)
            .When(x => x.PropertyType != PropertyType.Option)
            .WithMessage("Options are only allowed for PropertyType Option.");

        RuleFor(x => x.Options)
            .Must(options => options is null || options.Count > 0)
            .When(x => x.PropertyType == PropertyType.Option)
            .WithMessage("At least one option is required for PropertyType Option.");

        RuleForEach(x => x.Options)
            .NotNull().WithMessage("Options must not contain null items.")
            .SetValidator(new CreateEventSessionCustomPropertyOptionDtoValidator());

        RuleFor(x => x.Options)
            .Must(options => options is null || options.Count(o => o is not null && o.IsDefault) <= 1)
            .WithMessage("Only one option can be marked as default.");

        RuleFor(x => x)
            .Must(HaveMatchingDefaultShape)
            .WithMessage("Default value fields must match the selected PropertyType.");
    }

    private static bool HaveMatchingDefaultShape(CreateEventSessionCustomPropertyDefinitionDto dto)
    {
        var hasText = !string.IsNullOrWhiteSpace(dto.DefaultTextValue);
        var hasNumber = dto.DefaultNumberValue.HasValue;
        var hasBoolean = dto.DefaultBooleanValue.HasValue;
        var hasDateTime = dto.DefaultDateTimeValue.HasValue;

        return dto.PropertyType switch
        {
            PropertyType.Text or PropertyType.Url => !hasNumber && !hasBoolean && !hasDateTime,
            PropertyType.Number => !hasText && !hasBoolean && !hasDateTime,
            PropertyType.Boolean => !hasText && !hasNumber && !hasDateTime,
            PropertyType.DateTime => !hasText && !hasNumber && !hasBoolean,
            PropertyType.Option => !hasText && !hasNumber && !hasBoolean && !hasDateTime,
            _ => false,
        };
    }
}
