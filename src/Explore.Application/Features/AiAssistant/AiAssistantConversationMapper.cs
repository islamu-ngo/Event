using Explore.Application.DTOs.Ai;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant;

internal static class AiAssistantConversationMapper
{
    public static AiConversationSummaryDto ToSummary(AiConversation conversation)
        => new()
        {
            Id = conversation.Id,
            TenantId = conversation.TenantId,
            UserId = conversation.UserId,
            ActorId = conversation.ActorId,
            Status = conversation.Status.ToString(),
            Title = conversation.Title,
            Provider = conversation.Provider,
            ModelId = conversation.ModelId,
            BlockedReason = conversation.BlockedReason,
            LastMessageSequence = conversation.LastMessageSequence,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt
        };

    public static AiConversationDto ToDetail(AiConversation conversation)
    {
        var summary = ToSummary(conversation);

        return new AiConversationDto
        {
            Id = summary.Id,
            TenantId = summary.TenantId,
            UserId = summary.UserId,
            ActorId = summary.ActorId,
            Status = summary.Status,
            Title = summary.Title,
            Provider = summary.Provider,
            ModelId = summary.ModelId,
            BlockedReason = summary.BlockedReason,
            LastMessageSequence = summary.LastMessageSequence,
            CreatedAt = summary.CreatedAt,
            UpdatedAt = summary.UpdatedAt,
            Messages = conversation.Messages
                .OrderBy(message => message.Sequence)
                .Select(ToMessage)
                .ToList(),
            Runs = conversation.Runs
                .OrderBy(run => run.QueuedAt)
                .ThenBy(run => run.Id)
                .Select(ToRun)
                .ToList(),
            References = conversation.References
                .OrderBy(reference => reference.CreatedAt)
                .ThenBy(reference => reference.Id)
                .Select(ToReference)
                .ToList(),
            ProposedActions = conversation.ProposedActions
                .OrderBy(action => action.CreatedAt)
                .ThenBy(action => action.Id)
                .Select(ToProposedAction)
                .ToList()
        };
    }

    public static AiRunDto ToRun(AiRun run)
        => new()
        {
            Id = run.Id,
            Status = run.Status.ToString(),
            Provider = run.Provider,
            ModelId = run.ModelId,
            QueuedAt = run.QueuedAt,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            FailureCode = run.FailureCode,
            FailureMessage = run.FailureMessage
        };

    private static AiMessageDto ToMessage(AiMessage message)
        => new()
        {
            Id = message.Id,
            Sequence = message.Sequence,
            Role = message.Role.ToString(),
            Content = message.Content,
            Images = AiMessageImageAttachmentSerializer.DeserializeMetadata(message.ImageAttachmentsJson),
            CreatedAt = message.CreatedAt
        };

    private static AiConversationReferenceDto ToReference(AiConversationReference reference)
        => new()
        {
            Id = reference.Id,
            Kind = reference.Kind.ToString(),
            ReferenceId = reference.ReferenceId,
            DisplayName = reference.DisplayName,
            Summary = reference.Summary,
            CreatedAt = reference.CreatedAt
        };

    private static AiProposedActionDto ToProposedAction(AiProposedAction action)
        => new()
        {
            Id = action.Id,
            MessageId = action.MessageId,
            Kind = action.Kind.ToString(),
            Status = action.Status.ToString(),
            PayloadJson = action.PayloadJson,
            ConfirmedBy = action.ConfirmedBy,
            ConfirmedAt = action.ConfirmedAt,
            RejectedBy = action.RejectedBy,
            RejectedAt = action.RejectedAt,
            ResultResourceId = action.ResultResourceId,
            FailureCode = action.FailureCode,
            FailureMessage = action.FailureMessage,
            CreatedAt = action.CreatedAt
        };
}
