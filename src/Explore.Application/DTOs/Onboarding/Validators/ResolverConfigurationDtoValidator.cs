using Explore.Application.DTOs.Onboarding;
using FluentValidation;

namespace Explore.Application.DTOs.Onboarding.Validators;

public class ResolverConfigurationDtoValidator : AbstractValidator<ResolverConfigurationDto>
{
    public ResolverConfigurationDtoValidator()
    {
        RuleFor(x => x.HeaderEnabled)
            .Equal(true)
            .WithMessage("Header resolver cannot be disabled because YARP propagation depends on it.");

        RuleFor(x => x)
            .Must(x => x.HeaderEnabled || x.SubdomainEnabled || x.CustomDomainEnabled || x.PathEnabled)
            .WithMessage("At least one tenant resolver must be enabled.");

        RuleFor(x => x.PathPrefix)
            .Must(BeAValidPathPrefix)
            .When(x => x.PathEnabled)
            .WithMessage("PathPrefix must start with '/' and must not end with '/'.");

        RuleFor(x => x.InstanceBaseDomain)
            .NotEmpty()
            .When(x => x.SubdomainEnabled)
            .WithMessage("InstanceBaseDomain is required when the subdomain resolver is enabled.");
    }

    private static bool BeAValidPathPrefix(string? pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
        {
            return false;
        }

        var normalized = pathPrefix.Trim();
        return normalized.StartsWith('/') && normalized.Length > 1 && !normalized.EndsWith('/');
    }
}
