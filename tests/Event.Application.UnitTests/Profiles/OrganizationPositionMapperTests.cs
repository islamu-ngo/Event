using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class OrganizationPositionMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "Organization member")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "Organization member")]
    public async Task Projection_PreservesSerializedContract(bool list, string? description)
    {
        var source = new OrganizationPosition { Id = 31, MasterCode = "MEMBER", FullName = "Member", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(OrganizationPositionMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(OrganizationPositionMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":31,"masterCode":"MEMBER","fullName":"Member","description":null}""")!;
        expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull() => await Assert.That(OrganizationPositionMapper.ToDetail(null)).IsNull();
}
