using FluentValidation;

namespace Explore.Application.DTOs.EventOrganizerClaim.Validators;

public sealed class SubmitEventOrganizerClaimDtoValidator : AbstractValidator<SubmitEventOrganizerClaimDto>
{
    public SubmitEventOrganizerClaimDtoValidator()
    {
        RuleFor(dto => dto.ClaimantActorId).NotEmpty();
        RuleFor(dto => dto.EvidenceType).NotEmpty().MaximumLength(80);
        RuleFor(dto => dto.EvidenceReference).NotEmpty().MaximumLength(512);
    }
}
