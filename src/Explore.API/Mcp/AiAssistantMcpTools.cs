using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Explore.API.Mcp;

[McpServerToolType]
public sealed class AiAssistantMcpTools(IMediator mediator)
{
    [McpServerTool(
        Name = "propose_ai_tool_action",
        Title = "Propose AI tool action",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false)]
    [Authorize(Policy = McpAuthorizationPolicies.Propose)]
    [Description("Validate a registry-backed AI tool payload and persist it as a proposed action. The action is not executed until a user confirms it through the normal API/HAL flow.")]
    public async Task<string> ProposeAiToolActionAsync(
        [Description("AI conversation identifier that will own the proposed action.")]
        Guid conversationId,
        [Description("Registry tool name to validate, for example CreateEventDraft.")]
        string toolName,
        [Description("JSON object payload that matches the selected registry tool schema.")]
        string payloadJson,
        [Description("Optional short human-readable summary of the proposed action.")]
        string? summary = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = McpAdapterTelemetry.StartToolCall("propose_ai_tool_action", projected: false);

        try
        {
            var response = await mediator.Send(
                new ProposeAiToolActionCommand
                {
                    ConversationId = conversationId,
                    ToolName = toolName,
                    PayloadJson = payloadJson,
                    Summary = summary
                },
                cancellationToken);

            var outcome = response.IsSuccess ? "succeeded" : "failed";
            if (response.IsSuccess)
            {
                McpAdapterTelemetry.MarkSuccess(activity);
            }
            else
            {
                McpAdapterTelemetry.MarkFailure(activity, response.FailureCode);
            }

            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                "propose_ai_tool_action",
                projected: false,
                outcome,
                response.FailureCode);

            return JsonSerializer.Serialize(
                new AiMcpCommandResultDescriptor(
                    response.IsSuccess,
                    response.Id == Guid.Empty ? null : response.Id,
                    response.Message,
                    response.FailureCode,
                    response.Errors ?? []),
                AiToolRegistryMcpJsonContext.Default.AiMcpCommandResultDescriptor);
        }
        catch (OperationCanceledException)
        {
            McpAdapterTelemetry.MarkCancelled(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                "propose_ai_tool_action",
                projected: false,
                outcome: "cancelled",
                failureCode: "cancelled");
            throw;
        }
        catch
        {
            McpAdapterTelemetry.MarkFailure(activity, "unknown");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                "propose_ai_tool_action",
                projected: false,
                outcome: "failed",
                failureCode: "unknown");
            throw;
        }
    }

    public sealed record AiMcpCommandResultDescriptor(
        bool Success,
        Guid? Id,
        string? Message,
        string? FailureCode,
        IReadOnlyList<string> Errors);
}
