using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.RegistrationForms.Requests.Queries;

namespace Explore.Application.Features.Events.Handlers.Queries;

public class GetEventDetailsRequestHandler : IQueryHandler<GetEventDetailsRequest, EventDto?>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventDetailsProjectionService _detailsProjectionService;
    private readonly ITenantLifecycleAccessService _lifecycle;
    private readonly IQueryHandler<GetOptionalQuestionnaireQuery, OptionalQuestionnaireDto?> _optionalQuestionnaireHandler;

    public GetEventDetailsRequestHandler(
        IEventRepository eventRepository,
        IEventDetailsProjectionService detailsProjectionService,
        IQueryHandler<GetOptionalQuestionnaireQuery, OptionalQuestionnaireDto?> optionalQuestionnaireHandler,
        ITenantLifecycleAccessService lifecycle)
    {
        _eventRepository = eventRepository;
        _detailsProjectionService = detailsProjectionService;
        _lifecycle = lifecycle;
        _optionalQuestionnaireHandler = optionalQuestionnaireHandler;
    }

    public async Task<EventDto?> QueryAsync(GetEventDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventDto = await _detailsProjectionService.BuildAsync(request.Id, cancellationToken);

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
