using Explore.Application.Authorization;
using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.DTOs.EventSeries.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSeries.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;
using DomainEventSeries = Explore.Domain.EventSeries;

namespace Explore.Application.Features.EventSeries.Handlers.Commands;

public class UpdateEventSeriesCommandHandler : IRequestHandler<UpdateEventSeriesCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventSeriesRepository _eventSeriesRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly HybridCache _cache;

    public UpdateEventSeriesCommandHandler(
        IEventSeriesRepository eventSeriesRepository,
        IStorageObjectRepository storageObjectRepository,
        HybridCache cache)
    {
        _eventSeriesRepository = eventSeriesRepository;
        _storageObjectRepository = storageObjectRepository;
        _cache = cache;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventSeriesCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateEventSeriesDtoValidator();
        var validationResult = await validator.ValidateAsync(request.EventSeriesDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event series update failed due to validation errors.");
        }

        var series = await _eventSeriesRepository.GetEventSeriesWithEvents(request.EventSeriesId);
        if (series == null)
        {
            return BaseCommandResponse.NotFound<Guid>("Event series not found.");
        }

        if (series.ActorId != request.ActorId || series.TenantId != request.TenantId)
        {
            throw new AuthorizationException(ResourceKinds.Actor, AuthorizationActions.Update);
        }

        if (series.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The event series was modified by another request. Reload and retry.",
                nameof(DomainEventSeries),
                series.Id.ToString());
        }

        Guid? featuredImageId = request.EventSeriesDto.FeaturedImage?.Value.HasValue == true
            ? request.EventSeriesDto.FeaturedImage.Value.Value
            : null;
        if (!await ImageReferenceEligibility.AreEligibleAsync(
                _storageObjectRepository,
                series.TenantId,
                featuredImageId))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Featured image must be an active public safe-raster object in the current tenant."],
                "Event series update failed due to validation errors.");
        }

        ApplyTitle(series, request.EventSeriesDto.Title);
        ApplyDescription(series, request.EventSeriesDto.Description);
        ApplySlug(series, request.EventSeriesDto.Slug);
        ApplyFeaturedImage(series, request.EventSeriesDto.FeaturedImage);
        ApplyPublication(series, request.EventSeriesDto.Publication);

        await _eventSeriesRepository.Update(series);

        foreach (var eventEntity in series.Events)
        {
            await _cache.RemoveAsync($"event:detail:{eventEntity.Id}", cancellationToken);
        }

        await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(series.TenantId), cancellationToken);

        return BaseCommandResponse.Success(series.Id, "Event series updated successfully.");
    }

    private static void ApplyTitle(DomainEventSeries series, UpdateEventSeriesTitleDto? group)
    {
        if (group is not null)
        {
            series.Title = group.Value;
        }
    }

    private static void ApplyDescription(DomainEventSeries series, UpdateEventSeriesDescriptionDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            series.Description = group.Value.Value;
        }
    }

    private static void ApplySlug(DomainEventSeries series, UpdateEventSeriesSlugDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            series.Slug = group.Value.Value;
        }
    }

    private static void ApplyFeaturedImage(DomainEventSeries series, UpdateEventSeriesFeaturedImageDto? group)
    {
        if (group?.Value.HasValue == true)
        {
            series.FeaturedImageId = group.Value.Value;
        }
    }

    private static void ApplyPublication(DomainEventSeries series, UpdateEventSeriesPublicationDto? group)
    {
        if (group is not null)
        {
            series.IsPublished = group.IsPublished;
        }
    }
}
