using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Telemetry;
using Explore.Domain.Ai;
using Microsoft.Extensions.Options;
using Refit;

namespace Explore.Infrastructure.Ai;

public class AnthropicCompatibleChatProvider : IAiChatProvider
{
    public const string HttpClientName = "AnthropicCompatibleAiClient";

    private const string CreateEventDraftToolName = "create_event_draft";
    private static readonly RefitSettings RefitSettings = new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        })
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AiProviderSettings> _options;
    private readonly AiProviderSettingsValidator _validator;
    private readonly BusinessMetrics _metrics;
    private readonly int _providerId;
    private readonly string _providerName;
    private readonly string _httpClientName;
    private readonly string _defaultEndpointUrl;
    private readonly string _notConfiguredMessage;

    public AnthropicCompatibleChatProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AiProviderSettings> options,
        AiProviderSettingsValidator validator,
        BusinessMetrics metrics)
        : this(
            httpClientFactory,
            options,
            validator,
            metrics,
            AiProviderSettings.ProviderAnthropicCompatible,
            AiProviderDefaults.ProviderAnthropicCompatible,
            HttpClientName,
            string.Empty,
            "Anthropic-compatible provider is not enabled or configured.")
    {
    }

    protected AnthropicCompatibleChatProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AiProviderSettings> options,
        AiProviderSettingsValidator validator,
        BusinessMetrics metrics,
        int providerId,
        string providerName,
        string httpClientName,
        string defaultEndpointUrl,
        string notConfiguredMessage)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _validator = validator;
        _metrics = metrics;
        _providerId = providerId;
        _providerName = providerName;
        _httpClientName = httpClientName;
        _defaultEndpointUrl = defaultEndpointUrl;
        _notConfiguredMessage = notConfiguredMessage;
    }

    public async Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = ResolveSettings(request);
        var providerName = _providerName;
        var startedAt = Stopwatch.GetTimestamp();
        using var telemetryActivity = AiProviderTelemetry.StartRequest(providerName, request);

        if (!IsRunnableProvider(settings))
        {
            return CompleteFailure(
                RecordFailure("provider_not_configured", _notConfiguredMessage),
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
            var api = CreateApiClient(settings);
            using var providerResponse = await api.CreateMessageAsync(payload!, ResolveApiKey(settings), timeoutCts.Token);

            if (!providerResponse.IsSuccessStatusCode)
            {
                var failure = providerResponse.StatusCode is { } statusCode
                    ? RecordHttpFailure(statusCode)
                    : RecordFailure("provider_unreachable", "AI provider request failed before a response was received.", isTransient: true);
                return CompleteFailure(failure, startedAt, telemetryActivity);
            }

            if (providerResponse.Content is null)
            {
                return CompleteFailure(
                    RecordFailure("invalid_response", "AI provider returned an invalid response."),
                    startedAt,
                    telemetryActivity);
            }

            if (!TryMapResponse(providerResponse.Content, GetProviderRequestId(providerResponse.Headers), request, out var chatResponse, out var responseFailure))
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
        catch (ApiException ex)
        {
            return CompleteFailure(
                RecordHttpFailure(ex.StatusCode),
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

    private IAnthropicMessagesApi CreateApiClient(AiProviderSettings settings)
    {
        var client = _httpClientFactory.CreateClient(_httpClientName);
        client.BaseAddress = BuildMessagesBaseUri(settings.EndpointUrl, _defaultEndpointUrl);
        return RestService.For<IAnthropicMessagesApi>(client, RefitSettings);
    }

    private static string? ResolveApiKey(AiProviderSettings settings) =>
        string.IsNullOrWhiteSpace(settings.ApiKey) ? null : settings.ApiKey;

    private bool IsRunnableProvider(AiProviderSettings settings) =>
        settings.Enabled
        && settings.Provider == _providerId;

    private AiProviderSettings ResolveSettings(AiChatPayload request)
    {
        var defaults = _options.Value;
        var providerConfiguration = request.ProviderConfiguration;

        if (providerConfiguration is null
            || providerConfiguration.Provider != _providerId)
        {
            return defaults;
        }

        return new AiProviderSettings
        {
            Enabled = true,
            Provider = _providerId,
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

    private static Uri BuildMessagesBaseUri(string? endpointUrl, string defaultEndpointUrl)
    {
        var effectiveEndpointUrl = string.IsNullOrWhiteSpace(endpointUrl)
            ? defaultEndpointUrl
            : endpointUrl.Trim();
        var endpoint = new Uri(effectiveEndpointUrl, UriKind.Absolute);
        var path = endpoint.AbsolutePath.TrimEnd('/');

        if (path.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(endpoint) { Path = NormalizeBasePath(path[..^"/messages".Length]) };
            return builder.Uri;
        }

        return new UriBuilder(endpoint) { Path = NormalizeBasePath(path) }.Uri;
    }

    private static string NormalizeBasePath(string path) =>
        string.IsNullOrWhiteSpace(path) || path == "/"
            ? string.Empty
            : path.TrimEnd('/');

    private bool TryCreatePayload(
        AiProviderSettings settings,
        AiChatPayload request,
        out AnthropicCreateMessageRequest? payload,
        out AiChatProviderResult? failure)
    {
        payload = null;
        failure = null;

        if (!TryBuildTools(request, out var tools))
        {
            failure = RecordFailure("invalid_action_schema", "AI action schema must be a valid JSON object.");
            return false;
        }

        if (!TryBuildMessages(request, out var messages, out failure))
        {
            return false;
        }

        payload = new AnthropicCreateMessageRequest
        {
            Model = string.IsNullOrWhiteSpace(request.ModelId) ? settings.ModelId.Trim() : request.ModelId.Trim(),
            MaxTokens = request.Options.MaxOutputTokens,
            System = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt.Trim(),
            Messages = messages,
            Temperature = request.Options.Temperature,
            Tools = tools.Count == 0 ? null : tools,
            ChatTemplateKwargs = CreateChatTemplateKwargs(settings, request)
        };

        return true;
    }

    private static AnthropicChatTemplateKwargs? CreateChatTemplateKwargs(
        AiProviderSettings settings,
        AiChatPayload request)
    {
        if (!ShouldDisableTemplateThinking(request) || !IsLocalOrPrivateEndpoint(settings.EndpointUrl))
        {
            return null;
        }

        return new AnthropicChatTemplateKwargs { EnableThinking = false };
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

    private bool TryBuildTools(AiChatPayload request, out IReadOnlyList<AnthropicApiTool> tools)
    {
        var builtTools = new List<AnthropicApiTool>();
        if (!request.Options.ToolProposalsEnabled || request.ActionSchema is null)
        {
            tools = builtTools;
            return true;
        }

        foreach (var kind in request.ActionSchema.AllowedKinds.Distinct())
        {
            if (kind != AiProposedActionKind.CreateEventDraft)
            {
                continue;
            }

            if (!TryCreateJsonSchema(request.ActionSchema.JsonSchema, out var schema))
            {
                tools = [];
                return false;
            }

            builtTools.Add(new AnthropicApiTool(
                CreateEventDraftToolName,
                "Propose a draft event payload. The platform persists this as a proposal that still requires explicit user confirmation.",
                schema!.Value));
        }

        tools = builtTools;
        return true;
    }

    private bool TryBuildMessages(
        AiChatPayload request,
        out IReadOnlyList<AnthropicApiMessage> messages,
        out AiChatProviderResult? failure)
    {
        failure = null;
        var builtMessages = new List<AnthropicApiMessage>(request.Messages.Count);

        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case AiMessageRole.System:
                case AiMessageRole.User:
                    builtMessages.Add(MapUserMessage(message));
                    break;
                case AiMessageRole.Assistant:
                    if (message.Images.Count > 0)
                    {
                        messages = [];
                        failure = RecordFailure("unsupported_image_role", "Image input blocks are only supported for user messages.");
                        return false;
                    }

                    builtMessages.Add(AnthropicApiMessage.Text("assistant", message.Content));
                    break;
                case AiMessageRole.Tool:
                    if (message.Images.Count > 0)
                    {
                        messages = [];
                        failure = RecordFailure("unsupported_image_role", "Image input blocks are not supported on tool result messages.");
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(message.Name))
                    {
                        messages = [];
                        failure = RecordFailure("invalid_tool_result", "Tool result messages require the provider tool call id in AiChatMessage.Name.");
                        return false;
                    }

                    builtMessages.Add(MapToolResultMessage(message));
                    break;
            }
        }

        messages = builtMessages;
        return true;
    }

    private static AnthropicApiMessage MapUserMessage(AiChatMessage message)
    {
        if (message.Images.Count == 0)
        {
            return AnthropicApiMessage.Text("user", message.Content);
        }

        var blocks = new List<AnthropicApiContentBlock>();
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            blocks.Add(AnthropicApiContentBlock.FromText(message.Content));
        }

        foreach (var image in message.Images)
        {
            blocks.Add(AnthropicApiContentBlock.FromImage(image.MediaType.Trim(), image.Data.Trim()));
        }

        return AnthropicApiMessage.WithContentBlocks("user", blocks);
    }

    private static AnthropicApiMessage MapToolResultMessage(AiChatMessage message)
    {
        var blocks = new List<AnthropicApiContentBlock>
        {
            AnthropicApiContentBlock.FromToolResult(
                message.Name!,
                isError: false,
                content: message.Content)
        };

        return AnthropicApiMessage.WithContentBlocks("user", blocks);
    }

    private bool TryCreateJsonSchema(string schemaJson, out JsonElement? schema)
    {
        schema = null;
        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            schema = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool TryMapResponse(
        AnthropicMessageResponse providerResponse,
        string? providerRequestId,
        AiChatPayload request,
        out AiChatResponse? chatResponse,
        out AiChatProviderResult? failure)
    {
        chatResponse = null;
        failure = null;

        if (IsContentFiltered(providerResponse.StopReason))
        {
            failure = RecordFailure("content_filtered", "AI provider blocked the response because of content safety policy.");
            return false;
        }

        if (providerResponse.Content is null || providerResponse.Content.Count == 0)
        {
            failure = RecordFailure("invalid_response", "AI provider returned an empty response.");
            return false;
        }

        var toolUseBlocks = providerResponse.Content
            .Where(b => string.Equals(b.Type, "tool_use", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!request.Options.ToolProposalsEnabled && toolUseBlocks.Count > 0)
        {
            failure = RecordFailure(
                "unexpected_tool_response",
                "AI provider returned a tool response, but tool proposals were not enabled for this run.");
            return false;
        }

        if (request.Options.ToolProposalsEnabled && toolUseBlocks.Count > 0)
        {
            if (!TryMapProposedActions(toolUseBlocks, out var proposedActions, out failure))
            {
                return false;
            }

            var textFromToolResponse = string.Join("\n", providerResponse.Content
                .Where(b => string.Equals(b.Type, "text", StringComparison.OrdinalIgnoreCase))
                .Select(b => b.Text ?? string.Empty)
                .Where(t => !string.IsNullOrWhiteSpace(t)));

            if (!AiStructuredOutputResponseMapper.TryMapAssistantMessage(
                    request,
                    string.IsNullOrWhiteSpace(textFromToolResponse) ? "Event draft proposed." : textFromToolResponse,
                    out var toolAssistantMessage,
                    out var toolStructuredFailure))
            {
                failure = RecordFailure(toolStructuredFailure!.Code, toolStructuredFailure.Message);
                return false;
            }

            chatResponse = new AiChatResponse(
                toolAssistantMessage,
                proposedActions,
                MapUsage(providerResponse.Usage),
                providerRequestId,
                providerResponse.StopReason);

            return true;
        }

        var assistantText = string.Join("\n", providerResponse.Content
            .Where(b => string.Equals(b.Type, "text", StringComparison.OrdinalIgnoreCase))
            .Select(b => b.Text ?? string.Empty)
            .Where(t => !string.IsNullOrWhiteSpace(t)));

        if (string.IsNullOrWhiteSpace(assistantText))
        {
            failure = RecordFailure("invalid_response", "AI provider returned an empty text response.");
            return false;
        }

        if (!AiStructuredOutputResponseMapper.TryMapAssistantMessage(
                request,
                assistantText,
                out var assistantMessage,
                out var structuredOutputFailure))
        {
            failure = RecordFailure(structuredOutputFailure!.Code, structuredOutputFailure.Message);
            return false;
        }

        chatResponse = new AiChatResponse(
            assistantMessage,
            [],
            MapUsage(providerResponse.Usage),
            providerRequestId,
            providerResponse.StopReason);

        return true;
    }

    private static AiTokenUsage MapUsage(AnthropicApiUsage? usage) => new(
        usage?.InputTokens,
        usage?.OutputTokens,
        (usage?.InputTokens) + (usage?.OutputTokens));

    private bool TryMapProposedActions(
        IReadOnlyList<AnthropicApiContentBlock> toolUseBlocks,
        out IReadOnlyList<AiProposedActionCandidate> proposedActions,
        out AiChatProviderResult? failure)
    {
        proposedActions = [];
        failure = null;

        var actions = new List<AiProposedActionCandidate>();
        foreach (var block in toolUseBlocks)
        {
            if (!string.Equals(block.Name, CreateEventDraftToolName, StringComparison.Ordinal))
            {
                continue;
            }

            if (block.Input is null)
            {
                failure = RecordFailure("invalid_tool_arguments", "AI provider returned invalid action arguments.");
                return false;
            }

            var payloadJson = block.Input.Value.GetRawText();
            actions.Add(new AiProposedActionCandidate(
                AiProposedActionKind.CreateEventDraft,
                payloadJson,
                "Create an event draft"));
        }

        proposedActions = actions;
        return true;
    }

    private static bool IsContentFiltered(string? stopReason) =>
        string.Equals(stopReason, "content_filtered", StringComparison.OrdinalIgnoreCase);

    private static string? GetProviderRequestId(HttpResponseHeaders? headers)
    {
        if (headers is null)
        {
            return null;
        }

        string[] headerNames = ["x-request-id", "request-id"];
        foreach (var headerName in headerNames)
        {
            if (headers.TryGetValues(headerName, out var values))
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
        _metrics.RecordAiProviderRequest(_providerName, "failed", code);
        return AiChatProviderResult.Failure(code, message, isTransient);
    }

    private void RecordSuccess(
        AiChatResponse response,
        long startedAt,
        Activity? telemetryActivity)
    {
        _metrics.RecordAiProviderRequest(_providerName, "succeeded");
        _metrics.RecordAiProviderRequestDuration(
            Stopwatch.GetElapsedTime(startedAt),
            _providerName,
            "succeeded");
        _metrics.RecordAiProviderTokenUsage(
            _providerName,
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.TotalTokens);
        _metrics.RecordAiProviderProposedActions(
            _providerName,
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
            _providerName,
            "failed",
            error.Code);
        AiProviderTelemetry.MarkFailure(telemetryActivity, error.Code, error.IsTransient);
        return result;
    }
}
