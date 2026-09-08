using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionLanguages.Handlers.Queries;

public class GetEventSessionLanguageListRequestHandler : IRequestHandler<GetEventSessionLanguageListRequest, PaginatedResult<EventSessionLanguageListDto>>
{
    private readonly IEventSessionLanguageRepository _repository;
    private readonly IMapper _mapper;

    public GetEventSessionLanguageListRequestHandler(IEventSessionLanguageRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<PaginatedResult<EventSessionLanguageListDto>> Handle(GetEventSessionLanguageListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventSessionLanguageListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (eventSessionLanguages, totalCount) = await _repository.GetLanguagesWithDetailsPaged(pageNumber, pageSize, cancellationToken);
        var dtos = _mapper.Map<List<EventSessionLanguageListDto>>(eventSessionLanguages);
        return PaginatedResult<EventSessionLanguageListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
