namespace Explore.API.Hateoas.Policies;

using System.Collections.Generic;
using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Ai;
using Explore.Application.Hateoas;

public sealed class AiConversationDetailLinkPolicy : ILinkPolicy<AiConversationDto>
{
    public IEnumerable<LinkDefinition> GetLinks(AiConversationDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetAiConversation,
            new { conversationId = dto.Id },
            "GET",
            "AI conversation",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.AiConversations.View, ResourceDescriptors.AiConversation, dto);

        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetAiConversations,
            null,
            "GET",
            "AI conversations",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.AiConversations.View, ResourceDescriptors.AiConversation, dto);

        if (IsActive(dto.Status))
        {
            yield return new LinkDefinition(
                LinkRelations.SendMessage,
                RouteNames.SendAiMessage,
                new { conversationId = dto.Id },
                "POST",
                "Send message",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.AiConversations.SendMessage, ResourceDescriptors.AiConversation, dto);
        }
    }

    private static bool IsActive(string status) =>
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);
}

public static class AiProposedActionLinkPolicy
{
    public static IEnumerable<LinkDefinition> GetLinks(AiConversationDto conversation, AiProposedActionDto action)
    {
        if (!IsActive(conversation.Status) || !IsProposed(action.Status))
        {
            yield break;
        }

        var routeValues = new { conversationId = conversation.Id, proposedActionId = action.Id };

        yield return new LinkDefinition(
            LinkRelations.ConfirmAction,
            RouteNames.ConfirmAiProposedAction,
            routeValues,
            "POST",
            "Confirm proposed action",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.AiConversations.ConfirmAction, ResourceDescriptors.AiConversation, conversation);

        yield return new LinkDefinition(
            LinkRelations.RejectAction,
            RouteNames.RejectAiProposedAction,
            routeValues,
            "POST",
            "Reject proposed action",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.AiConversations.RejectAction, ResourceDescriptors.AiConversation, conversation);
    }

    private static bool IsActive(string status) =>
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    private static bool IsProposed(string status) =>
        string.Equals(status, "Proposed", StringComparison.OrdinalIgnoreCase);
}

public sealed class AiConversationCollectionLinkPolicy : ICollectionLinkPolicy<AiConversationSummaryDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(AiConversationSummaryDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetAiConversation,
            new { conversationId = dto.Id },
            "GET",
            "AI conversation",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.AiConversations.View, ResourceDescriptors.AiConversationSummary, dto);

        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetAiConversations,
            null,
            "GET",
            "AI conversations",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.AiConversations.View, ResourceDescriptors.AiConversationSummary, dto);

        if (IsActive(dto.Status))
        {
            yield return new LinkDefinition(
                LinkRelations.SendMessage,
                RouteNames.SendAiMessage,
                new { conversationId = dto.Id },
                "POST",
                "Send message",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.AiConversations.SendMessage, ResourceDescriptors.AiConversationSummary, dto);
        }
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Create,
            RouteNames.CreateAiConversation,
            null,
            "POST",
            "Create AI conversation",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.AiConversations.Create, ResourceKinds.AiConversation);
    }

    private static bool IsActive(string status) =>
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);
}
