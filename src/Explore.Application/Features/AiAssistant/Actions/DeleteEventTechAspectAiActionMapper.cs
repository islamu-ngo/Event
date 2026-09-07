using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Features.EventAspects.Requests.Commands;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Actions;

public sealed class DeleteEventTechAspectAiActionMapper
{
    public DeleteEventTechAspectAiActionMappingResult Map(AiParsedProposedAction action)
    {
        if (action.Kind != AiProposedActionKind.DeleteEventTechAspect)
        {
            return DeleteEventTechAspectAiActionMappingResult.Failure(
                "unsupported_action_kind",
                "AI proposed action kind is not supported for Tech aspect deletion mapping.");
        }

        return Map(action.PayloadJson);
    }

    public DeleteEventTechAspectAiActionMappingResult Map(string payloadJson)
    {
        var commonResult = EventAspectDeletionPayloadMapper.Map(
            payloadJson,
            DeleteEventTechAspectAiToolDefinition.AllowedPayloadFields,
            "tech",
            "DELETE_TECH_ASPECT",
            "AI Tech aspect deletion payload");

        if (!commonResult.Succeeded)
        {
            return DeleteEventTechAspectAiActionMappingResult.Failure(
                commonResult.FailureCode!,
                commonResult.FailureMessage!);
        }

        var command = new DeleteEventTechAspectCommand { EventId = commonResult.EventId!.Value };
        return DeleteEventTechAspectAiActionMappingResult.Success(
            commonResult.EventId.Value,
            command,
            commonResult.DestructiveContext!);
    }
}

public sealed record DeleteEventTechAspectAiActionMappingResult(
    bool Succeeded,
    Guid? EventId,
    DeleteEventTechAspectCommand? Command,
    EventAspectAiDestructiveContext? DestructiveContext,
    string? FailureCode,
    string? FailureMessage)
{
    public static DeleteEventTechAspectAiActionMappingResult Success(
        Guid eventId,
        DeleteEventTechAspectCommand command,
        EventAspectAiDestructiveContext destructiveContext)
        => new(true, eventId, command, destructiveContext, null, null);

    public static DeleteEventTechAspectAiActionMappingResult Failure(string failureCode, string failureMessage)
        => new(false, null, null, null, failureCode, failureMessage);
}
