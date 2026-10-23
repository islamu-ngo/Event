using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Actors.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Application.Contracts.Operations;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Actors.Handlers.Commands;

public sealed class ModerateActorCommandHandler(
    IAdminContext adminContext,
    IActorRepository actorRepository,
    AtprotoModerationCompensationPlanner compensationPlanner,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    IEnumerable<IAtprotoDiscoveryCacheInvalidator> discoveryCacheInvalidators,
    TimeProvider timeProvider)
    : ICommandHandler<ModerateActorCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ModerateActorCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ActorId == Guid.Empty || request.Moderation is null)
        {
            return ValidationFailure(request.ActorId, "Actor moderation requires a target and action.");
        }

        var validation = await new GlobalModerationRequestValidator()
            .ValidateAsync(request.Moderation, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation(
                validation.Errors.Select(error => error.ErrorMessage),
                "Actor moderation failed validation.",
                request.ActorId);
        }

        Guid? operatorUserId = await adminContext.ResolveUserIdAsync(cancellationToken);
        if (!operatorUserId.HasValue)
        {
            return ValidationFailure(
                request.ActorId,
                "Authenticated instance administrator context is required.",
                FailureCodes.AuthenticationRequired);
        }

        if (!await adminContext.IsInstanceAdminAsync(operatorUserId.Value, cancellationToken))
        {
            return ValidationFailure(
                request.ActorId,
                "Only instance administrators can moderate global actors.",
                FailureCodes.AdminRequired);
        }

        var moderatedAt = timeProvider.GetUtcNow().UtcDateTime;
        var successorIds = new Dictionary<AtprotoPublicationLineage, Guid>();
        var outcome = await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            Actor? actor = await actorRepository.GetForModerationAsync(request.ActorId, token);
            if (actor is null || actor.IsDeleted)
            {
                throw new NotFoundException(nameof(Actor), request.ActorId);
            }

            bool changed = request.Moderation.Action == GlobalModerationAction.Suspend
                ? !actor.IsSuspended
                : actor.IsSuspended;

            if (request.Moderation.Action == GlobalModerationAction.Suspend)
            {
                actor.Suspend(request.Moderation.ReasonCode, moderatedAt, operatorUserId.Value);
            }
            else
            {
                actor.Reinstate(request.Moderation.ReasonCode, moderatedAt, operatorUserId.Value);
            }

            if (changed)
            {
                await actorRepository.Update(actor);
            }

            if (request.Moderation.Action == GlobalModerationAction.Suspend)
            {
                await compensationPlanner.PlanDeletesAsync(
                    new AtprotoModerationTarget(actor.Id, null, null, actor.ConcurrencyStamp),
                    successorIds,
                    moderatedAt,
                    token);
            }

            return (
                Response: BaseCommandResponse.Success(
                    actor.Id,
                    changed
                        ? "Actor moderation updated successfully."
                        : "Actor already has the requested moderation state."),
                Changed: changed);
        }, cancellationToken);

        await cache.RemoveAsync($"actor:detail:{outcome.Response.Id}", cancellationToken);
        await InvalidatePublicEventCachesAsync(cancellationToken);

        return outcome.Response;
    }

    private async Task InvalidatePublicEventCachesAsync(CancellationToken cancellationToken)
    {
        await cache.RemoveByTagAsync(CacheTags.Events, cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventLists, cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventDetails, cancellationToken);

        foreach (IAtprotoDiscoveryCacheInvalidator invalidator in discoveryCacheInvalidators)
        {
            await invalidator.InvalidateAsync(cancellationToken);
        }
    }

    private static BaseCommandResponse<Guid> ValidationFailure(
        Guid actorId,
        string message,
        string? failureCode = null) => failureCode switch
        {
            FailureCodes.AuthenticationRequired => BaseCommandResponse.Authentication<Guid>(message),
            FailureCodes.AdminRequired => BaseCommandResponse.Authorization<Guid>(message),
            _ => BaseCommandResponse.Validation<Guid>([message], message, actorId)
        };
}
