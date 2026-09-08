using Explore.Application.DTOs.EventSession;
using FluentValidation;

namespace Explore.Application.DTOs.EventSession.Validators;

public sealed class ScheduleEventSessionRequestDtoValidator : AbstractValidator<ScheduleEventSessionRequestDto>
{
    public ScheduleEventSessionRequestDtoValidator()
    {
        RuleFor(x => x.ExpectedConcurrencyStamp)
            .NotEqual(Guid.Empty)
            .WithMessage("A non-empty concurrency stamp is required to schedule an event session.");

        RuleFor(x => x.StartTime)
            .NotEqual(default(DateTimeOffset))
            .WithMessage("StartTime is required.");

        RuleFor(x => x.EndTime)
            .NotEqual(default(DateTimeOffset))
            .WithMessage("EndTime is required.")
            .GreaterThan(x => x.StartTime)
            .WithMessage("EndTime must be after StartTime.");
    }
}
