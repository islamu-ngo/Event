using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryType;
using Explore.Application.Features.CategoryTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.CategoryTypes.Handlers.Queries;

public class GetCategoryTypeDetailsRequestHandler : IRequestHandler<GetCategoryTypeDetailsRequest, CategoryTypeDto>
{
    private readonly ICategoryTypeRepository _repository;
    private readonly IMapper _mapper;

    public GetCategoryTypeDetailsRequestHandler(ICategoryTypeRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<CategoryTypeDto> Handle(GetCategoryTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var categoryType = await _repository.GetCategoryTypeWithDetails(request.Id);
        return _mapper.Map<CategoryTypeDto>(categoryType);
    }
}
