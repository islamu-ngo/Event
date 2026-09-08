using System.Diagnostics;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Domain.Ai;

namespace Explore.Infrastructure.Ai;

public static class AiProviderTelemetry
{
    public const string ActivitySourceName = "Explore.Ai.Provider";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static Activity? StartRequest(string? provider, AiChatPayload request)
    {
        var activity = ActivitySource.StartActivity("ai.provider.request", ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("ai.provider", NormalizeProvider(provider));
        activity.SetTag("ai.tool_proposals.enabled", request.Options.ToolProposalsEnabled);
        activity.SetTag("ai.streaming.requested", request.Options.StreamingEnabled);
        activity.SetTag("ai.message.count", request.Messages.Count);
        activity.SetTag("ai.system_prompt.present", !string.IsNullOrWhiteSpace(request.SystemPrompt));
        activity.SetTag("ai.action_schema.present", request.ActionSchema is not null);
        return activity;
    }

    public static void MarkSuccess(Activity? activity, AiChatResponse response)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("ai.outcome", "succeeded");
        activity.SetTag("ai.finish_reason", NormalizeFinishReason(response.FinishReason));
        SetPositiveTag(activity, "ai.tokens.input", response.Usage.InputTokens);
        SetPositiveTag(activity, "ai.tokens.output", response.Usage.OutputTokens);
        SetPositiveTag(activity, "ai.tokens.total", response.Usage.TotalTokens);
        activity.SetTag("ai.proposed_actions.count", response.ProposedActions.Count);
        activity.SetTag("ai.proposed_action.kind", NormalizeActionKind(response.ProposedActions));
        activity.SetStatus(ActivityStatusCode.Ok);
    }

    public static void MarkFailure(Activity? activity, string? failureCategory, bool isTransient)
    {
        if (activity is null)
        {
            return;
        }

        var normalizedFailureCategory = NormalizeFailureCategory(failureCategory);
        activity.SetTag("ai.outcome", "failed");
        activity.SetTag("ai.failure_category", normalizedFailureCategory);
        activity.SetTag("ai.failure.transient", isTransient);
        activity.SetStatus(ActivityStatusCode.Error, normalizedFailureCategory);
    }

    public static void MarkCancelled(Activity? activity)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("ai.outcome", "cancelled");
        activity.SetStatus(ActivityStatusCode.Unset);
    }

    private static void SetPositiveTag(Activity activity, string key, int? value)
    {
        if (value is > 0)
        {
            activity.SetTag(key, value.Value);
        }
    }

    private static string NormalizeTag(string? value) => string.IsNullOrWhiteSpace(value)
        ? "unknown"
        : value.Trim().ToLowerInvariant();

    private static string NormalizeProvider(string? provider) => NormalizeTag(provider);

    private static string NormalizeFinishReason(string? finishReason) => NormalizeTag(finishReason) switch
    {
        "stop" => "stop",
        "length" => "length",
        "tool_calls" => "tool_calls",
        "content_filter" => "content_filter",
        "content_filtered" => "content_filtered",
        _ => "unknown"
    };

    private static string NormalizeFailureCategory(string? failureCategory)
    {
        var normalized = NormalizeTag(failureCategory ?? "none");
        if (normalized == "none")
        {
            return normalized;
        }

        if (normalized.StartsWith("http_", StringComparison.Ordinal)
            && int.TryParse(normalized.AsSpan(5), out var statusCode)
            && statusCode is >= 100 and <= 599)
        {
            return normalized;
        }

        return normalized switch
        {
            "provider_disabled" => "provider_disabled",
            "provider_not_configured" => "provider_not_configured",
            "invalid_settings" => "invalid_settings",
            "streaming_not_supported" => "streaming_not_supported",
            "empty_messages" => "empty_messages",
            "unsupported_message_role" => "unsupported_message_role",
            "invalid_action_schema" => "invalid_action_schema",
            "provider_timeout" => "provider_timeout",
            "provider_unreachable" => "provider_unreachable",
            "invalid_response" => "invalid_response",
            "provider_failure" => "provider_failure",
            "content_filtered" => "content_filtered",
            "invalid_tool_arguments" => "invalid_tool_arguments",
            _ => "unknown"
        };
    }

    private static string NormalizeActionKind(IReadOnlyList<AiProposedActionCandidate> proposedActions)
    {
        if (proposedActions.Count == 0)
        {
            return "none";
        }

        return proposedActions.All(action => action.Kind == AiProposedActionKind.CreateEventDraft)
            ? "create_event_draft"
            : "unknown";
    }
}
