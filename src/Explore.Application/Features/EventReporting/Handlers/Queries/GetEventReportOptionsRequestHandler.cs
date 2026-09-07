using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Policies;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Explore.Application.Features.EventReporting.Handlers.Queries;

public sealed class GetEventReportOptionsRequestHandler(
    IEventRepository eventRepository,
    ITenantContext tenantContext,
    IEventReportingIntakeGuard intakeGuard,
    HybridCache cache,
    IOptions<EventReportSubmissionOptions> optionsAccessor)
    : IRequestHandler<GetEventReportOptionsRequest, EventReportOptionsDto?>
{
    private const string ReasonOptionsCacheKey = "event-reporting:reason-options:v1";
    private const string EventNotReportableStatusCode = "event_not_reportable_status";

    public async Task<EventReportOptionsDto?> Handle(
        GetEventReportOptionsRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        if (request.EventId == Guid.Empty || tenantId == Guid.Empty)
        {
            return null;
        }

        var eventEntity = await eventRepository.GetAuthorizationTargetByIdAsync(
            request.EventId,
            cancellationToken);
        if (eventEntity is null || eventEntity.TenantId != tenantId)
        {
            return null;
        }

        if (!await eventRepository.IsPubliclyEligibleAsync(
                tenantId,
                request.EventId,
                cancellationToken))
        {
            return null;
        }

        var maxReporterTextLength = Math.Max(1, optionsAccessor.Value.MaxReporterTextLength);
        EventReportingIntakeDecision intakeDecision = await intakeGuard.ResolveAsync(tenantId, cancellationToken);
        if (!intakeDecision.IntakeEnabled)
        {
            return new EventReportOptionsDto
            {
                EventId = request.EventId,
                IsReportable = false,
                UnavailableReasonCode = intakeDecision.ReasonCode,
                UnavailableReasonMessage = intakeDecision.Message,
                MaxReporterTextLength = maxReporterTextLength,
                ReasonOptions = []
            };
        }

        if (eventEntity.EventStatusId != (int)EventStatusEnum.Published)
        {
            return new EventReportOptionsDto
            {
                EventId = request.EventId,
                IsReportable = false,
                UnavailableReasonCode = EventNotReportableStatusCode,
                UnavailableReasonMessage = "Only published events can be reported.",
                MaxReporterTextLength = maxReporterTextLength,
                ReasonOptions = []
            };
        }

        var reasonOptions = await cache.GetOrCreateAsync(
            ReasonOptionsCacheKey,
            static _ => new ValueTask<IReadOnlyList<EventReportReasonOptionDto>>(BuildReasonOptions()),
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromHours(12),
                LocalCacheExpiration = TimeSpan.FromHours(1)
            },
            cancellationToken: cancellationToken);

        return new EventReportOptionsDto
        {
            EventId = request.EventId,
            IsReportable = true,
            MaxReporterTextLength = maxReporterTextLength,
            ReasonOptions = reasonOptions
        };
    }

    private static IReadOnlyList<EventReportReasonOptionDto> BuildReasonOptions()
    {
        return EventReportReasonCodePolicy.GetReasonOptions()
            .Select(option => new EventReportReasonOptionDto
            {
                ReasonId = option.Id,
                ReasonCode = option.Code,
                ReasonName = option.DisplayName,
                Description = option.Description
            })
            .ToArray();
    }
}
