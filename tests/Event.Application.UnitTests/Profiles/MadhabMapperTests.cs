using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class MadhabMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "School of jurisprudence")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "School of jurisprudence")]
    public async Task Projection_PreservesSerializedContract(bool list, string? description)
    {
        var source = new Madhab { Id = 4, MasterCode = "MALIKI", FullName = "Maliki", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(MadhabMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(MadhabMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":4,"masterCode":"MALIKI","fullName":"Maliki","description":null}""")!;
        expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull() => await Assert.That(MadhabMapper.ToDetail(null)).IsNull();
}
