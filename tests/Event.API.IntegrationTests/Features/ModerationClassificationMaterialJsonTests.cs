using System.Text.Json;
using Explore.API.Serialization;
using Explore.Domain;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationClassificationMaterialJsonTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ModerationClassificationMaterialJsonConverter() }
    };

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task ExactNativeGuidCoordinatesRoundTripWithoutComputedParent(int depth)
    {
        var material = Material(depth);
        var json = JsonSerializer.Serialize(material, Options);
        await Assert.That(JsonSerializer.Deserialize<ModerationClassificationMaterial>(json, Options)).IsEqualTo(material);
        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.EnumerateObject().Select(property => property.Name)
            .Order(StringComparer.Ordinal).SequenceEqual(
                new[] { "eventId", "eventStamp", "sessionId", "sessionStamp", "resourceId", "resourceStamp" }
                    .Order(StringComparer.Ordinal))).IsTrue();
    }

    [Test]
    [Arguments("parent")]
    [Arguments("isApproved")]
    [Arguments("dateOfBirth")]
    [Arguments("$type")]
    public async Task DerivedOrClaimedAuthorityMembersCannotBind(string member)
    {
        var document = JsonSerializer.SerializeToNode(Material(2), Options)!.AsObject();
        document[member] = "caller assertion";
        await Assert.That(() => JsonSerializer.Deserialize<ModerationClassificationMaterial>(document.ToJsonString(), Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments("eventId")]
    [Arguments("eventStamp")]
    [Arguments("sessionId")]
    [Arguments("sessionStamp")]
    [Arguments("resourceId")]
    [Arguments("resourceStamp")]
    public async Task DuplicateNativeCoordinatesCannotBind(string member)
    {
        var json = JsonSerializer.Serialize(Material(2), Options);
        var value = member is "eventId" or "eventStamp" ? $"\"{Guid.NewGuid():D}\"" : "null";
        var duplicate = json[..^1] + $",\"{member}\":{value}}}";
        await Assert.That(() => JsonSerializer.Deserialize<ModerationClassificationMaterial>(duplicate, Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments("eventId")]
    [Arguments("eventStamp")]
    public async Task MissingOrEmptyEventCoordinatesCannotBind(string member)
    {
        var document = JsonSerializer.SerializeToNode(Material(0), Options)!.AsObject();
        document.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize<ModerationClassificationMaterial>(document.ToJsonString(), Options))
            .Throws<JsonException>();
        document[member] = Guid.Empty;
        await Assert.That(() => JsonSerializer.Deserialize<ModerationClassificationMaterial>(document.ToJsonString(), Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments("sessionId")]
    [Arguments("sessionStamp")]
    [Arguments("resourceId")]
    [Arguments("resourceStamp")]
    public async Task HalfNativeAncestryPairsCannotBind(string member)
    {
        var document = JsonSerializer.SerializeToNode(Material(2), Options)!.AsObject();
        document[member] = null;
        await Assert.That(() => JsonSerializer.Deserialize<ModerationClassificationMaterial>(document.ToJsonString(), Options))
            .Throws<JsonException>();
    }

    [Test]
    public async Task NullMaterialCannotBecomeAnInventedRoot()
    {
        await Assert.That(() => JsonSerializer.Deserialize<ModerationClassificationMaterial>("null", Options))
            .Throws<JsonException>();
    }

    private static ModerationClassificationMaterial Material(int depth) =>
        new(Guid.NewGuid(), Guid.NewGuid(), depth > 0 ? Guid.NewGuid() : null, depth > 0 ? Guid.NewGuid() : null,
            depth > 1 ? Guid.NewGuid() : null, depth > 1 ? Guid.NewGuid() : null);
}
