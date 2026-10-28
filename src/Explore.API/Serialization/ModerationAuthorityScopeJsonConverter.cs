using System.Text.Json;
using System.Text.Json.Serialization;
using ISLAMU.Moderation.Domain;

namespace Explore.API.Serialization;

/// <summary>Reconstructs jurisdiction intent through the domain ancestry factories; JSON never supplies authority.</summary>
public sealed class ModerationAuthorityScopeJsonConverter : JsonConverter<AuthorityScope>
{
    public override bool HandleNull => true;

    public override AuthorityScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("A moderation scope requires a jurisdiction object.");
        ModerationScopeKind? kind = null;
        Guid? id = null;
        AuthorityScope? parent = null;
        var parentSeen = false;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            switch (property.Name)
            {
                case "kind" when kind is null:
                    kind = property.Value.Deserialize<ModerationScopeKind>(options);
                    break;
                case "id" when id is null:
                    id = property.Value.Deserialize<Guid>(options);
                    break;
                case "parent" when !parentSeen:
                    parentSeen = true;
                    parent = property.Value.ValueKind == JsonValueKind.Null
                        ? null : property.Value.Deserialize<AuthorityScope>(options);
                    break;
                default:
                    throw new JsonException("Moderation scope members must be known and unique.");
            }
        }
        if (kind is null || id is null)
            throw new JsonException("A moderation scope requires its kind and identifier.");
        try
        {
            if (kind == ModerationScopeKind.TrustDomain)
            {
                if (parent is not null)
                    throw new JsonException("A trust domain cannot have a parent.");
                return AuthorityScope.TrustDomain(id.Value);
            }
            var ancestor = parent ?? throw new JsonException("A child jurisdiction requires its complete ancestry.");
            return kind switch
            {
                ModerationScopeKind.Solution => AuthorityScope.Solution(ancestor, id.Value),
                ModerationScopeKind.Instance => AuthorityScope.Instance(ancestor, id.Value),
                ModerationScopeKind.Tenant => AuthorityScope.Tenant(ancestor, id.Value),
                ModerationScopeKind.Community => AuthorityScope.Community(ancestor, id.Value),
                ModerationScopeKind.Resource => AuthorityScope.Resource(ancestor, id.Value),
                ModerationScopeKind.Event => AuthorityScope.Event(ancestor, id.Value),
                _ => throw new JsonException("A moderation scope requires a known jurisdiction kind.")
            };
        }
        catch (ArgumentException)
        {
            throw new JsonException("The moderation jurisdiction ancestry is invalid.");
        }
    }

    public override void Write(Utf8JsonWriter writer, AuthorityScope value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WritePropertyName("kind");
        JsonSerializer.Serialize(writer, value.Kind, options);
        writer.WriteString("id", value.Id);
        if (value.Parent is { } parent)
        {
            writer.WritePropertyName("parent");
            JsonSerializer.Serialize(writer, parent, options);
        }
        writer.WriteEndObject();
    }
}
