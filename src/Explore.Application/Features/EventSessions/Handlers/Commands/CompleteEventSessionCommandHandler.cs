using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessions.Handlers.Commands;

public sealed class CompleteEventSessionCommandHandler(
    IEventSessionRepository eventSessionRepository,
    IEventRepository eventRepository,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    TimeProvider timeProvider)
    : EventSessionLifecycleTransitionCommandHandlerBase<CompleteEventSessionCommand>(
        eventSessionRepository,
        eventRepository,
        unitOfWork,
        cache,
        timeProvider)
{
    protected override string ActionName => "complete";
    protected override string PastTenseActionName => "completed";
    protected override string ConcurrencyFailureCode => "event_session_complete_concurrency_conflict";
    protected override string InvalidStatusFailureCode => "event_session_complete_invalid_status";

    protected override bool IsAlreadyApplied(EventSession session) =>
        session.EventSessionStatusId == (int)EventSessionStatusEnum.Completed;

    protected override bool ApplyTransition(EventSession session, EventStatusEnum parentEventStatus, DateTime occurredAt) =>
        session.Complete(parentEventStatus, occurredAt);
}
