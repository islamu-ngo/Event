using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class EventStatusMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "Public event")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "Public event")]
    public async Task Projection_PreservesSerializedContract(bool list, string? description)
    {
        var source = new EventStatus { Id = 37, MasterCode = "PUBLISHED", FullName = "Published", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(EventStatusMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(EventStatusMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":37,"masterCode":"PUBLISHED","fullName":"Published","description":null}""")!;
        expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull() => await Assert.That(EventStatusMapper.ToDetail(null)).IsNull();
}
