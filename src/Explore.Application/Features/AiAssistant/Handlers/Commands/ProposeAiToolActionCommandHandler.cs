using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Responses;
using Explore.Domain.Ai;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class ProposeAiToolActionCommandHandler(
    IAiConversationRepository conversationRepository,
    IEventRepository eventRepository,
    IAiToolContractRegistry toolRegistry,
    ICurrentUserService currentUserService,
    IAuthorizationProvider authorizationProvider)
    : IRequestHandler<ProposeAiToolActionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
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

        var conversation = await conversationRepository.GetByIdForUpdateAsync(
            request.ConversationId,
            cancellationToken);

        if (conversation is null || conversation.UserId != userId)
        {
            return Failure("AI conversation was not found.", "conversation_not_found");
        }

        if (conversation.Status != AiConversationStatus.Active)
        {
            return Failure("AI conversation is not ready for proposed actions.", "conversation_not_active");
        }

        if (!await IsToolProposalAuthorizedAsync(definition, conversation, request.PayloadJson, cancellationToken))
        {
            return Failure(
                "AI tool proposal is not authorized for the selected resource.",
                "tool_authorization_denied");
        }

        var proposedAction = conversation.ProposeAction(
            definition.Kind,
            request.PayloadJson,
            messageId: null,
            userId,
            DateTime.UtcNow);

        await conversationRepository.Update(conversation);

        return BaseCommandResponse.Success(
            proposedAction.Id,
            "AI tool action proposed. Confirm before execution.");
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

    private async Task<bool> IsToolProposalAuthorizedAsync(
        AiToolDefinition definition,
        AiConversation conversation,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        if (definition.RequiredAuthorization is null)
        {
            return true;
        }

        using var payload = JsonDocument.Parse(payloadJson);
        var authorizationContext = await BuildAuthorizationContextAsync(
            definition.RequiredAuthorization,
            conversation,
            payload.RootElement,
            cancellationToken);

        if (authorizationContext is null)
        {
            return false;
        }

        var decision = await authorizationProvider.AuthorizeAsync(
            new AuthorizationRequest(
                AuthorizationCapabilityCatalog.Require(
                    definition.RequiredAuthorization.ResourceKind,
                    definition.RequiredAuthorization.Action),
                authorizationContext.Value.ResourceId,
                Facts: authorizationContext.Value.Facts),
            cancellationToken);
        return decision.IsAllowed;
    }

    /// <summary>
    /// Builds trusted facts for a model-proposed tool action.
    /// <para>
    /// The tool payload is model output, so it may only name a target: the event it names is loaded and
    /// supplies the tenant. Organization and group identifiers in the payload are deliberately ignored —
    /// echoing them back as policy facts would let a proposed action claim an authority zone the
    /// conversation never had.
    /// </para>
    /// </summary>
    private async Task<(string ResourceId, IAuthorizationFacts? Facts)?> BuildAuthorizationContextAsync(
        AiToolAuthorizationRequirement requirement,
        AiConversation conversation,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var tenantId = conversation.TenantId;
        Guid? resolvedEventId = null;

        if (TryGetGuid(payload, "eventId", out var eventId))
        {
            var targetEvent = await eventRepository.GetAuthorizationTargetByIdAsync(eventId, cancellationToken);
            if (targetEvent is null)
            {
                return null;
            }

            tenantId = targetEvent.TenantId;
            resolvedEventId = targetEvent.Id;
        }

        IAuthorizationFacts facts =
            requirement.ResourceKind == ResourceKinds.Event && requirement.Action == AuthorizationActions.Create
                ? new PreCreateAuthorizationFacts(tenantId)
                : resolvedEventId is { } trustedEventId
                    ? new EventScopedAuthorizationFacts(tenantId, trustedEventId)
                    : new TenantScopedAuthorizationFacts(tenantId);

        return (
            ResolveResourceId(requirement.ResourceKind, payload, conversation.TenantId),
            facts);
    }

    private static string ResolveResourceId(string resourceKind, JsonElement payload, Guid tenantId)
    {
        var fieldNames = resourceKind switch
        {
            ResourceKinds.Event => new[] { "eventId" },
            ResourceKinds.EventSession => ["sessionId", "eventId"],
            ResourceKinds.EventSessionGroup => ["groupId", "eventId"],
            ResourceKinds.EventDay => ["dayId", "eventId"],
            ResourceKinds.EventAgendaItem => ["agendaItemId", "eventId"],
            ResourceKinds.CustomPropertyDefinition => ["definitionId", "eventId"],
            ResourceKinds.CustomPropertyValue =>
            [
                "eventCustomPropertyDefinitionId",
                "definitionId",
                "eventId"
            ],
            ResourceKinds.CustomPropertyTemplate =>
            [
                "templateId",
                "sessionTemplateId",
                "eventTemplateId",
                "sessionId",
                "eventId"
            ],
            _ => Array.Empty<string>()
        };

        foreach (var fieldName in fieldNames)
        {
            if (TryGetGuid(payload, fieldName, out var value))
            {
                return value.ToString();
            }
        }

        return tenantId.ToString();
    }

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
