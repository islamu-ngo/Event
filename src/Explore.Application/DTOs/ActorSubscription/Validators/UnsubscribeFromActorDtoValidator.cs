using FluentValidation;

namespace Explore.Application.DTOs.ActorSubscription.Validators;

public class UnsubscribeFromActorDtoValidator : AbstractValidator<UnsubscribeFromActorDto>
{
    public UnsubscribeFromActorDtoValidator()
    {
        RuleFor(dto => dto.TargetActorId)
            .NotEmpty().WithMessage("Target actor ID is required.");

        RuleFor(dto => dto.ExpectedConcurrencyStamp)
            .NotEmpty().WithMessage("Expected concurrency stamp is required.");
    }
}
