using FluentValidation;

namespace Explore.Application.DTOs.Integrations.Validators;

public sealed class ResolveIntegrationSyncAmbiguityDtoValidator : AbstractValidator<ResolveIntegrationSyncAmbiguityDto>
{
    public ResolveIntegrationSyncAmbiguityDtoValidator()
    {
        RuleFor(request => request.Decision).IsInEnum();
        RuleFor(request => request.EvidenceReference).NotEmpty().MaximumLength(200);
    }
}
