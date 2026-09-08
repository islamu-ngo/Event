using FluentValidation;

namespace Explore.Application.DTOs.EventSessionAgendaItem.Validators;

public class UpdateEventSessionAgendaItemDtoValidator : AbstractValidator<UpdateEventSessionAgendaItemDto>
{
    public UpdateEventSessionAgendaItemDtoValidator()
    {
        RuleFor(request => request)
            .Must(request => request.Relationship is not null || request.Content is not null
                || request.Schedule is not null || request.Location is not null)
            .WithMessage("At least one update group is required.");

        When(request => request.Relationship is not null, () =>
            RuleFor(request => request.Relationship!.EventSessionId).NotEmpty());
        When(request => request.Content is not null, () =>
        {
            RuleFor(request => request.Content!)
                .Must(content => content.Title is not null || content.Description.HasValue)
                .WithMessage("Content must include at least one value.");
            RuleFor(request => request.Content!.Title).MaximumLength(500);
            RuleFor(request => request.Content!.Description.Value).MaximumLength(500)
                .When(request => request.Content!.Description.HasValue);
        });
        When(request => request.Schedule is not null, () =>
            RuleFor(request => request.Schedule!)
                .Must(schedule => schedule.StartTime.HasValue || schedule.EndTime.HasValue)
                .WithMessage("Schedule must include at least one value."));
        When(request => request.Location is not null, () =>
            RuleFor(request => request.Location!.Value.HasValue)
                .Equal(true).WithMessage("Location must include a value."));
    }
}
