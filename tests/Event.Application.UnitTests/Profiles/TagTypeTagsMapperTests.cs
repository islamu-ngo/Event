using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class TagTypeTagsMapperTests
{
    private static readonly Guid LinkId = Guid.Parse("01900000-0000-7000-8000-000000000130");
    private static readonly Guid TagId = Guid.Parse("01900000-0000-7000-8000-000000000131");
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000132");

    [Test]
    [Arguments(false, true, true)]
    [Arguments(false, true, false)]
    [Arguments(false, false, true)]
    [Arguments(false, false, false)]
    [Arguments(true, true, true)]
    [Arguments(true, true, false)]
    [Arguments(true, false, true)]
    [Arguments(true, false, false)]
    public async Task Projection_PreservesRelationshipIdsAndNullableLabels(bool list, bool tagLoaded, bool typeLoaded)
    {
        var source = Source();
        if (!tagLoaded)
            source.Tag = null!;
        if (!typeLoaded)
            source.TagType = null!;
        var expected = Expected(list);
        if (!tagLoaded)
        {
            expected["tagFullName"] = null;
            expected["tagMasterCode"] = null;
        }
        if (!typeLoaded)
        {
            expected["tagTypeFullName"] = null;
            expected["tagTypeMasterCode"] = null;
        }
        await AssertContract(source, list, expected);
    }

    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    public async Task Projection_PreservesNullAndEmptyLabels(bool list, string? value)
    {
        var source = Source();
        source.Tag.FullName = value!;
        source.Tag.MasterCode = value!;
        source.TagType.FullName = value!;
        source.TagType.MasterCode = value!;
        var expected = Expected(list);
        foreach (var field in new[] { "tagFullName", "tagMasterCode", "tagTypeFullName", "tagTypeMasterCode" })
            expected[field] = value;
        await AssertContract(source, list, expected);
    }

    [Test]
    public async Task MissingRelationship_RemainsNull() => await Assert.That(TagTypeTagsMapper.ToDetail(null)).IsNull();

    [Test]
    public async Task Create_RejectsBodyTenantAuthorityAndDoesNotImportNavigationOrIdentity()
    {
        var source = new CreateTagTypeTagsDto
        {
            TagId = TagId,
            TagTypeId = 19,
            TenantId = Guid.Parse("01900000-0000-7000-8000-000000000133")
        };
        var result = TagTypeTagsMapper.Create(source, TenantId);
        await Assert.That(result.TenantId).IsEqualTo(TenantId);
        await Assert.That(result.TagId).IsEqualTo(TagId);
        await Assert.That(result.TagTypeId).IsEqualTo(19);
        await Assert.That(result.Id).IsEqualTo(Guid.Empty);
        await Assert.That(result.Tag).IsNull();
        await Assert.That(result.TagType).IsNull();
        await Assert.That(result.Tenant).IsNull();
    }

    private static async Task AssertContract(TagTypeTags source, bool list, JsonNode expected)
    {
        var actual = list
            ? JsonSerializer.SerializeToNode(TagTypeTagsMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(TagTypeTagsMapper.ToDetail(source), JsonSerializerOptions.Web);
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    private static JsonNode Expected(bool list)
    {
        var expected = JsonNode.Parse("""
            {"id":"01900000-0000-7000-8000-000000000130","tagId":"01900000-0000-7000-8000-000000000131",
             "tagFullName":"Community","tagMasterCode":"COMMUNITY","tagTypeId":19,
             "tagTypeFullName":"Interest","tagTypeMasterCode":"INTEREST"}
            """)!;
        if (!list)
            expected["tenantId"] = "01900000-0000-7000-8000-000000000132";
        return expected;
    }

    private static TagTypeTags Source() => new()
    {
        Id = LinkId,
        TagId = TagId,
        Tag = new Tag { Id = TagId, MasterCode = "COMMUNITY", FullName = "Community", TenantId = TenantId, Tenant = null! },
        TagTypeId = 19,
        TagType = new TagType { Id = 19, MasterCode = "INTEREST", FullName = "Interest", Description = "Not surfaced" },
        TenantId = TenantId,
        Tenant = null!
    };
}
