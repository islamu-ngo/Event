using AutoMapper;
using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.DTOs.EventAspects.Validators;
using Explore.Application.Features.EventAspects.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventAspects.Handlers.Commands;

public sealed class CreateEventIslamicAspectCommandHandler(
    IEventRepository eventRepository,
    IEventIslamicAspectRepository aspectRepository,
    IMadhabRepository madhabRepository,
    ILanguageRepository languageRepository,
    IMapper mapper,
    HybridCache cache)
    : IRequestHandler<CreateEventIslamicAspectCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        CreateEventIslamicAspectCommand request,
        CancellationToken cancellationToken)
    {
        Event? parentEvent = await eventRepository.GetById(request.EventId);
        if (parentEvent is null)
            return Failure(request.EventId, "event_not_found", "Event not found.");

        if (await aspectRepository.GetByEventIdWithDetails(request.EventId) is not null)
            return Failure(request.EventId, "event_islamic_aspect_exists", "Islamic aspect already exists.");

        var validator = new CreateUpdateIslamicAspectDtoValidator(madhabRepository, languageRepository);
        var validation = await validator.ValidateAsync(request.AspectDto, cancellationToken);
        if (!validation.IsValid)
            return Failure(request.EventId, "event_islamic_aspect_validation_failed", "Validation failed.", validation.Errors.Select(error => error.ErrorMessage));

        EventIslamicAspect aspect = mapper.Map<EventIslamicAspect>(request.AspectDto);
        aspect.Id = request.EventId;
        await aspectRepository.Create(aspect);
        await InvalidateAsync(cache, request.EventId, parentEvent.TenantId, cancellationToken);
        return Success(aspect.Id, "Islamic aspect created successfully.");
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string code,
        string message,
        IEnumerable<string>? errors = null) => BaseCommandResponse.Failure<Guid>(
            code,
            message,
            errors ?? [message],
            id);

    private static async Task InvalidateAsync(
        HybridCache targetCache,
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await targetCache.RemoveAsync($"event:detail:{eventId}", cancellationToken);
        await targetCache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantId), cancellationToken);
    }
}

public sealed class UpdateEventIslamicAspectCommandHandler(
    IEventRepository eventRepository,
    IEventIslamicAspectRepository aspectRepository,
    IMadhabRepository madhabRepository,
    ILanguageRepository languageRepository,
    HybridCache cache)
    : IRequestHandler<UpdateEventIslamicAspectCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateEventIslamicAspectCommand request,
        CancellationToken cancellationToken)
    {
        var patchValidator = new UpdateEventIslamicAspectDtoValidator();
        var patchValidation = await patchValidator.ValidateAsync(request.AspectDto, cancellationToken);
        if (!patchValidation.IsValid)
            return Failure(request.EventId, "event_islamic_aspect_validation_failed", "Validation failed.", patchValidation.Errors.Select(error => error.ErrorMessage));

        Event? parentEvent = await eventRepository.GetById(request.EventId);
        if (parentEvent is null)
            return Failure(request.EventId, "event_not_found", "Event not found.");

        EventIslamicAspect? aspect = await aspectRepository.GetByEventIdWithDetails(request.EventId);
        if (aspect is null)
            return Failure(request.EventId, "event_islamic_aspect_not_found", "Islamic aspect not found.");

        var candidate = new CreateUpdateIslamicAspectDto
        {
            MadhabId = request.AspectDto.Jurisprudence?.MadhabId is { HasValue: true } madhab
                ? madhab.Value
                : aspect.MadhabId,
            ReferencePrayer = request.AspectDto.PrayerSchedule?.ReferencePrayer is { HasValue: true } prayer
                ? prayer.Value
                : aspect.ReferencePrayer,
            PrayerTimeOffset = request.AspectDto.PrayerSchedule?.PrayerTimeOffset is { HasValue: true } offset
                ? offset.Value
                : aspect.PrayerTimeOffset,
            GenderMode = request.AspectDto.Participation?.GenderMode ?? aspect.GenderMode,
            IncludesQuranRecitation = request.AspectDto.Participation?.IncludesQuranRecitation
                ?? aspect.IncludesQuranRecitation,
            PrimaryLanguageId = request.AspectDto.Language?.PrimaryLanguageId is { HasValue: true } language
                ? language.Value
                : aspect.PrimaryLanguageId
        };
        var validator = new CreateUpdateIslamicAspectDtoValidator(madhabRepository, languageRepository);
        var validation = await validator.ValidateAsync(candidate, cancellationToken);
        if (!validation.IsValid)
            return Failure(request.EventId, "event_islamic_aspect_validation_failed", "Validation failed.", validation.Errors.Select(error => error.ErrorMessage));

        Apply(aspect, request.AspectDto);
        await aspectRepository.Update(aspect);
        await cache.RemoveAsync($"event:detail:{request.EventId}", cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventListByTenant(parentEvent.TenantId), cancellationToken);
        return BaseCommandResponse.Success(aspect.Id, "Islamic aspect updated successfully.");
    }

    private static void Apply(EventIslamicAspect aspect, UpdateEventIslamicAspectDto patch)
    {
        if (patch.Jurisprudence?.MadhabId is { HasValue: true } madhab)
            aspect.MadhabId = madhab.Value;
        if (patch.PrayerSchedule?.ReferencePrayer is { HasValue: true } prayer)
            aspect.ReferencePrayer = prayer.Value;
        if (patch.PrayerSchedule?.PrayerTimeOffset is { HasValue: true } offset)
            aspect.PrayerTimeOffset = offset.Value;
        if (patch.Participation?.GenderMode is { } genderMode)
            aspect.GenderMode = genderMode;
        if (patch.Participation?.IncludesQuranRecitation is { } includesRecitation)
            aspect.IncludesQuranRecitation = includesRecitation;
        if (patch.Language?.PrimaryLanguageId is { HasValue: true } language)
            aspect.PrimaryLanguageId = language.Value;
    }

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string code,
        string message,
        IEnumerable<string>? errors = null) => BaseCommandResponse.Failure<Guid>(
            code,
            message,
            errors ?? [message],
            id);
}
