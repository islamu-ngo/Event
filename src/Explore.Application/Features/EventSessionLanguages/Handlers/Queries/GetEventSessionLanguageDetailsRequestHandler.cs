using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionLanguages.Handlers.Queries;

public class GetEventSessionLanguageDetailsRequestHandler : IRequestHandler<GetEventSessionLanguageDetailsRequest, EventSessionLanguageDto>
{
    private readonly IEventSessionLanguageRepository _repository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IMapper _mapper;

    public GetEventSessionLanguageDetailsRequestHandler(
        IEventSessionLanguageRepository repository,
        IEventSessionRepository eventSessionRepository,
        IMapper mapper)
    {
        _repository = repository;
        _eventSessionRepository = eventSessionRepository;
        _mapper = mapper;
    }

    public async Task<EventSessionLanguageDto> Handle(GetEventSessionLanguageDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventSessionLanguage = await _repository.GetById(request.Id);
        var dto = _mapper.Map<EventSessionLanguageDto>(eventSessionLanguage);
        if (eventSessionLanguage is not null)
        {
            var eventSession = await _eventSessionRepository.GetById(eventSessionLanguage.EventSessionId);
            dto.EventId = eventSession?.EventId ?? Guid.Empty;
        }

        return dto;
    }
}
