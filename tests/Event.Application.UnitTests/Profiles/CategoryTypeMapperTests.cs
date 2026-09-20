using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class CategoryTypeMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "Classification details")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "Classification details")]
    public async Task Projection_PreservesDetailAndDescriptionFreeList(bool list, string? description)
    {
        var source = new CategoryType { Id = 43, MasterCode = "TOPIC", FullName = "Topic", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(CategoryTypeMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(CategoryTypeMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":43,"masterCode":"TOPIC","fullName":"Topic"}""")!;
        if (!list)
            expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull() => await Assert.That(CategoryTypeMapper.ToDetail(null)).IsNull();
}
