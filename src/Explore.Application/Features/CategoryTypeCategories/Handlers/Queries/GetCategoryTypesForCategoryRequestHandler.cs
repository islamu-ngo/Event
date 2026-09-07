using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryType;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoryTypesForCategoryRequestHandler : IRequestHandler<GetCategoryTypesForCategoryRequest, List<CategoryTypeListDto>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;
    private readonly IMapper _mapper;

    public GetCategoryTypesForCategoryRequestHandler(ICategoryTypeCategoriesRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<CategoryTypeListDto>> Handle(GetCategoryTypesForCategoryRequest request, CancellationToken cancellationToken)
    {
        var categoryTypes = await _repository.GetCategoryTypesForCategory(request.CategoryId);
        return _mapper.Map<List<CategoryTypeListDto>>(categoryTypes);
    }
}
