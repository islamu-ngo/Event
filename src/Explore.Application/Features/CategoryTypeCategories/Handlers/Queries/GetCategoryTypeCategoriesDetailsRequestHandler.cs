using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoryTypeCategoriesDetailsRequestHandler : IRequestHandler<GetCategoryTypeCategoriesDetailsRequest, CategoryTypeCategoriesDto>
{
    private readonly ICategoryTypeCategoriesRepository _repository;
    private readonly IMapper _mapper;

    public GetCategoryTypeCategoriesDetailsRequestHandler(ICategoryTypeCategoriesRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<CategoryTypeCategoriesDto> Handle(GetCategoryTypeCategoriesDetailsRequest request, CancellationToken cancellationToken)
    {
        var categoryTypeCategories = await _repository.GetById(request.Id);
        return _mapper.Map<CategoryTypeCategoriesDto>(categoryTypeCategories);
    }
}
