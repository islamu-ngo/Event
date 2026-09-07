using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Explore.API.Mcp;

public static class McpAdapterTelemetry
{
    public const string ActivitySourceName = "Explore.Mcp";
    public const string MeterName = "Explore.Mcp";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>(
        "explore.mcp.tool_calls",
        unit: "{call}",
        description: "Total MCP tool calls by bounded tool name, outcome, and failure code.");
    private static readonly Histogram<double> ToolCallDuration = Meter.CreateHistogram<double>(
        "explore.mcp.tool_call_duration",
        unit: "s",
        description: "MCP tool-call duration by bounded tool name, outcome, and failure code.");

    private static readonly Counter<long> GatewayInvocations = Meter.CreateCounter<long>(
        "explore.mcp.gateway_invocations",
        unit: "{call}",
        description: "AI context gateway invocations by entity, outcome, and failure code.");

    public static Activity? StartToolCall(string? toolName, bool projected)
    {
        var activity = ActivitySource.StartActivity("mcp.tool.call", ActivityKind.Server);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("mcp.tool.name", NormalizeToolNameForDiagnostics(toolName));
        activity.SetTag("mcp.tool.projected", projected);
        return activity;
    }

    public static void RecordToolCall(
        TimeSpan duration,
        string? toolName,
        bool projected,
        string? outcome,
        string? failureCode = null)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("tool_name", NormalizeToolNameForDiagnostics(toolName)),
            new("projected", projected),
            new("outcome", NormalizeOutcomeForDiagnostics(outcome)),
            new("failure_code", NormalizeFailureCodeForDiagnostics(failureCode))
        };

        ToolCalls.Add(1, tags);

        if (duration >= TimeSpan.Zero)
        {
            ToolCallDuration.Record(duration.TotalSeconds, tags);
        }
    }

    public static void RecordGatewayInvocation(
        string? entityName,
        string? outcome,
        string? failureCode = null,
        int disclosedFieldCount = 0,
        int redactedFieldCount = 0,
        int deniedFieldCount = 0)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("entity_name", NormalizeTag(entityName)),
            new("outcome", NormalizeOutcomeForDiagnostics(outcome)),
            new("failure_code", NormalizeFailureCodeForDiagnostics(failureCode)),
            new("disclosed_fields", disclosedFieldCount),
            new("redacted_fields", redactedFieldCount),
            new("denied_fields", deniedFieldCount)
        };

        GatewayInvocations.Add(1, tags);
    }

    public static void MarkSuccess(Activity? activity)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("mcp.outcome", "succeeded");
        activity.SetStatus(ActivityStatusCode.Ok);
    }

    public static void MarkFailure(Activity? activity, string? failureCode)
    {
        if (activity is null)
        {
            return;
        }

        var normalizedFailureCode = NormalizeFailureCodeForDiagnostics(failureCode);
        activity.SetTag("mcp.outcome", "failed");
        activity.SetTag("mcp.failure_code", normalizedFailureCode);
        activity.SetStatus(ActivityStatusCode.Error, normalizedFailureCode);
    }

    public static void MarkCancelled(Activity? activity)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("mcp.outcome", "cancelled");
        activity.SetStatus(ActivityStatusCode.Unset);
    }

    public static string NormalizeToolNameForDiagnostics(string? toolName)
        => NormalizeTag(toolName) switch
        {
            "list_ai_tool_contracts" => "list_ai_tool_contracts",
            "propose_ai_tool_action" => "propose_ai_tool_action",
            "propose_create_event_draft" => "propose_create_event_draft",
            "propose_update_event_draft" => "propose_update_event_draft",
            "propose_publish_event" => "propose_publish_event",
            "propose_delete_event" => "propose_delete_event",
            "propose_upsert_event_islamic_aspect" => "propose_upsert_event_islamic_aspect",
            "propose_delete_event_islamic_aspect" => "propose_delete_event_islamic_aspect",
            "propose_upsert_event_tech_aspect" => "propose_upsert_event_tech_aspect",
            "propose_delete_event_tech_aspect" => "propose_delete_event_tech_aspect",
            "propose_create_event_session" => "propose_create_event_session",
            "propose_update_event_session" => "propose_update_event_session",
            "propose_delete_event_session" => "propose_delete_event_session",
            "propose_create_event_session_group" => "propose_create_event_session_group",
            "propose_update_event_session_group" => "propose_update_event_session_group",
            "propose_delete_event_session_group" => "propose_delete_event_session_group",
            "propose_assign_session_to_event_session_group" => "propose_assign_session_to_event_session_group",
            "propose_unassign_session_from_event_session_group" => "propose_unassign_session_from_event_session_group",
            "propose_create_event_day" => "propose_create_event_day",
            "propose_update_event_day" => "propose_update_event_day",
            "propose_delete_event_day" => "propose_delete_event_day",
            "propose_create_event_agenda_item" => "propose_create_event_agenda_item",
            "propose_update_event_agenda_item" => "propose_update_event_agenda_item",
            "propose_delete_event_agenda_item" => "propose_delete_event_agenda_item",
            "propose_create_event_custom_property_definition" => "propose_create_event_custom_property_definition",
            "propose_update_event_custom_property_definition" => "propose_update_event_custom_property_definition",
            "propose_delete_event_custom_property_definition" => "propose_delete_event_custom_property_definition",
            "propose_purge_event_custom_property_definition" => "propose_purge_event_custom_property_definition",
            "propose_set_event_custom_property_value" => "propose_set_event_custom_property_value",
            "propose_set_event_custom_property_multi_values" => "propose_set_event_custom_property_multi_values",
            "propose_assign_event_team_role" => "propose_assign_event_team_role",
            "propose_revoke_event_team_role" => "propose_revoke_event_team_role",
            "propose_create_event_template" => "propose_create_event_template",
            "propose_update_event_template" => "propose_update_event_template",
            "propose_delete_event_template" => "propose_delete_event_template",
            "propose_create_event_session_template" => "propose_create_event_session_template",
            "propose_update_event_session_template" => "propose_update_event_session_template",
            "propose_delete_event_session_template" => "propose_delete_event_session_template",
            "propose_apply_event_template_sync" => "propose_apply_event_template_sync",
            "propose_apply_event_session_template_sync" => "propose_apply_event_session_template_sync",
            "propose_light_moderate_event" => "propose_light_moderate_event",
            "propose_heavy_moderate_event" => "propose_heavy_moderate_event",
            "propose_unmoderate_event" => "propose_unmoderate_event",
            "search_public_events" => "search_public_events",
            "get_public_event" => "get_public_event",
            "get_public_event_program_summary" => "get_public_event_program_summary",
            "list_public_event_sessions" => "list_public_event_sessions",
            "list_my_events" => "list_my_events",
            "get_event_creation_context" => "get_event_creation_context",
            "get_event_publish_readiness" => "get_event_publish_readiness",
            "get_event_program_management_context" => "get_event_program_management_context",
            "get_event_custom_properties_context" => "get_event_custom_properties_context",
            "get_event_registrations_context" => "get_event_registrations_context",
            "get_event_team_context" => "get_event_team_context",
            "get_event_template_catalog_context" => "get_event_template_catalog_context",
            "get_event_template_sync_context" => "get_event_template_sync_context",
            "get_event_session_template_sync_context" => "get_event_session_template_sync_context",
            _ => "unknown"
        };

    public static string NormalizeOutcomeForDiagnostics(string? outcome)
        => NormalizeTag(outcome) switch
        {
            "succeeded" => "succeeded",
            "failed" => "failed",
            "cancelled" => "cancelled",
            _ => "unknown"
        };

    public static string NormalizeFailureCodeForDiagnostics(string? failureCode)
    {
        var normalized = NormalizeTag(failureCode ?? "none");
        if (normalized == "none")
        {
            return normalized;
        }

        return normalized switch
        {
            "invalid_tool_arguments" => "invalid_tool_arguments",
            "validation_failed" => "validation_failed",
            "not_found" => "not_found",
            "forbidden" => "forbidden",
            "unauthorized" => "unauthorized",
            "quota_exceeded" => "quota_exceeded",
            "cancelled" => "cancelled",
            _ => "unknown"
        };
    }

    private static string NormalizeTag(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim().ToLowerInvariant();
}
