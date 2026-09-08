using Explore.Application.Contracts.Persistence;
using FluentValidation;

namespace Explore.Application.DTOs.Category.Validators;

public class UpdateCategoryDtoValidator : AbstractValidator<UpdateCategoryDto>
{
    private readonly ICategoryRepository _categoryRepository;

    public UpdateCategoryDtoValidator(Guid categoryId, ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;

        RuleFor(dto => dto.MasterCode!)
            .SetValidator(new UpdateCategoryMasterCodeDtoValidator())
            .When(dto => dto.MasterCode is not null);

        RuleFor(dto => dto.FullName!)
            .SetValidator(new UpdateCategoryFullNameDtoValidator())
            .When(dto => dto.FullName is not null);

        RuleFor(dto => dto.Parent!)
            .SetValidator(new UpdateCategoryParentDtoValidator(categoryId, _categoryRepository))
            .When(dto => dto.Parent is not null);

        RuleFor(dto => dto)
            .Must(HasAnyGroup)
            .WithMessage("At least one category update group must be provided.");
    }

    private static bool HasAnyGroup(UpdateCategoryDto dto) =>
        dto.MasterCode is not null ||
        dto.FullName is not null ||
        dto.Parent is not null;
}

public class UpdateCategoryMasterCodeDtoValidator : AbstractValidator<UpdateCategoryMasterCodeDto>
{
    public UpdateCategoryMasterCodeDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Master code is required.")
            .MaximumLength(100).WithMessage("Master code must not exceed 100 characters.");
    }
}

public class UpdateCategoryFullNameDtoValidator : AbstractValidator<UpdateCategoryFullNameDto>
{
    public UpdateCategoryFullNameDtoValidator()
    {
        RuleFor(dto => dto.Value)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(200).WithMessage("Full name must not exceed 200 characters.");
    }
}

public class UpdateCategoryParentDtoValidator : AbstractValidator<UpdateCategoryParentDto>
{
    public UpdateCategoryParentDtoValidator(Guid categoryId, ICategoryRepository categoryRepository)
    {
        RuleFor(dto => dto)
            .Must(dto => dto.ParentId.HasValue)
            .WithMessage("Parent group must include ParentId.");

        RuleFor(dto => dto.ParentId.Value!.Value)
            .MustAsync(async (parentId, cancellation) => await categoryRepository.Exists(parentId))
            .When(dto => dto.ParentId.HasValue && dto.ParentId.Value.HasValue)
            .WithMessage("Parent category does not exist.");

        RuleFor(dto => dto.ParentId.Value!.Value)
            .Must(parentId => parentId != categoryId)
            .When(dto => dto.ParentId.HasValue && dto.ParentId.Value.HasValue)
            .WithMessage("A category cannot be its own parent.");
    }
}
