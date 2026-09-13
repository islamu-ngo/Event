using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionLanguages.Handlers.Queries;

public class GetLanguagesBySessionQueryHandler :
    IQueryHandler<GetLanguagesBySessionQuery, List<EventSessionLanguageListDto>>
{
    private readonly IEventSessionLanguageRepository _repository;
    private readonly IEventSessionRepository _eventSessionRepository;

    public GetLanguagesBySessionQueryHandler(
        IEventSessionLanguageRepository repository,
        IEventSessionRepository eventSessionRepository)
    {
        _repository = repository;
        _eventSessionRepository = eventSessionRepository;
    }

    public async Task<List<EventSessionLanguageListDto>> QueryAsync(GetLanguagesBySessionQuery request, CancellationToken cancellationToken)
    {
        var eventSession = await _eventSessionRepository.GetPublicSessionWithDetailsAsync(
            request.EventSessionId,
            cancellationToken);
        if (eventSession is null)
            return [];

        return await MapLanguagesAsync(eventSession, request.EventSessionId, cancellationToken);
    }

    private async Task<List<EventSessionLanguageListDto>> MapLanguagesAsync(
        Explore.Domain.EventSession eventSession,
        Guid eventSessionId,
        CancellationToken cancellationToken)
    {
        var eventSessionLanguages = await _repository.GetBySession(eventSessionId, cancellationToken);
        var dtos = eventSessionLanguages.Select(EventSessionMapper.ToListItem).ToList();
        foreach (var dto in dtos)
        {
            dto.EventId = eventSession.EventId;
            dto.TenantId = eventSession.TenantId;
        }

        return dtos;
    }
}

public sealed class GetManagedLanguagesBySessionQueryHandler(
    IEventSessionLanguageRepository repository,
    IEventSessionRepository eventSessionRepository)
    : IQueryHandler<GetManagedLanguagesBySessionQuery, List<EventSessionLanguageListDto>>
{
    public async Task<List<EventSessionLanguageListDto>> QueryAsync(
        GetManagedLanguagesBySessionQuery request,
        CancellationToken cancellationToken)
    {
        var eventSession = await eventSessionRepository.GetSessionWithDetails(request.EventSessionId);
        if (eventSession is null || eventSession.EventId != request.EventId)
            return [];

        var assignments = await repository.GetBySession(request.EventSessionId, cancellationToken);
        var dtos = assignments.Select(EventSessionMapper.ToListItem).ToList();
        foreach (var dto in dtos)
        {
            dto.EventId = eventSession.EventId;
            dto.TenantId = eventSession.TenantId;
        }

        return dtos;
    }
}
