using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Domain;

namespace Explore.API.Serialization;

/// <summary>Accepts six native version coordinates, never computed ancestry or caller approval.</summary>
public sealed class ModerationClassificationMaterialJsonConverter : JsonConverter<ModerationClassificationMaterial>
{
    public override bool HandleNull => true;

    public override ModerationClassificationMaterial Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Material intent requires explicit native coordinates.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Guid? eventId = null, eventStamp = null, sessionId = null, sessionStamp = null, resourceId = null, resourceStamp = null;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new JsonException("Material coordinates must be unique.");
            switch (property.Name)
            {
                case "eventId":
                    eventId = property.Value.Deserialize<Guid>(options);
                    break;
                case "eventStamp":
                    eventStamp = property.Value.Deserialize<Guid>(options);
                    break;
                case "sessionId":
                    sessionId = property.Value.Deserialize<Guid?>(options);
                    break;
                case "sessionStamp":
                    sessionStamp = property.Value.Deserialize<Guid?>(options);
                    break;
                case "resourceId":
                    resourceId = property.Value.Deserialize<Guid?>(options);
                    break;
                case "resourceStamp":
                    resourceStamp = property.Value.Deserialize<Guid?>(options);
                    break;
                default:
                    throw new JsonException("Material intent contains an unknown member.");
            }
        }
        if (eventId is null || eventStamp is null)
            throw new JsonException("Material intent requires its native Event identity and GUID stamp.");
        var material = new ModerationClassificationMaterial(eventId.Value, eventStamp.Value,
            sessionId, sessionStamp, resourceId, resourceStamp);
        try
        {
            material.Validate();
            return material;
        }
        catch (ArgumentException)
        {
            throw new JsonException("The native material ancestry coordinates are invalid.");
        }
    }

    public override void Write(
        Utf8JsonWriter writer, ModerationClassificationMaterial value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString("eventId", value.EventId);
        writer.WriteString("eventStamp", value.EventStamp);
        writer.WritePropertyName("sessionId");
        JsonSerializer.Serialize(writer, value.SessionId, options);
        writer.WritePropertyName("sessionStamp");
        JsonSerializer.Serialize(writer, value.SessionStamp, options);
        writer.WritePropertyName("resourceId");
        JsonSerializer.Serialize(writer, value.ResourceId, options);
        writer.WritePropertyName("resourceStamp");
        JsonSerializer.Serialize(writer, value.ResourceStamp, options);
        writer.WriteEndObject();
    }
}
