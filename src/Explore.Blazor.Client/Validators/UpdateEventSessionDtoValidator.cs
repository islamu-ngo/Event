using Explore.Blazor.Client.Clients;
using FluentValidation;

namespace Explore.Blazor.Client.Validators;

public sealed class UpdateEventSessionDtoValidator : AbstractValidator<UpdateEventSessionDto>
{
    public UpdateEventSessionDtoValidator()
    {
        RuleFor(x => x.Title!.Value!.Value).NotEmpty();
        RuleFor(x => x.Schedule!.StartTime!.Value).NotEmpty();
        RuleFor(x => x.Schedule!.EndTime!.Value)
            .NotEmpty()
            .GreaterThan(x => x.Schedule!.StartTime!.Value);
        RuleFor(x => x.Event!.EventId).NotEmpty();
    }
}
