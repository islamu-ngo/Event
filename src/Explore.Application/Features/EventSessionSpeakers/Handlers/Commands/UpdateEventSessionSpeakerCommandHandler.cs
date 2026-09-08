using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionSpeaker.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionSpeakers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Commands;

public class UpdateEventSessionSpeakerCommandHandler : IRequestHandler<UpdateEventSessionSpeakerCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;
    private readonly IActorRepository _actorRepository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly HybridCache _cache;

    public UpdateEventSessionSpeakerCommandHandler(
        IEventSessionSpeakerRepository speakerRepository,
        IActorRepository actorRepository,
        IEventSessionRepository eventSessionRepository,
        HybridCache cache)
    {
        _speakerRepository = speakerRepository;
        _actorRepository = actorRepository;
        _eventSessionRepository = eventSessionRepository;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventSessionSpeakerCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateEventSessionSpeakerDtoValidator();
        var validationResult = await validator.ValidateAsync(request.SpeakerDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return ValidationFailure(validationResult.Errors.Select(e => e.ErrorMessage).ToList());
        }

        var speaker = await _speakerRepository.GetById(request.EventSessionSpeakerId);

        if (speaker == null)
        {
            return BaseCommandResponse.Failure<Guid>(
                "event_session_speaker_not_found",
                "Speaker assignment not found.");
        }

        if (speaker.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                $"Event session speaker {request.EventSessionSpeakerId} was modified by another request.");
        }

        var previousSession = await _eventSessionRepository.GetById(speaker.EventSessionId);
        if (previousSession is null)
        {
            return ValidationFailure("Speaker assignment context no longer matches its persisted event session.");
        }

        request = request with
        {
            EventSessionId = speaker.EventSessionId,
            EventId = previousSession.EventId,
            TenantId = previousSession.TenantId,
        };

        var targetSessionId = request.SpeakerDto.Session?.EventSessionId ?? speaker.EventSessionId;
        var targetActorId = request.SpeakerDto.Actor?.ActorId ?? speaker.ActorId;

        var targetSession = await _eventSessionRepository.GetById(targetSessionId);
        if (targetSession is null)
        {
            return ValidationFailure("Event session not found.");
        }

        if (targetSession.TenantId != speaker.TenantId)
        {
            return ValidationFailure("Event session must belong to the same tenant as the speaker assignment.");
        }

        if (targetSession.EventId != request.EventId)
        {
            return ValidationFailure("Event session must belong to the same event as the speaker assignment.");
        }

        var targetActor = await _actorRepository.GetById(targetActorId);
        if (targetActor is null)
        {
            return ValidationFailure("Actor not found.");
        }

        var duplicate = await _speakerRepository.GetBySessionAndActor(
            targetSessionId,
            targetActorId,
            request.EventSessionSpeakerId,
            cancellationToken);
        if (duplicate is not null)
        {
            return ValidationFailure("Actor is already assigned as a speaker for this event session.");
        }

        ApplySession(speaker, request.SpeakerDto.Session, targetSession);
        ApplyActor(speaker, request.SpeakerDto.Actor);

        await _speakerRepository.Update(speaker);
        await InvalidateCachesAsync(previousSession?.EventId, targetSession.EventId, targetSession.TenantId, cancellationToken);

        return BaseCommandResponse.Success(speaker.Id, "Speaker assignment updated successfully.");
    }

    private static void ApplySession(EventSessionSpeaker entity, DTOs.EventSessionSpeaker.UpdateEventSessionSpeakerSessionDto? dto, EventSession targetSession)
    {
        if (dto is null)
        {
            return;
        }

        entity.EventSessionId = dto.EventSessionId;
        entity.TenantId = targetSession.TenantId;
    }

    private static void ApplyActor(EventSessionSpeaker entity, DTOs.EventSessionSpeaker.UpdateEventSessionSpeakerActorDto? dto)
    {
        if (dto is null)
        {
            return;
        }

        entity.ActorId = dto.ActorId;
    }

    private async Task InvalidateCachesAsync(Guid? previousEventId, Guid currentEventId, Guid tenantId, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync($"event:detail:{currentEventId}", cancellationToken);

        if (previousEventId.HasValue && previousEventId.Value != currentEventId)
        {
            await _cache.RemoveAsync($"event:detail:{previousEventId.Value}", cancellationToken);
        }

        await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantId), cancellationToken);
    }

    private static BaseCommandResponse<Guid> ValidationFailure(string error) =>
        ValidationFailure(new List<string> { error });

    private static BaseCommandResponse<Guid> ValidationFailure(List<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, "Speaker assignment update failed.");
}
