using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.DTOs.Tag;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class TagMapperTests
{
    private static readonly Guid TagId = Guid.Parse("01900000-0000-7000-8000-000000000101");
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000102");

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Projection_PreservesCodeNameAndListTenantOmission(bool list, bool empty)
    {
        var source = Source();
        if (empty)
        {
            source.MasterCode = "";
            source.FullName = "";
        }
        var expected = JsonNode.Parse("""{"id":"01900000-0000-7000-8000-000000000101","masterCode":"COMMUNITY","fullName":"Community"}""")!;
        if (empty)
        {
            expected["masterCode"] = "";
            expected["fullName"] = "";
        }
        if (!list)
            expected["tenantId"] = "01900000-0000-7000-8000-000000000102";
        var actual = list
            ? JsonSerializer.SerializeToNode(TagMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(TagMapper.ToDetail(source), JsonSerializerOptions.Web);
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingTag_RemainsNull() => await Assert.That(TagMapper.ToDetail(null)).IsNull();

    [Test]
    public async Task Create_UsesTrustedTenantWithoutImportingIdentityOrNavigation()
    {
        var result = TagMapper.Create(new CreateTagDto { MasterCode = "NEW", FullName = "New tag" }, TenantId);
        await Assert.That(result.TenantId).IsEqualTo(TenantId);
        await Assert.That(result.MasterCode).IsEqualTo("NEW");
        await Assert.That(result.FullName).IsEqualTo("New tag");
        await Assert.That(result.Id).IsEqualTo(Guid.Empty);
        await Assert.That(result.Tenant).IsNull();
    }

    [Test]
    public async Task ListProjection_PreservesOrderAndOwnsItsValues()
    {
        var first = Source();
        var second = Source();
        second.Id = Guid.Parse("01900000-0000-7000-8000-000000000103");
        var result = new[] { second, first }.Select(TagMapper.ToListItem).ToList();
        first.FullName = "Changed";
        await Assert.That(result[0].Id).IsEqualTo(Guid.Parse("01900000-0000-7000-8000-000000000103"));
        await Assert.That(result[1].Id).IsEqualTo(TagId);
        await Assert.That(result[1].FullName).IsEqualTo("Community");
    }

    private static Tag Source() => new()
    {
        Id = TagId,
        TenantId = TenantId,
        Tenant = null!,
        MasterCode = "COMMUNITY",
        FullName = "Community"
    };
}
