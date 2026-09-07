using Explore.Domain.Enums;
using Explore.Domain.References;
using FluentValidation;

namespace Explore.Application.DTOs.CustomPropertyDefinition.Validators;

public class CreateCustomPropertyDefinitionDtoValidator : AbstractValidator<CreateCustomPropertyDefinitionDto>
{
    public CreateCustomPropertyDefinitionDtoValidator()
    {
        RuleFor(x => x.EntityTypeName)
            .Must(ReferenceTypeRegistry.SupportsSharedCustomPropertyDefinitions)
            .WithMessage("Only Organization and Group shared custom-property definitions are supported in this slice.");

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

        RuleFor(x => x.RegexPattern)
            .MaximumLength(1000).WithMessage("RegexPattern must not exceed 1000 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.RegexPattern));

        RuleFor(x => x.AllowedUrlSchemes)
            .MaximumLength(500).WithMessage("AllowedUrlSchemes must not exceed 500 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.AllowedUrlSchemes));

        RuleFor(x => x.MaxLength)
            .GreaterThanOrEqualTo(x => x.MinLength ?? 0)
            .When(x => x.MinLength.HasValue && x.MaxLength.HasValue)
            .WithMessage("MaxLength must be greater than or equal to MinLength.");

        RuleFor(x => x.MaxNumber)
            .GreaterThanOrEqualTo(x => x.MinNumber ?? decimal.Zero)
            .When(x => x.MinNumber.HasValue && x.MaxNumber.HasValue)
            .WithMessage("MaxNumber must be greater than or equal to MinNumber.");

        RuleFor(x => x.MaxDateTime)
            .GreaterThanOrEqualTo(x => x.MinDateTime ?? DateTimeOffset.MinValue)
            .When(x => x.MinDateTime.HasValue && x.MaxDateTime.HasValue)
            .WithMessage("MaxDateTime must be greater than or equal to MinDateTime.");

        RuleFor(x => x.Options)
            .Must(options => options.Count == 0)
            .When(x => x.PropertyType != PropertyType.Option)
            .WithMessage("Options are only allowed for PropertyType Option.");

        RuleFor(x => x.Options)
            .Must(options => options.Count > 0)
            .When(x => x.PropertyType == PropertyType.Option)
            .WithMessage("At least one option is required for PropertyType Option.");

        RuleForEach(x => x.Options)
            .SetValidator(new CreateCustomPropertyOptionDtoValidator());

        RuleFor(x => x.Options)
            .Must(options => options.Count(o => o.IsDefault) <= 1)
            .WithMessage("Only one option can be marked as default.");

        RuleFor(x => x)
            .Must(HaveMatchingDefaultShape)
            .WithMessage("Default value fields must match the selected PropertyType.");
    }

    private static bool HaveMatchingDefaultShape(CreateCustomPropertyDefinitionDto dto)
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
