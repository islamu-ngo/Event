using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class PermissionMapperTests
{
    [Test]
    [Arguments(0, "PLATFORM", "Platform")]
    [Arguments(1, "TENANT", "Tenant")]
    [Arguments(2, "ORGANIZATION", "Organization")]
    [Arguments(3, "GROUP", "Group")]
    [Arguments(4, "EVENT", "Event")]
    [Arguments(-7, "UNKNOWN", "Unknown")]
    public async Task Projection_DisclosesOnlyListFieldsAndCanonicalScope(int scopeId, string code, string name)
    {
        var actual = JsonSerializer.SerializeToNode(PermissionMapper.ToListItem(Source(scopeId)), JsonSerializerOptions.Web);
        var expected = JsonNode.Parse("""{"id":59,"masterCode":"event:update","fullName":"Edit event","resourceKind":"event","action":"update","groupName":"Events"}""")!;
        expected["roleScopeId"] = scopeId;
        expected["roleScopeCode"] = code;
        expected["roleScopeName"] = name;
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    [Test]
    public async Task MissingNavigation_PreservesCanonicalMetadata()
    {
        var source = Source(4);
        source.RoleScope = null!;
        var result = PermissionMapper.ToListItem(source);
        await Assert.That(result.RoleScopeCode).IsEqualTo("EVENT");
        await Assert.That(result.RoleScopeName).IsEqualTo("Event");
        await Assert.That(result.RoleScopeId).IsEqualTo(4);
    }

    [Test]
    public async Task Projection_OwnsValuesWithoutMutatingPermissionPolicy()
    {
        var source = Source(1);
        var result = PermissionMapper.ToListItem(source);
        source.FullName = "Changed";
        await Assert.That(result.FullName).IsEqualTo("Edit event");
        await Assert.That(source.IsFiltered).IsTrue();
        await Assert.That(source.IsSystem).IsTrue();
        await Assert.That(source.IsActive).IsFalse();
    }

    private static Permission Source(int scopeId) => new()
    {
        Id = 59,
        MasterCode = "event:update",
        FullName = "Edit event",
        ResourceKind = "event",
        Action = "update",
        GroupName = "Events",
        Description = "Non-list description",
        FieldScope = "private-field-scope",
        RoleScopeId = scopeId,
        RoleScope = new RoleScope { Id = 99, MasterCode = "STALE", FullName = "Stale navigation" },
        IsSystem = true,
        IsFiltered = true,
        IsActive = false,
        CreatedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
        CreatedBy = Guid.Parse("01900000-0000-7000-8000-000000000071"),
        UpdatedAt = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
        UpdatedBy = Guid.Parse("01900000-0000-7000-8000-000000000072")
    };
}
