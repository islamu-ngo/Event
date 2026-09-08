using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoryTypeCategoriesListRequestHandler : IRequestHandler<GetCategoryTypeCategoriesListRequest, List<CategoryTypeCategoriesListDto>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;
    private readonly IMapper _mapper;

    public GetCategoryTypeCategoriesListRequestHandler(ICategoryTypeCategoriesRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<CategoryTypeCategoriesListDto>> Handle(GetCategoryTypeCategoriesListRequest request, CancellationToken cancellationToken)
    {
        var categoryTypeCategories = await _repository.GetAll();
        return _mapper.Map<List<CategoryTypeCategoriesListDto>>(categoryTypeCategories);
    }
}
