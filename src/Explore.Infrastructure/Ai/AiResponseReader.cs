using System.Text.Json;
using Explore.Infrastructure.Http;

namespace Explore.Infrastructure.Ai;

/// <summary>Validates wire values before any provider DTO, JsonDocument or SDK object is allocated.</summary>
public static class AiResponseReader
{
    public static async Task<byte[]> ReadAsync(
        HttpContent content, AiResponseLimits limits, bool discovery, CancellationToken cancellationToken = default)
    {
        if (!limits.IsValid)
            throw new ProviderResponseRejectedException("response_policy_invalid");

        var bytes = await BoundedResponseReader.ReadJsonAsync(content,
            discovery ? limits.MaximumDiscoveryBytes : limits.MaximumChatBytes,
            limits.MaximumDepth, limits.MaximumCollectionItems, limits.MaximumScalarBytes, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ValidateShape(bytes, limits, discovery);
        }
        catch (JsonException)
        {
            throw new ProviderResponseRejectedException("response_invalid_json");
        }
        catch (InvalidOperationException)
        {
            // Decoding a recognized escaped name/identifier can reject invalid Unicode
            // after the structural scan, without exposing the offending provider value.
            throw new ProviderResponseRejectedException("response_invalid_json");
        }
        return bytes;
    }

    public static ProviderResponseRejectedException? FindRejection(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ProviderResponseRejectedException rejection)
                return rejection;
        }
        return null;
    }

    private static void ValidateShape(ReadOnlySpan<byte> bytes, AiResponseLimits limits, bool discovery)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = limits.MaximumDepth });
        var containers = new Stack<Container>();
        Field field = Field.None;
        int toolCalls = 0;
        int choices = 0;
        int models = 0;
        Span<char> identifier = stackalloc char[256];

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var parent = containers.Pop();
                if (++parent.Items > limits.MaximumCollectionItems)
                    throw new ProviderResponseRejectedException("response_item_limit");
                containers.Push(parent);
                field = ReadField(ref reader);
                continue;
            }

            if (reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject)
            {
                var container = containers.Pop();
                if (container.ToolArguments && reader.BytesConsumed - container.Start > limits.MaximumToolArgumentBytes)
                    throw new ProviderResponseRejectedException("response_tool_argument_limit");
                field = Field.None;
                continue;
            }

            bool callItem = false;
            if (containers.TryPeek(out var array) && array.Array)
            {
                containers.Pop();
                callItem = array.CallItems;
                if (++array.Items > limits.MaximumCollectionItems)
                    throw new ProviderResponseRejectedException("response_item_limit");
                if (array.Choices && ++choices > limits.MaximumChoices)
                    throw new ProviderResponseRejectedException("response_choice_limit");
                if (array.Models && ++models > limits.MaximumCollectionItems)
                    throw new ProviderResponseRejectedException("response_item_limit");
                if (array.ToolCalls && ++toolCalls > limits.MaximumToolCalls)
                    throw new ProviderResponseRejectedException("response_tool_call_limit");
                containers.Push(array);
            }

            bool arguments = !discovery && field == Field.Arguments;
            if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
            {
                containers.Push(new Container
                {
                    Array = reader.TokenType == JsonTokenType.StartArray,
                    Choices = !discovery && field == Field.Choices && reader.CurrentDepth == 1,
                    Models = discovery && field == Field.Data && reader.CurrentDepth == 1,
                    ToolCalls = !discovery && field == Field.ToolCalls,
                    CallItems = !discovery && field == Field.CallItems && reader.CurrentDepth == 1,
                    CallItem = callItem,
                    ToolArguments = arguments,
                    Start = reader.TokenStartIndex
                });
            }
            else
            {
                if (arguments && reader.ValueSpan.Length > limits.MaximumToolArgumentBytes)
                    throw new ProviderResponseRejectedException("response_tool_argument_limit");

                if (discovery && field == Field.Id && reader.TokenType == JsonTokenType.String)
                {
                    try
                    {
                        reader.CopyString(identifier[..limits.MaximumModelIdentifierCharacters]);
                    }
                    catch (ArgumentException)
                    {
                        throw new ProviderResponseRejectedException("response_identifier_limit");
                    }
                }
                if (!discovery && field == Field.Type && reader.TokenType == JsonTokenType.String
                    && containers.TryPeek(out var callContainer) && callContainer.CallItem
                    && IsToolCall(ref reader)
                    && ++toolCalls > limits.MaximumToolCalls)
                    throw new ProviderResponseRejectedException("response_tool_call_limit");
            }
            field = Field.None;
        }

        if (containers.Count != 0)
            throw new ProviderResponseRejectedException("response_invalid_json");
    }

    private static Field ReadField(ref Utf8JsonReader reader)
    {
        // A recognized name has at most ten UTF-16 characters, each encoded by at most
        // six JSON bytes. Unknown names were already checked against the scalar ceiling.
        if (reader.ValueSpan.Length > 60)
            return Field.None;
        Span<char> name = stackalloc char[60];
        name = name[..reader.CopyString(name)];
        if (name.Equals("choices", StringComparison.OrdinalIgnoreCase)) return Field.Choices;
        if (name.Equals("tool_calls", StringComparison.OrdinalIgnoreCase)) return Field.ToolCalls;
        if (name.Equals("arguments", StringComparison.OrdinalIgnoreCase)
            || name.Equals("input", StringComparison.OrdinalIgnoreCase)) return Field.Arguments;
        if (name.Equals("id", StringComparison.OrdinalIgnoreCase)) return Field.Id;
        if (name.Equals("data", StringComparison.OrdinalIgnoreCase)) return Field.Data;
        if (name.Equals("type", StringComparison.OrdinalIgnoreCase)) return Field.Type;
        if (name.Equals("output", StringComparison.OrdinalIgnoreCase)
            || name.Equals("content", StringComparison.OrdinalIgnoreCase)) return Field.CallItems;
        return Field.None;
    }

    private static bool IsToolCall(ref Utf8JsonReader reader)
    {
        if (reader.ValueSpan.Length > 78)
            return false;
        Span<char> value = stackalloc char[78];
        value = value[..reader.CopyString(value)];
        return value.Equals("function_call", StringComparison.OrdinalIgnoreCase)
            || value.Equals("tool_use", StringComparison.OrdinalIgnoreCase);
    }

    private enum Field { None, Choices, ToolCalls, Arguments, Id, Data, Type, CallItems }

    private struct Container
    {
        public bool Array;
        public bool Choices;
        public bool Models;
        public bool ToolCalls;
        public bool CallItems;
        public bool CallItem;
        public bool ToolArguments;
        public int Items;
        public long Start;
    }
}
