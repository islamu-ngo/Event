// ABOUTME: Applies local draft-event workflow updates without AutoMapper broad-field ownership leaks.
// ABOUTME: Preserves status, actor, tenant, and session-derived projection fields as server-owned state.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Lifecycle;
using Explore.Domain.Services.Scheduling;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Events.Handlers.Commands;

public sealed class UpdateEventDraftCommandHandler : IRequestHandler<UpdateEventDraftCommand, BaseCommandResponse<Guid>>
{
    public const string ConcurrencyConflictCode = "event_draft_concurrency_conflict";
    private const string DraftNotEditableCode = "event_draft_not_editable";

    private readonly IEventRepository _eventRepository;
    private readonly IEventParticipationConfigurationRepository _participationConfigurationRepository;
    private readonly IAudienceAgeRepository _audienceAgeRepository;
    private readonly IAudienceGenderRepository _audienceGenderRepository;
    private readonly IEventTypeRepository _eventTypeRepository;
    private readonly IVisibilityTypeRepository _visibilityTypeRepository;
    private readonly IEventFormatRepository _eventFormatRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IEventSeriesRepository _eventSeriesRepository;
    private readonly IEventRegistrationPolicyRepository _eventRegistrationPolicyRepository;
    private readonly IEventScheduleProjectionCalculator _scheduleProjectionCalculator;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISettingMutationLock _mutationLock;
    private readonly IVisitorAccessCapabilityResolver _visitorCapabilities;

