using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class ActorTypeMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "Organization actor")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "Organization actor")]
    public async Task Projection_PreservesSerializedContract(bool list, string? description)
    {
        var source = new ActorType { Id = 11, MasterCode = "ORGANIZATION", FullName = "Organization", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(ActorTypeMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(ActorTypeMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":11,"masterCode":"ORGANIZATION","fullName":"Organization","description":null}""")!;
        expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull() => await Assert.That(ActorTypeMapper.ToDetail(null)).IsNull();
}
