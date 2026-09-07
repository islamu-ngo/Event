using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.RegistrationForms.Requests.Queries;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Events.Handlers.Queries;

public class GetEventDetailsRequestHandler : IRequestHandler<GetEventDetailsRequest, EventDto>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventDetailsProjectionService _detailsProjectionService;
    private readonly HybridCache _cache;
    private readonly ISender _sender;

    public GetEventDetailsRequestHandler(
        IEventRepository eventRepository,
        IEventDetailsProjectionService detailsProjectionService,
        HybridCache cache,
        ISender sender)
    {
        _eventRepository = eventRepository;
        _detailsProjectionService = detailsProjectionService;
        _cache = cache;
        _sender = sender;
    }

    public async Task<EventDto> Handle(GetEventDetailsRequest request, CancellationToken cancellationToken)
    {
        var cacheKey = $"event:detail:{request.Id}";

        var eventDto = await _cache.GetOrCreateAsync(
            cacheKey,
            async token =>
            {
                return await _detailsProjectionService.BuildAsync(request.Id, token);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            tags:
            [
                CacheTags.Events,
                CacheTags.EventDetails,
                CacheTags.Event(request.Id)
            ],
            cancellationToken: cancellationToken);

        if (eventDto is null)
            return eventDto;

        var isPubliclyEligible = await _eventRepository.IsPubliclyEligibleAsync(
            eventDto.TenantId,
            eventDto.Id,
            cancellationToken);

        if (!isPubliclyEligible)
            return null;

        var optionalQuestionnaire = await _sender.Send(
            new GetOptionalQuestionnaireQuery(request.Id), cancellationToken);
        var responseDto = eventDto.CreateRequestCopy();
        if (responseDto.ParticipationConfiguration is not null)
        {
            responseDto.ParticipationConfiguration = CopyParticipationConfiguration(
                responseDto.ParticipationConfiguration,
                optionalQuestionnaire is not null);
        }

        responseDto.IsPubliclyEligible = true;
        responseDto.IsManagementView = false;
        await _detailsProjectionService.ResolveImageUrlsAsync(responseDto, cancellationToken);

        return responseDto;
    }

    private static EventParticipationConfigurationDto CopyParticipationConfiguration(
        EventParticipationConfigurationDto source,
        bool hasValidOptionalQuestionnaire) => new()
        {
            EventId = source.EventId,
            ConcurrencyStamp = source.ConcurrencyStamp,
            ParticipationHandlingModeId = source.ParticipationHandlingModeId,
            ParticipationHandlingModeCode = source.ParticipationHandlingModeCode,
            ParticipationHandlingModeName = source.ParticipationHandlingModeName,
            AdvanceRegistrationObligationId = source.AdvanceRegistrationObligationId,
            AdvanceRegistrationObligationCode = source.AdvanceRegistrationObligationCode,
            AdvanceRegistrationObligationName = source.AdvanceRegistrationObligationName,
            IdentityAccessModeId = source.IdentityAccessModeId,
            IdentityAccessModeCode = source.IdentityAccessModeCode,
            IdentityAccessModeName = source.IdentityAccessModeName,
            GuestRecoveryPolicy = source.GuestRecoveryPolicy,
            HasValidOptionalQuestionnaire = hasValidOptionalQuestionnaire
        };
}
