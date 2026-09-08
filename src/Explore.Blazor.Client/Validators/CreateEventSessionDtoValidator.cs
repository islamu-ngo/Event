using Explore.Blazor.Client.Clients;
using FluentValidation;

namespace Explore.Blazor.Client.Validators;

public sealed class CreateEventSessionDtoValidator : AbstractValidator<CreateEventSessionDto>
{
    public CreateEventSessionDtoValidator()
    {
        RuleFor(x => x.Title).NotEmpty();
        RuleFor(x => x.StartTime).NotEmpty();
        RuleFor(x => x.EndTime).NotEmpty().GreaterThan(x => x.StartTime);
        RuleFor(x => x.EventId).NotEmpty();
    }
}
