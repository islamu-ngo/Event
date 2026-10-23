using Explore.Application.Caching;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Services.Lifecycle;
using Explore.Domain.Enums;
using Explore.Domain.Ai;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class AttachAiDraftImageCommandHandler(
    IAiEventDraftRecoveryRepository recovery,
    IEventRepository events,
    IStorageObjectRepository objects,
    IStorageUploadSessionRepository sessions,
    IAiConversationRepository conversations,
    ICurrentUserService currentUser,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    RequestAuthorization<AttachAiDraftImageCommand> authorization,
    ILogger<AttachAiDraftImageCommandHandler> logger,
    TimeProvider timeProvider) : ICommandHandler<AttachAiDraftImageCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        AttachAiDraftImageCommand request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId != request.Identity.PrincipalUserId ||
            tenant.TenantId != request.Identity.TenantId)
            return Denied();
        await authorization.AuthorizeAsync(request, logger, cancellationToken);
        Guid eventStamp = Guid.CreateVersion7();
        var response = await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            var execution = await recovery.ReadForSettlementAsync(
                request.Identity, request.ExecutionId, request.ExpectedExecutionStamp, token);
            if (execution is null || execution.PrimaryEventId != request.EventId ||
                execution.ImageStorageObjectId != request.StorageObjectId)
                return Denied();
            await authorization.AuthorizeAsync(request, logger, token);
            var action = await conversations.GetProposedActionAsync(request.Identity.ProposedActionId, token);
            if (action?.Conversation?.Status != AiConversationStatus.Active ||
                !AiProposedAction.IsWithinLifetime(execution.ActionCreatedAt, timeProvider.GetUtcNow().UtcDateTime) ||
                execution.ImageUploadSessionId is not { } sessionId)
                return Denied();
            var session = await sessions.GetForAuthorizationAsync(sessionId, token);
            var image = await objects.GetForAuthorizationAsync(request.StorageObjectId, tenant.TenantId, token);
            if (session is null || session.TenantId != request.Identity.TenantId ||
                session.UserId != request.Identity.PrincipalUserId ||
                session.IdempotencyKey != execution.ImageUploadOperationKey ||
                session.Status != StorageUploadSessionStates.Finalized || session.StorageObjectId != request.StorageObjectId ||
                session.Purpose != StorageObjectPurposes.EventImage || session.Visibility != StorageObjectVisibilities.PublicImage ||
                !SafeRasterContentPolicy.IsEligibleImageReference(image, request.Identity.TenantId) ||
                image!.CreatedBy != request.Identity.PrincipalUserId || image.Purpose != StorageObjectPurposes.EventImage ||
                image.Provider != session.Provider || image.ObjectKey != session.ObjectKey ||
                image.StorageProviderBindingId != session.StorageProviderBindingId)
                return Denied();
            var target = await events.GetAuthorizedForUpdateAsync(
                tenant.TenantId, request.Identity.PrincipalUserId, request.EventId, token);
            if (target is null || target.ActorId != request.Identity.ActingActorId ||
                target.CreatedBy != request.Identity.PrincipalUserId ||
                !EventLifecycleRules.IsDraftEditable((EventStatusEnum)target.EventStatusId))
                return Denied();
            // The native custody owner locks and revalidates the original finalized session
            // and object before any event mutation, without transferring bytes or replacing custody.
            if (!execution.CompletedAt.HasValue)
                await recovery.SaveSettlementAsync(request.Identity, execution, token);
            if (target.FeaturedImageId == request.StorageObjectId)
                return BaseCommandResponse.Success(target.Id, "Draft image is already attached.");
            if (target.FeaturedImageId.HasValue || execution.CompletedAt.HasValue)
                return Denied();
            target.FeaturedImageId = request.StorageObjectId;
            target.ConcurrencyStamp = eventStamp;
            await events.Update(target);
            return BaseCommandResponse.Success(target.Id, "Draft image attached.");
        }, cancellationToken);
        if (response.IsSuccess)
        {
            await cache.RemoveByTagAsync(CacheTags.Event(request.EventId), cancellationToken);
            await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenant.TenantId), cancellationToken);
        }
        return response;
    }

    private static BaseCommandResponse<Guid> Denied() =>
        BaseCommandResponse.Failure<Guid>("ai_draft_image_authority_changed",
            "The draft image could not be attached under current authority.");
}
