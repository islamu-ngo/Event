using FluentValidation;

namespace Explore.Application.DTOs.ActorSubscription.Validators;

public class SubscribeToActorDtoValidator : AbstractValidator<SubscribeToActorDto>
{
    public SubscribeToActorDtoValidator()
    {
        RuleFor(dto => dto.TargetActorId)
            .NotEmpty().WithMessage("Target actor ID is required.");
    }
}
