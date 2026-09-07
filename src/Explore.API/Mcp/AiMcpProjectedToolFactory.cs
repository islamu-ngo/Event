using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Tools;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Explore.API.Mcp;

public static class AiMcpProjectedToolFactory
{
    public static IReadOnlyList<McpServerTool> CreateTools(IAiToolContractRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return registry.Definitions
            .Where(definition => definition.ExposeToMcp)
            .Select(definition => new AiMcpProjectedProposalTool(definition))
            .ToArray();
    }

    public static string BuildToolName(AiToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return $"propose_{ToSnakeCase(definition.Name)}";
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 8);
        var previousWasSeparator = false;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!char.IsLetterOrDigit(character))
            {
                AppendSeparator(builder, ref previousWasSeparator);
                continue;
            }

            if (char.IsUpper(character) &&
                index > 0 &&
                !previousWasSeparator &&
                char.IsLetterOrDigit(value[index - 1]) &&
                !char.IsUpper(value[index - 1]))
            {
                AppendSeparator(builder, ref previousWasSeparator);
            }

            builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
            previousWasSeparator = false;
        }

        return builder.ToString().Trim('_');
    }

    private static void AppendSeparator(StringBuilder builder, ref bool previousWasSeparator)
    {
        if (builder.Length == 0 || previousWasSeparator)
        {
            return;
        }

        builder.Append('_');
        previousWasSeparator = true;
    }
}

public sealed class AiMcpProjectedToolOptionsSetup(IAiToolContractRegistry registry) : IConfigureOptions<McpServerOptions>
{
    public void Configure(McpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ToolCollection ??= new McpServerPrimitiveCollection<McpServerTool>(StringComparer.Ordinal);

        foreach (var tool in AiMcpProjectedToolFactory.CreateTools(registry))
        {
            options.ToolCollection.TryAdd(tool);
        }
    }
}

public sealed class AiMcpProjectedProposalTool : McpServerTool
{
    private const string ConversationIdArgumentName = "conversationId";
    private const string SummaryArgumentName = "summary";
    private const string InvalidToolArgumentsMessage = "Invalid MCP tool arguments.";
    private static readonly IReadOnlyList<object> AuthorizationMetadata =
        [new AuthorizeAttribute { Policy = McpAuthorizationPolicies.Propose }];

    private readonly AiToolDefinition _definition;
    private readonly IReadOnlyDictionary<string, string> _payloadFieldsByArgumentName;

    public AiMcpProjectedProposalTool(AiToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _definition = definition;
        _payloadFieldsByArgumentName = definition.AllowedPayloadFields
            .ToDictionary(field => field, field => field, StringComparer.OrdinalIgnoreCase);
        ProtocolTool = BuildProtocolTool(definition);
    }

    public override Tool ProtocolTool { get; }

    public override IReadOnlyList<object> Metadata => AuthorizationMetadata;

    public ProposeAiToolActionCommand MapArgumentsToCommand(IEnumerable<KeyValuePair<string, JsonElement>> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        RejectUnexpectedArguments(arguments);

        var conversationId = ReadConversationId(arguments);
        var summary = ReadSummary(arguments);
        var payloadJson = BuildPayloadJson(arguments);

        return new ProposeAiToolActionCommand
        {
            ConversationId = conversationId,
            ToolName = _definition.Name,
            PayloadJson = payloadJson,
            Summary = summary
        };
    }

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = Stopwatch.GetTimestamp();
        using var activity = McpAdapterTelemetry.StartToolCall(ProtocolTool.Name, projected: true);
        ProposeAiToolActionCommand command;
        try
        {
            if (request.Params.Arguments is null)
            {
                throw new ArgumentException("MCP tool arguments are required.");
            }

            command = MapArgumentsToCommand(request.Params.Arguments);
        }
        catch (ArgumentException)
        {
            McpAdapterTelemetry.MarkFailure(activity, "invalid_tool_arguments");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                ProtocolTool.Name,
                projected: true,
                outcome: "failed",
                failureCode: "invalid_tool_arguments");

