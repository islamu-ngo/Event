using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class LanguageMapperTests
{
    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(false, "Spoken language")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "Spoken language")]
    public async Task Projection_PreservesSerializedScalarContract(bool list, string? description)
    {
        var source = new Language { Id = 17, MasterCode = "ar", FullName = "Arabic", Description = description };
        var actual = list
            ? JsonSerializer.SerializeToNode(LanguageMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(LanguageMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":17,"masterCode":"ar","fullName":"Arabic","description":null}""")!;
        expected["description"] = description;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingLookup_RemainsNull()
    {
        await Assert.That(LanguageMapper.ToDetail(null)).IsNull();
    }

    [Test]
    public async Task ListProjection_PreservesOrderAndOwnsItsValues()
    {
        var first = new Language { Id = 23, MasterCode = "en", FullName = "English" };
        var second = new Language { Id = 17, MasterCode = "ar", FullName = "Arabic" };
        var mapped = new[] { first, second }.Select(LanguageMapper.ToListItem).ToList();
        first.FullName = "Changed";
        await Assert.That(mapped.Select(item => item.Id).SequenceEqual(new[] { 23, 17 })).IsTrue();
        await Assert.That(mapped[0].FullName).IsEqualTo("English");
    }
}
