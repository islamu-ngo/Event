using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryType;
using Explore.Application.Features.CategoryTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypes.Handlers.Queries;

public class GetCategoryTypeListRequestHandler : IQueryHandler<GetCategoryTypeListRequest, List<CategoryTypeListDto>>
{
    private readonly ICategoryTypeRepository _repository;

    public GetCategoryTypeListRequestHandler(ICategoryTypeRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<CategoryTypeListDto>> QueryAsync(GetCategoryTypeListRequest request, CancellationToken cancellationToken)
    {
        var categoryTypes = await _repository.GetCategoryTypesWithDetails();
        return categoryTypes.Select(CategoryTypeMapper.ToListItem).ToList();
    }
}
