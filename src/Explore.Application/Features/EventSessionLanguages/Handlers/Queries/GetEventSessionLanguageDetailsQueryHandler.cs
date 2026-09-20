using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionLanguages.Handlers.Queries;

public class GetEventSessionLanguageDetailsQueryHandler : IQueryHandler<GetEventSessionLanguageDetailsQuery, EventSessionLanguageDto?>
{
    private readonly IEventSessionLanguageRepository _repository;
    private readonly IEventSessionRepository _eventSessionRepository;

    public GetEventSessionLanguageDetailsQueryHandler(
        IEventSessionLanguageRepository repository,
        IEventSessionRepository eventSessionRepository)
    {
        _repository = repository;
        _eventSessionRepository = eventSessionRepository;
    }

    public async Task<EventSessionLanguageDto?> QueryAsync(GetEventSessionLanguageDetailsQuery request, CancellationToken cancellationToken)
    {
        var eventSessionLanguage = await _repository.GetById(request.Id);
        if (eventSessionLanguage is null)
            return null;

        var dto = EventSessionMapper.ToDetail(eventSessionLanguage);
        var eventSession = await _eventSessionRepository.GetById(eventSessionLanguage.EventSessionId);
        dto.EventId = eventSession?.EventId ?? Guid.Empty;

        return dto;
    }
}
