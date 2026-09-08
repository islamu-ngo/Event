using FluentValidation;

namespace Explore.Application.DTOs.TagTypeTags.Validators;

public class UpdateTagTypeTagsDtoValidator : AbstractValidator<UpdateTagTypeTagsDto>
{
    public UpdateTagTypeTagsDtoValidator()
    {
        RuleFor(request => request.Relationship).NotNull();
        When(request => request.Relationship is not null, () =>
        {
            RuleFor(request => request.Relationship!)
                .Must(relationship => relationship.TagId.HasValue || relationship.TagTypeId.HasValue)
                .WithMessage("Relationship must include at least one value.");
            RuleFor(request => request.Relationship!.TagId)
                .Must(id => !id.HasValue || id.Value != Guid.Empty)
                .WithMessage("Tag is required.");
            RuleFor(request => request.Relationship!.TagTypeId)
                .Must(id => !id.HasValue || id.Value > 0)
                .WithMessage("Tag Type is required.");
        });
    }
}
