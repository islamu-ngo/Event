using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class EventFormatMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "Online and in person")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "Online and in person")]
    public async Task Projection_PreservesSerializedContract(bool list, string? description)
    {
        var source = new EventFormat { Id = 13, MasterCode = "HYBRID", FullName = "Hybrid", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(EventFormatMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(EventFormatMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":13,"masterCode":"HYBRID","fullName":"Hybrid","description":null}""")!;
        expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull() => await Assert.That(EventFormatMapper.ToDetail(null)).IsNull();
}
