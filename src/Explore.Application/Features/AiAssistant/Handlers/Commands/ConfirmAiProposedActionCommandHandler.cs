using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Ai;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class ConfirmAiProposedActionCommandHandler(
    IAiConversationRepository conversationRepository,
    IOrganizationMemberRepository organizationMemberRepository,
    IGroupMemberRepository groupMemberRepository,
    IActorRepository actorRepository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>> createEventCommand,
    ICommandHandler<CreateStorageUploadSessionCommand, BaseCommandResponse<StorageUploadSessionDto>> createUpload,
    ICommandHandler<FinalizeStorageUploadSessionCommand, BaseCommandResponse<StorageUploadSessionDto>> finalizeUpload,
    IUnitOfWork unitOfWork,
    IAiEventDraftRecoveryRepository recovery,
    IAiToolContractRegistry registry,
    ICommandHandler<AttachAiDraftImageCommand, BaseCommandResponse<Guid>> attachImage,
    TimeProvider timeProvider,
    RequestAuthorization<ConfirmAiProposedActionCommand> authorization,
    ILogger<ConfirmAiProposedActionCommandHandler> logger) : ICommandHandler<ConfirmAiProposedActionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ConfirmAiProposedActionCommand request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUserService.IsAuthenticated || currentUserService.UserId is not { } userId)
        {
            return Failure("AI proposed action confirmation requires an authenticated user.", ["User is not authenticated."], "unauthenticated");
        }

        AiProposedAction? action = await conversationRepository.GetProposedActionAsync(request.ProposedActionId, cancellationToken);
        if (!IsActionVisibleToCurrentPrincipal(action, tenantContext.TenantId, userId)
            || action!.ConversationId != request.ConversationId)
        {
            return Failure("AI proposed action was not found.", ["Proposed action was not found."], "proposed_action_not_found");
        }

        await authorization.AuthorizeAsync(request, logger, cancellationToken);
        if (!string.Equals(request.ExpectedVersion, action.ConsentVersionFor(userId), StringComparison.Ordinal))
            return Failure("The displayed AI action preview has changed.",
                ["Reload and review the exact action before confirming."], "proposed_action_version_changed");
        if (action.Kind != AiProposedActionKind.CreateEventDraft)
            return Failure("This action has no implemented execution owner.", ["No tool was dispatched."],
                "unsupported_action_kind", action.Id);
        var validation = registry.ValidatePayload(action.Kind, action.PayloadJson);
        if (!validation.Succeeded)
            return Failure("The persisted tool arguments are invalid.", ["No tool was dispatched."],
                validation.FailureCode ?? "invalid_tool_arguments", action.Id);

        if (action.Status == AiProposedActionStatus.Rejected)
        {
            return Failure("AI proposed action was already rejected.", ["Rejected proposed actions cannot be confirmed."], "proposed_action_rejected", action.Id);
        }

        if (action.Status == AiProposedActionStatus.Failed)
        {
            return Failure("AI proposed action previously failed.", ["Failed proposed actions cannot be confirmed again."], "proposed_action_failed", action.Id);
        }

        if (action.Status == AiProposedActionStatus.Proposed)
        {
            var candidate = action;
            var claim = await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                var current = await conversationRepository.GetProposedActionForUpdateAsync(
                    request.ProposedActionId, candidate.ActingActorId, token);
                if (!IsActionVisibleToCurrentPrincipal(current, tenantContext.TenantId, userId)
                    || current!.ConversationId != request.ConversationId)
                    return (Action: (AiProposedAction?)null, FailureCode: "proposed_action_authority_changed");
                await authorization.AuthorizeAsync(request, logger, token);
                if (!string.Equals(request.ExpectedVersion, current.ConsentVersionFor(userId), StringComparison.Ordinal))
                    return (Action: (AiProposedAction?)null, FailureCode: "proposed_action_version_changed");
                if (!IsLive(current))
                    return (Action: (AiProposedAction?)null, FailureCode: "proposed_action_expired");
                if (current.Status != AiProposedActionStatus.Proposed || current.Kind != candidate.Kind
                    || current.PayloadJson != candidate.PayloadJson || current.ActingActorId != candidate.ActingActorId
                    || current.CreatedAt != candidate.CreatedAt || current.MessageId != candidate.MessageId
                    || current.Conversation!.ActorId != current.ActingActorId)
                    return (Action: (AiProposedAction?)null, FailureCode: "proposed_action_authority_changed");
                DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                current.Confirm(userId, new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc));
                await conversationRepository.UpdateProposedActionAsync(current, token);
                return (Action: (AiProposedAction?)current, FailureCode: (string?)null);
            }, cancellationToken);
            action = claim.Action;
            if (action is null)
                return Failure("AI action authority or lifetime changed.", ["No tool was dispatched."],
                    claim.FailureCode!, request.ProposedActionId);
        }
        if (action.Status is not (AiProposedActionStatus.Confirmed or AiProposedActionStatus.Executed)
            || action.ConfirmedBy != userId || action.ConfirmedAt is null || action.ActingActorId is null)
            return Failure("Exact persisted confirmation is required.", ["No tool was dispatched."],
                "proposed_action_authority_changed", action.Id);

        var identity = AiEventDraftRecoveryIdentity.FromConfirmedAction(action);
        try
        {
            if (!IsLive(action))
            {
                var completed = await recovery.ReadAsync(identity, cancellationToken);
                return completed is { Succeeded: true, CompletedAt: not null, PrimaryEventId: { } eventId }
                    ? Success(eventId, "AI action was already executed.")
                    : Failure("AI action lifetime expired.", ["No further effects were dispatched."],
                        "proposed_action_expired", action.Id);
            }
            var execution = await recovery.AcquireAsync(identity, cancellationToken);
            if (execution is null)
                return Failure("The original execution is unavailable under current authority.",
                    ["No replacement execution was created."], "proposed_action_authority_changed", action.Id);
            if (execution.CompletedAt.HasValue)
                return execution.Succeeded && execution.PrimaryEventId.HasValue
                    ? Success(execution.PrimaryEventId.Value, "AI action was already executed.")
                    : Failure("The original execution is terminal.", ["No tool was dispatched."],
                        "proposed_action_failed", action.Id);
            if (!IsLive(action))
                return Failure("AI action lifetime expired.", ["No further effects were dispatched."],
                    "proposed_action_expired", action.Id);

            if (execution.PrimaryEventId is null)
            {
                var result = await ExecuteAsync(action, execution, cancellationToken);
                execution = await recovery.ReadAsync(identity, cancellationToken);
                if (execution?.PrimaryEventId is null)
                    return Failure("The original draft has not been recovered.", ["Retry the original confirmation."],
                        result.FailureCode ?? "ai_primary_recovery_required", action.Id);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!await IsCurrentActionLiveAsync(identity, cancellationToken))
                return Failure("AI action authority or lifetime changed.", ["No further effects were dispatched."],
                    "proposed_action_authority_changed", action.Id);
            await authorization.AuthorizeAsync(request, logger, cancellationToken);
            var image = await ResolveFeaturedImageAsync(action, execution, identity, cancellationToken);
            if (!image.Succeeded)
                return await AuthorizedRecoveryRequiredAsync(identity, cancellationToken);
            execution = await recovery.ReadAsync(identity, cancellationToken);
            if (execution is null)
                return Failure("AI action authority changed.", ["No further effects were dispatched."],
                    "proposed_action_authority_changed", action.Id);
            if (execution.ImageStorageObjectId.HasValue)
            {
                var attached = await attachImage.ExecuteAsync(
                    AttachAiDraftImageCommand.FromExecution(execution), cancellationToken);
                if (!attached.IsSuccess)
                    return await AuthorizedRecoveryRequiredAsync(identity, cancellationToken);
            }
            return await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                var current = await recovery.ReadForSettlementAsync(
                    identity, execution.Id, execution.ConcurrencyStamp, token);
                if (current is null || !await IsCurrentActionLiveAsync(identity, token))
                    return Failure("AI action authority changed before settlement.", ["Retry the original confirmation."],
                        "proposed_action_authority_changed", action.Id);
                await authorization.AuthorizeAsync(request, logger, token);
                current.MarkSucceeded(timeProvider.GetUtcNow().UtcDateTime);
                await recovery.SaveSettlementAsync(identity, current, token);
                return Success(current.PrimaryEventId!.Value, "AI proposed action confirmed and executed.");
            }, cancellationToken);
        }
        catch
        {
            // Uncertain acknowledgement does not authorize another effect. This read
            // only reports the original primary under current independent authority.
            using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await authorization.AuthorizeAsync(request, logger, bounded.Token);
                var committed = await recovery.ReadAsync(identity, bounded.Token);
                if (committed?.PrimaryEventId is { } eventId)
                    return committed.Succeeded ? Success(eventId, "AI action was already executed.") : RecoveryRequired(eventId);
            }
            catch { /* Preserve the original failure when recovery is unavailable. */ }
            throw;
        }
    }

    private bool IsLive(AiProposedAction action)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        return AiProposedAction.IsWithinLifetime(action.CreatedAt, now)
            && action.Conversation?.Status == AiConversationStatus.Active;
    }

    private async Task<bool> IsCurrentActionLiveAsync(
        AiEventDraftRecoveryIdentity identity, CancellationToken cancellationToken)
    {
        var current = await conversationRepository.GetProposedActionAsync(identity.ProposedActionId, cancellationToken);
        return IsActionVisibleToCurrentPrincipal(current, tenantContext.TenantId, identity.PrincipalUserId)
            && current!.Status is AiProposedActionStatus.Confirmed or AiProposedActionStatus.Executed
            && current.Kind == AiProposedActionKind.CreateEventDraft
            && current.ConfirmedBy == identity.PrincipalUserId && current.ActingActorId == identity.ActingActorId
            && current.ConfirmedAt == identity.ConfirmedAt
            && AiEventDraftRecoveryIdentity.FromConfirmedAction(current) == identity && IsLive(current);
    }

    private static BaseCommandResponse<Guid> RecoveryRequired(Guid eventId) =>
        Failure("The original draft exists; image or completion recovery is required.",
            ["Retry this confirmation, not a new proposal."], "ai_draft_recovery_required", eventId);

    private async Task<BaseCommandResponse<Guid>> AuthorizedRecoveryRequiredAsync(
        AiEventDraftRecoveryIdentity identity, CancellationToken cancellationToken)
    {
        await authorization.AuthorizeAsync(new ConfirmAiProposedActionCommand
        {
            ConversationId = identity.ConversationId,
            ProposedActionId = identity.ProposedActionId
        }, logger, cancellationToken);
        var current = await recovery.ReadAsync(identity, cancellationToken);
        return current?.PrimaryEventId is { } eventId ? RecoveryRequired(eventId)
            : Failure("AI action authority changed.", ["No recovery identity was disclosed."],
                "proposed_action_authority_changed", identity.ProposedActionId);
    }

    private async Task<CreateEventDraftAiToolExecutionResult> ExecuteAsync(
        AiProposedAction action,
        AiToolExecution execution,
        CancellationToken cancellationToken)
    {
        if (action.Kind != AiProposedActionKind.CreateEventDraft)
        {
            return CreateEventDraftAiToolExecutionResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for confirmation.");
        }

        var mappingContext = await CreateMappingContextAsync(action.Conversation!, cancellationToken);
        if (!mappingContext.Succeeded)
        {
            return CreateEventDraftAiToolExecutionResult.Failure(
                mappingContext.FailureCode ?? "invalid_actor_context",
                mappingContext.FailureMessage ?? "AI proposed action actor context is invalid.");
        }

        var executor = new CreateEventDraftAiToolExecutor(createEventCommand);
        return await executor.ExecuteAsync(
            execution,
            action.PayloadJson,
            mappingContext.Context,
            cancellationToken);
    }

    private async Task<CreateEventDraftAiFeaturedImageResolutionResult> ResolveFeaturedImageAsync(
        AiProposedAction action,
        AiToolExecution execution,
        AiEventDraftRecoveryIdentity identity,
        CancellationToken cancellationToken)
    {
        if (!IsLive(action))
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                "proposed_action_expired", "No further image effects are permitted.");
        if (execution.ImageStorageObjectId is { } retainedObject)
            return CreateEventDraftAiFeaturedImageResolutionResult.Success(retainedObject);
        AiMessage? imageMessage = FindSourceImageMessage(action);
        if (imageMessage is null)
        {
            return execution.ImageSourceMessageId is null
                ? CreateEventDraftAiFeaturedImageResolutionResult.Success(null)
                : CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                    "ai_image_source_changed", "The original image source is no longer available.");
        }
        if (imageMessage.Id != execution.ImageSourceMessageId ||
            execution.ImageSourceDigest is not { } sourceDigest ||
            AiToolExecution.DigestPayload(imageMessage.ImageAttachmentsJson!) != sourceDigest ||
            string.IsNullOrWhiteSpace(execution.ImageUploadOperationKey))
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                "ai_image_source_changed", "The original image source is no longer available.");

        StoredAiMessageImageAttachmentDto? image = AiMessageImageAttachmentSerializer
            .DeserializeForStorage(imageMessage.ImageAttachmentsJson)
            .FirstOrDefault();
        if (image is null)
        {
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                "invalid_ai_image_attachment", "The retained image source has no usable attachment.");
        }

        var mediaType = image.MediaType.Trim();
        if (!SafeRasterContentPolicy.IsBrowserImageMimeType(mediaType))
        {
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                "invalid_ai_image_attachment",
                "AI event draft image attachment must be an image.");
        }

        if (!TryDecodeBase64Image(image.Data, out var imageBytes))
        {
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                "invalid_ai_image_attachment",
                "AI event draft image attachment contains invalid base64 data.");
        }

        if (imageBytes.Length == 0
            || imageBytes.Length > AiMessageImageAttachmentSerializer.MaxImageBytes
            || image.SizeBytes is { } declaredSize && declaredSize != imageBytes.LongLength)
        {
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                "invalid_ai_image_attachment",
                "AI event draft image attachment size is not allowed.");
        }

        var fileName = ResolveImageFileName(image);
        var extension = ResolveImageExtension(mediaType, fileName);
        if (!SafeRasterContentPolicy.MatchesExtension(mediaType, extension)
            || !SafeRasterContentPolicy.MatchesContainer(imageBytes, mediaType))
        {
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                "invalid_ai_image_attachment",
                "AI event draft image bytes do not match the declared image metadata.");
        }

        var uploadSession = await createUpload.ExecuteAsync(new CreateStorageUploadSessionCommand
        {
            TenantId = action.TenantId,
            UploadSessionDto = new CreateStorageUploadSessionDto
            {
                ExpectedSizeBytes = imageBytes.LongLength,
                ContentType = mediaType,
                OriginalFileName = fileName,
                SafeDisplayName = fileName,
                Extension = extension,
                Purpose = StorageObjectPurposes.EventImage,
                Visibility = StorageObjectVisibilities.PublicImage,
                IdempotencyKey = execution.ImageUploadOperationKey
            }
        }, cancellationToken);

        if (!uploadSession.IsSuccess || uploadSession.Id is null || uploadSession.Id.Id == Guid.Empty)
        {
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                uploadSession.FailureCode ?? "ai_image_upload_session_failed",
                uploadSession.Message ?? "AI event draft image upload session could not be created.");
        }

        Guid sessionId = uploadSession.Id.Id;
        var sessionExecution = await StoreImageCustodyAsync(identity, execution,
            value => value.BindImageUploadSession(imageMessage.Id, sourceDigest, sessionId),
            cancellationToken);
        if (sessionExecution is null)
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                "ai_image_authority_changed", "Image custody could not be retained under current authority.");
        execution = sessionExecution;

        Guid storageObjectId;
        if (uploadSession.Id.Status == StorageUploadSessionStates.Finalized &&
            uploadSession.Id.StorageObjectId is { } finalizedId && finalizedId != Guid.Empty)
        {
            storageObjectId = finalizedId;
        }
        else if (uploadSession.Id.Status == StorageUploadSessionStates.Reserved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsLive(action))
                return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                    "proposed_action_expired", "No further image effects are permitted.");
            await using var content = new MemoryStream(imageBytes, writable: false);
            var finalizeResult = await finalizeUpload.ExecuteAsync(new FinalizeStorageUploadSessionCommand
            {
                UploadSessionId = sessionId,
                Content = content,
                ContentType = mediaType,
                ContentLength = imageBytes.LongLength,
                TenantId = action.TenantId
            }, cancellationToken);
            if (!finalizeResult.IsSuccess || finalizeResult.Id?.StorageObjectId is not { } uploadedId || uploadedId == Guid.Empty)
                return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                    "ai_image_recovery_required", "The original image transfer requires reconciliation.");
            storageObjectId = uploadedId;
        }
        else
        {
            // Uploading is uncertain ownership, not permission to transfer again.
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                "ai_image_recovery_required", "The original image transfer requires reconciliation.");
        }

        var completedCustody = await StoreImageCustodyAsync(identity, execution,
            value => value.BindImageStorageObject(sessionId, storageObjectId), cancellationToken);
        if (completedCustody is null)
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                "ai_image_authority_changed", "The original storage object could not be retained under current authority.");
        return CreateEventDraftAiFeaturedImageResolutionResult.Success(storageObjectId);
    }

    private Task<AiToolExecution?> StoreImageCustodyAsync(
        AiEventDraftRecoveryIdentity identity, AiToolExecution expected,
        Action<AiToolExecution> bind, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableAsync<AiToolExecution?>(async token =>
        {
            var current = await recovery.ReadForSettlementAsync(
                identity, expected.Id, expected.ConcurrencyStamp, token);
            if (current is null || !await IsCurrentActionLiveAsync(identity, token)) return null;
            await authorization.AuthorizeAsync(new ConfirmAiProposedActionCommand
            {
                ConversationId = identity.ConversationId,
                ProposedActionId = identity.ProposedActionId
            }, logger, token);
            bind(current);
            await recovery.SaveSettlementAsync(identity, current, token);
            return current;
        }, cancellationToken);

    private static AiMessage? FindSourceImageMessage(AiProposedAction action)
    {
        if (action.Message?.Role == AiMessageRole.User && !string.IsNullOrWhiteSpace(action.Message.ImageAttachmentsJson))
        {
            return action.Message;
        }

        if (action.MessageId is null || action.Conversation is null)
        {
            return null;
        }

        var actionMessageSequence = action.Message?.Sequence ?? long.MaxValue;
        return action.Conversation.Messages
            .Where(message =>
                message.Role == AiMessageRole.User
                && message.Sequence < actionMessageSequence
                && !string.IsNullOrWhiteSpace(message.ImageAttachmentsJson))
            .OrderByDescending(message => message.Sequence)
            .FirstOrDefault();
    }

    private static bool TryDecodeBase64Image(string data, out byte[] imageBytes)
    {
        var normalizedData = NormalizeBase64Data(data);
        try
        {
            imageBytes = Convert.FromBase64String(normalizedData);
            return true;
        }
        catch (FormatException)
        {
            imageBytes = [];
            return false;
        }
    }

    private static string NormalizeBase64Data(string data)
    {
        var trimmed = data.Trim();
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var commaIndex = trimmed.IndexOf(',', StringComparison.Ordinal);
        return commaIndex >= 0 && commaIndex < trimmed.Length - 1
            ? trimmed[(commaIndex + 1)..].Trim()
            : trimmed;
    }

    private static string ResolveImageFileName(StoredAiMessageImageAttachmentDto image)
    {
        var fileName = string.IsNullOrWhiteSpace(image.FileName)
            ? "ai-event-poster"
            : Path.GetFileName(image.FileName.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "ai-event-poster";
        }

        fileName = new string(fileName
            .Select(character => character is '/' or '\\' || char.IsControl(character) ? '_' : character)
            .ToArray());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "ai-event-poster";
        }

        if (!Path.HasExtension(fileName))
        {
            fileName = $"{fileName}.{ResolveImageExtension(image.MediaType, fileName)}";
        }

        return fileName.Length <= 500 ? fileName : fileName[..500];
    }

    private static string ResolveImageExtension(string mediaType, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.TrimStart('.').ToLowerInvariant();
        }

        var normalizedMediaType = mediaType.Trim().ToLowerInvariant();
        return normalizedMediaType switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            "image/gif" => "gif",
            _ => "img"
        };
    }

    private async Task<ActorMappingContextResult> CreateMappingContextAsync(
        AiConversation conversation,
        CancellationToken cancellationToken)
    {
        var userId = conversation.UserId;
        var allowedOrganizationIds = await organizationMemberRepository.GetOrganizationIdsWhereUserHasPermission(userId, PermissionCodes.EventCreate, cancellationToken);
        var allowedGroupIds = await groupMemberRepository.GetGroupIdsWhereUserHasPermission(userId, PermissionCodes.EventCreate, cancellationToken);

        var context = new CreateEventDraftAiActionMappingContext(
            allowedOrganizationIds.ToHashSet(),
            allowedGroupIds.ToHashSet());

        if (conversation.ActorId is not { } actorId)
        {
            return ActorMappingContextResult.Success(context);
        }

        Actor? actor = await actorRepository.GetActorWithDetails(actorId, cancellationToken);
        if (actor is null)
        {
            return ActorMappingContextResult.Failure(
                "invalid_actor_context",
                "Selected AI actor context is not available in this tenant.");
        }

        if (actor.ActorTypeId == (int)ActorTypeEnum.User && actor.UserId == userId)
        {
            return ActorMappingContextResult.Success(context with { ForcePersonalOwnerScope = true });
        }

        if (actor.ActorTypeId == (int)ActorTypeEnum.Organization
            && actor.OrganizationId is { } organizationId
            && context.AllowedOrganizationIds.Contains(organizationId))
        {
            return ActorMappingContextResult.Success(context with { ForcedOrganizationId = organizationId });
        }

        if (actor.ActorTypeId == (int)ActorTypeEnum.Group
            && actor.GroupId is { } groupId
            && context.AllowedGroupIds.Contains(groupId))
        {
            return ActorMappingContextResult.Success(context with { ForcedGroupId = groupId });
        }

        return ActorMappingContextResult.Failure(
            "actor_context_not_allowed",
            "Selected AI actor context is not allowed to create events for this user.");
    }

    private static string GetToolName(AiProposedActionKind kind)
        => kind == AiProposedActionKind.CreateEventDraft ? "CreateEventDraft" : kind.ToString();

    private static bool IsActionVisibleToCurrentPrincipal(AiProposedAction? action, Guid tenantId, Guid userId)
        => action?.Conversation is not null
            && action.TenantId == tenantId
            && action.Conversation.TenantId == tenantId
            && action.Conversation.UserId == userId;

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(
        string message,
        IEnumerable<string> errors,
        string? failureCode = null,
        Guid id = default) => failureCode is null
            ? BaseCommandResponse.Validation<Guid>(errors, message, id)
            : BaseCommandResponse.Failure<Guid>(failureCode, message, errors, id);
}
