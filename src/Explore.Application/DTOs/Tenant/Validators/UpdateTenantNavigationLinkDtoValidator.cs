using Explore.Application.Validation;
using FluentValidation;

namespace Explore.Application.DTOs.Tenant.Validators;

public class UpdateTenantNavigationLinkDtoValidator : AbstractValidator<UpdateTenantNavigationLinkDto>
{
    /// <summary>
    /// Dangerous URL schemes that must be rejected to prevent XSS and injection attacks.
    /// </summary>
    public UpdateTenantNavigationLinkDtoValidator(bool requireHttps = true)
    {
        RuleFor(dto => dto.Label!.Value)
            .NotEmpty().WithMessage("Label is required.")
            .MaximumLength(50).WithMessage("{PropertyName} must not exceed 50 characters.")
            .Must(label => !ContainsControlChars(label)).WithMessage("{PropertyName} contains invalid characters.")
            .When(dto => dto.Label is not null);

        RuleFor(dto => dto.Url!.Value)
            .NotEmpty().WithMessage("Url is required.")
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters.")
            .Must(url => !ContainsControlChars(url)).WithMessage("{PropertyName} contains invalid characters.")
            .Must(url => UrlSchemePolicy.IsAllowed(url, requireHttps))
            .WithMessage(requireHttps
                ? "{PropertyName} must be a relative path starting with '/' or an HTTPS URL."
                : "{PropertyName} must be a relative path starting with '/' or an HTTP/HTTPS URL.")
            .When(dto => dto.Url is not null);

        RuleFor(dto => dto.Icon!)
            .Must(icon => icon.Value.HasValue)
            .When(dto => dto.Icon is not null)
            .WithMessage("Icon group must include Value.");

        RuleFor(dto => dto.Icon!.Value.Value)
            .MaximumLength(100).WithMessage("Icon must not exceed 100 characters.")
            .Must(icon => icon == null || !ContainsControlChars(icon)).WithMessage("Icon contains invalid characters.")
            .When(dto => dto.Icon is not null && dto.Icon.Value.HasValue);

        RuleFor(dto => dto.OpenInNewTab!.Value)
            .NotNull().WithMessage("OpenInNewTab group must include Value.")
            .When(dto => dto.OpenInNewTab is not null);

        RuleFor(dto => dto)
            .Must(dto => dto.Label is not null || dto.Url is not null || dto.Icon is not null || dto.OpenInNewTab is not null)
            .WithMessage("At least one navigation link update group must be provided.");
    }

    private static bool ContainsControlChars(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var c in value)
        {
            if (char.IsControl(c) && c != '\t')
                return true;
        }

        return false;
    }
}
