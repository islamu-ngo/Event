using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryType;
using Explore.Application.Features.CategoryTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.CategoryTypes.Handlers.Queries;

public class GetCategoryTypeListRequestHandler : IRequestHandler<GetCategoryTypeListRequest, List<CategoryTypeListDto>>
{
    private readonly ICategoryTypeRepository _repository;

    public GetCategoryTypeListRequestHandler(ICategoryTypeRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<CategoryTypeListDto>> Handle(GetCategoryTypeListRequest request, CancellationToken cancellationToken)
    {
        var categoryTypes = await _repository.GetCategoryTypesWithDetails();
        return categoryTypes.Select(CategoryTypeMapper.ToListItem).ToList();
    }
}
