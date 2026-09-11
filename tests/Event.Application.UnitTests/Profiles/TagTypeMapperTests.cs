using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class TagTypeMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "Tag grouping details")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "Tag grouping details")]
    public async Task Projection_PreservesDetailAndDescriptionFreeList(bool list, string? description)
    {
        var source = new TagType { Id = 47, MasterCode = "INTEREST", FullName = "Interest", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(TagTypeMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(TagTypeMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":47,"masterCode":"INTEREST","fullName":"Interest"}""")!;
        if (!list)
            expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull() => await Assert.That(TagTypeMapper.ToDetail(null)).IsNull();
}
