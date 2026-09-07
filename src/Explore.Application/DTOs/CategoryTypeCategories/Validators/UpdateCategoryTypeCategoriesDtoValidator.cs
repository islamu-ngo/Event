using FluentValidation;

namespace Explore.Application.DTOs.CategoryTypeCategories.Validators;

public class UpdateCategoryTypeCategoriesDtoValidator : AbstractValidator<UpdateCategoryTypeCategoriesDto>
{
    public UpdateCategoryTypeCategoriesDtoValidator()
    {
        RuleFor(request => request.Relationship).NotNull();
        When(request => request.Relationship is not null, () =>
        {
            RuleFor(request => request.Relationship!)
                .Must(relationship => relationship.CategoryId.HasValue || relationship.CategoryTypeId.HasValue)
                .WithMessage("Relationship must include at least one value.");
            RuleFor(request => request.Relationship!.CategoryId)
                .Must(id => !id.HasValue || id.Value != Guid.Empty)
                .WithMessage("Category is required.");
            RuleFor(request => request.Relationship!.CategoryTypeId)
                .Must(id => !id.HasValue || id.Value > 0)
                .WithMessage("Category Type is required.");
        });
    }
}
