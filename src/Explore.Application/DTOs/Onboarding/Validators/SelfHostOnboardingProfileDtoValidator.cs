using FluentValidation;

namespace Explore.Application.DTOs.Onboarding.Validators;

public sealed class SelfHostOnboardingProfileDtoValidator : AbstractValidator<SelfHostOnboardingProfileDto>
{
    public SelfHostOnboardingProfileDtoValidator()
    {
        RuleFor(x => x.SiteName)
            .NotEmpty()
            .WithMessage("SiteName is required.")
            .MaximumLength(200)
            .WithMessage("SiteName must not exceed 200 characters.");

        RuleFor(x => x.SupportEmail)
            .EmailAddress()
            .WithMessage("SupportEmail must be a valid email address.")
            .When(x => !string.IsNullOrWhiteSpace(x.SupportEmail));

        RuleFor(x => x.CanonicalUrl)
            .Must(BeAbsoluteHttpUrl)
            .WithMessage("CanonicalUrl must be an absolute http or https URL.")
            .When(x => !string.IsNullOrWhiteSpace(x.CanonicalUrl));

        RuleFor(x => x.Locale)
            .NotEmpty()
            .WithMessage("Locale is required.")
            .MaximumLength(20)
            .WithMessage("Locale must not exceed 20 characters.");

        RuleFor(x => x.TimeZone)
            .NotEmpty()
            .WithMessage("TimeZone is required.")
            .MaximumLength(100)
            .WithMessage("TimeZone must not exceed 100 characters.")
            .Must(BeValidTimeZone)
            .WithMessage("TimeZone must be a valid system time zone identifier.");

        RuleFor(x => x.Purpose)
            .MaximumLength(500)
            .WithMessage("Purpose must not exceed 500 characters.")
            .When(x => x.Purpose is not null);
    }

    private static bool BeAbsoluteHttpUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static bool BeValidTimeZone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(value.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
