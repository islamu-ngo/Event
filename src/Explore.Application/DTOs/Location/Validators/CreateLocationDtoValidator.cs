using FluentValidation;

namespace Explore.Application.DTOs.Location.Validators;

public class CreateLocationDtoValidator : AbstractValidator<CreateLocationDto>
{
    public CreateLocationDtoValidator()
    {
        RuleFor(p => p.FullName)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .NotNull()
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters.")
            .When(UsesManualAddress);

        RuleFor(p => p.Address)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .NotNull()
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters.")
            .When(UsesManualAddress);

        RuleFor(p => p.Postcode)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .NotNull()
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters.")
            .When(UsesManualAddress);

        RuleFor(p => p.Country)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .NotNull()
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters.")
            .When(UsesManualAddress);

        RuleFor(p => p.City)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .NotNull()
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters.")
            .When(UsesManualAddress);

        RuleFor(p => p.Timezone)
            .MaximumLength(500).When(p => !string.IsNullOrEmpty(p.Timezone))
            .WithMessage("{PropertyName} must not exceed 500 characters.");

        RuleFor(p => p.AddressSelectionToken)
            .NotEmpty()
            .MaximumLength(8192)
            .When(p => p.AddressSelectionToken is not null);

        RuleFor(p => p.OrganizationId)
            .NotEqual(Guid.Empty)
            .When(p => p.OrganizationId.HasValue);
    }

    private static bool UsesManualAddress(CreateLocationDto dto) =>
        string.IsNullOrWhiteSpace(dto.AddressSelectionToken);
}