    public UpdateEventDraftCommandHandler(
        IEventRepository eventRepository,
        IEventParticipationConfigurationRepository participationConfigurationRepository,
        IAudienceAgeRepository audienceAgeRepository,
        IAudienceGenderRepository audienceGenderRepository,
        IEventTypeRepository eventTypeRepository,
        IVisibilityTypeRepository visibilityTypeRepository,
        IEventFormatRepository eventFormatRepository,
        IStorageObjectRepository storageObjectRepository,
        IEventSeriesRepository eventSeriesRepository,
        IEventRegistrationPolicyRepository eventRegistrationPolicyRepository,
        IEventScheduleProjectionCalculator scheduleProjectionCalculator,
        HybridCache cache,
        IUnitOfWork unitOfWork,
        ISettingMutationLock mutationLock,
        IVisitorAccessCapabilityResolver visitorCapabilities)
    {
        _eventRepository = eventRepository;
        _participationConfigurationRepository = participationConfigurationRepository;
        _audienceAgeRepository = audienceAgeRepository;
        _audienceGenderRepository = audienceGenderRepository;
        _eventTypeRepository = eventTypeRepository;
        _visibilityTypeRepository = visibilityTypeRepository;
        _eventFormatRepository = eventFormatRepository;
        _storageObjectRepository = storageObjectRepository;
        _eventSeriesRepository = eventSeriesRepository;
        _eventRegistrationPolicyRepository = eventRegistrationPolicyRepository;
        _scheduleProjectionCalculator = scheduleProjectionCalculator;
        _cache = cache;
        _unitOfWork = unitOfWork;
        _mutationLock = mutationLock;
        _visitorCapabilities = visitorCapabilities;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventDraftCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateEventDraftRequestDtoValidator(
            _audienceAgeRepository,
            _audienceGenderRepository,
            _eventTypeRepository,
            _visibilityTypeRepository,
            _eventFormatRepository,
            _storageObjectRepository,
            _eventSeriesRepository,
            _eventRegistrationPolicyRepository);

        var validationResult = await validator.ValidateAsync(request.Draft, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Event draft update failed.");
        }

        Guid? tenantId = null;
        BaseCommandResponse<Guid> response = await _mutationLock.ExecuteOrderedGroupsAsync(
            [VisitorAccessCapabilityResolver.AuthoritySettingKeys],
            token => _unitOfWork.ExecuteSerializableAsync(async innerToken =>
            {
                var result = await UpdateAsync(request, innerToken);
                tenantId = result.TenantId;
                return result.Response;
            }, token), cancellationToken);
        if (response.IsSuccess && tenantId.HasValue)
        {
            await _cache.RemoveAsync($"event:detail:{request.Id}", cancellationToken);
            await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantId.Value), cancellationToken);
        }

        return response;
    }

    private async Task<(BaseCommandResponse<Guid> Response, Guid? TenantId)> UpdateAsync(
        UpdateEventDraftCommand request, CancellationToken cancellationToken)
    {
        var eventEntity = await _eventRepository.GetScheduleGraphForUpdateAsync(request.Id, cancellationToken);
        if (eventEntity is null)
        {
            return (BaseCommandResponse.Validation<Guid>(["Event not found."], "Event not found."), null);
        }

        if (eventEntity.ConcurrencyStamp != request.Draft.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The event draft changed since it was loaded. Refresh the event and try again.",
                "event",
                eventEntity.Id.ToString());
        }

        EventStatusEnum currentStatus = (EventStatusEnum)eventEntity.EventStatusId;
        if (!EventLifecycleRules.IsDraftEditable(currentStatus))
        {
            return (BaseCommandResponse.Failure<Guid>(
                DraftNotEditableCode,
                "Event draft update failed.",
                ["Only draft events can be updated through the draft workflow."]), null);
        }

        if (!await ImageReferenceEligibility.AreEligibleAsync(
                _storageObjectRepository,
                eventEntity.TenantId,
                request.Draft.FeaturedImageId,
                request.Draft.BackgroundImageId))
        {
            return (BaseCommandResponse.Validation<Guid>(
                ["Every image must be an active public safe-raster object in the current tenant."],
                "Event draft update failed."), null);
        }

        EventParticipationConfiguration? participationConfiguration =
            await _participationConfigurationRepository.GetByEventAndTenantAsync(
                eventEntity.Id,
                eventEntity.TenantId,
                cancellationToken);
        if (participationConfiguration is null)
        {
            return (BaseCommandResponse.Failure<Guid>(
                "event_participation_configuration_not_found",
                "Event participation configuration not found."), null);
        }

        if (participationConfiguration.ConcurrencyStamp
            != request.Draft.ExpectedParticipationConfigurationConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The event participation configuration changed since it was loaded. Refresh the event and try again.",
                "event_participation_configuration",
                eventEntity.Id.ToString());
        }

        var capability = await _visitorCapabilities.ResolveAsync(eventEntity.TenantId, cancellationToken);
        if (request.Draft.ParticipationConfiguration.IdentityAccessModeId == (int)IdentityAccessModeEnum.AccountRequired
            && !capability.AllowsAccountRequiredParticipation)
        {
            return (BaseCommandResponse.Failure<Guid>("event_visitor_account_onboarding_required",
                "Account-required participation requires an allowed public onboarding provider."), null);
        }

        eventEntity.EnsureDraftEditable();
        participationConfiguration.Reconfigure(
            request.Draft.ParticipationConfiguration.ParticipationHandlingModeId,
            request.Draft.ParticipationConfiguration.AdvanceRegistrationObligationId,
            request.Draft.ParticipationConfiguration.IdentityAccessModeId,
            request.Draft.ParticipationConfiguration.GuestRecoveryPolicy);

        var draft = request.Draft;
        eventEntity.Title = draft.Title;
        eventEntity.Subtitle = draft.Subtitle;
        eventEntity.Description = draft.Description;
        eventEntity.Content = draft.Content;
        eventEntity.Slug = draft.Slug;
        eventEntity.EventTypeId = draft.EventTypeId;
        eventEntity.AudienceGenderId = draft.AudienceGenderId;
        eventEntity.AudienceAgeId = draft.AudienceAgeId;
        eventEntity.FeaturedImageId = draft.FeaturedImageId;
        eventEntity.VisibilityTypeId = draft.VisibilityTypeId;
        eventEntity.EventFormatId = draft.EventFormatId;
        eventEntity.MadhabId = draft.MadhabId;
        var timezoneId = ScheduleTimeZoneResolver.NormalizeOrUtc(draft.EventTimeZoneId ?? draft.Timezone);
        eventEntity.Timezone = timezoneId;
        eventEntity.EventTimeZoneId = timezoneId;
        eventEntity.BackgroundColor = draft.BackgroundColor;
        eventEntity.BackgroundEffect = draft.BackgroundEffect;
        eventEntity.BackgroundImageId = draft.BackgroundImageId;
        eventEntity.SourceTemplateId = draft.TemplateId;
        eventEntity.EventSeriesId = draft.EventSeriesId;
        eventEntity.SeriesOrder = draft.SeriesOrder;
        eventEntity.RegistrationPolicyId = draft.RegistrationPolicyId;
        eventEntity.ApplyScheduleTimeZone(timezoneId, _scheduleProjectionCalculator);

        await _participationConfigurationRepository.UpdateAsync(participationConfiguration, cancellationToken);
        await _eventRepository.Update(eventEntity);

        return (BaseCommandResponse.Success(eventEntity.Id, "Event draft updated successfully."), eventEntity.TenantId);
    }
}
