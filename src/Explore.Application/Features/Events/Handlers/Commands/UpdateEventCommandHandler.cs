using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.Event.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Services.Scheduling;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Events.Handlers.Commands;

public class UpdateEventCommandHandler : IRequestHandler<UpdateEventCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventRepository _eventRepository;
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
    private readonly IUserContext _userContext;
    private readonly AtprotoEventPublicationPlanner _atprotoPublicationPlanner;
    private readonly NotificationFanoutOccurrenceCoordinator _fanoutCoordinator;
    private readonly IEventLifecycleScheduler _eventLifecycleScheduler;
    private readonly IRefundCampaignRepository _refundCampaignRepository;
    private readonly TimeProvider _timeProvider;

    public UpdateEventCommandHandler(
        IEventRepository eventRepository,
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
        IUserContext userContext,
        AtprotoEventPublicationPlanner atprotoPublicationPlanner,
        NotificationFanoutOccurrenceCoordinator fanoutCoordinator,
        IEventLifecycleScheduler eventLifecycleScheduler,
        IRefundCampaignRepository refundCampaignRepository,
        TimeProvider timeProvider,
        ISettingMutationLock mutationLock)
    {
        _eventRepository = eventRepository;
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
        _userContext = userContext;
        _atprotoPublicationPlanner = atprotoPublicationPlanner;
        _fanoutCoordinator = fanoutCoordinator;
        _eventLifecycleScheduler = eventLifecycleScheduler;
        _refundCampaignRepository = refundCampaignRepository;
        _timeProvider = timeProvider;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateEventCommand request, CancellationToken cancellationToken)
    {
        var validator = new UpdateEventDtoValidator(
            _audienceAgeRepository,
            _audienceGenderRepository,
            _eventTypeRepository,
            _visibilityTypeRepository,
            _eventFormatRepository,
            _storageObjectRepository,
            _eventSeriesRepository,
            _eventRegistrationPolicyRepository);

        var validationResult = await validator.ValidateAsync(request.UpdateEventDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "Event update failed.");
        }

        Guid currentUserId = _userContext.GetRequiredUserId();
        DateTime occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
        Guid federationOutboxId = Guid.CreateVersion7();
        Guid occurrenceId = Guid.CreateVersion7();
        Guid pointerOutboxMessageId = Guid.CreateVersion7();
        Guid eventIdForCache = Guid.Empty;
        Guid tenantIdForCache = Guid.Empty;

        BaseCommandResponse<Guid> response = await _mutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Email.DeliveryEnabled]],
            policyToken => _unitOfWork.ExecuteSerializableAsync(async token =>
        {
            Explore.Domain.Event? eventEntity = await _eventRepository.GetScheduleGraphForUpdateAsync(request.EventId, token);
            if (eventEntity is null)
            {
                return BaseCommandResponse.NotFound<Guid>("Event not found.");
            }

            if (eventEntity.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
            {
                throw new ConcurrencyConflictException(
                    ConcurrencyConflictException.ConcurrentUpdate,
                    "The event changed since it was loaded. Refresh the event and try again.",
                    "event",
                    eventEntity.Id.ToString());
            }

            var update = request.UpdateEventDto;
            Guid? featuredImageId = update.FeaturedImage?.Value.HasValue == true
                ? update.FeaturedImage.Value.Value
                : null;
            Guid? backgroundImageId = update.BackgroundImage?.Value.HasValue == true
                ? update.BackgroundImage.Value.Value
                : null;
            if (!await ImageReferenceEligibility.AreEligibleAsync(
                    _storageObjectRepository,
                    eventEntity.TenantId,
                    featuredImageId,
                    backgroundImageId))
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["Every image must be an active public safe-raster object in the current tenant."],
                    "Event update failed.");
            }

            string previousTitle = eventEntity.Title;
            string previousTimezone = eventEntity.GetEffectiveScheduleTimeZoneId();
            bool timezoneRequested = TryResolveRequestedTimezone(
                update.Timezone,
                update.EventTimeZone,
                out string requestedTimezone);
            bool timezoneChanged = timezoneRequested
                && !string.Equals(previousTimezone, requestedTimezone, StringComparison.Ordinal);
            NotificationFanoutSessionDisplayTimeV1[] beforeSessionTimes = timezoneChanged
                    ? CapturePublishedSessionDisplayTimes(eventEntity, previousTimezone)
                    : [];

            ApplyTitle(eventEntity, update.Title);
            ApplySubtitle(eventEntity, update.Subtitle);
            ApplyDescription(eventEntity, update.Description);
            ApplyContent(eventEntity, update.Content);
            ApplySlug(eventEntity, update.Slug);
            ApplyEventType(eventEntity, update.EventType);
            ApplyAudienceGender(eventEntity, update.AudienceGender);
            ApplyAudienceAge(eventEntity, update.AudienceAge);
            ApplyFeaturedImage(eventEntity, update.FeaturedImage);
            ApplyVisibility(eventEntity, update.Visibility);
            ApplyFormat(eventEntity, update.Format);
            ApplyMadhab(eventEntity, update.Madhab);
            ApplyTimezone(eventEntity, update.Timezone, update.EventTimeZone);
            ApplyBackgroundColor(eventEntity, update.BackgroundColor);
            ApplyBackgroundEffect(eventEntity, update.BackgroundEffect);
            ApplyBackgroundImage(eventEntity, update.BackgroundImage);
            ApplyTemplate(eventEntity, update.SourceTemplate);
            ApplySeries(eventEntity, update.SeriesMembership);
            ApplySeriesOrder(eventEntity, update.SeriesOrder);
            ApplyRegistrationPolicy(eventEntity, update.RegistrationPolicy);

            await _eventRepository.Update(eventEntity);
            if (eventEntity.EventStatusId == (int)EventStatusEnum.Published)
            {
                await _atprotoPublicationPlanner.PlanEventAsync(
                    new AtprotoEventPublicationInput(
                        eventEntity.TenantId,
                        currentUserId,
                        eventEntity.Id,
                        eventEntity.ConcurrencyStamp,
                        PdsSyncOperation.Update,
                        federationOutboxId,
                        occurredAt),
                    token);
            }

            if (eventEntity.EventStatusId == (int)EventStatusEnum.Published && timezoneChanged)
            {
                string currentTimezone = eventEntity.GetEffectiveScheduleTimeZoneId();
                NotificationFanoutSessionDisplayTimeV1[] afterSessionTimes = CapturePublishedSessionDisplayTimes(
                    eventEntity,
                    currentTimezone);
                if (HaveAttendeeVisibleTimeChanges(beforeSessionTimes, afterSessionTimes))
                {
                    var before = new NotificationFanoutSnapshotV1(
                        previousTitle,
                        SessionTitle: null,
                        StartsAt: null,
                        EndsAt: null,
                        Timezone: previousTimezone,
                        Location: null,
                        SessionDisplayTimes: beforeSessionTimes);
                    var after = new NotificationFanoutSnapshotV1(
                        eventEntity.Title,
                        SessionTitle: null,
                        StartsAt: null,
                        EndsAt: null,
                        Timezone: currentTimezone,
                        Location: null,
                        SessionDisplayTimes: afterSessionTimes);
                    await _fanoutCoordinator.CoordinateInCurrentTransactionAsync(
                        new NotificationFanoutOccurrenceCandidate(
                            occurrenceId,
                            pointerOutboxMessageId,
                            eventEntity.TenantId,
                            eventEntity.Id,
                            SessionId: null,
                            occurredAt,
                            occurredAt,
                            request.ExpectedConcurrencyStamp,
                            NotificationFanoutTemplateJson.Serialize(new NotificationFanoutChangeSetV1(
                                [NotificationFanoutChangeField.Timezone])),
                            NotificationFanoutTemplateJson.Serialize(before),
                            NotificationFanoutTemplateJson.Serialize(after),
                            NotificationFanoutRecipientTemplateFactory.EventUpdatedTemplateKey,
                            NotificationFanoutRecipientTemplateFactory.CurrentTemplateVersion,
                            (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
                            NotificationFanoutRecipientTemplateFactory.CurrentPolicyVersion,
                            occurredAt,
                            "event_timezone_update_command",
                            eventEntity.Id),
                        token);
                    RefundCampaign materialChange = RefundCampaign.CreateMaterialChange(
                        Guid.CreateVersion7(), eventEntity.TenantId, eventEntity.Id, currentUserId,
                        "Published event timezone changed.", occurredAt);
                    await _refundCampaignRepository.CreateAsync(
                        materialChange,
                        RefundOutboxMessageFactory.CreateCampaignProcess(materialChange, occurredAt),
                        token);
                }

                await _eventLifecycleScheduler.ReprojectEventRemindersInCurrentTransactionAsync(
                    new EventReminderReprojectionInput(
                        eventEntity.TenantId,
                        eventEntity.Id,
                        RegistrationOrderId: null,
                        SessionId: null,
                        eventEntity.Title,
                        occurredAt,
                        currentTimezone),
                    token);
            }

            eventIdForCache = eventEntity.Id;
            tenantIdForCache = eventEntity.TenantId;
            return BaseCommandResponse.Success(eventEntity.Id, "Event updated successfully.");
        }, policyToken), cancellationToken);

        if (!response.IsSuccess)
        {
            return response;
        }

        try
        {
            await _cache.RemoveAsync($"event:detail:{eventIdForCache}", cancellationToken);
            await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantIdForCache), cancellationToken);
        }
        catch (Exception)
        {
            // Best-effort cache invalidation - Redis may be unavailable in local dev
        }

        return response;
    }

    private static bool TryResolveRequestedTimezone(
        UpdateEventTimezoneDto? timezone,
        UpdateEventEventTimeZoneDto? eventTimeZone,
        out string timezoneId)
    {
        bool hasTimezone = timezone?.Value.HasValue == true;
        bool hasEventTimezone = eventTimeZone?.Value.HasValue == true;
        if (!hasTimezone && !hasEventTimezone)
        {
            timezoneId = string.Empty;
            return false;
        }

        string? requested = hasEventTimezone
            ? eventTimeZone!.Value.Value
            : timezone!.Value.Value;
        timezoneId = ScheduleTimeZoneResolver.NormalizeOrUtc(requested);
        return true;
    }

    private static NotificationFanoutSessionDisplayTimeV1[] CapturePublishedSessionDisplayTimes(
        Explore.Domain.Event eventEntity,
        string timezoneId)
    {
        TimeZoneInfo timezone = ScheduleTimeZoneResolver.ResolveRequired(timezoneId);
        return eventEntity.Sessions
            .Where(session => session.ContributesToPublicScheduleSummary())
            .OrderBy(session => session.Id)
            .Select(session => new NotificationFanoutSessionDisplayTimeV1(
                session.Id,
                session.Title,
                TimeZoneInfo.ConvertTime(session.StartTime!.Value, timezone),
                session.EndTime.HasValue
                    ? TimeZoneInfo.ConvertTime(session.EndTime.Value, timezone)
                    : null))
            .ToArray();
    }

    private static bool HaveAttendeeVisibleTimeChanges(
        NotificationFanoutSessionDisplayTimeV1[] before,
        NotificationFanoutSessionDisplayTimeV1[] after)
    {
        return before.Length == after.Length
            && before.Zip(after).Any(pair =>
                pair.First.SessionId != pair.Second.SessionId
                || !pair.First.StartsAt.EqualsExact(pair.Second.StartsAt)
                || !ExactEquals(pair.First.EndsAt, pair.Second.EndsAt));
    }

    private static bool ExactEquals(DateTimeOffset? left, DateTimeOffset? right) =>
        left.HasValue == right.HasValue
        && (!left.HasValue || left.Value.EqualsExact(right!.Value));

    private static void ApplyTitle(Explore.Domain.Event eventEntity, UpdateEventTitleDto? update)
    {
        if (update is not null)
        {
            eventEntity.Title = update.Value;
        }
    }

    private static void ApplySubtitle(Explore.Domain.Event eventEntity, UpdateEventSubtitleDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.Subtitle = update.Value.Value;
        }
    }

    private static void ApplyDescription(Explore.Domain.Event eventEntity, UpdateEventDescriptionDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.Description = update.Value.Value;
        }
    }

    private static void ApplyContent(Explore.Domain.Event eventEntity, UpdateEventContentDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.Content = update.Value.Value;
        }
    }

    private static void ApplySlug(Explore.Domain.Event eventEntity, UpdateEventSlugDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.Slug = update.Value.Value;
        }
    }

    private static void ApplyEventType(Explore.Domain.Event eventEntity, UpdateEventEventTypeDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.EventTypeId = update.Value.Value;
        }
    }

    private static void ApplyAudienceGender(Explore.Domain.Event eventEntity, UpdateEventAudienceGenderDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.AudienceGenderId = update.Value.Value;
        }
    }

    private static void ApplyAudienceAge(Explore.Domain.Event eventEntity, UpdateEventAudienceAgeDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.AudienceAgeId = update.Value.Value;
        }
    }

    private static void ApplyFeaturedImage(Explore.Domain.Event eventEntity, UpdateEventFeaturedImageDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.FeaturedImageId = update.Value.Value;
        }
    }

    private static void ApplyVisibility(Explore.Domain.Event eventEntity, UpdateEventVisibilityDto? update)
    {
        if (update is not null)
        {
            eventEntity.VisibilityTypeId = update.Value;
        }
    }

    private static void ApplyFormat(Explore.Domain.Event eventEntity, UpdateEventFormatDto? update)
    {
        if (update is not null)
        {
            eventEntity.EventFormatId = update.Value;
        }
    }

    private static void ApplyMadhab(Explore.Domain.Event eventEntity, UpdateEventMadhabDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.MadhabId = update.Value.Value;
        }
    }

    private void ApplyTimezone(
        Explore.Domain.Event eventEntity,
        UpdateEventTimezoneDto? timezone,
        UpdateEventEventTimeZoneDto? eventTimeZone)
    {
        if (!TryResolveRequestedTimezone(timezone, eventTimeZone, out string timezoneId))
        {
            return;
        }

        eventEntity.ApplyScheduleTimeZone(timezoneId, _scheduleProjectionCalculator);
    }

    private static void ApplyBackgroundColor(Explore.Domain.Event eventEntity, UpdateEventBackgroundColorDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.BackgroundColor = update.Value.Value;
        }
    }

    private static void ApplyBackgroundEffect(Explore.Domain.Event eventEntity, UpdateEventBackgroundEffectDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.BackgroundEffect = update.Value.Value;
        }
    }

    private static void ApplyBackgroundImage(Explore.Domain.Event eventEntity, UpdateEventBackgroundImageDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.BackgroundImageId = update.Value.Value;
        }
    }

    private static void ApplyTemplate(Explore.Domain.Event eventEntity, UpdateEventSourceTemplateDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.SourceTemplateId = update.Value.Value;
        }
    }

    private static void ApplySeries(Explore.Domain.Event eventEntity, UpdateEventSeriesMembershipDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.EventSeriesId = update.Value.Value;
        }
    }

    private static void ApplySeriesOrder(Explore.Domain.Event eventEntity, UpdateEventSeriesOrderDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.SeriesOrder = update.Value.Value;
        }
    }

    private static void ApplyRegistrationPolicy(Explore.Domain.Event eventEntity, UpdateEventRegistrationPolicyDto? update)
    {
        if (update?.Value.HasValue == true)
        {
            eventEntity.RegistrationPolicyId = update.Value.Value;
        }
    }
}
