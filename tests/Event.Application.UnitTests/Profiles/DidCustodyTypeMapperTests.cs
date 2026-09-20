using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class DidCustodyTypeMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "Actor-owned identity")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "Actor-owned identity")]
    public async Task Projection_PreservesSerializedContract(bool list, string? description)
    {
        var source = new DidCustodyType { Id = 23, MasterCode = "EXTERNAL", FullName = "External", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(DidCustodyTypeMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(DidCustodyTypeMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":23,"masterCode":"EXTERNAL","fullName":"External","description":null}""")!;
        expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull() => await Assert.That(DidCustodyTypeMapper.ToDetail(null)).IsNull();
}
