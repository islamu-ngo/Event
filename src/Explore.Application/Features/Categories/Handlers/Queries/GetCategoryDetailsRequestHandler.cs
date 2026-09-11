using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Category;
using Explore.Application.Features.Categories.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Categories.Handlers.Queries;

public class GetCategoryDetailsRequestHandler : IRequestHandler<GetCategoryDetailsRequest, CategoryDto>
{
    private readonly ICategoryRepository _categoryRepository;

    public GetCategoryDetailsRequestHandler(
        ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<CategoryDto> Handle(GetCategoryDetailsRequest request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetCategoryWithDetails(request.Id);
        return category is null ? null! : CustomPropertyMapper.ToDetail(category);
    }
}
