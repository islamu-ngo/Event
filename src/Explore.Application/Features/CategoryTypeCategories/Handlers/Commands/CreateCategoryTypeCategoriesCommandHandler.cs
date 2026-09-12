using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryTypeCategories.Validators;
using Explore.Application.Features.CategoryTypeCategories.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Commands;

public class CreateCategoryTypeCategoriesCommandHandler : ICommandHandler<CreateCategoryTypeCategoriesCommand, BaseCommandResponse<Guid>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryTypeRepository _categoryTypeRepository;
    private readonly ITenantContext _tenantContext;

    public CreateCategoryTypeCategoriesCommandHandler(
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

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateCategoryTypeCategoriesCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateCategoryTypeCategoriesDtoValidator(_categoryRepository, _categoryTypeRepository, _repository);
        var validationResult = await validator.ValidateAsync(request.CategoryTypeCategoriesDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Category Type Categories creation failed.");
        }

        var categoryTypeCategories = CategoryTypeCategoriesMapper.Create(request.CategoryTypeCategoriesDto, _tenantContext.TenantId);

        categoryTypeCategories = await _repository.Create(categoryTypeCategories);

        return BaseCommandResponse.Success(
            categoryTypeCategories.Id,
            "Category Type Categories created successfully.");
    }
}
