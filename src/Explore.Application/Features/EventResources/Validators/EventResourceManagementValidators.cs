using Explore.Application.Features.EventResources.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.EventResources.Validators;

public sealed class CreateEventResourceValidator : AbstractValidator<CreateEventResourceCommand>
{
    public CreateEventResourceValidator()
    {
        RuleFor(value => value.EventId).NotEmpty();
        RuleFor(value => value.ResourceId).Must(value => value != Guid.Empty && value.Version == 7)
            .WithMessage("A caller-retained UUIDv7 resource identity is required.");
        RuleFor(value => value.Draft).NotNull().SetValidator(new EventResourceDraftValidator());
    }
}

public sealed class UpdateEventResourceValidator : AbstractValidator<UpdateEventResourceCommand>
{
    public UpdateEventResourceValidator()
    {
        RuleFor(value => value.ResourceId).NotEmpty();
        RuleFor(value => value.ExpectedVersion).NotEmpty();
        RuleFor(value => value.Draft).NotNull().SetValidator(new EventResourceDraftValidator());
    }
}

public sealed class EventResourceVersionValidator : AbstractValidator<(Guid ResourceId, Guid ExpectedVersion)>
{
    public EventResourceVersionValidator()
    {
        RuleFor(value => value.ResourceId).NotEmpty();
        RuleFor(value => value.ExpectedVersion).NotEmpty();
    }
}
