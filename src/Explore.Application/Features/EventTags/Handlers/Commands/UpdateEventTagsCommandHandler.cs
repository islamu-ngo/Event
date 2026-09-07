using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Authorization;
using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTags.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTags.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTags.Handlers.Commands;

public class UpdateEventTagsCommandHandler : IRequestHandler<UpdateEventTagsCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventTagsRepository _eventTagsRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ITagRepository _tagRepository;
    private readonly HybridCache _cache;

    public UpdateEventTagsCommandHandler(
        IEventTagsRepository eventTagsRepository,
        IEventRepository eventRepository,
        ITagRepository tagRepository,
        HybridCache cache)
    {
        _eventTagsRepository = eventTagsRepository;
        _eventRepository = eventRepository;
        _tagRepository = tagRepository;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventTagsCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateEventTagsDtoValidator();
        var validationResult = await validator.ValidateAsync(request.EventTagsDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return ValidationFailure(validationResult.Errors.Select(e => e.ErrorMessage).ToList());
        }

        var eventTags = await _eventTagsRepository.GetById(request.EventTagId);

        if (eventTags == null)
        {
            return BaseCommandResponse.NotFound<Guid>("Event Tag not found.");
        }

        request = request with
        {
            EventId = eventTags.EventId,
            TenantId = eventTags.TenantId
        };

        if (eventTags.EventId != request.EventId || eventTags.TenantId != request.TenantId)
        {
            throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Update);
        }

        if (eventTags.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                $"Event tag {request.EventTagId} was modified by another request.");
        }

        var targetEventId = request.EventTagsDto.Event?.EventId ?? eventTags.EventId;
        var targetTagId = request.EventTagsDto.Tag?.TagId ?? eventTags.TagId;

        var targetEvent = await _eventRepository.GetById(targetEventId);
        if (targetEvent is null)
        {
            return ValidationFailure("Event not found.");
        }

        if (targetEvent.TenantId != eventTags.TenantId)
        {
            return ValidationFailure("Event must belong to the same tenant as the tag assignment.");
        }

        var targetTag = await _tagRepository.GetById(targetTagId);
        if (targetTag is null)
        {
            return ValidationFailure("Tag not found.");
        }

        if (targetTag.TenantId != eventTags.TenantId)
        {
            return ValidationFailure("Tag must belong to the same tenant as the tag assignment.");
        }

        var duplicate = await _eventTagsRepository.GetByEventAndTag(targetEventId, targetTagId, request.EventTagId);
        if (duplicate is not null)
        {
            return ValidationFailure("Tag is already assigned to this event.");
        }

        var previousEventId = eventTags.EventId;
        ApplyEvent(eventTags, request.EventTagsDto.Event, targetEvent);
        ApplyTag(eventTags, request.EventTagsDto.Tag);

        await _eventTagsRepository.Update(eventTags);
        await InvalidateCachesAsync(previousEventId, eventTags.EventId, targetEvent.TenantId, cancellationToken);

        return BaseCommandResponse.Success(eventTags.Id, "Event Tag updated successfully.");
    }

    private static void ApplyEvent(Explore.Domain.EventTags entity, DTOs.EventTags.UpdateEventTagsEventDto? dto, Event targetEvent)
    {
        if (dto is null)
        {
            return;
        }

        entity.EventId = dto.EventId;
        entity.TenantId = targetEvent.TenantId;
    }

    private static void ApplyTag(Explore.Domain.EventTags entity, DTOs.EventTags.UpdateEventTagsTagDto? dto)
    {
        if (dto is null)
        {
            return;
        }

        entity.TagId = dto.TagId;
    }

    private async Task InvalidateCachesAsync(Guid previousEventId, Guid currentEventId, Guid tenantId, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync($"event:detail:{currentEventId}", cancellationToken);

        if (previousEventId != currentEventId)
        {
            await _cache.RemoveAsync($"event:detail:{previousEventId}", cancellationToken);
        }

        await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantId), cancellationToken);
    }

    private static BaseCommandResponse<Guid> ValidationFailure(string error) =>
        ValidationFailure(new List<string> { error });

    private static BaseCommandResponse<Guid> ValidationFailure(List<string> errors) =>
        BaseCommandResponse.Validation<Guid>(errors, "Event Tag update failed.");
}
