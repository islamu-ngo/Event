using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryTypeCategories.Validators;
using Explore.Application.Features.CategoryTypeCategories.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Commands;

public class CreateCategoryTypeCategoriesCommandHandler : IRequestHandler<CreateCategoryTypeCategoriesCommand, BaseCommandResponse<Guid>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;
    private readonly IMapper _mapper;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryTypeRepository _categoryTypeRepository;
    private readonly ITenantContext _tenantContext;

    public CreateCategoryTypeCategoriesCommandHandler(
        ICategoryTypeCategoriesRepository repository,
        IMapper mapper,
        ICategoryRepository categoryRepository,
        ICategoryTypeRepository categoryTypeRepository,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _mapper = mapper;
        _categoryRepository = categoryRepository;
        _categoryTypeRepository = categoryTypeRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateCategoryTypeCategoriesCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateCategoryTypeCategoriesDtoValidator(_categoryRepository, _categoryTypeRepository, _repository);
        var validationResult = await validator.ValidateAsync(request.CategoryTypeCategoriesDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Category Type Categories creation failed.");
        }

        var categoryTypeCategories = _mapper.Map<Domain.CategoryTypeCategories>(request.CategoryTypeCategoriesDto);

        // Set TenantId from request context
        categoryTypeCategories.TenantId = _tenantContext.TenantId;

        categoryTypeCategories = await _repository.Create(categoryTypeCategories);

        return BaseCommandResponse.Success(
            categoryTypeCategories.Id,
            "Category Type Categories created successfully.");
    }
}
