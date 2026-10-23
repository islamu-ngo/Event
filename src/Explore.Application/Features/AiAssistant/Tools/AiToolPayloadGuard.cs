using System.Text;
using System.Text.Json;

namespace Explore.Application.Features.AiAssistant.Tools;

public static class AiToolPayloadGuard
{
    public const int MaximumArgumentBytes = 64 * 1024;
    public const int MaximumArgumentDepth = 16;

    public static AiToolValidationResult ValidateJsonObject(
        string payloadJson,
        IReadOnlySet<string> allowedFields,
        IReadOnlySet<string>? forbiddenFields = null,
        string? schemaJson = null)
    {
        if (payloadJson.Length > MaximumArgumentBytes ||
            Encoding.UTF8.GetByteCount(payloadJson) > MaximumArgumentBytes)
            return Failure("tool_arguments_too_large", "AI tool payload exceeds the argument budget.");
        try
        {
            using var document = JsonDocument.Parse(payloadJson, new JsonDocumentOptions { MaxDepth = MaximumArgumentDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Failure(
                    "invalid_tool_arguments",
                    "AI tool payload must be a JSON object.");
            }

            if (HasDuplicateMembers(document.RootElement))
                return Failure("ambiguous_tool_arguments", "AI tool payload contains ambiguous object members.");

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

    private static bool HasDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicateMembers(property.Value))
                    return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                if (HasDuplicateMembers(child))
                    return true;
        }
        return false;
    }

    private static AiToolValidationResult Failure(string failureCode, string failureMessage)
        => AiToolValidationResult.Failure(failureCode, failureMessage, AiToolCorrectionMessages.SchemaExactRetry);
}
