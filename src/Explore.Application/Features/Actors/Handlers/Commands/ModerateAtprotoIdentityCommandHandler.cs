using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Actors.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Actors.Handlers.Commands;

public sealed class ModerateAtprotoIdentityCommandHandler(
    IAdminContext adminContext,
    IAtprotoIdentityRepository identityRepository,
    IAtprotoRecordRepository recordRepository,
    IPdsSyncOutboxRepository pdsSyncOutboxRepository,
    IEventRepository eventRepository,
    AtprotoEventPublicationPlanner atprotoPublicationPlanner,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    IEnumerable<IAtprotoDiscoveryCacheInvalidator> discoveryCacheInvalidators,
    TimeProvider timeProvider)
    : IRequestHandler<ModerateAtprotoIdentityCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ModerateAtprotoIdentityCommand request,
        CancellationToken cancellationToken)
    {
        if (request.AtprotoIdentityId == Guid.Empty || request.Moderation is null)
        {
            return ValidationFailure(request.AtprotoIdentityId, "AT Protocol identity moderation requires a target and action.");
        }

        var validation = await new GlobalModerationRequestValidator()
            .ValidateAsync(request.Moderation, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation(
                validation.Errors.Select(error => error.ErrorMessage),
                "AT Protocol identity moderation failed validation.",
                request.AtprotoIdentityId);
        }

        Guid? operatorUserId = await adminContext.ResolveUserIdAsync(cancellationToken);
        if (!operatorUserId.HasValue)
        {
            return ValidationFailure(
                request.AtprotoIdentityId,
                "Authenticated instance administrator context is required.",
                FailureCodes.AuthenticationRequired);
        }

        if (!await adminContext.IsInstanceAdminAsync(operatorUserId.Value, cancellationToken))
        {
            return ValidationFailure(
                request.AtprotoIdentityId,
                "Only instance administrators can moderate global AT Protocol identities.",
                FailureCodes.AdminRequired);
        }

        var moderatedAt = timeProvider.GetUtcNow().UtcDateTime;
        var outcome = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            AtprotoIdentity? identity = await identityRepository.GetById(request.AtprotoIdentityId);
            if (identity is null || identity.IsDeleted)
            {
                throw new NotFoundException(nameof(AtprotoIdentity), request.AtprotoIdentityId);
            }

            bool changed = request.Moderation.Action == GlobalModerationAction.Suspend
                ? !identity.IsSuspended
                : identity.IsSuspended;

            if (request.Moderation.Action == GlobalModerationAction.Suspend)
            {
                identity.Suspend(request.Moderation.ReasonCode, moderatedAt, operatorUserId.Value);
            }
            else
            {
                identity.Reinstate(request.Moderation.ReasonCode, moderatedAt, operatorUserId.Value);
            }

            if (changed)
            {
                await identityRepository.Update(identity);
            }

            if (request.Moderation.Action == GlobalModerationAction.Suspend)
            {
                IReadOnlyList<AtprotoOutboundRecordOwnership> ownerships =
                    await recordRepository.GetLiveGroundedEventOwnershipsForActorAndDidAsync(
                        identity.ActorId,
                        identity.Did,
                        token);
                IReadOnlyList<PdsSyncOutbox> unsettledMutations =
                    await pdsSyncOutboxRepository.GetUnsettledEventMutationsForActorAndDidAsync(
                        identity.ActorId,
                        identity.Did,
                        AtprotoEventPublicationPlanner.EventSourceType,
                        AtprotoEventPublicationPlanner.EventCollection,
                        token);
                await PlanDeleteCompensationAsync(
                    ownerships,
                    unsettledMutations,
                    moderatedAt,
                    token);
            }

            return (
                Response: BaseCommandResponse.Success(
                    identity.Id,
                    changed
                        ? "AT Protocol identity moderation updated successfully."
                        : "AT Protocol identity already has the requested moderation state."),
                Changed: changed);
        }, cancellationToken);

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

    private async Task PlanDeleteCompensationAsync(
        IReadOnlyList<AtprotoOutboundRecordOwnership> ownerships,
        IReadOnlyList<PdsSyncOutbox> unsettledMutations,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        var targets = ownerships
            .Select(ownership => (
                ownership.TenantId,
                ownership.UserId,
                EventId: ownership.SourceEntityId))
            .Concat(unsettledMutations.Select(outbox => (
                outbox.TenantId,
                outbox.UserId,
                EventId: outbox.SourceEntityId)))
            .DistinctBy(target => (target.TenantId, target.EventId));

        foreach (var target in targets)
        {
            Event? eventEntity = await eventRepository.GetAtprotoLifecycleStateAsync(
                target.TenantId,
                target.EventId,
                cancellationToken);
            if (eventEntity is null)
            {
                continue;
            }

            await atprotoPublicationPlanner.PlanEventAsync(
                new AtprotoEventPublicationInput(
                    target.TenantId,
                    target.UserId,
                    target.EventId,
                    eventEntity.ConcurrencyStamp,
                    PdsSyncOperation.Delete,
                    Guid.CreateVersion7(),
                    createdAtUtc),
                cancellationToken);
        }
    }

    private static BaseCommandResponse<Guid> ValidationFailure(
        Guid identityId,
        string message,
        string? failureCode = null) => failureCode switch
        {
            FailureCodes.AuthenticationRequired => BaseCommandResponse.Authentication<Guid>(message),
            FailureCodes.AdminRequired => BaseCommandResponse.Authorization<Guid>(message),
            _ => BaseCommandResponse.Validation<Guid>([message], message, identityId)
        };
}
