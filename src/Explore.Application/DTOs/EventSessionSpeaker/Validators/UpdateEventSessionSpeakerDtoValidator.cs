using FluentValidation;

namespace Explore.Application.DTOs.EventSessionSpeaker.Validators;

public class UpdateEventSessionSpeakerDtoValidator : AbstractValidator<UpdateEventSessionSpeakerDto>
{
    public UpdateEventSessionSpeakerDtoValidator()
    {
        RuleFor(x => x)
            .Must(dto => dto.Session is not null || dto.Actor is not null)
            .WithMessage("At least one event session speaker update group must be provided.");

        When(x => x.Session is not null, () =>
        {
            RuleFor(x => x.Session!.EventSessionId)
                .NotEmpty()
                .WithMessage("EventSessionId is required.");
        });

        When(x => x.Actor is not null, () =>
        {
            RuleFor(x => x.Actor!.ActorId)
                .NotEmpty()
                .WithMessage("ActorId is required.");
        });
    }
}
