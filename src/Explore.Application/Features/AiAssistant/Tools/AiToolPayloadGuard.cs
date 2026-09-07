using System.Text.Json;

namespace Explore.Application.Features.AiAssistant.Tools;

public static class AiToolPayloadGuard
{
    public static AiToolValidationResult ValidateJsonObject(
        string payloadJson,
        IReadOnlySet<string> allowedFields,
        IReadOnlySet<string>? forbiddenFields = null,
        string? schemaJson = null)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Failure(
                    "invalid_tool_arguments",
                    "AI tool payload must be a JSON object.");
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (forbiddenFields?.Contains(property.Name) == true)
                {
                    return Failure(
                        "forbidden_tool_argument",
                        "AI tool payload contains a field that is not allowed.");
                }

                if (!allowedFields.Contains(property.Name))
                {
                    return Failure(
                        "unsupported_tool_argument",
                        "AI tool payload contains an unsupported field.");
                }
            }

            if (!string.IsNullOrWhiteSpace(schemaJson))
            {
                return AiToolJsonSchemaPayloadValidator.Validate(document.RootElement, schemaJson);
            }

            return AiToolValidationResult.Success();
        }
        catch (JsonException)
        {
            return Failure(
                "invalid_tool_arguments",
                "AI tool payload must be valid JSON.");
        }
    }

    private static AiToolValidationResult Failure(string failureCode, string failureMessage)
        => AiToolValidationResult.Failure(failureCode, failureMessage, AiToolCorrectionMessages.SchemaExactRetry);
}
