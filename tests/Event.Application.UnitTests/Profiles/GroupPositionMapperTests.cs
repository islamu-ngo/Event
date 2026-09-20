using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class GroupPositionMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "Group coordinator")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "Group coordinator")]
    public async Task Projection_PreservesSerializedContract(bool list, string? description)
    {
        var source = new GroupPosition { Id = 41, MasterCode = "COORDINATOR", FullName = "Coordinator", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(GroupPositionMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(GroupPositionMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":41,"masterCode":"COORDINATOR","fullName":"Coordinator","description":null}""")!;
        expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull() => await Assert.That(GroupPositionMapper.ToDetail(null)).IsNull();
}
