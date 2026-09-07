using System.ComponentModel;
using System.Text.Json;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Application.Features.AiAssistant.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Explore.API.Mcp;

[McpServerResourceType]
public sealed class AiAssistantMcpResources(IMediator mediator, IAiContextRedactor redactor)
{
    [McpServerResource(
        Name = "ai_conversations",
        Title = "AI conversations",
        UriTemplate = "islamu-event://ai/conversations",
        MimeType = "application/json")]
    [Authorize(Policy = McpAuthorizationPolicies.Read)]
    [Description("List recent authenticated AI assistant conversation summaries visible to the current principal.")]
    public async Task<string> ListConversationsAsync(CancellationToken cancellationToken = default)
    {
        var conversations = await mediator.Send(new GetAiConversationListQuery { Limit = 10 }, cancellationToken);
        var descriptor = new AiMcpConversationListDescriptor(
            conversations.Select(MapSummary).ToArray());

        return JsonSerializer.Serialize(
            descriptor,
            AiToolRegistryMcpJsonContext.Default.AiMcpConversationListDescriptor);
    }

    [McpServerResource(
        Name = "ai_conversation_detail",
        Title = "AI conversation detail",
        UriTemplate = "islamu-event://ai/conversations/{conversationId}",
        MimeType = "application/json")]
    [Authorize(Policy = McpAuthorizationPolicies.Read)]
    [Description("Read safe AI assistant conversation metadata without raw proposed-action payloads.")]
    public async Task<string> GetConversationAsync(
        [Description("AI conversation identifier to read.")]
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await mediator.Send(
            new GetAiConversationDetailQuery { ConversationId = conversationId },
            cancellationToken);

        var descriptor = conversation is null
            ? AiMcpConversationDetailDescriptor.NotFound(conversationId)
            : MapDetail(conversation);

        return JsonSerializer.Serialize(
            descriptor,
            AiToolRegistryMcpJsonContext.Default.AiMcpConversationDetailDescriptor);
    }

    private static AiMcpConversationSummaryDescriptor MapSummary(AiConversationSummaryDto conversation)
        => new(
            conversation.Id,
            conversation.Status,
            conversation.Title,
            conversation.Provider,
            conversation.ModelId,
            conversation.LastMessageSequence,
            conversation.CreatedAt,
            conversation.UpdatedAt);

    private AiMcpConversationDetailDescriptor MapDetail(AiConversationDto conversation)
        => new(
            Found: true,
            ConversationId: conversation.Id,
            Status: conversation.Status,
            Title: conversation.Title,
            Provider: conversation.Provider,
            ModelId: conversation.ModelId,
            LastMessageSequence: conversation.LastMessageSequence,
            CreatedAt: conversation.CreatedAt,
            UpdatedAt: conversation.UpdatedAt,
            Messages: conversation.Messages
                .OrderBy(message => message.Sequence)
                .Select(message => new AiMcpMessageDescriptor(
                    message.Id,
                    message.Sequence,
                    message.Role,
                    message.CreatedAt,
                    HasContent: !string.IsNullOrWhiteSpace(message.Content)))
                .ToArray(),
            Runs: conversation.Runs
                .OrderByDescending(run => run.QueuedAt)
                .Select(run => new AiMcpRunDescriptor(
                    run.Id,
                    run.Status,
                    run.Provider,
                    run.ModelId,
                    run.QueuedAt,
                    run.StartedAt,
                    run.CompletedAt,
                    run.FailureCode))
                .ToArray(),
            References: conversation.References
                .OrderBy(reference => reference.CreatedAt)
                .Select(reference => new AiMcpReferenceDescriptor(
                    reference.Id,
                    reference.Kind,
                    reference.ReferenceId,
                    reference.DisplayName,
                    redactor.RedactEmbeddedPii(reference.Summary),
                    reference.CreatedAt))
                .ToArray(),
            ProposedActions: conversation.ProposedActions
                .OrderBy(action => action.CreatedAt)
                .Select(action => new AiMcpProposedActionDescriptor(
                    action.Id,
                    action.Kind,
                    action.Status,
                    action.ResultResourceId,
        action.FailureCode,
        action.CreatedAt,
        !string.IsNullOrEmpty(action.PayloadJson)))
                .ToArray());

    public sealed record AiMcpConversationListDescriptor(IReadOnlyList<AiMcpConversationSummaryDescriptor> Conversations);

    public sealed record AiMcpConversationSummaryDescriptor(
        Guid Id,
        string Status,
        string? Title,
        string? Provider,
        string? ModelId,
        long LastMessageSequence,
        DateTime CreatedAt,
        DateTime? UpdatedAt);

    public sealed record AiMcpConversationDetailDescriptor(
        bool Found,
        Guid ConversationId,
        string? Status,
        string? Title,
        string? Provider,
        string? ModelId,
        long LastMessageSequence,
        DateTime? CreatedAt,
        DateTime? UpdatedAt,
        IReadOnlyList<AiMcpMessageDescriptor> Messages,
        IReadOnlyList<AiMcpRunDescriptor> Runs,
        IReadOnlyList<AiMcpReferenceDescriptor> References,
        IReadOnlyList<AiMcpProposedActionDescriptor> ProposedActions)
    {
        public static AiMcpConversationDetailDescriptor NotFound(Guid conversationId)
            => new(
                Found: false,
                ConversationId: conversationId,
                Status: null,
                Title: null,
                Provider: null,
                ModelId: null,
                LastMessageSequence: 0,
                CreatedAt: null,
                UpdatedAt: null,
                Messages: [],
                Runs: [],
                References: [],
                ProposedActions: []);
    }

    public sealed record AiMcpMessageDescriptor(Guid Id, long Sequence, string Role, DateTime CreatedAt, bool HasContent);

    public sealed record AiMcpRunDescriptor(
        Guid Id,
        string Status,
        string Provider,
        string ModelId,
        DateTime QueuedAt,
        DateTime? StartedAt,
        DateTime? CompletedAt,
        string? FailureCode);

    public sealed record AiMcpReferenceDescriptor(
        Guid Id,
        string Kind,
        Guid ReferenceId,
        string DisplayName,
        string? Summary,
        DateTime CreatedAt);

    public sealed record AiMcpProposedActionDescriptor(
        Guid Id,
        string Kind,
        string Status,
        Guid? ResultResourceId,
    string? FailureCode,
    DateTime CreatedAt,
    bool HasPayload);
}
