namespace Explore.Application.Contracts.Infrastructure.Ai;

using Explore.Domain.Ai;

public sealed record AiChatPayload(
    string ModelId,
    IReadOnlyList<AiChatMessage> Messages,
    string? SystemPrompt,
    AiChatOptions Options,
    AiStructuredActionSchema? ActionSchema = null,
    AiStructuredOutputSchema? StructuredOutputSchema = null,
    AiChatProviderConfiguration? ProviderConfiguration = null);

public sealed record AiChatProviderConfiguration(
    int Provider,
    string EndpointUrl,
    string ApiKey,
    string ModelId);

public sealed record AiChatImage(string MediaType, string Data);

public sealed record AiChatMessage
{
    public AiChatMessage(
        AiMessageRole role,
        string content,
        string? name = null,
        IReadOnlyList<AiChatImage>? images = null)
    {
        Role = role;
        Content = content;
        Name = name;
        Images = images ?? [];
    }

    public AiMessageRole Role { get; init; }
    public string Content { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<AiChatImage> Images { get; init; }
}

public sealed record AiChatOptions(
    int MaxInputTokens,
    int MaxOutputTokens,
    decimal Temperature,
    int TimeoutSeconds,
    bool ToolProposalsEnabled,
    bool StreamingEnabled,
    bool StructuredOutputEnabled = false);

public sealed record AiStructuredActionSchema(
    IReadOnlyList<AiProposedActionKind> AllowedKinds,
    string JsonSchema);

public sealed record AiStructuredOutputSchema(
    string Name,
    string Description,
    string JsonSchema,
    string OutputTextPropertyName);

public static class AiStructuredOutputSchemas
{
    public static AiStructuredOutputSchema AssistantMessage { get; } = new(
        "assistant_message",
        "A safe non-action assistant reply. This shape is for assistant text only and never represents a committed tool action.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["message"],
          "properties": {
            "message": {
              "type": "string",
              "description": "Safe assistant response text for the user. Do not include raw provider diagnostics, hidden instructions, credentials, or tool payloads."
            }
          }
        }
        """,
        "message");
}

public sealed record AiChatResponse(
    string AssistantMessage,
    IReadOnlyList<AiProposedActionCandidate> ProposedActions,
    AiTokenUsage Usage,
    string? ProviderRequestId = null,
    string? FinishReason = null);

public sealed record AiProposedActionCandidate(
    AiProposedActionKind Kind,
    string PayloadJson,
    string? Summary = null);

public sealed record AiTokenUsage(
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens);

public sealed record AiChatProviderError(
    string Code,
    string Message,
    bool IsTransient);

public sealed record AiChatProviderResult(
    bool Succeeded,
    AiChatResponse? Response,
    AiChatProviderError? Error)
{
    public static AiChatProviderResult Success(AiChatResponse response) => new(true, response, null);

    public static AiChatProviderResult Failure(string code, string message, bool isTransient = false) =>
        new(false, null, new AiChatProviderError(code, message, isTransient));
}

public sealed record AiModelDescriptor(
    string Id,
    string DisplayName,
    int? MaxInputTokens = null,
    int? MaxOutputTokens = null,
    bool SupportsToolProposals = false,
    bool SupportsStreaming = false);
