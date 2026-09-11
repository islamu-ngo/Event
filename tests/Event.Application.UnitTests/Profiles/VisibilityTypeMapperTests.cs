using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class VisibilityTypeMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "Restricted visibility")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "Restricted visibility")]
    public async Task Projection_PreservesSerializedContract(bool list, string? description)
    {
        var source = new VisibilityType { Id = 19, MasterCode = "PRIVATE", FullName = "Private", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(VisibilityTypeMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(VisibilityTypeMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":19,"masterCode":"PRIVATE","fullName":"Private","description":null}""")!;
        expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull() => await Assert.That(VisibilityTypeMapper.ToDetail(null)).IsNull();
}
