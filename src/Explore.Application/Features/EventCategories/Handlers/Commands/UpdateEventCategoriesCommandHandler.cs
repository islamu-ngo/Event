using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Authorization;
using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCategories.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventCategories.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventCategories.Handlers.Commands;

public class UpdateEventCategoriesCommandHandler : IRequestHandler<UpdateEventCategoriesCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventCategoriesRepository _eventCategoriesRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly HybridCache _cache;

    public UpdateEventCategoriesCommandHandler(
        IEventCategoriesRepository eventCategoriesRepository,
        IEventRepository eventRepository,
        ICategoryRepository categoryRepository,
        HybridCache cache)
    {
        _eventCategoriesRepository = eventCategoriesRepository;
        _eventRepository = eventRepository;
        _categoryRepository = categoryRepository;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventCategoriesCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateEventCategoriesDtoValidator();
        var validationResult = await validator.ValidateAsync(request.EventCategoriesDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return ValidationFailure(validationResult.Errors.Select(e => e.ErrorMessage).ToList());
        }

        var eventCategories = await _eventCategoriesRepository.GetById(request.EventCategoryId);

        if (eventCategories == null)
        {
            return BaseCommandResponse.NotFound<Guid>("Event Category not found.");
        }

        request = request with
        {
            EventId = eventCategories.EventId,
            TenantId = eventCategories.TenantId
        };

        if (eventCategories.EventId != request.EventId || eventCategories.TenantId != request.TenantId)
        {
            throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Update);
        }

        if (eventCategories.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                $"Event category {request.EventCategoryId} was modified by another request.");
        }

        var targetEventId = request.EventCategoriesDto.Event?.EventId ?? eventCategories.EventId;
        var targetCategoryId = request.EventCategoriesDto.Category?.CategoryId ?? eventCategories.CategoryId;

        var targetEvent = await _eventRepository.GetById(targetEventId);
        if (targetEvent is null)
        {
            return ValidationFailure("Event not found.");
        }

        if (targetEvent.TenantId != eventCategories.TenantId)
        {
            return ValidationFailure("Event must belong to the same tenant as the category assignment.");
        }

        var targetCategory = await _categoryRepository.GetById(targetCategoryId);
        if (targetCategory is null)
        {
            return ValidationFailure("Category not found.");
        }

        if (targetCategory.TenantId != eventCategories.TenantId)
        {
            return ValidationFailure("Category must belong to the same tenant as the category assignment.");
        }

        var duplicate = await _eventCategoriesRepository.GetByEventAndCategory(targetEventId, targetCategoryId, request.EventCategoryId);
        if (duplicate is not null)
        {
            return ValidationFailure("Category is already assigned to this event.");
        }

        var previousEventId = eventCategories.EventId;
        ApplyEvent(eventCategories, request.EventCategoriesDto.Event, targetEvent);
        ApplyCategory(eventCategories, request.EventCategoriesDto.Category);

        await _eventCategoriesRepository.Update(eventCategories);
        await InvalidateCachesAsync(previousEventId, eventCategories.EventId, targetEvent.TenantId, cancellationToken);

        return BaseCommandResponse.Success(eventCategories.Id, "Event Category updated successfully.");
    }

    private static void ApplyEvent(Explore.Domain.EventCategories entity, DTOs.EventCategories.UpdateEventCategoriesEventDto? dto, Event targetEvent)
    {
        if (dto is null)
        {
            return;
        }

        entity.EventId = dto.EventId;
        entity.TenantId = targetEvent.TenantId;
    }

    private static void ApplyCategory(Explore.Domain.EventCategories entity, DTOs.EventCategories.UpdateEventCategoriesCategoryDto? dto)
    {
        if (dto is null)
        {
            return;
        }

        entity.CategoryId = dto.CategoryId;
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
        BaseCommandResponse.Validation<Guid>(errors, "Event Category update failed.");
}
