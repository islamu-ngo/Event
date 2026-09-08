using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessions.Handlers.Commands;

public sealed class ArchiveEventSessionCommandHandler(
    IEventSessionRepository eventSessionRepository,
    IEventRepository eventRepository,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    TimeProvider timeProvider)
    : EventSessionLifecycleTransitionCommandHandlerBase<ArchiveEventSessionCommand>(
        eventSessionRepository,
        eventRepository,
        unitOfWork,
        cache,
        timeProvider)
{
    protected override string ActionName => "archive";
    protected override string PastTenseActionName => "archived";
    protected override string ConcurrencyFailureCode => "event_session_archive_concurrency_conflict";
    protected override string InvalidStatusFailureCode => "event_session_archive_invalid_status";

    protected override bool IsAlreadyApplied(EventSession session) =>
        session.EventSessionStatusId == (int)EventSessionStatusEnum.Archived;

    protected override bool ApplyTransition(EventSession session, EventStatusEnum parentEventStatus, DateTime occurredAt) =>
        session.Archive(parentEventStatus, occurredAt);
}
