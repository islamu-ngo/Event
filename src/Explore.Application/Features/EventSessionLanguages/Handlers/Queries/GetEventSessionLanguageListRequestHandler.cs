using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionLanguages.Handlers.Queries;

public class GetEventSessionLanguageListRequestHandler : IRequestHandler<GetEventSessionLanguageListRequest, PaginatedResult<EventSessionLanguageListDto>>
{
    private readonly IEventSessionLanguageRepository _repository;

    public GetEventSessionLanguageListRequestHandler(IEventSessionLanguageRepository repository)
    {
        _repository = repository;
    }

    public async Task<PaginatedResult<EventSessionLanguageListDto>> Handle(GetEventSessionLanguageListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventSessionLanguageListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (eventSessionLanguages, totalCount) = await _repository.GetLanguagesWithDetailsPaged(pageNumber, pageSize, cancellationToken);
        var dtos = eventSessionLanguages.Select(EventSessionMapper.ToListItem).ToList();
        return PaginatedResult<EventSessionLanguageListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
