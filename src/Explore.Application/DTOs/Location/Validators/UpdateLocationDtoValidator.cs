using FluentValidation;
using FluentValidation.Results;

namespace Explore.Application.DTOs.Location.Validators;

public class UpdateLocationDtoValidator : AbstractValidator<UpdateLocationDto>
{
    public UpdateLocationDtoValidator()
    {
        var fullNameValidator = new UpdateLocationFullNameDtoValidator();
        var addressValidator = new UpdateLocationAddressDtoValidator();
        var postcodeValidator = new UpdateLocationPostcodeDtoValidator();
        var countryValidator = new UpdateLocationCountryDtoValidator();
        var cityValidator = new UpdateLocationCityDtoValidator();
        var timezoneValidator = new UpdateLocationTimezoneDtoValidator();

        RuleFor(dto => dto).Custom((dto, context) =>
        {
            if (dto.FullName is { } fullName)
            {
                AddFailures(context, nameof(dto.FullName), fullNameValidator.Validate(fullName));
            }
            if (dto.Address is { } address)
            {
                AddFailures(context, nameof(dto.Address), addressValidator.Validate(address));
            }
            if (dto.Postcode is { } postcode)
            {
                AddFailures(context, nameof(dto.Postcode), postcodeValidator.Validate(postcode));
            }
            if (dto.Country is { } country)
            {
                AddFailures(context, nameof(dto.Country), countryValidator.Validate(country));
            }
            if (dto.City is { } city)
            {
                AddFailures(context, nameof(dto.City), cityValidator.Validate(city));
            }
            if (dto.Timezone is { } timezone)
            {
                AddFailures(context, nameof(dto.Timezone), timezoneValidator.Validate(timezone));
            }
        });

        RuleFor(dto => dto)
            .Must(HasAnyGroup)
            .WithMessage("At least one location update group must be provided.");

        RuleFor(dto => dto.AddressSelectionToken)
            .NotEmpty()
            .MaximumLength(8192)
            .When(dto => dto.AddressSelectionToken is not null);

        RuleFor(dto => dto.OrganizationId)
            .NotEqual(Guid.Empty)
            .When(dto => dto.OrganizationId.HasValue);

        RuleFor(dto => dto)
            .Must(HasUnambiguousAddressInput)
            .WithMessage(
                "AddressSelectionToken cannot be combined with manual location update groups.");
    }

    private static void AddFailures(
        ValidationContext<UpdateLocationDto> context,
        string groupName,
        ValidationResult result)
    {
        foreach (ValidationFailure failure in result.Errors)
        {
            string propertyName = string.IsNullOrEmpty(failure.PropertyName)
                ? groupName
                : $"{groupName}.{failure.PropertyName}";
            context.AddFailure(propertyName, failure.ErrorMessage);
        }
    }

    private static bool HasAnyGroup(UpdateLocationDto dto) =>
        dto.FullName is not null ||
        dto.Address is not null ||
        dto.Postcode is not null ||
        dto.Country is not null ||
        dto.City is not null ||
        dto.Timezone is not null ||
        dto.AddressSelectionToken is not null;

    private static bool HasUnambiguousAddressInput(UpdateLocationDto dto) =>
        dto.AddressSelectionToken is null
        || (dto.FullName is null
            && dto.Address is null
            && dto.Postcode is null
            && dto.Country is null
            && dto.City is null
            && dto.Timezone is null);
}

public class UpdateLocationFullNameDtoValidator : AbstractValidator<UpdateLocationFullNameDto>
{
    public UpdateLocationFullNameDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(500).WithMessage("Full name must not exceed 500 characters.");
    }
}

public class UpdateLocationAddressDtoValidator : AbstractValidator<UpdateLocationAddressDto>
{
    public UpdateLocationAddressDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Address is required.")
            .MaximumLength(500).WithMessage("Address must not exceed 500 characters.");
    }
}

public class UpdateLocationPostcodeDtoValidator : AbstractValidator<UpdateLocationPostcodeDto>
{
    public UpdateLocationPostcodeDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Postcode is required.")
            .MaximumLength(500).WithMessage("Postcode must not exceed 500 characters.");
    }
}

public class UpdateLocationCountryDtoValidator : AbstractValidator<UpdateLocationCountryDto>
{
    public UpdateLocationCountryDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Country is required.")
            .MaximumLength(500).WithMessage("Country must not exceed 500 characters.");
    }
}

public class UpdateLocationCityDtoValidator : AbstractValidator<UpdateLocationCityDto>
{
    public UpdateLocationCityDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(500).WithMessage("City must not exceed 500 characters.");
    }
}

public class UpdateLocationTimezoneDtoValidator : AbstractValidator<UpdateLocationTimezoneDto>
{
    public UpdateLocationTimezoneDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("Timezone group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .NotEmpty().WithMessage("Timezone must not be blank. Use null to clear it.")
            .MaximumLength(500).WithMessage("Timezone must not exceed 500 characters.")
            .When(dto => dto.Value.HasValue && dto.Value.Value is not null);
    }
}
