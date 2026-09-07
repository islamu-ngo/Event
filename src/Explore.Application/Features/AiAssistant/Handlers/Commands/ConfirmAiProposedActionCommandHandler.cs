using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Ai;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class ConfirmAiProposedActionCommandHandler(
    IAiConversationRepository conversationRepository,
    IOrganizationMemberRepository organizationMemberRepository,
    IGroupMemberRepository groupMemberRepository,
    IActorRepository actorRepository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    IMediator mediator) : IRequestHandler<ConfirmAiProposedActionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ConfirmAiProposedActionCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.UserId is not { } userId)
        {
            return Failure("AI proposed action confirmation requires an authenticated user.", ["User is not authenticated."], "unauthenticated");
        }

        AiProposedAction? action = await conversationRepository.GetProposedActionForUpdateAsync(request.ProposedActionId, cancellationToken);
        if (!IsActionVisibleToCurrentPrincipal(action, tenantContext.TenantId, userId))
        {
            return Failure("AI proposed action was not found.", ["Proposed action was not found."], "proposed_action_not_found");
        }

        if (action!.Status == AiProposedActionStatus.Executed)
        {
            return Success(action.ResultResourceId ?? action.Id, "AI proposed action was already executed.");
        }

        if (action.Status == AiProposedActionStatus.Rejected)
        {
            return Failure("AI proposed action was already rejected.", ["Rejected proposed actions cannot be confirmed."], "proposed_action_rejected", action.Id);
        }

        if (action.Status == AiProposedActionStatus.Failed)
        {
            return Failure("AI proposed action previously failed.", ["Failed proposed actions cannot be confirmed again."], "proposed_action_failed", action.Id);
        }

        var utcNow = DateTime.UtcNow;
        if (action.Status == AiProposedActionStatus.Proposed)
        {
            action.Confirm(userId, utcNow);
        }

        var execution = new AiToolExecution
        {
            Id = Guid.CreateVersion7(),
            TenantId = action.TenantId,
            ProposedActionId = action.Id,
            ToolName = GetToolName(action.Kind),
            StartedAt = utcNow
        };

        CreateEventDraftAiToolExecutionResult executionResult = await ExecuteAsync(action, cancellationToken);
        if (!executionResult.Succeeded)
        {
            action.MarkFailed(
                executionResult.FailureCode ?? "ai_tool_execution_failed",
                executionResult.FailureMessage ?? "AI tool execution failed.");
            execution.MarkFailed(action.FailureCode!, action.FailureMessage, DateTime.UtcNow);
            await conversationRepository.CreateToolExecutionAsync(execution, cancellationToken);
            await conversationRepository.UpdateProposedActionAsync(action, cancellationToken);

            return Failure(
                "AI proposed action confirmation failed.",
                [action.FailureMessage ?? "AI tool execution failed."],
                action.FailureCode,
                action.Id);
        }

        action.MarkExecuted(executionResult.ResultResourceId!.Value);
        execution.MarkSucceeded(DateTime.UtcNow);
        await conversationRepository.CreateToolExecutionAsync(execution, cancellationToken);
        await conversationRepository.UpdateProposedActionAsync(action, cancellationToken);

        return Success(executionResult.ResultResourceId.Value, "AI proposed action confirmed and executed.");
    }

    private async Task<CreateEventDraftAiToolExecutionResult> ExecuteAsync(
        AiProposedAction action,
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

        var executor = new CreateEventDraftAiToolExecutor(mediator);
        return await executor.ExecuteAsync(
            action.PayloadJson,
            mappingContext.Context,
            ct => ResolveFeaturedImageAsync(action, ct),
            cancellationToken);
    }

    private async Task<CreateEventDraftAiFeaturedImageResolutionResult> ResolveFeaturedImageAsync(
        AiProposedAction action,
        CancellationToken cancellationToken)
    {
        AiMessage? imageMessage = FindSourceImageMessage(action);
        if (imageMessage is null)
        {
            return CreateEventDraftAiFeaturedImageResolutionResult.Success(null);
        }

        StoredAiMessageImageAttachmentDto? image = AiMessageImageAttachmentSerializer
            .DeserializeForStorage(imageMessage.ImageAttachmentsJson)
            .FirstOrDefault();
        if (image is null)
        {
            return CreateEventDraftAiFeaturedImageResolutionResult.Success(null);
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

        var uploadSession = await mediator.Send(new CreateStorageUploadSessionCommand
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
                IdempotencyKey = $"ai-action:{action.Id}:featured-image"
            }
        }, cancellationToken);

        if (!uploadSession.IsSuccess || uploadSession.Id is null || uploadSession.Id.Id == Guid.Empty)
        {
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                uploadSession.FailureCode ?? "ai_image_upload_session_failed",
                uploadSession.Message ?? "AI event draft image upload session could not be created.");
        }

        await using var content = new MemoryStream(imageBytes, writable: false);
        var finalizeResult = await mediator.Send(new FinalizeStorageUploadSessionCommand
        {
            UploadSessionId = uploadSession.Id.Id,
            Content = content,
            ContentType = mediaType,
            ContentLength = imageBytes.LongLength,
            TenantId = action.TenantId
        }, cancellationToken);

        if (!finalizeResult.IsSuccess || finalizeResult.Id?.StorageObjectId is not { } storageObjectId || storageObjectId == Guid.Empty)
        {
            return CreateEventDraftAiFeaturedImageResolutionResult.Failure(
                finalizeResult.FailureCode ?? "ai_image_upload_failed",
                finalizeResult.Message ?? "AI event draft image could not be stored.");
        }

        return CreateEventDraftAiFeaturedImageResolutionResult.Success(storageObjectId);
    }

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
