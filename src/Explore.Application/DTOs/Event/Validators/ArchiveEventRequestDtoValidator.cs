using Explore.Application.DTOs.Event;
using FluentValidation;

namespace Explore.Application.DTOs.Event.Validators;

public sealed class ArchiveEventRequestDtoValidator : AbstractValidator<ArchiveEventRequestDto>
{
    public ArchiveEventRequestDtoValidator()
    {
        RuleFor(x => x.ExpectedConcurrencyStamp)
            .NotEqual(Guid.Empty)
            .WithMessage("A non-empty concurrency stamp is required to archive an event.");
    }
}
