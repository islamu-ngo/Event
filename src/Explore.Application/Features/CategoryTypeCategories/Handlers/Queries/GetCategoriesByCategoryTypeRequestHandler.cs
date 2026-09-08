using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Category;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoriesByCategoryTypeRequestHandler : IRequestHandler<GetCategoriesByCategoryTypeRequest, List<CategoryListDto>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;
    private readonly IMapper _mapper;

    public GetCategoriesByCategoryTypeRequestHandler(ICategoryTypeCategoriesRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<CategoryListDto>> Handle(GetCategoriesByCategoryTypeRequest request, CancellationToken cancellationToken)
    {
        var categories = await _repository.GetCategoriesByCategoryType(request.CategoryTypeId);
        return _mapper.Map<List<CategoryListDto>>(categories);
    }
}
