using FluentValidation;

namespace Explore.Application.DTOs.Organization.Validators;

public class UpdateOrganizationDtoValidator : AbstractValidator<UpdateOrganizationDto>
{
    public UpdateOrganizationDtoValidator()
    {
        RuleFor(dto => dto.FullName!)
            .SetValidator(new UpdateOrganizationFullNameDtoValidator())
            .When(dto => dto.FullName is not null);

        RuleFor(dto => dto.WebsiteUrl!)
            .SetValidator(new UpdateOrganizationWebsiteUrlDtoValidator())
            .When(dto => dto.WebsiteUrl is not null);

        RuleFor(dto => dto.Email!)
            .SetValidator(new UpdateOrganizationEmailDtoValidator())
            .When(dto => dto.Email is not null);

        RuleFor(dto => dto.Country!)
            .SetValidator(new UpdateOrganizationCountryDtoValidator())
            .When(dto => dto.Country is not null);

        RuleFor(dto => dto.City!)
            .SetValidator(new UpdateOrganizationCityDtoValidator())
            .When(dto => dto.City is not null);

        RuleFor(dto => dto.Postcode!)
            .SetValidator(new UpdateOrganizationPostcodeDtoValidator())
            .When(dto => dto.Postcode is not null);

        RuleFor(dto => dto.Address!)
            .SetValidator(new UpdateOrganizationAddressDtoValidator())
            .When(dto => dto.Address is not null);

        RuleFor(dto => dto)
            .Must(HasAnyGroup)
            .WithMessage("At least one organization update group must be provided.");
    }

    private static bool HasAnyGroup(UpdateOrganizationDto dto) =>
        dto.FullName is not null ||
        dto.WebsiteUrl is not null ||
        dto.Email is not null ||
        dto.Country is not null ||
        dto.City is not null ||
        dto.Postcode is not null ||
        dto.Address is not null;
}

public class UpdateOrganizationFullNameDtoValidator : AbstractValidator<UpdateOrganizationFullNameDto>
{
    public UpdateOrganizationFullNameDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Organization name is required.")
            .MaximumLength(500).WithMessage("Organization name must not exceed 500 characters.");
    }
}

public class UpdateOrganizationWebsiteUrlDtoValidator : AbstractValidator<UpdateOrganizationWebsiteUrlDto>
{
    public UpdateOrganizationWebsiteUrlDtoValidator()
    {
        RuleFor(dto => dto)
            .Must(dto => dto.Value.HasValue)
            .WithMessage("WebsiteUrl group must include Value.");

        RuleFor(dto => dto.Value.Value)
            .MaximumLength(200).WithMessage("Website URL must not exceed 200 characters.")
            .Must(uri => Uri.IsWellFormedUriString(uri, UriKind.Absolute))
            .WithMessage("Website URL must be a valid URI.")
            .When(dto => dto.Value.HasValue && dto.Value.Value is not null);
    }
}

public class UpdateOrganizationEmailDtoValidator : AbstractValidator<UpdateOrganizationEmailDto>
{
    public UpdateOrganizationEmailDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.");
    }
}

public class UpdateOrganizationCountryDtoValidator : AbstractValidator<UpdateOrganizationCountryDto>
{
    public UpdateOrganizationCountryDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Country is required.")
            .MaximumLength(200).WithMessage("Country must not exceed 200 characters.");
    }
}

public class UpdateOrganizationCityDtoValidator : AbstractValidator<UpdateOrganizationCityDto>
{
    public UpdateOrganizationCityDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(200).WithMessage("City must not exceed 200 characters.");
    }
}

public class UpdateOrganizationPostcodeDtoValidator : AbstractValidator<UpdateOrganizationPostcodeDto>
{
    public UpdateOrganizationPostcodeDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .GreaterThan(0).WithMessage("Postcode must be greater than 0.");
    }
}

public class UpdateOrganizationAddressDtoValidator : AbstractValidator<UpdateOrganizationAddressDto>
{
    public UpdateOrganizationAddressDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Address is required.")
            .MaximumLength(500).WithMessage("Address must not exceed 500 characters.");
    }
}
