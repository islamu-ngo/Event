using FluentValidation;

namespace Explore.Application.DTOs.EventOrganizerClaim.Validators;

public sealed class ReviewEventOrganizerClaimDtoValidator : AbstractValidator<ReviewEventOrganizerClaimDto>
{
    public ReviewEventOrganizerClaimDtoValidator()
    {
        RuleFor(dto => dto.Decision).IsInEnum();
        RuleFor(dto => dto.ReasonCode).NotEmpty().MaximumLength(80);
        RuleFor(dto => dto.ExpectedConcurrencyStamp).NotEmpty();
    }
}
