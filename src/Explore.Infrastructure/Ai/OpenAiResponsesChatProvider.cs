using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Telemetry;
using Explore.Domain.Ai;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Ai;

public sealed class OpenAiResponsesChatProvider : IAiChatProvider, IAiModelCatalog
{
    public const string HttpClientName = "OpenAiResponsesAiClient";

    private const string DefaultEndpointUrl = "https://api.openai.com/v1";
    private const string CreateEventDraftToolName = "create_event_draft";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AiProviderSettings> _options;
    private readonly AiProviderSettingsValidator _validator;
    private readonly BusinessMetrics _metrics;

    public OpenAiResponsesChatProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AiProviderSettings> options,
        AiProviderSettingsValidator validator,
        BusinessMetrics metrics)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _validator = validator;
        _metrics = metrics;
    }

    public async Task<IReadOnlyList<AiModelDescriptor>> ListAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = _options.Value;

        if (!IsRunnableOpenAi(settings) || !ValidateSettings(settings).Succeeded)
        {
            return [];
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var models = await DiscoverModelsAsync(httpClient, settings.EndpointUrl, settings.ApiKey, cancellationToken);
            if (models.Count > 0)
            {
                return models;
            }
        }
        catch (HttpRequestException)
        {
        }
        catch (JsonException)
        {
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (UriFormatException)
        {
        }

        return [CreateConfiguredModelDescriptor(settings)];
    }

    public static async Task<IReadOnlyList<AiModelDescriptor>> DiscoverModelsAsync(
        HttpClient httpClient,
        string? endpointUrl,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsUri(endpointUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var modelsResponse = JsonSerializer.Deserialize<OpenAiModelsListResponse>(json, JsonOptions);

        if (modelsResponse?.Data is null || modelsResponse.Data.Count == 0)
        {
            return [];
        }

        return modelsResponse.Data
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .Select(m => new AiModelDescriptor(m.Id!, m.Id!))
            .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = ResolveSettings(request);
        var providerName = AiProviderDefaults.ProviderOpenAi;
        var startedAt = Stopwatch.GetTimestamp();
        using var telemetryActivity = AiProviderTelemetry.StartRequest(providerName, request);

        if (!IsRunnableOpenAi(settings))
        {
            return CompleteFailure(
                RecordFailure("provider_not_configured", "OpenAI provider is not enabled or configured."),
                startedAt,
                telemetryActivity);
        }

        var validation = ValidateSettings(settings);
        if (!validation.Succeeded)
        {
            return CompleteFailure(
                RecordFailure("invalid_settings", "AI provider settings are invalid."),
                startedAt,
                telemetryActivity);
        }

        if (request.Options.StreamingEnabled)
        {
            return CompleteFailure(
                RecordFailure("streaming_not_supported", "Streaming AI responses are not supported by this adapter yet."),
                startedAt,
                telemetryActivity);
        }

        if (request.Messages.Count == 0)
        {
            return CompleteFailure(
                RecordFailure("empty_messages", "At least one message is required."),
                startedAt,
                telemetryActivity);
        }

        if (request.Messages.Any(message => message.Role == AiMessageRole.Tool))
        {
            return CompleteFailure(
                RecordFailure("unsupported_message_role", "Tool result messages are not supported by this adapter yet."),
                startedAt,
                telemetryActivity);
        }

        var structuredOutputFailure = AiStructuredOutputResponseMapper.ValidateRequest(request);
        if (structuredOutputFailure is not null)
        {
            return CompleteFailure(
                RecordFailure(structuredOutputFailure.Code, structuredOutputFailure.Message),
                startedAt,
                telemetryActivity);
        }

        if (!TryCreatePayload(settings, request, out var payload, out var payloadFailure))
        {
            return CompleteFailure(payloadFailure!, startedAt, telemetryActivity);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(GetTimeout(settings, request.Options));

        try
        {
            using var httpRequest = CreateHttpRequest(settings, payload!);
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return CompleteFailure(RecordHttpFailure(response.StatusCode), startedAt, telemetryActivity);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            var providerResponse = await JsonSerializer.DeserializeAsync<OpenAiResponsesResponse>(stream, JsonOptions, timeoutCts.Token);

            if (providerResponse is null)
            {
                return CompleteFailure(
                    RecordFailure("invalid_response", "AI provider returned an invalid response."),
                    startedAt,
                    telemetryActivity);
            }

            if (!TryMapResponse(providerResponse, response, request, out var chatResponse, out var responseFailure))
            {
                return CompleteFailure(responseFailure!, startedAt, telemetryActivity);
            }

            RecordSuccess(chatResponse!, startedAt, telemetryActivity);
            return AiChatProviderResult.Success(chatResponse!);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _metrics.RecordAiProviderRequestDuration(
                Stopwatch.GetElapsedTime(startedAt),
                providerName,
                "cancelled");
            AiProviderTelemetry.MarkCancelled(telemetryActivity);
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CompleteFailure(
                RecordFailure("provider_timeout", "AI provider request timed out.", isTransient: true),
                startedAt,
                telemetryActivity);
        }
        catch (HttpRequestException)
        {
            return CompleteFailure(
                RecordFailure("provider_unreachable", "AI provider request failed before a response was received.", isTransient: true),
                startedAt,
                telemetryActivity);
        }
        catch (JsonException)
        {
            return CompleteFailure(
                RecordFailure("invalid_response", "AI provider returned an invalid response."),
                startedAt,
                telemetryActivity);
        }
    }

    private static bool IsRunnableOpenAi(AiProviderSettings settings) =>
        settings.Enabled
        && settings.Provider == AiProviderSettings.ProviderOpenAi;

    private AiProviderSettings ResolveSettings(AiChatPayload request)
    {
        var defaults = _options.Value;
        var providerConfiguration = request.ProviderConfiguration;

        if (providerConfiguration is null
            || providerConfiguration.Provider != AiProviderSettings.ProviderOpenAi)
        {
            return defaults;
        }

        return new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAi,
            EndpointUrl = providerConfiguration.EndpointUrl.Trim(),
            ApiKey = providerConfiguration.ApiKey.Trim(),
            ModelId = providerConfiguration.ModelId.Trim(),
            AzureCredentialMode = defaults.AzureCredentialMode,
            AzureTenantId = defaults.AzureTenantId,
            MaxInputTokens = request.Options.MaxInputTokens,
            MaxOutputTokens = request.Options.MaxOutputTokens,
            Temperature = request.Options.Temperature,
            TimeoutSeconds = request.Options.TimeoutSeconds,
            RetentionDays = defaults.RetentionDays,
            DailyMessageLimit = defaults.DailyMessageLimit,
            ToolProposalsEnabled = defaults.ToolProposalsEnabled,
            StreamingEnabled = defaults.StreamingEnabled,
            AllowLocalProviderEndpoints = defaults.AllowLocalProviderEndpoints
        };
    }

    private Microsoft.Extensions.Options.ValidateOptionsResult ValidateSettings(AiProviderSettings settings) =>
        _validator.Validate(null, settings);

    private static TimeSpan GetTimeout(AiProviderSettings settings, AiChatOptions options)
    {
        var boundedSettingsTimeout = Math.Clamp(settings.TimeoutSeconds, 1, AiProviderDefaults.MaxTimeoutSeconds);
        var boundedRequestTimeout = Math.Clamp(options.TimeoutSeconds, 1, AiProviderDefaults.MaxTimeoutSeconds);
        return TimeSpan.FromSeconds(Math.Min(boundedSettingsTimeout, boundedRequestTimeout));
    }

    private static HttpRequestMessage CreateHttpRequest(AiProviderSettings settings, OpenAiResponsesRequest payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildResponsesUri(settings.EndpointUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private static Uri BuildResponsesUri(string? endpointUrl)
    {
        var endpoint = CreateEndpointUri(endpointUrl);
        var path = endpoint.AbsolutePath.TrimEnd('/');

        if (path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        var builder = new UriBuilder(endpoint)
        {
            Path = string.IsNullOrEmpty(path)
                ? "responses"
                : $"{path}/responses"
        };

        return builder.Uri;
    }

    private static Uri BuildModelsUri(string? endpointUrl)
    {
        var endpoint = CreateEndpointUri(endpointUrl);
        var path = endpoint.AbsolutePath.TrimEnd('/');

        if (path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        const string responsesSuffix = "/responses";
        if (path.EndsWith(responsesSuffix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^responsesSuffix.Length].TrimEnd('/');
        }

        var builder = new UriBuilder(endpoint)
        {
            Path = string.IsNullOrEmpty(path)
                ? "models"
                : $"{path}/models",
            Query = string.Empty
        };

        return builder.Uri;
    }

    private static Uri CreateEndpointUri(string? endpointUrl) =>
        new(string.IsNullOrWhiteSpace(endpointUrl) ? DefaultEndpointUrl : endpointUrl.Trim(), UriKind.Absolute);

    private static AiModelDescriptor CreateConfiguredModelDescriptor(AiProviderSettings settings) =>
        new(
            settings.ModelId.Trim(),
            settings.ModelId.Trim(),
            settings.MaxInputTokens,
            settings.MaxOutputTokens,
            SupportsToolProposals: true,
            SupportsStreaming: false);

    private bool TryCreatePayload(
        AiProviderSettings settings,
        AiChatPayload request,
        out OpenAiResponsesRequest? payload,
        out AiChatProviderResult? failure)
    {
        payload = null;
        failure = null;

        var tools = new List<OpenAiResponsesTool>();
        if (request.Options.ToolProposalsEnabled && request.ActionSchema is not null)
        {
            foreach (var kind in request.ActionSchema.AllowedKinds.Distinct())
            {
                if (kind != AiProposedActionKind.CreateEventDraft)
                {
                    continue;
                }

                if (!TryCreateJsonSchema(request.ActionSchema.JsonSchema, out var schema, out failure))
                {
                    return false;
                }

                tools.Add(new OpenAiResponsesTool(
                    "function",
                    CreateEventDraftToolName,
                    "Propose a draft event payload. The platform persists this as a proposal that still requires explicit user confirmation.",
                    schema!.Value));
            }
        }

        OpenAiResponsesTextConfig? text = null;
        if (request.Options.StructuredOutputEnabled && request.StructuredOutputSchema is not null)
        {
            if (!TryCreateJsonSchema(
                    request.StructuredOutputSchema.JsonSchema,
                    "invalid_structured_output_schema",
                    "AI structured output schema must be a valid JSON object.",
                    out var structuredSchema,
                    out failure))
            {
                return false;
            }

            text = new OpenAiResponsesTextConfig(new OpenAiResponsesTextFormat(
                "json_schema",
                request.StructuredOutputSchema.Name.Trim(),
                request.StructuredOutputSchema.Description.Trim(),
                structuredSchema!.Value));
        }

        if (!TryBuildInput(request, out var input, out failure))
        {
            return false;
        }

        payload = new OpenAiResponsesRequest
        {
            Model = string.IsNullOrWhiteSpace(request.ModelId) ? settings.ModelId.Trim() : request.ModelId.Trim(),
            Input = input!,
            Store = true,
            Stream = false,
            Temperature = request.Options.Temperature,
            MaxOutputTokens = request.Options.MaxOutputTokens,
            Tools = tools.Count == 0 ? null : tools,
            ToolChoice = tools.Count == 0 ? null : "auto",
            Text = text
        };

        return true;
    }

    private bool TryBuildInput(AiChatPayload request, out object? input, out AiChatProviderResult? failure)
    {
        input = null;
        failure = null;

        if (!request.Messages.Any(message => message.Images.Count > 0))
        {
            input = BuildInputTranscript(request);
            return true;
        }

        var messages = new List<OpenAiResponsesInputMessage>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(OpenAiResponsesInputMessage.Text("system", request.SystemPrompt.Trim()));
        }

        foreach (var message in request.Messages)
        {
            if (message.Images.Count > 0 && message.Role != AiMessageRole.User)
            {
                failure = RecordFailure("unsupported_image_role", "Image input blocks are only supported for user messages.");
                return false;
            }

            messages.Add(new OpenAiResponsesInputMessage(ToResponsesRole(message.Role), MapResponsesContent(message)));
        }

        input = messages;
        return true;
    }

    private static IReadOnlyList<OpenAiResponsesInputContent> MapResponsesContent(AiChatMessage message)
    {
        var content = new List<OpenAiResponsesInputContent>();
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            content.Add(OpenAiResponsesInputContent.FromText(message.Content));
        }

        foreach (var image in message.Images)
        {
            content.Add(OpenAiResponsesInputContent.FromImage(CreateImageDataUrl(image)));
        }

        return content;
    }

    private static string BuildInputTranscript(AiChatPayload request)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            builder.Append("System:");
            builder.AppendLine();
            builder.AppendLine(request.SystemPrompt.Trim());
            builder.AppendLine();
        }

        foreach (var message in request.Messages)
        {
            builder.Append(ToTranscriptRole(message.Role));
            builder.Append(':');
            builder.AppendLine();
            builder.AppendLine(message.Content);
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private bool TryCreateJsonSchema(
        string schemaJson,
        out JsonElement? schema,
        out AiChatProviderResult? failure)
        => TryCreateJsonSchema(
            schemaJson,
            "invalid_action_schema",
            "AI action schema must be a valid JSON object.",
            out schema,
            out failure);

    private bool TryCreateJsonSchema(
        string schemaJson,
        string failureCode,
        string failureMessage,
        out JsonElement? schema,
        out AiChatProviderResult? failure)
    {
        schema = null;
        failure = null;

        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                failure = RecordFailure(failureCode, failureMessage);
                return false;
            }

            schema = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            failure = RecordFailure(failureCode, failureMessage);
            return false;
        }
    }

    private bool TryMapResponse(
        OpenAiResponsesResponse providerResponse,
        HttpResponseMessage response,
        AiChatPayload request,
        out AiChatResponse? chatResponse,
        out AiChatProviderResult? failure)
    {
        chatResponse = null;
        failure = null;

        if (IsContentFiltered(providerResponse.IncompleteDetails?.Reason))
        {
            failure = RecordFailure("content_filtered", "AI provider blocked the response because of content safety policy.");
            return false;
        }

        var text = string.Join(
            Environment.NewLine,
            (providerResponse.Output ?? [])
                .Where(item => string.Equals(item.Type, "message", StringComparison.OrdinalIgnoreCase))
                .SelectMany(item => item.Content ?? [])
                .Where(content => string.Equals(content.Type, "output_text", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(content.Text))
                .Select(content => content.Text!.Trim()));

        var functionCalls = request.Options.ToolProposalsEnabled
            ? (providerResponse.Output ?? []).Where(item => string.Equals(item.Type, "function_call", StringComparison.OrdinalIgnoreCase)).ToList()
            : [];

        if (!TryMapProposedActions(functionCalls, out var proposedActions, out failure))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text) && proposedActions.Count == 0)
        {
            failure = RecordFailure("invalid_response", "AI provider returned an empty text response.");
            return false;
        }

        if (!AiStructuredOutputResponseMapper.TryMapAssistantMessage(
                request,
                text,
                out var assistantMessage,
                out var structuredOutputFailure))
        {
            failure = RecordFailure(structuredOutputFailure!.Code, structuredOutputFailure.Message);
            return false;
        }

        chatResponse = new AiChatResponse(
            assistantMessage,
            proposedActions,
            new AiTokenUsage(
                providerResponse.Usage?.InputTokens,
                providerResponse.Usage?.OutputTokens,
                providerResponse.Usage?.TotalTokens),
            GetProviderRequestId(response) ?? providerResponse.Id,
            providerResponse.Status);

        return true;
    }

    private bool TryMapProposedActions(
        IReadOnlyList<OpenAiResponsesOutputItem> functionCalls,
        out IReadOnlyList<AiProposedActionCandidate> proposedActions,
        out AiChatProviderResult? failure)
    {
        proposedActions = [];
        failure = null;

        if (functionCalls.Count == 0)
        {
            return true;
        }

        var actions = new List<AiProposedActionCandidate>();
        foreach (var functionCall in functionCalls)
        {
            if (!string.Equals(functionCall.Name, CreateEventDraftToolName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryNormalizeToolArguments(functionCall.Arguments, out var payloadJson, out failure))
            {
                return false;
            }

            actions.Add(new AiProposedActionCandidate(
                AiProposedActionKind.CreateEventDraft,
                payloadJson!,
                "Create an event draft"));
        }

        proposedActions = actions;
        return true;
    }

    private bool TryNormalizeToolArguments(string? arguments, out string? payloadJson, out AiChatProviderResult? failure)
    {
        payloadJson = null;
        failure = null;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            failure = RecordFailure("invalid_tool_arguments", "AI provider returned invalid action arguments.");
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                failure = RecordFailure("invalid_tool_arguments", "AI provider returned invalid action arguments.");
                return false;
            }

            payloadJson = document.RootElement.GetRawText();
            return true;
        }
        catch (JsonException)
        {
            failure = RecordFailure("invalid_tool_arguments", "AI provider returned invalid action arguments.");
            return false;
        }
    }

    private static string ToTranscriptRole(AiMessageRole role) => role switch
    {
        AiMessageRole.System => "System",
        AiMessageRole.User => "User",
        AiMessageRole.Assistant => "Assistant",
        _ => "User"
    };

    private static string ToResponsesRole(AiMessageRole role) => role switch
    {
        AiMessageRole.System => "system",
        AiMessageRole.User => "user",
        AiMessageRole.Assistant => "assistant",
        _ => "user"
    };

    private static string CreateImageDataUrl(AiChatImage image)
    {
        var data = image.Data.Trim();
        return data.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? data
            : $"data:{image.MediaType.Trim()};base64,{data}";
    }

    private static bool IsContentFiltered(string? reason) =>
        string.Equals(reason, "content_filter", StringComparison.OrdinalIgnoreCase)
        || string.Equals(reason, "content_filtered", StringComparison.OrdinalIgnoreCase)
        || string.Equals(reason, "safety", StringComparison.OrdinalIgnoreCase);

    private static string? GetProviderRequestId(HttpResponseMessage response)
    {
        string[] headerNames = ["x-request-id", "openai-request-id", "request-id"];
        foreach (var headerName in headerNames)
        {
            if (response.Headers.TryGetValues(headerName, out var values))
            {
                return values.FirstOrDefault();
            }
        }

        return null;
    }

    private AiChatProviderResult RecordHttpFailure(HttpStatusCode statusCode)
    {
        var category = $"http_{(int)statusCode}";
        var isTransient = statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;

        return RecordFailure(
            category,
            $"AI provider request failed with HTTP status {(int)statusCode}.",
            isTransient);
    }

    private AiChatProviderResult RecordFailure(string code, string message, bool isTransient = false)
    {
        _metrics.RecordAiProviderRequest(AiProviderDefaults.ProviderOpenAi, "failed", code);
        return AiChatProviderResult.Failure(code, message, isTransient);
    }

    private void RecordSuccess(
        AiChatResponse response,
        long startedAt,
        Activity? telemetryActivity)
    {
        _metrics.RecordAiProviderRequest(AiProviderDefaults.ProviderOpenAi, "succeeded");
        _metrics.RecordAiProviderRequestDuration(
            Stopwatch.GetElapsedTime(startedAt),
            AiProviderDefaults.ProviderOpenAi,
            "succeeded");
        _metrics.RecordAiProviderTokenUsage(
            AiProviderDefaults.ProviderOpenAi,
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.TotalTokens);
        _metrics.RecordAiProviderProposedActions(
            AiProviderDefaults.ProviderOpenAi,
            response.ProposedActions.Count,
            "create_event_draft");
        AiProviderTelemetry.MarkSuccess(telemetryActivity, response);
    }

    private AiChatProviderResult CompleteFailure(
        AiChatProviderResult result,
        long startedAt,
        Activity? telemetryActivity)
    {
        var error = result.Error!;
        _metrics.RecordAiProviderRequestDuration(
            Stopwatch.GetElapsedTime(startedAt),
            AiProviderDefaults.ProviderOpenAi,
            "failed",
            error.Code);
        AiProviderTelemetry.MarkFailure(telemetryActivity, error.Code, error.IsTransient);
        return result;
    }

    private sealed record OpenAiResponsesRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("input")]
        public object Input { get; init; } = string.Empty;

        [JsonPropertyName("store")]
        public bool Store { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("temperature")]
        public decimal Temperature { get; init; }

        [JsonPropertyName("max_output_tokens")]
        public int MaxOutputTokens { get; init; }

        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<OpenAiResponsesTool>? Tools { get; init; }

        [JsonPropertyName("tool_choice")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolChoice { get; init; }

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OpenAiResponsesTextConfig? Text { get; init; }
    }

    private sealed record OpenAiResponsesTool(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] JsonElement Parameters)
    {
        [JsonPropertyName("strict")]
        public bool Strict => true;
    }

    private sealed record OpenAiResponsesTextConfig(
        [property: JsonPropertyName("format")] OpenAiResponsesTextFormat Format);

    private sealed record OpenAiResponsesTextFormat(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("schema")] JsonElement Schema)
    {
        [JsonPropertyName("strict")]
        public bool Strict => true;
    }

    private sealed record OpenAiResponsesInputMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] IReadOnlyList<OpenAiResponsesInputContent> Content)
    {
        public static OpenAiResponsesInputMessage Text(string role, string text) =>
            new(role, [OpenAiResponsesInputContent.FromText(text)]);
    }

    private sealed record OpenAiResponsesInputContent
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; init; }

        [JsonPropertyName("image_url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ImageUrl { get; init; }

        [JsonPropertyName("detail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Detail { get; init; }

        public static OpenAiResponsesInputContent FromText(string text) => new()
        {
            Type = "input_text",
            Text = text
        };

        public static OpenAiResponsesInputContent FromImage(string imageUrl) => new()
        {
            Type = "input_image",
            ImageUrl = imageUrl,
            Detail = "auto"
        };
    }

    private sealed record OpenAiResponsesResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("output")] IReadOnlyList<OpenAiResponsesOutputItem>? Output,
        [property: JsonPropertyName("usage")] OpenAiResponsesUsage? Usage,
        [property: JsonPropertyName("incomplete_details")] OpenAiResponsesIncompleteDetails? IncompleteDetails);

    private sealed record OpenAiModelsListResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<OpenAiModelDescriptor>? Data);

    private sealed record OpenAiModelDescriptor(
        [property: JsonPropertyName("id")] string? Id);

    private sealed record OpenAiResponsesOutputItem(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("content")] IReadOnlyList<OpenAiResponsesContent>? Content,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("arguments")] string? Arguments);

    private sealed record OpenAiResponsesContent(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text);

    private sealed record OpenAiResponsesUsage(
        [property: JsonPropertyName("input_tokens")] int? InputTokens,
        [property: JsonPropertyName("output_tokens")] int? OutputTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);

    private sealed record OpenAiResponsesIncompleteDetails(
        [property: JsonPropertyName("reason")] string? Reason);
}
