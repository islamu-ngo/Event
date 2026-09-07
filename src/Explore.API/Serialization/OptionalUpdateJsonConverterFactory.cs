using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Models.Common;

namespace Explore.API.Serialization;

public sealed class OptionalUpdateJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType
            && typeToConvert.GetGenericTypeDefinition() == typeof(OptionalUpdate<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OptionalUpdateJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

public sealed class OptionalUpdateJsonConverter<T> : JsonConverter<OptionalUpdate<T>>
{
    public override bool HandleNull => true;

    public override OptionalUpdate<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return OptionalUpdate<T>.Unspecified();
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("OptionalUpdate<T> must be a JSON object or null.");
        }

        var hasValue = false;
        var hasValueProperty = false;
        var value = default(T);
        var valueProperty = false;

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "hasValue":
                    if (hasValueProperty || (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False))
                    {
                        throw new JsonException("OptionalUpdate<T>.hasValue must be a boolean.");
                    }

                    hasValueProperty = true;
                    hasValue = property.Value.GetBoolean();
                    break;

                case "value":
                    if (valueProperty)
                    {
                        throw new JsonException("OptionalUpdate<T>.value cannot be specified more than once.");
                    }

                    valueProperty = true;
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        if (typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) is null)
                        {
                            throw new JsonException("OptionalUpdate<T>.value cannot be null for a non-nullable value type.");
                        }

                        value = default;
                    }
                    else
                    {
                        value = property.Value.Deserialize<T>(options);
                    }
                    break;

                default:
                    throw new JsonException($"Unknown OptionalUpdate<T> property '{property.Name}'.");
            }
        }

        if (hasValue && !valueProperty)
        {
            throw new JsonException("OptionalUpdate<T>.value is required when hasValue is true.");
        }

        return new OptionalUpdate<T>(hasValue, value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        OptionalUpdate<T> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("hasValue", value.HasValue);
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value.Value, options);
        writer.WriteEndObject();
    }
}
