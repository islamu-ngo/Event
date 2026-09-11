using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class EventSessionStatusMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "Confirmed session")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "Confirmed session")]
    public async Task Projection_PreservesSerializedContract(bool list, string? description)
    {
        var source = new EventSessionStatus { Id = 29, MasterCode = "SCHEDULED", FullName = "Scheduled", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(EventSessionStatusMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(EventSessionStatusMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":29,"masterCode":"SCHEDULED","fullName":"Scheduled","description":null}""")!;
        expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull() => await Assert.That(EventSessionStatusMapper.ToDetail(null)).IsNull();
}
