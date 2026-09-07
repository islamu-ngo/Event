using FluentValidation;

namespace Explore.Application.DTOs.EventSessionGroup.Validators;

public class UpdateEventSessionGroupRequestDtoValidator : AbstractValidator<UpdateEventSessionGroupRequestDto>
{
    public UpdateEventSessionGroupRequestDtoValidator()
    {
        RuleFor(request => request)
            .Must(request => request.Metadata is not null || request.Placement is not null
                || request.Ordering is not null || request.Publication is not null)
            .WithMessage("At least one update group is required.");

        When(request => request.Metadata is not null, () =>
        {
            RuleFor(request => request.Metadata!)
                .Must(metadata => metadata.Name is not null || metadata.Slug.HasValue
                    || metadata.Description.HasValue || metadata.Color.HasValue)
                .WithMessage("Metadata must include at least one value.");
            RuleFor(request => request.Metadata!.Name).MaximumLength(200);
            RuleFor(request => request.Metadata!.Slug.Value).MaximumLength(200)
                .When(request => request.Metadata!.Slug.HasValue);
            RuleFor(request => request.Metadata!.Description.Value).MaximumLength(2000)
                .When(request => request.Metadata!.Description.HasValue);
            RuleFor(request => request.Metadata!.Color.Value).MaximumLength(32)
                .When(request => request.Metadata!.Color.HasValue);
        });
        When(request => request.Placement is not null, () =>
            RuleFor(request => request.Placement!)
                .Must(placement => placement.LocationId.HasValue || placement.RoomId.HasValue)
                .WithMessage("Placement must include at least one value."));
        When(request => request.Ordering is not null, () =>
            RuleFor(request => request.Ordering!.SortOrder).NotNull().GreaterThanOrEqualTo(0));
        When(request => request.Publication is not null, () =>
            RuleFor(request => request.Publication!.IsPublished).NotNull());
    }
}
