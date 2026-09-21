using Explore.Application.Caching;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.RegistrationForms.Requests.Queries;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Events.Handlers.Queries;

public class GetEventDetailsRequestHandler : IQueryHandler<GetEventDetailsRequest, EventDto?>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventDetailsProjectionService _detailsProjectionService;
    private readonly HybridCache _cache;
    private readonly ITenantLifecycleAccessService _lifecycle;
    private readonly IQueryHandler<GetOptionalQuestionnaireQuery, OptionalQuestionnaireDto?> _optionalQuestionnaireHandler;

    public GetEventDetailsRequestHandler(
        IEventRepository eventRepository,
        IEventDetailsProjectionService detailsProjectionService,
        HybridCache cache,
        IQueryHandler<GetOptionalQuestionnaireQuery, OptionalQuestionnaireDto?> optionalQuestionnaireHandler,
        ITenantLifecycleAccessService lifecycle)
    {
        _eventRepository = eventRepository;
        _detailsProjectionService = detailsProjectionService;
        _cache = cache;
        _lifecycle = lifecycle;
        _optionalQuestionnaireHandler = optionalQuestionnaireHandler;
    }

    public async Task<EventDto?> QueryAsync(GetEventDetailsRequest request, CancellationToken cancellationToken)
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

        if (eventDto is null || !await _lifecycle.IsPublicAsync(eventDto.TenantId, cancellationToken))
            return null;

        var isPubliclyEligible = await _eventRepository.IsPubliclyEligibleAsync(
            eventDto.TenantId,
            eventDto.Id,
            cancellationToken);

        if (!isPubliclyEligible)
            return null;

        var optionalQuestionnaire = await _optionalQuestionnaireHandler.QueryAsync(
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
