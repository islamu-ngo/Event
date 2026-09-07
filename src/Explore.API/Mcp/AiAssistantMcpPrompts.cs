using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Explore.API.Mcp;

[McpServerPromptType]
public sealed class AiAssistantMcpPrompts
{
    [McpServerPrompt(
        Name = "create_event_draft_with_confirmation",
        Title = "Create event draft with confirmation")]
    [Authorize(Policy = McpAuthorizationPolicies.Propose)]
    [Description("Guide an external agent through proposing an event draft while preserving ISLAMU Event confirmation and authorization boundaries.")]
    public string CreateEventDraftWithConfirmation()
        => """
           You are operating against the ISLAMU Event MCP adapter.

           Required workflow:
           1. Call list_ai_tool_contracts and use the CreateEventDraft schema plus projected McpToolName exactly.
           2. Prefer the projected propose_create_event_draft MCP tool when available, passing the target conversation id plus allowed registry payload fields. Use propose_ai_tool_action only as the generic fallback.
           3. Treat the returned proposed action id as pending. Do not claim an event was created.
           4. wait for the user or product UI to confirm the proposed action through the normal API/HAL confirmation flow.

           Safety constraints:
           - Do not include tenant values, provider URLs, API keys, model secrets, raw provider responses, or prompt transcripts.
           - Do not attempt direct repository mutation or event creation.
           - If payload validation fails, ask the user for missing safe fields instead of guessing privileged fields.
           """;

    [McpServerPrompt(
        Name = "manage_event_with_confirmation",
        Title = "Manage event with confirmation")]
    [Authorize(Policy = McpAuthorizationPolicies.Propose)]
    [Description("Guide an external agent through event-management proposals while preserving HAL, concurrency, and confirmation boundaries.")]
    public string ManageEventWithConfirmation()
        => """
           You are operating against the ISLAMU Event MCP adapter for an existing event.

           Required workflow:
           1. Read event_management_context for the event and treat its HAL-derived actions plus concurrency stamp as the authority.
           2. Call list_ai_tool_contracts and use the projected proposal tool matching the intended change.
           3. For draft edits, pass eventId, expectedConcurrencyStamp, and only the allowed draft fields to propose_update_event_draft.
           4. For publishing, call get_event_publish_readiness first and use propose_publish_event only when readiness is ready and has zero errors.
           5. For event deletion, use propose_delete_event only when the context exposes delete and include managementContextHasDelete=true, destructiveSummary, confirmationPhrase=DELETE_EVENT, and acknowledgedConsequences=true.
           6. For Islamic or Tech aspects, use the aspect-specific upsert/delete proposal tools, include aspectKind, managementContextHasEdit=true, and the current expectedConcurrencyStamp.
           7. Treat every returned proposed action id as pending. Do not claim any event or aspect was changed until the user confirms through the normal product/API confirmation flow.

           Safety constraints:
           - Do not include tenant values, actor values, provider URLs, API keys, model secrets, raw provider responses, prompt transcripts, outbox data, audit fields, or raw concurrencyStamp fields.
           - Do not infer permissions from roles or claims; rely on HAL-derived actions in event_management_context.
           - Do not call direct write endpoints or repositories from MCP. Use proposal tools only.
           - If required context or destructive confirmation metadata is missing, ask the user for explicit confirmation instead of guessing.
           """;
}