            return CreateResult(
                new AiAssistantMcpTools.AiMcpCommandResultDescriptor(
                    false,
                    null,
                    InvalidToolArgumentsMessage,
                    "invalid_tool_arguments",
                    [InvalidToolArgumentsMessage]),
                isError: true);
        }

        try
        {
            var services = request.Services
                ?? throw new InvalidOperationException("MCP request services were unavailable.");
            var mediator = services.GetRequiredService<IMediator>();
            var response = await mediator.Send(command, cancellationToken);

            if (response.IsSuccess)
            {
                McpAdapterTelemetry.MarkSuccess(activity);
            }
            else
            {
                McpAdapterTelemetry.MarkFailure(activity, response.FailureCode);
            }

            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                ProtocolTool.Name,
                projected: true,
                outcome: response.IsSuccess ? "succeeded" : "failed",
                failureCode: response.FailureCode);

            return CreateResult(
                new AiAssistantMcpTools.AiMcpCommandResultDescriptor(
                    response.IsSuccess,
                    response.Id == Guid.Empty ? null : response.Id,
                    response.Message,
                    response.FailureCode,
                    response.Errors ?? []),
                isError: !response.IsSuccess);
        }
        catch (OperationCanceledException)
        {
            McpAdapterTelemetry.MarkCancelled(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                ProtocolTool.Name,
                projected: true,
                outcome: "cancelled",
                failureCode: "cancelled");
            throw;
        }
        catch
        {
            McpAdapterTelemetry.MarkFailure(activity, "unknown");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                ProtocolTool.Name,
                projected: true,
                outcome: "failed",
                failureCode: "unknown");
            throw;
        }
    }

    private static Tool BuildProtocolTool(AiToolDefinition definition)
    {
        var metadata = definition.EffectiveAgentMetadata;
        var requiresConfirmation = definition.ConfirmationMode == AiToolConfirmationMode.Required ||
            metadata.ApprovalMode == AiToolApprovalMode.HumanConfirmationRequired;

        return new Tool
        {
            Name = AiMcpProjectedToolFactory.BuildToolName(definition),
            Title = definition.DisplayName,
            Description = BuildDescription(definition),
            InputSchema = BuildInputSchema(definition),
            Annotations = new ToolAnnotations
            {
                Title = definition.DisplayName,
                ReadOnlyHint = false,
                DestructiveHint = metadata.DestructiveHint,
                IdempotentHint = false,
                OpenWorldHint = false
            },
            Meta = new JsonObject
            {
                ["islamuToolName"] = definition.Name,
                ["islamuProposedActionKind"] = definition.Kind.ToString(),
                ["islamuRiskClass"] = metadata.RiskClass.ToString(),
                ["islamuApprovalMode"] = metadata.ApprovalMode.ToString(),
                ["islamuRequiresConfirmation"] = requiresConfirmation,
                ["islamuDestructive"] = metadata.DestructiveHint,
                ["islamuSafeActionInstructions"] = metadata.SafeActionInstructions
            }
        };
    }

    private static string BuildDescription(AiToolDefinition definition)
        => $"Propose {definition.DisplayName} through ISLAMU Event's confirmation workflow. " +
            "This MCP tool only persists a proposed action; it never directly creates, updates, publishes, or deletes domain data.";

    private static JsonElement BuildInputSchema(AiToolDefinition definition)
    {
        using var document = JsonDocument.Parse(definition.JsonSchema);
        var registrySchema = document.RootElement;
        var properties = new JsonObject
        {
            [ConversationIdArgumentName] = new JsonObject
            {
                ["type"] = "string",
                ["format"] = "uuid",
                ["description"] = "AI conversation identifier that will own the proposed action."
            },
            [SummaryArgumentName] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional short human-readable summary of the proposed action."
            }
        };

        if (registrySchema.TryGetProperty("properties", out var registryProperties))
        {
            foreach (var property in registryProperties.EnumerateObject())
            {
                if (!definition.AllowedPayloadFields.Contains(property.Name) ||
                    definition.ForbiddenPayloadFields.Contains(property.Name))
                {
                    continue;
                }

                properties[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        }

        var required = new JsonArray(ConversationIdArgumentName);
        if (registrySchema.TryGetProperty("required", out var registryRequired))
        {
            foreach (var requiredProperty in registryRequired.EnumerateArray())
            {
                var propertyName = requiredProperty.GetString();
                if (propertyName is not null &&
                    definition.AllowedPayloadFields.Contains(propertyName) &&
                    !definition.ForbiddenPayloadFields.Contains(propertyName))
                {
                    required.Add(propertyName);
                }
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = required,
            ["properties"] = properties
        };

        using var projectedDocument = JsonDocument.Parse(schema.ToJsonString());
        return projectedDocument.RootElement.Clone();
    }

    private void RejectUnexpectedArguments(IEnumerable<KeyValuePair<string, JsonElement>> arguments)
    {
        foreach (var argumentName in arguments.Select(argument => argument.Key))
        {
            if (IsMcpEnvelopeArgument(argumentName) ||
                _payloadFieldsByArgumentName.ContainsKey(argumentName))
            {
                continue;
            }

            throw new ArgumentException(
                $"Unsupported argument '{argumentName}' for MCP tool '{ProtocolTool.Name}'.");
        }
    }

    private static bool IsMcpEnvelopeArgument(string argumentName)
        => string.Equals(argumentName, ConversationIdArgumentName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argumentName, SummaryArgumentName, StringComparison.OrdinalIgnoreCase);

    private static Guid ReadConversationId(IEnumerable<KeyValuePair<string, JsonElement>> arguments)
    {
        if (!TryGetArgument(arguments, ConversationIdArgumentName, out var conversationIdElement) ||
            conversationIdElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(conversationIdElement.GetString(), out var conversationId) ||
            conversationId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty conversationId string is required for projected MCP proposal tools.");
        }

        return conversationId;
    }

    private static string? ReadSummary(IEnumerable<KeyValuePair<string, JsonElement>> arguments)
    {
        if (!TryGetArgument(arguments, SummaryArgumentName, out var summaryElement) ||
            summaryElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (summaryElement.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("summary must be a string when supplied.");
        }

        var summary = summaryElement.GetString();
        return string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
    }

    private string BuildPayloadJson(IEnumerable<KeyValuePair<string, JsonElement>> arguments)
    {
        var payload = new JsonObject();
        foreach (var argument in arguments)
        {
            if (!_payloadFieldsByArgumentName.TryGetValue(argument.Key, out var payloadFieldName))
            {
                continue;
            }

            payload[payloadFieldName] = argument.Value.ValueKind == JsonValueKind.Null
                ? null
                : JsonNode.Parse(argument.Value.GetRawText());
        }

        return payload.ToJsonString();
    }

    private static bool TryGetArgument(
        IEnumerable<KeyValuePair<string, JsonElement>> arguments,
        string name,
        out JsonElement value)
    {
        foreach (var argument in arguments)
        {
            if (string.Equals(argument.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = argument.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static CallToolResult CreateResult(
        AiAssistantMcpTools.AiMcpCommandResultDescriptor descriptor,
        bool isError)
    {
        var json = JsonSerializer.Serialize(
            descriptor,
            AiToolRegistryMcpJsonContext.Default.AiMcpCommandResultDescriptor);

        return new CallToolResult
        {
            IsError = isError,
            Content = [new TextContentBlock { Text = json }]
        };
    }
}
