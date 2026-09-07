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

public sealed class CreateEventTechAspectCommandHandler(
    IEventRepository eventRepository,
    IEventTechAspectRepository aspectRepository,
    IMapper mapper,
    HybridCache cache)
    : IRequestHandler<CreateEventTechAspectCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        CreateEventTechAspectCommand request,
        CancellationToken cancellationToken)
    {
        Event? parentEvent = await eventRepository.GetById(request.EventId);
        if (parentEvent is null)
            return Failure(request.EventId, "event_not_found", "Event not found.");

        if (await aspectRepository.GetByEventId(request.EventId) is not null)
            return Failure(request.EventId, "event_tech_aspect_exists", "Tech aspect already exists.");

        var validator = new CreateUpdateTechAspectDtoValidator();
        var validation = await validator.ValidateAsync(request.AspectDto, cancellationToken);
        if (!validation.IsValid)
            return Failure(request.EventId, "event_tech_aspect_validation_failed", "Validation failed.", validation.Errors.Select(error => error.ErrorMessage));

        EventTechAspect aspect = mapper.Map<EventTechAspect>(request.AspectDto);
        aspect.Id = request.EventId;
        await aspectRepository.Create(aspect);
        await cache.RemoveAsync($"event:detail:{request.EventId}", cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventListByTenant(parentEvent.TenantId), cancellationToken);
        return BaseCommandResponse.Success(aspect.Id, "Tech aspect created successfully.");
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

public sealed class UpdateEventTechAspectCommandHandler(
    IEventRepository eventRepository,
    IEventTechAspectRepository aspectRepository,
    HybridCache cache)
    : IRequestHandler<UpdateEventTechAspectCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateEventTechAspectCommand request,
        CancellationToken cancellationToken)
    {
        var patchValidator = new UpdateEventTechAspectDtoValidator();
        var patchValidation = await patchValidator.ValidateAsync(request.AspectDto, cancellationToken);
        if (!patchValidation.IsValid)
            return Failure(request.EventId, "event_tech_aspect_validation_failed", "Validation failed.", patchValidation.Errors.Select(error => error.ErrorMessage));

        Event? parentEvent = await eventRepository.GetById(request.EventId);
        if (parentEvent is null)
            return Failure(request.EventId, "event_not_found", "Event not found.");

        EventTechAspect? aspect = await aspectRepository.GetByEventId(request.EventId);
        if (aspect is null)
            return Failure(request.EventId, "event_tech_aspect_not_found", "Tech aspect not found.");

        var candidate = new CreateUpdateTechAspectDto
        {
            GithubRepoUrl = request.AspectDto.Repository?.GithubRepoUrl is { HasValue: true } repository
                ? repository.Value
                : aspect.GithubRepoUrl,
            HackathonTrack = request.AspectDto.Classification?.HackathonTrack is { HasValue: true } track
                ? track.Value
                : aspect.HackathonTrack,
            SkillLevel = request.AspectDto.Classification?.SkillLevel ?? aspect.SkillLevel,
            TechStackTags = request.AspectDto.Classification?.TechStackTags is { HasValue: true } tags
                ? tags.Value
                : aspect.TechStackTags,
            RequiresLaptop = request.AspectDto.Participation?.RequiresLaptop ?? aspect.RequiresLaptop,
            IsCodingCompetition = request.AspectDto.Participation?.IsCodingCompetition ?? aspect.IsCodingCompetition,
            MaxTeamSize = request.AspectDto.Prize?.MaxTeamSize is { HasValue: true } teamSize
                ? teamSize.Value
                : aspect.MaxTeamSize,
            PrizePool = request.AspectDto.Prize?.PrizePool is { HasValue: true } prizePool
                ? prizePool.Value
                : aspect.PrizePool,
            PrizeCurrencyCode = request.AspectDto.Prize?.PrizeCurrencyCode is { HasValue: true } currency
                ? currency.Value
                : aspect.PrizeCurrencyCode
        };
        var validator = new CreateUpdateTechAspectDtoValidator();
        var validation = await validator.ValidateAsync(candidate, cancellationToken);
        if (!validation.IsValid)
            return Failure(request.EventId, "event_tech_aspect_validation_failed", "Validation failed.", validation.Errors.Select(error => error.ErrorMessage));

        Apply(aspect, request.AspectDto);
        await aspectRepository.Update(aspect);
        await cache.RemoveAsync($"event:detail:{request.EventId}", cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventListByTenant(parentEvent.TenantId), cancellationToken);
        return BaseCommandResponse.Success(aspect.Id, "Tech aspect updated successfully.");
    }

    private static void Apply(EventTechAspect aspect, UpdateEventTechAspectDto patch)
    {
        if (patch.Repository?.GithubRepoUrl is { HasValue: true } repository)
            aspect.GithubRepoUrl = repository.Value;
        if (patch.Classification?.HackathonTrack is { HasValue: true } track)
            aspect.HackathonTrack = track.Value;
        if (patch.Classification?.SkillLevel is { } skillLevel)
            aspect.SkillLevel = skillLevel;
        if (patch.Classification?.TechStackTags is { HasValue: true } tags)
            aspect.TechStackTags = tags.Value;
        if (patch.Participation?.RequiresLaptop is { } requiresLaptop)
            aspect.RequiresLaptop = requiresLaptop;
        if (patch.Participation?.IsCodingCompetition is { } competition)
            aspect.IsCodingCompetition = competition;
        if (patch.Prize?.MaxTeamSize is { HasValue: true } teamSize)
            aspect.MaxTeamSize = teamSize.Value;
        if (patch.Prize?.PrizePool is { HasValue: true } prizePool)
            aspect.PrizePool = prizePool.Value;
        if (patch.Prize?.PrizeCurrencyCode is { HasValue: true } currency)
            aspect.PrizeCurrencyCode = currency.Value;
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
