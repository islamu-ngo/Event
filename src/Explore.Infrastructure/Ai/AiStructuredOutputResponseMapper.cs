using System.Text.Json;
using Explore.Application.Contracts.Infrastructure.Ai;

namespace Explore.Infrastructure.Ai;

internal static class AiStructuredOutputResponseMapper
{
    public static AiStructuredOutputFailure? ValidateRequest(AiChatPayload request)
    {
        if (!request.Options.StructuredOutputEnabled && request.StructuredOutputSchema is null)
        {
            return null;
        }

        if (!request.Options.StructuredOutputEnabled)
        {
            return new AiStructuredOutputFailure(
                "structured_output_not_enabled",
                "Structured output schema was supplied without enabling structured output mode.");
        }

        if (request.StructuredOutputSchema is null)
        {
            return new AiStructuredOutputFailure(
                "structured_output_schema_required",
                "Structured output mode requires an explicit response schema.");
        }

        if (request.Options.ToolProposalsEnabled || request.ActionSchema is not null)
        {
            return new AiStructuredOutputFailure(
                "structured_output_conflict",
                "Structured output mode cannot be combined with tool proposal mode.");
        }

        if (!IsSafeSchemaName(request.StructuredOutputSchema.Name))
        {
            return new AiStructuredOutputFailure(
                "invalid_structured_output_schema",
                "Structured output schema name is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.StructuredOutputSchema.OutputTextPropertyName))
        {
            return new AiStructuredOutputFailure(
                "invalid_structured_output_schema",
                "Structured output schema text property is invalid.");
        }

        return null;
    }

    public static bool TryMapAssistantMessage(
        AiChatPayload request,
        string? rawContent,
        out string assistantMessage,
        out AiStructuredOutputFailure? failure)
    {
        assistantMessage = rawContent ?? string.Empty;
        failure = null;

        if (!request.Options.StructuredOutputEnabled || request.StructuredOutputSchema is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            failure = CreateInvalidOutputFailure();
            assistantMessage = string.Empty;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawContent);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(request.StructuredOutputSchema.OutputTextPropertyName, out var textProperty) ||
                textProperty.ValueKind != JsonValueKind.String)
            {
                failure = CreateInvalidOutputFailure();
                assistantMessage = string.Empty;
                return false;
            }

            var message = textProperty.GetString();
            if (string.IsNullOrWhiteSpace(message))
            {
                failure = CreateInvalidOutputFailure();
                assistantMessage = string.Empty;
                return false;
            }

            assistantMessage = message.Trim();
            return true;
        }
        catch (JsonException)
        {
            failure = CreateInvalidOutputFailure();
            assistantMessage = string.Empty;
            return false;
        }
    }

    private static AiStructuredOutputFailure CreateInvalidOutputFailure() => new(
        "invalid_structured_output",
        "AI provider returned a response that did not match the requested structured output schema.");

    private static bool IsSafeSchemaName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
        {
            return false;
        }

        return name.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }
}

internal sealed record AiStructuredOutputFailure(string Code, string Message);
