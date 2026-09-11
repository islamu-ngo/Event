using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class FileTypeMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "Image file")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "Image file")]
    public async Task Projection_PreservesSerializedContract(bool list, string? description)
    {
        var source = new FileType { Id = 61, MasterCode = "IMAGE", FullName = "Image", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(FileTypeMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(FileTypeMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":61,"masterCode":"IMAGE","fullName":"Image","description":null}""")!;
        expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull() => await Assert.That(FileTypeMapper.ToDetail(null)).IsNull();
}
