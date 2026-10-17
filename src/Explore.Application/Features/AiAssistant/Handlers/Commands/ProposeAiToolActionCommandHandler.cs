using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Responses;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class ProposeAiToolActionCommandHandler(
    IAiConversationRepository conversationRepository,
    IEventRepository eventRepository,
    IAiToolContractRegistry toolRegistry,
    ICurrentUserService currentUserService,
    IHumanAiToolProposalPreparation proposalPreparation,
    IHumanAiModerationProposalPreparation moderationPreparation,
    ISerializableAttemptOwner attemptOwner)
    : ICommandHandler<ProposeAiToolActionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ProposeAiToolActionCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.UserId is not { } userId)
        {
            return Failure("AI tool proposals require an authenticated user.", "unauthenticated");
        }

        var definition = ResolveDefinition(request.ToolName);
        if (definition is null || !definition.ExposeToMcp)
        {
            return Failure("AI tool is not available for MCP proposal.", "unknown_tool");
        }

        var validation = toolRegistry.ValidatePayload(definition.Kind, request.PayloadJson);
        if (!validation.Succeeded)
        {
            return Failure(
                validation.FailureMessage ?? "AI tool payload failed validation.",
                validation.FailureCode ?? "invalid_tool_arguments");
        }

        var candidate = await conversationRepository.GetByIdWithDetailsAsync(
            request.ConversationId,
            cancellationToken);

        if (candidate is null || candidate.UserId != userId)
        {
            return Failure("AI conversation was not found.", "conversation_not_found");
        }

        if (candidate.Status != AiConversationStatus.Active)
        {
            return Failure("AI conversation is not ready for proposed actions.", "conversation_not_active");
        }

        if (definition is
            {
                Kind: AiProposedActionKind.HeavyModerateEvent,
                RequiredAuthorization: { ResourceKind: ResourceKinds.Event, Action: AuthorizationActions.Events.ModerateHeavy }
            })
        {
            using var payload = JsonDocument.Parse(request.PayloadJson);
            if (TryGetGuid(payload.RootElement, "eventId", out var eventId)
                && await eventRepository.GetAiProposalAuthorizationTargetByIdAsync(eventId, cancellationToken) is null)
            {
                var prepared = await moderationPreparation.PrepareAsync(request, candidate, cancellationToken);
                return await attemptOwner.ExecuteAsync(async token =>
                {
                    await prepared.AcquireAsync(request, token);
                    var conversation = await conversationRepository.GetByIdForUpdateAsync(request.ConversationId, candidate.ActorId, token);
                    if (conversation is null || conversation.UserId != userId || conversation.TenantId != candidate.TenantId
                        || conversation.ConcurrencyStamp != candidate.ConcurrencyStamp || conversation.ActorId != candidate.ActorId
                        || conversation.Status != AiConversationStatus.Active)
                        throw new UnauthorizedAccessException("AI conversation authority changed before proposal commit.");
                    var action = conversation.ProposeAction(
                        definition.Kind, request.PayloadJson, messageId: null, userId, DateTime.UtcNow);
                    prepared.RetainProposal(conversation, action);
                    await conversationRepository.Update(conversation);
                    return BaseCommandResponse.Success(action.Id, "AI tool action proposed. Confirm before execution.");
                }, cancellationToken);
            }
        }

        try
        {
            var prepared = await proposalPreparation.PrepareAsync(request, candidate, cancellationToken);
            return await attemptOwner.ExecuteAsync(async token =>
            {
                await prepared.AcquireAsync(request, token);
                var conversation = await conversationRepository.GetByIdForUpdateAsync(request.ConversationId, candidate.ActorId, token);
                if (conversation is null || conversation.UserId != userId || conversation.TenantId != candidate.TenantId
                    || conversation.ConcurrencyStamp != candidate.ConcurrencyStamp || conversation.ActorId != candidate.ActorId
                    || conversation.IsDeleted || conversation.Status != AiConversationStatus.Active)
                    throw new ConversationAuthorityChangedException();
                var proposedAction = conversation.ProposeAction(
                    definition.Kind, request.PayloadJson, messageId: null, userId, DateTime.UtcNow);
                prepared.RetainProposal(conversation, proposedAction);
                await conversationRepository.Update(conversation);
                return BaseCommandResponse.Success(proposedAction.Id, "AI tool action proposed. Confirm before execution.");
            }, cancellationToken);
        }
        catch (ConversationAuthorityChangedException)
        {
            return Failure("AI conversation authority changed before proposal commit.", "conversation_authority_changed");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            || exception is AuthorizationException and not AuthorizationProviderUnavailableException)
        {
            return Failure("AI tool proposal is not authorized for the selected resource.", "tool_authorization_denied");
        }
    }

    private AiToolDefinition? ResolveDefinition(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return null;
        }

        return toolRegistry.Definitions.FirstOrDefault(definition =>
            string.Equals(definition.Name, toolName.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(definition.Kind.ToString(), toolName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ConversationAuthorityChangedException : Exception;

    private static bool TryGetGuid(JsonElement payload, string fieldName, out Guid value)
    {
        value = Guid.Empty;
        return payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty(fieldName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            Guid.TryParse(property.GetString(), out value);
    }

    private static BaseCommandResponse<Guid> Failure(string message, string failureCode) =>
        BaseCommandResponse.Failure<Guid>(failureCode, message, [message], Guid.Empty);
}
