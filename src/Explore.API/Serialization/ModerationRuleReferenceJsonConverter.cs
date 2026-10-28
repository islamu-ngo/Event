using System.Text.Json;
using System.Text.Json.Serialization;
using ISLAMU.Moderation.Domain;

namespace Explore.API.Serialization;

/// <summary>Restores immutable rule intent through its qualified owner/code/revision factory, not an approval assertion.</summary>
public sealed class ModerationRuleReferenceJsonConverter : JsonConverter<ModerationRuleReference>
{
    public override bool HandleNull => true;

    public override ModerationRuleReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("A moderation rule requires an immutable meaning reference.");
        Guid? ownerId = null;
        string? code = null;
        long? revision = null;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            switch (property.Name)
            {
                case "ownerId" when ownerId is null:
                    ownerId = property.Value.Deserialize<Guid>(options);
                    break;
                case "code" when code is null:
                    code = property.Value.Deserialize<string>(options)
                        ?? throw new JsonException("A moderation rule requires its stable code.");
                    break;
                case "revision" when revision is null:
                    revision = property.Value.Deserialize<long>(options);
                    break;
                default:
                    throw new JsonException("Moderation rule members must be known and unique.");
            }
        }
        if (ownerId is null || code is null || revision is null)
            throw new JsonException("A moderation rule requires its owner, code and revision.");
        try
        {
            return ModerationRuleReference.Create(ownerId.Value, code, revision.Value);
        }
        catch (ArgumentException)
        {
            throw new JsonException("The immutable moderation rule reference is invalid.");
        }
    }

    public override void Write(Utf8JsonWriter writer, ModerationRuleReference value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString("ownerId", value.OwnerId);
        writer.WriteString("code", value.Code);
        writer.WriteNumber("revision", value.Revision);
        writer.WriteEndObject();
    }
}
