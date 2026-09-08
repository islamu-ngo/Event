using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionLanguages.Handlers.Queries;

public class GetLanguagesBySessionRequestHandler :
    IRequestHandler<GetLanguagesBySessionRequest, List<EventSessionLanguageListDto>>
{
    private readonly IEventSessionLanguageRepository _repository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IMapper _mapper;

    public GetLanguagesBySessionRequestHandler(
        IEventSessionLanguageRepository repository,
        IEventSessionRepository eventSessionRepository,
        IMapper mapper)
    {
        _repository = repository;
        _eventSessionRepository = eventSessionRepository;
        _mapper = mapper;
    }

    public async Task<List<EventSessionLanguageListDto>> Handle(GetLanguagesBySessionRequest request, CancellationToken cancellationToken)
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
        var dtos = _mapper.Map<List<EventSessionLanguageListDto>>(eventSessionLanguages);
        foreach (var dto in dtos)
        {
            dto.EventId = eventSession.EventId;
            dto.TenantId = eventSession.TenantId;
        }

        return dtos;
    }
}

public sealed class GetManagedLanguagesBySessionRequestHandler(
    IEventSessionLanguageRepository repository,
    IEventSessionRepository eventSessionRepository,
    IMapper mapper)
    : IRequestHandler<GetManagedLanguagesBySessionRequest, List<EventSessionLanguageListDto>>
{
    public async Task<List<EventSessionLanguageListDto>> Handle(
        GetManagedLanguagesBySessionRequest request,
        CancellationToken cancellationToken)
    {
        var eventSession = await eventSessionRepository.GetSessionWithDetails(request.EventSessionId);
        if (eventSession is null || eventSession.EventId != request.EventId)
            return [];

        var assignments = await repository.GetBySession(request.EventSessionId, cancellationToken);
        var dtos = mapper.Map<List<EventSessionLanguageListDto>>(assignments);
        foreach (var dto in dtos)
        {
            dto.EventId = eventSession.EventId;
            dto.TenantId = eventSession.TenantId;
        }

        return dtos;
    }
}
