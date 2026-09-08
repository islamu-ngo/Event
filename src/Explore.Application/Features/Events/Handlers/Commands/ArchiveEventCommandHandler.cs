using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event.Validators;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Services.Lifecycle;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Events.Handlers.Commands;

public sealed class ArchiveEventCommandHandler(
    IEventRepository eventRepository,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    IUserContext userContext,
    AtprotoEventPublicationPlanner atprotoPublicationPlanner,
    TimeProvider timeProvider) : IRequestHandler<ArchiveEventCommand, BaseCommandResponse<Guid>>
{
    private const string ConcurrencyConflictCode = "event_archive_concurrency_conflict";
    private const string TransitionNotAllowedCode = "event_archive_transition_not_allowed";

    public async Task<BaseCommandResponse<Guid>> Handle(ArchiveEventCommand request, CancellationToken cancellationToken)
    {
        var validator = new ArchiveEventRequestDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(request.Id, "Event archive request is invalid.", validationResult.Errors.Select(e => e.ErrorMessage));
        }

        var @event = await eventRepository.GetById(request.Id);
        if (@event is null)
        {
            return Failure(request.Id, "Event was not found.", new[] { "Event was not found." });
        }

        EventStatusEnum currentStatus = (EventStatusEnum)@event.EventStatusId;
        if (currentStatus == EventStatusEnum.Archived)
        {
            return Success(@event.Id, "Event is already archived.");
        }

        if (@event.ConcurrencyStamp != request.Request.ExpectedConcurrencyStamp)
        {
            return Failure(request.Id, "Event was modified by another user.", new[] { "The event was modified by another user. Refresh and try again." }, ConcurrencyConflictCode);
        }

        if (!EventLifecycleRules.CanTransition(currentStatus, EventStatusEnum.Archived))
        {
            return Failure(request.Id, "Event cannot be archived from its current status.", new[] { "Event cannot be archived from its current status." }, TransitionNotAllowedCode);
        }

        Guid currentUserId = userContext.GetRequiredUserId();
        Guid federationOutboxId = Guid.CreateVersion7();
        DateTime occurredAt = timeProvider.GetUtcNow().UtcDateTime;
        DateTime federationCreatedAt = occurredAt;
        Guid? tenantIdToInvalidate = null;
        bool mutationAttempted = false;
        BaseCommandResponse<Guid> response = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            tenantIdToInvalidate = null;
            var attemptEvent = await eventRepository.GetById(request.Id);
            if (attemptEvent is null)
                return Failure(request.Id, "Event was not found.", new[] { "Event was not found." });

            EventStatusEnum attemptStatus = (EventStatusEnum)attemptEvent.EventStatusId;
            if (mutationAttempted && attemptStatus == EventStatusEnum.Archived)
            {
                tenantIdToInvalidate = attemptEvent.TenantId;
                return Success(attemptEvent.Id, "Event archived successfully.");
            }

            if (attemptEvent.ConcurrencyStamp != request.Request.ExpectedConcurrencyStamp)
            {
                return Failure(request.Id, "Event was modified by another user.", new[] { "The event was modified by another user. Refresh and try again." }, ConcurrencyConflictCode);
            }

            if (!EventLifecycleRules.CanTransition(attemptStatus, EventStatusEnum.Archived))
            {
                return Failure(request.Id, "Event cannot be archived from its current status.", new[] { "Event cannot be archived from its current status." }, TransitionNotAllowedCode);
            }

            attemptEvent.Archive(occurredAt);
            mutationAttempted = true;

            await eventRepository.Update(attemptEvent);
            await atprotoPublicationPlanner.PlanEventAsync(
                new AtprotoEventPublicationInput(
                    attemptEvent.TenantId,
                    currentUserId,
                    attemptEvent.Id,
                    attemptEvent.ConcurrencyStamp,
                    PdsSyncOperation.Delete,
                    federationOutboxId,
                    federationCreatedAt),
                token);

            tenantIdToInvalidate = attemptEvent.TenantId;
            return Success(attemptEvent.Id, "Event archived successfully.");
        }, cancellationToken);

        if (response.IsSuccess && tenantIdToInvalidate.HasValue)
        {
            await cache.RemoveAsync($"event:detail:{request.Id}", cancellationToken);
            await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantIdToInvalidate.Value), cancellationToken);
        }

        return response;
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, IEnumerable<string> errors, string? failureCode = null) =>
        failureCode is null
            ? BaseCommandResponse.Validation(errors, message, id)
            : BaseCommandResponse.Failure(failureCode, message, errors, id);
}
