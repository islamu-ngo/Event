using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryTypeCategories.Validators;
using Explore.Application.Features.CategoryTypeCategories.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Commands;

public class UpdateCategoryTypeCategoriesCommandHandler : IRequestHandler<UpdateCategoryTypeCategoriesCommand, BaseCommandResponse<Guid>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryTypeRepository _categoryTypeRepository;
    private readonly ITenantContext _tenantContext;

    public UpdateCategoryTypeCategoriesCommandHandler(
        ICategoryTypeCategoriesRepository repository,
        ICategoryRepository categoryRepository,
        ICategoryTypeRepository categoryTypeRepository,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _categoryRepository = categoryRepository;
        _categoryTypeRepository = categoryTypeRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateCategoryTypeCategoriesCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateCategoryTypeCategoriesDtoValidator();
        var validationResult = await validator.ValidateAsync(request.CategoryTypeCategoriesDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Category Type Categories update failed.");
        }

        var link = await _repository.GetById(request.CategoryTypeCategoriesId);
        if (link == null || link.TenantId != _tenantContext.TenantId)
        {
            const string message = "Category Type Categories not found.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        Guid categoryId = request.CategoryTypeCategoriesDto.Relationship?.CategoryId ?? link.CategoryId;
        int categoryTypeId = request.CategoryTypeCategoriesDto.Relationship?.CategoryTypeId ?? link.CategoryTypeId;
        var category = await _categoryRepository.GetById(categoryId);
        if (category is null || category.TenantId != link.TenantId || !await _categoryTypeRepository.Exists(categoryTypeId))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Relationship targets were not found in the current tenant."],
                "Category Type Categories update failed.");
        }

        if ((categoryId != link.CategoryId || categoryTypeId != link.CategoryTypeId)
            && await _repository.Exists(categoryId, categoryTypeId))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Category and Category Type relationship already exists."],
                "Category Type Categories update failed.");
        }

        link.CategoryId = categoryId;
        link.CategoryTypeId = categoryTypeId;
        await _repository.Update(link);

        return BaseCommandResponse.Success(link.Id, "Category Type Categories updated successfully.");
    }
}
