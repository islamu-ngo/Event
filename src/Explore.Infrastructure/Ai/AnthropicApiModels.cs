using System.Text.Json.Serialization;

namespace Explore.Infrastructure.Ai;

public sealed class AnthropicCreateMessageRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; }

    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? System { get; init; }

    [JsonPropertyName("messages")]
    public IReadOnlyList<AnthropicApiMessage> Messages { get; init; } = [];

    [JsonPropertyName("temperature")]
    public decimal Temperature { get; init; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AnthropicApiTool>? Tools { get; init; }

    [JsonPropertyName("chat_template_kwargs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicChatTemplateKwargs? ChatTemplateKwargs { get; init; }
}

public sealed record AnthropicChatTemplateKwargs
{
    [JsonPropertyName("enable_thinking")]
    public bool EnableThinking { get; init; }
}

public sealed record AnthropicApiMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public object? Content { get; init; }

    public static AnthropicApiMessage Text(string role, string text) => new()
    {
        Role = role,
        Content = text
    };

    public static AnthropicApiMessage WithContentBlocks(string role, IReadOnlyList<AnthropicApiContentBlock> blocks) => new()
    {
        Role = role,
        Content = blocks
    };
}

public sealed record AnthropicApiTool(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("input_schema")] System.Text.Json.JsonElement InputSchema);

public sealed record AnthropicApiImageSource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("media_type")] string MediaType,
    [property: JsonPropertyName("data")] string Data);

public sealed record AnthropicApiContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("thinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Thinking { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicApiImageSource? Source { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? Input { get; init; }

    [JsonPropertyName("tool_use_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolUseId { get; init; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? BlockContent { get; init; }

    [JsonPropertyName("is_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsError { get; init; }

    public static AnthropicApiContentBlock FromText(string text) => new()
    {
        Type = "text",
        Text = text
    };

    public static AnthropicApiContentBlock FromImage(string mediaType, string data) => new()
    {
        Type = "image",
        Source = new AnthropicApiImageSource("base64", mediaType, data)
    };

    public static AnthropicApiContentBlock FromToolUse(string id, string name, System.Text.Json.JsonElement input) => new()
    {
        Type = "tool_use",
        Id = id,
        Name = name,
        Input = input
    };

    public static AnthropicApiContentBlock FromToolResult(string toolUseId, bool isError, object content) => new()
    {
        Type = "tool_result",
        ToolUseId = toolUseId,
        BlockContent = content,
        IsError = isError ? true : null
    };
}

public sealed record AnthropicMessageResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] IReadOnlyList<AnthropicApiContentBlock>? Content,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("usage")] AnthropicApiUsage? Usage);

public sealed record AnthropicApiUsage(
    [property: JsonPropertyName("input_tokens")] int? InputTokens,
    [property: JsonPropertyName("output_tokens")] int? OutputTokens,
    [property: JsonPropertyName("cache_read_input_tokens")] int? CacheReadInputTokens);
