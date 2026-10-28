using System.Text.Json;
using System.Text.Json.Serialization;
using ISLAMU.Moderation.Domain;

namespace Explore.API.Serialization;

/// <summary>Binds a canonical subject intent without accepting caller identity, roles or linkage evidence.</summary>
public sealed class ModerationSubjectReferenceJsonConverter : JsonConverter<SubjectReference>
{
    public override bool HandleNull => true;

    public override SubjectReference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("A moderation subject requires a canonical subject object.");
        ModerationSubjectKind? kind = null;
        Guid? id = null;
        AuthorityScope? origin = null;
        var originSeen = false;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            switch (property.Name)
            {
                case "kind" when kind is null:
                    kind = property.Value.Deserialize<ModerationSubjectKind>(options);
                    break;
                case "id" when id is null:
                    id = property.Value.Deserialize<Guid>(options);
                    break;
                case "origin" when !originSeen:
                    originSeen = true;
                    origin = property.Value.Deserialize<AuthorityScope>(options);
                    break;
                default:
                    throw new JsonException("Moderation subject members must be known and unique.");
            }
        }
        if (kind is null || id is null || origin is null)
            throw new JsonException("A moderation subject requires its kind, identifier and origin.");
        try
        {
            return SubjectReference.Create(kind.Value, id.Value, origin);
        }
        catch (ArgumentException)
        {
            throw new JsonException("The canonical moderation subject is invalid.");
        }
    }

    public override void Write(Utf8JsonWriter writer, SubjectReference value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WritePropertyName("kind");
        JsonSerializer.Serialize(writer, value.Kind, options);
        writer.WriteString("id", value.Id);
        writer.WritePropertyName("origin");
        JsonSerializer.Serialize(writer, value.Origin, options);
        writer.WriteEndObject();
    }
}
