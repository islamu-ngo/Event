using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionLanguages.Handlers.Queries;

public class GetEventSessionLanguageDetailsRequestHandler : IRequestHandler<GetEventSessionLanguageDetailsRequest, EventSessionLanguageDto>
{
    private readonly IEventSessionLanguageRepository _repository;
    private readonly IEventSessionRepository _eventSessionRepository;

    public GetEventSessionLanguageDetailsRequestHandler(
        IEventSessionLanguageRepository repository,
        IEventSessionRepository eventSessionRepository)
    {
        _repository = repository;
        _eventSessionRepository = eventSessionRepository;
    }

    public async Task<EventSessionLanguageDto> Handle(GetEventSessionLanguageDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventSessionLanguage = await _repository.GetById(request.Id);
        if (eventSessionLanguage is null)
            return null!;

        var dto = EventSessionMapper.ToDetail(eventSessionLanguage);
        var eventSession = await _eventSessionRepository.GetById(eventSessionLanguage.EventSessionId);
        dto.EventId = eventSession?.EventId ?? Guid.Empty;

        return dto;
    }
}
