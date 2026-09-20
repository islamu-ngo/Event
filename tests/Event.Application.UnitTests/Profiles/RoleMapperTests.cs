using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class RoleMapperTests
{
    [Test]
    [Arguments(false, 0, "PLATFORM", "Platform")]
    [Arguments(false, 1, "TENANT", "Tenant")]
    [Arguments(false, 2, "ORGANIZATION", "Organization")]
    [Arguments(false, 3, "GROUP", "Group")]
    [Arguments(false, 4, "EVENT", "Event")]
    [Arguments(false, -7, "UNKNOWN", "Unknown")]
    [Arguments(true, 0, "PLATFORM", "Platform")]
    [Arguments(true, 1, "TENANT", "Tenant")]
    [Arguments(true, 2, "ORGANIZATION", "Organization")]
    [Arguments(true, 3, "GROUP", "Group")]
    [Arguments(true, 4, "EVENT", "Event")]
    [Arguments(true, -7, "UNKNOWN", "Unknown")]
    public async Task Projection_UsesPersistedScopeIdRatherThanNavigationLabels(bool list, int scopeId, string code, string name)
    {
        var source = Source(scopeId);
        var actual = list
            ? JsonSerializer.SerializeToNode(RoleMapper.ToListItem(source), JsonSerializerOptions.Web)
            : JsonSerializer.SerializeToNode(RoleMapper.ToDetail(source), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":53,"masterCode":"MODERATOR","fullName":"Moderator","isSystem":true}""")!;
        expected["roleScopeId"] = scopeId;
        expected["roleScopeCode"] = code;
        expected["roleScopeName"] = name;
        if (!list)
            expected["description"] = "Role description";
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingNavigation_DoesNotEraseCanonicalMetadata()
    {
        var source = Source(3);
        source.RoleScope = null!;
        await Assert.That(RoleMapper.ToDetail(source)?.RoleScopeName).IsEqualTo("Group");
        await Assert.That(RoleMapper.ToListItem(source).RoleScopeCode).IsEqualTo("GROUP");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task Detail_PreservesNullableDescription(string? description)
    {
        var source = Source(1);
        source.Description = description;
        var result = RoleMapper.ToDetail(source);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Description).IsEqualTo(description);
    }

    [Test]
    public async Task MissingRole_RemainsNull() => await Assert.That(RoleMapper.ToDetail(null)).IsNull();

    private static Role Source(int scopeId) => new()
    {
        Id = 53,
        MasterCode = "MODERATOR",
        FullName = "Moderator",
        Description = "Role description",
        RoleScopeId = scopeId,
        RoleScope = new RoleScope { Id = 99, MasterCode = "STALE", FullName = "Stale navigation" },
        IsSystem = true
    };
}
