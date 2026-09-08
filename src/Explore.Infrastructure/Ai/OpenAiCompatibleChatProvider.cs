using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Telemetry;
using Explore.Domain.Ai;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Ai;

public sealed class OpenAiCompatibleChatProvider : IAiChatProvider, IAiModelCatalog
{
    public const string HttpClientName = "OpenAiCompatibleAiClient";

    private const string CreateEventDraftToolName = "create_event_draft";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AiProviderSettings> _options;
    private readonly AiProviderSettingsValidator _validator;
    private readonly BusinessMetrics _metrics;

    public OpenAiCompatibleChatProvider(
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

        if (!IsRunnableOpenAiCompatible(settings) || !ValidateSettings(settings).Succeeded)
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
            // Bootstrap should remain usable even if the provider omits the optional models endpoint.
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
        string endpointUrl,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            return [];
        }

        var modelsUri = BuildModelsUri(endpointUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
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
        var providerName = AiProviderDefaults.ProviderOpenAiCompatible;
        var startedAt = Stopwatch.GetTimestamp();
        using var telemetryActivity = AiProviderTelemetry.StartRequest(providerName, request);

        if (!IsRunnableOpenAiCompatible(settings))
        {
            return CompleteFailure(
                RecordFailure("provider_not_configured", "OpenAI-compatible provider is not enabled or configured."),
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
            var providerResponse = await JsonSerializer.DeserializeAsync<OpenAiChatCompletionResponse>(stream, JsonOptions, timeoutCts.Token);

            if (providerResponse is null || providerResponse.Choices.Count == 0)
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

    private static bool IsRunnableOpenAiCompatible(AiProviderSettings settings) =>
        settings.Enabled
        && settings.Provider == AiProviderSettings.ProviderOpenAiCompatible;

    private AiProviderSettings ResolveSettings(AiChatPayload request)
    {
        var defaults = _options.Value;
        var providerConfiguration = request.ProviderConfiguration;

        if (providerConfiguration is null
            || providerConfiguration.Provider != AiProviderSettings.ProviderOpenAiCompatible)
        {
            return defaults;
        }

        return new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
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

    private static HttpRequestMessage CreateHttpRequest(AiProviderSettings settings, OpenAiChatCompletionRequest payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri(settings.EndpointUrl));
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private static Uri BuildChatCompletionsUri(string endpointUrl)
    {
        var endpoint = new Uri(endpointUrl, UriKind.Absolute);
        var path = endpoint.AbsolutePath.TrimEnd('/');

        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        var builder = new UriBuilder(endpoint)
        {
            Path = string.IsNullOrEmpty(path)
                ? "chat/completions"
                : $"{path}/chat/completions"
        };

        return builder.Uri;
    }

    private static AiModelDescriptor CreateConfiguredModelDescriptor(AiProviderSettings settings) =>
        new(
            settings.ModelId.Trim(),
            settings.ModelId.Trim(),
            settings.MaxInputTokens,
            settings.MaxOutputTokens,
            SupportsToolProposals: true,
            SupportsStreaming: false);

    private static Uri BuildModelsUri(string endpointUrl)
    {
        var endpoint = new Uri(endpointUrl, UriKind.Absolute);
        var path = endpoint.AbsolutePath.TrimEnd('/');

        if (path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        const string chatCompletionsSuffix = "/chat/completions";
        if (path.EndsWith(chatCompletionsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^chatCompletionsSuffix.Length].TrimEnd('/');
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

    private bool TryCreatePayload(
        AiProviderSettings settings,
        AiChatPayload request,
        out OpenAiChatCompletionRequest? payload,
        out AiChatProviderResult? failure)
    {
        payload = null;
        failure = null;

        var tools = new List<OpenAiTool>();
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

                tools.Add(new OpenAiTool(
                    "function",
                    new OpenAiFunctionTool(
                        CreateEventDraftToolName,
                        "Propose a draft event payload. The platform persists this as a proposal that still requires explicit user confirmation.",
                        schema!.Value)));
            }
        }

        OpenAiResponseFormat? responseFormat = null;
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

            responseFormat = new OpenAiResponseFormat(
                "json_schema",
                new OpenAiResponseJsonSchema(
                    request.StructuredOutputSchema.Name.Trim(),
                    request.StructuredOutputSchema.Description.Trim(),
                    structuredSchema!.Value));
        }

        if (!TryBuildMessages(request, out var messages, out failure))
        {
            return false;
        }

        payload = new OpenAiChatCompletionRequest
        {
            Model = string.IsNullOrWhiteSpace(request.ModelId) ? settings.ModelId.Trim() : request.ModelId.Trim(),
            Messages = messages!,
            Temperature = request.Options.Temperature,
            MaxTokens = request.Options.MaxOutputTokens,
            Stream = false,
            Tools = tools.Count == 0 ? null : tools,
            ToolChoice = tools.Count == 0 ? null : "auto",
            ResponseFormat = responseFormat,
            ChatTemplateKwargs = CreateChatTemplateKwargs(settings, request)
        };

        return true;
    }

    private static OpenAiChatTemplateKwargs? CreateChatTemplateKwargs(
        AiProviderSettings settings,
        AiChatPayload request)
    {
        if (!ShouldDisableTemplateThinking(request) || !IsLocalOrPrivateEndpoint(settings.EndpointUrl))
        {
            return null;
        }

        return new OpenAiChatTemplateKwargs { EnableThinking = false };
    }

    private static bool ShouldDisableTemplateThinking(AiChatPayload request) =>
        request.Options.ToolProposalsEnabled
        || request.Options.StructuredOutputEnabled
        || request.Messages.Any(message => message.Images.Count > 0);

    private static bool IsLocalOrPrivateEndpoint(string? endpointUrl)
    {
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || (bytes[0] & 0xfe) == 0xfc;
        }

        return false;
    }

    private bool TryBuildMessages(
        AiChatPayload request,
        out IReadOnlyList<OpenAiMessage>? messages,
        out AiChatProviderResult? failure)
    {
        var builtMessages = new List<OpenAiMessage>();
        messages = null;
        failure = null;

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            builtMessages.Add(new OpenAiMessage("system", request.SystemPrompt.Trim(), null));
        }

        foreach (var message in request.Messages)
        {
            if (message.Images.Count > 0 && message.Role != AiMessageRole.User)
            {
                failure = RecordFailure("unsupported_image_role", "Image input blocks are only supported for user messages.");
                return false;
            }

            builtMessages.Add(new OpenAiMessage(
                ToOpenAiRole(message.Role),
                MapOpenAiContent(message),
                string.IsNullOrWhiteSpace(message.Name) ? null : message.Name.Trim()));
        }

        messages = builtMessages;
        return true;
    }

    private static object MapOpenAiContent(AiChatMessage message)
    {
        if (message.Images.Count == 0)
        {
            return message.Content;
        }

        var parts = new List<OpenAiMessageContentPart>();
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            parts.Add(OpenAiMessageContentPart.FromText(message.Content));
        }

        foreach (var image in message.Images)
        {
            parts.Add(OpenAiMessageContentPart.FromImage(CreateImageDataUrl(image)));
        }

        return parts;
    }

    private static string CreateImageDataUrl(AiChatImage image)
    {
        var data = image.Data.Trim();
        return data.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? data
            : $"data:{image.MediaType.Trim()};base64,{data}";
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
        OpenAiChatCompletionResponse providerResponse,
        HttpResponseMessage response,
        AiChatPayload request,
        out AiChatResponse? chatResponse,
        out AiChatProviderResult? failure)
    {
        chatResponse = null;
        failure = null;

        var choice = providerResponse.Choices[0];
        if (IsContentFiltered(choice.FinishReason))
        {
            failure = RecordFailure("content_filtered", "AI provider blocked the response because of content safety policy.");
            return false;
        }

        if (choice.Message is null)
        {
            failure = RecordFailure("invalid_response", "AI provider returned an invalid response.");
            return false;
        }

        var toolCalls = request.Options.ToolProposalsEnabled ? choice.Message.ToolCalls : null;
        if (!TryMapProposedActions(toolCalls, out var proposedActions, out failure))
        {
            return false;
        }

        if (!AiStructuredOutputResponseMapper.TryMapAssistantMessage(
                request,
                choice.Message.Content,
                out var assistantMessage,
                out var structuredOutputFailure))
        {
            failure = RecordFailure(structuredOutputFailure!.Code, structuredOutputFailure.Message);
            return false;
        }

        if (string.IsNullOrWhiteSpace(assistantMessage) && proposedActions.Count == 0)
        {
            failure = RecordFailure("invalid_response", "AI provider returned an empty text response.");
            return false;
        }

        chatResponse = new AiChatResponse(
            assistantMessage,
            proposedActions,
            new AiTokenUsage(
                providerResponse.Usage?.PromptTokens,
                providerResponse.Usage?.CompletionTokens,
                providerResponse.Usage?.TotalTokens),
            GetProviderRequestId(response),
            choice.FinishReason);

        return true;
    }

    private bool TryMapProposedActions(
        IReadOnlyList<OpenAiToolCall>? toolCalls,
        out IReadOnlyList<AiProposedActionCandidate> proposedActions,
        out AiChatProviderResult? failure)
    {
        proposedActions = [];
        failure = null;

        if (toolCalls is null || toolCalls.Count == 0)
        {
            return true;
        }

        var actions = new List<AiProposedActionCandidate>();
        foreach (var toolCall in toolCalls)
        {
            if (!string.Equals(toolCall.Type, "function", StringComparison.OrdinalIgnoreCase)
                || toolCall.Function is null
                || !string.Equals(toolCall.Function.Name, CreateEventDraftToolName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryNormalizeToolArguments(toolCall.Function.Arguments, out var payloadJson, out failure))
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

    private static string ToOpenAiRole(AiMessageRole role) => role switch
    {
        AiMessageRole.System => "system",
        AiMessageRole.User => "user",
        AiMessageRole.Assistant => "assistant",
        _ => "user"
    };

    private static bool IsContentFiltered(string? finishReason) =>
        string.Equals(finishReason, "content_filter", StringComparison.OrdinalIgnoreCase)
        || string.Equals(finishReason, "content_filtered", StringComparison.OrdinalIgnoreCase);

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
        _metrics.RecordAiProviderRequest(AiProviderDefaults.ProviderOpenAiCompatible, "failed", code);
        return AiChatProviderResult.Failure(code, message, isTransient);
    }

    private void RecordSuccess(
        AiChatResponse response,
        long startedAt,
        Activity? telemetryActivity)
    {
        _metrics.RecordAiProviderRequest(AiProviderDefaults.ProviderOpenAiCompatible, "succeeded");
        _metrics.RecordAiProviderRequestDuration(
            Stopwatch.GetElapsedTime(startedAt),
            AiProviderDefaults.ProviderOpenAiCompatible,
            "succeeded");
        _metrics.RecordAiProviderTokenUsage(
            AiProviderDefaults.ProviderOpenAiCompatible,
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.TotalTokens);
        _metrics.RecordAiProviderProposedActions(
            AiProviderDefaults.ProviderOpenAiCompatible,
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
            AiProviderDefaults.ProviderOpenAiCompatible,
            "failed",
            error.Code);
        AiProviderTelemetry.MarkFailure(telemetryActivity, error.Code, error.IsTransient);
        return result;
    }

    private sealed record OpenAiChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("messages")]
        public IReadOnlyList<OpenAiMessage> Messages { get; init; } = [];

        [JsonPropertyName("temperature")]
        public decimal Temperature { get; init; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<OpenAiTool>? Tools { get; init; }

        [JsonPropertyName("tool_choice")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolChoice { get; init; }

        [JsonPropertyName("response_format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OpenAiResponseFormat? ResponseFormat { get; init; }

        [JsonPropertyName("chat_template_kwargs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OpenAiChatTemplateKwargs? ChatTemplateKwargs { get; init; }
    }

    private sealed record OpenAiChatTemplateKwargs
    {
        [JsonPropertyName("enable_thinking")]
        public bool EnableThinking { get; init; }
    }

    private sealed record OpenAiMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] object Content,
        [property: JsonPropertyName("name")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name);

    private sealed record OpenAiMessageContentPart
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; init; }

        [JsonPropertyName("image_url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OpenAiImageUrl? ImageUrl { get; init; }

        public static OpenAiMessageContentPart FromText(string text) => new()
        {
            Type = "text",
            Text = text
        };

        public static OpenAiMessageContentPart FromImage(string imageUrl) => new()
        {
            Type = "image_url",
            ImageUrl = new OpenAiImageUrl(imageUrl)
        };
    }

    private sealed record OpenAiImageUrl(
        [property: JsonPropertyName("url")] string Url);

    private sealed record OpenAiTool(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] OpenAiFunctionTool Function);

    private sealed record OpenAiFunctionTool(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] JsonElement Parameters)
    {
        [JsonPropertyName("strict")]
        public bool Strict => true;
    }

    private sealed record OpenAiResponseFormat(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("json_schema")] OpenAiResponseJsonSchema JsonSchema);

    private sealed record OpenAiResponseJsonSchema(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("schema")] JsonElement Schema)
    {
        [JsonPropertyName("strict")]
        public bool Strict => true;
    }

    private sealed record OpenAiChatCompletionResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChoice> Choices,
        [property: JsonPropertyName("usage")] OpenAiUsage? Usage);

    private sealed record OpenAiModelsListResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<OpenAiModelDescriptor>? Data);

    private sealed record OpenAiModelDescriptor(
        [property: JsonPropertyName("id")] string? Id);

    private sealed record OpenAiChoice(
        [property: JsonPropertyName("message")] OpenAiResponseMessage? Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record OpenAiResponseMessage(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<OpenAiToolCall>? ToolCalls);

    private sealed record OpenAiToolCall(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("function")] OpenAiFunctionCall? Function);

    private sealed record OpenAiFunctionCall(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("arguments")] string? Arguments);

    private sealed record OpenAiUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);

}
