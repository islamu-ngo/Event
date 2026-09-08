using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Caching;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionSpeaker.Validators;
using Explore.Application.Features.EventSessionSpeakers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Commands;

public class CreateEventSessionSpeakerCommandHandler : IRequestHandler<CreateEventSessionSpeakerCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;
    private readonly IActorRepository _actorRepository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;

    public CreateEventSessionSpeakerCommandHandler(
        IEventSessionSpeakerRepository speakerRepository,
        IActorRepository actorRepository,
        IEventSessionRepository eventSessionRepository,
        ITenantContext tenantContext,
        IMapper mapper,
        HybridCache cache)
    {
        _speakerRepository = speakerRepository;
        _actorRepository = actorRepository;
        _eventSessionRepository = eventSessionRepository;
        _tenantContext = tenantContext;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateEventSessionSpeakerCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventSessionSpeakerDtoValidator(_actorRepository, _eventSessionRepository);
        var validationResult = await validator.ValidateAsync(request.SpeakerDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Speaker assignment creation failed.");
        }

        var eventSession = await _eventSessionRepository.GetById(request.SpeakerDto.EventSessionId);
        if (eventSession is null)
        {
            return ValidationFailure("Event session not found.");
        }

        if (eventSession.TenantId != _tenantContext.TenantId)
        {
            return ValidationFailure("Event session must belong to the current tenant.");
        }

        var actor = await _actorRepository.GetById(request.SpeakerDto.ActorId);
        if (actor is null)
        {
            return ValidationFailure("Actor not found.");
        }

        var duplicate = await _speakerRepository.GetBySessionAndActor(
            eventSession.Id,
            actor.Id,
            cancellationToken: cancellationToken);
        if (duplicate is not null)
        {
            return ValidationFailure("Actor is already assigned as a speaker for this event session.");
        }

        var speaker = _mapper.Map<EventSessionSpeaker>(request.SpeakerDto);

        speaker.TenantId = eventSession.TenantId;

        speaker = await _speakerRepository.Create(speaker);
        await _cache.RemoveAsync($"event:detail:{eventSession.EventId}", cancellationToken);
        await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(eventSession.TenantId), cancellationToken);

        return BaseCommandResponse.Success(speaker.Id, "Speaker assigned to session successfully.");
    }

    private static BaseCommandResponse<Guid> ValidationFailure(string error) =>
        BaseCommandResponse.Validation<Guid>([error], "Speaker assignment creation failed.");
}
