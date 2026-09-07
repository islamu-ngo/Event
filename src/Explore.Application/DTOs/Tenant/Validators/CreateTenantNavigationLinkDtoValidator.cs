using Explore.Application.Validation;
using FluentValidation;

namespace Explore.Application.DTOs.Tenant.Validators;

public class CreateTenantNavigationLinkDtoValidator : AbstractValidator<CreateTenantNavigationLinkDto>
{
    /// <summary>
    /// Dangerous URL schemes that must be rejected to prevent XSS and injection attacks.
    /// </summary>
    public CreateTenantNavigationLinkDtoValidator(bool requireHttps = true)
    {
        RuleFor(p => p.Label)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(50).WithMessage("{PropertyName} must not exceed 50 characters.")
            .Must(label => !ContainsControlChars(label)).WithMessage("{PropertyName} contains invalid characters.");

        RuleFor(p => p.Url)
            .NotEmpty().WithMessage("{PropertyName} is required.")
            .MaximumLength(500).WithMessage("{PropertyName} must not exceed 500 characters.")
            .Must(url => !ContainsControlChars(url)).WithMessage("{PropertyName} contains invalid characters.")
            .Must(url => UrlSchemePolicy.IsAllowed(url, requireHttps))
            .WithMessage(requireHttps
                ? "{PropertyName} must be a relative path starting with '/' or an HTTPS URL."
                : "{PropertyName} must be a relative path starting with '/' or an HTTP/HTTPS URL.");

        RuleFor(p => p.Icon)
            .MaximumLength(100).WithMessage("{PropertyName} must not exceed 100 characters.")
            .Must(icon => icon == null || !ContainsControlChars(icon)).WithMessage("{PropertyName} contains invalid characters.");
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
