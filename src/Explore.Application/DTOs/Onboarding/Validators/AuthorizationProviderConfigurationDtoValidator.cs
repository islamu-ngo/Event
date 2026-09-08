using Explore.Application.Utilities;
using FluentValidation;

namespace Explore.Application.DTOs.Onboarding.Validators;

public class AuthorizationProviderConfigurationDtoValidator : AbstractValidator<AuthorizationProviderConfigurationDto>
{
    private static readonly string[] ValidProviders = ["cerbos", "local"];

    public AuthorizationProviderConfigurationDtoValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty()
            .WithMessage("Authorization provider must be specified.")
            .Must(p => ValidProviders.Contains(p, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Authorization provider must be 'cerbos' or 'local'.");

        When(x => x.Provider.Equals("cerbos", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.CerbosGrpcEndpoint)
                .NotEmpty()
                .WithMessage("Cerbos gRPC endpoint is required when Cerbos is selected as authorization provider.");

            RuleFor(x => x.CerbosGrpcEndpoint)
                .Must(BeAValidGrpcEndpoint)
                .When(x => !string.IsNullOrWhiteSpace(x.CerbosGrpcEndpoint))
                .WithMessage("Cerbos gRPC endpoint must be a valid URL (e.g., https://cerbosgrpc.example.com:443).");
        });

        RuleFor(x => x.CerbosAdminEndpoint)
            .MaximumLength(512)
            .WithMessage("Cerbos Admin API endpoint must be 512 characters or fewer.");

    }

    private static bool BeAValidGrpcEndpoint(string? endpoint)
    {
        return GrpcEndpointNormalizer.IsValid(endpoint);
    }
}
