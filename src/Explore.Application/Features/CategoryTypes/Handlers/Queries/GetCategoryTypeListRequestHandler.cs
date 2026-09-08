using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryType;
using Explore.Application.Features.CategoryTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.CategoryTypes.Handlers.Queries;

public class GetCategoryTypeListRequestHandler : IRequestHandler<GetCategoryTypeListRequest, List<CategoryTypeListDto>>
{
    private readonly ICategoryTypeRepository _repository;
    private readonly IMapper _mapper;

    public GetCategoryTypeListRequestHandler(ICategoryTypeRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<CategoryTypeListDto>> Handle(GetCategoryTypeListRequest request, CancellationToken cancellationToken)
    {
        var categoryTypes = await _repository.GetCategoryTypesWithDetails();
        return _mapper.Map<List<CategoryTypeListDto>>(categoryTypes);
    }
}
