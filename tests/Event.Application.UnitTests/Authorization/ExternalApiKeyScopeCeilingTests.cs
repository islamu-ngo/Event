using Explore.Application.Features.ExternalApiKeys;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Authorization;

public class ExternalApiKeyScopeCeilingTests
{
    [Test]
    public async Task GetCeiling_ForUser_ContainsSelfServiceScopesAndExcludesAdminAndOrgGroup()
    {
        var ceiling = ExternalApiKeyScopeCeiling.GetCeiling(ExternalApiKeyOwnerType.User);

        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.EventsRead);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.EventsWrite);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.UsersRead);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.UsersWrite);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.LookupsRead);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.McpRead);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.McpPropose);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.RegistrationsWrite);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.ApiKeysManage);

        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.OrganizationsRead);
        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.OrganizationsWrite);
        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.GroupsRead);
        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.GroupsWrite);
        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.AdminTenant);
        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.AdminInstance);
    }

    [Test]
    public async Task GetCeiling_ForOrganization_IncludesUserScopesPlusOrgScopes()
    {
        var ceiling = ExternalApiKeyScopeCeiling.GetCeiling(ExternalApiKeyOwnerType.Organization);

        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.EventsRead);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.OrganizationsRead);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.OrganizationsWrite);

        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.GroupsRead);
        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.GroupsWrite);
        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.AdminTenant);
        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.AdminInstance);
    }

    [Test]
    public async Task GetCeiling_ForGroup_IncludesUserScopesPlusGroupScopes()
    {
        var ceiling = ExternalApiKeyScopeCeiling.GetCeiling(ExternalApiKeyOwnerType.Group);

        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.EventsRead);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.GroupsRead);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.GroupsWrite);

        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.OrganizationsRead);
        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.OrganizationsWrite);
        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.AdminTenant);
        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.AdminInstance);
    }

    [Test]
    public async Task GetCeiling_ForTenant_IncludesAllNonInstanceScopes()
    {
        var ceiling = ExternalApiKeyScopeCeiling.GetCeiling(ExternalApiKeyOwnerType.Tenant);

        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.AdminTenant);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.EventsWrite);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.OrganizationsWrite);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.GroupsWrite);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.UsersWrite);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.RegistrationsWrite);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.LookupsRead);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.McpRead);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.McpPropose);
        await Assert.That(ceiling).Contains(ExternalApiKeyScopes.ApiKeysManage);

        await Assert.That(ceiling).DoesNotContain(ExternalApiKeyScopes.AdminInstance);
    }

    [Test]
    public async Task GetCeiling_ForInstanceAdmin_ContainsAllScopes()
    {
        var ceiling = ExternalApiKeyScopeCeiling.GetCeiling(ExternalApiKeyOwnerType.InstanceAdmin);

        foreach (var scope in ExternalApiKeyScopes.All)
        {
            await Assert.That(ceiling).Contains(scope);
        }
    }

    [Test]
    public async Task AreWithinCeiling_ForUserWithOrgScope_ReturnsFalse()
    {
        bool result = ExternalApiKeyScopeCeiling.AreWithinCeiling(
            ExternalApiKeyOwnerType.User,
            [ExternalApiKeyScopes.EventsRead, ExternalApiKeyScopes.OrganizationsWrite]);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task AreWithinCeiling_ForUserWithOnlyUserScopes_ReturnsTrue()
    {
        bool result = ExternalApiKeyScopeCeiling.AreWithinCeiling(
            ExternalApiKeyOwnerType.User,
            [ExternalApiKeyScopes.EventsRead, ExternalApiKeyScopes.LookupsRead, ExternalApiKeyScopes.UsersRead]);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task AreWithinCeiling_ForOrgWithGroupScope_ReturnsFalse()
    {
        bool result = ExternalApiKeyScopeCeiling.AreWithinCeiling(
            ExternalApiKeyOwnerType.Organization,
            [ExternalApiKeyScopes.EventsRead, ExternalApiKeyScopes.GroupsRead]);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task AreWithinCeiling_ForGroupWithOrgScope_ReturnsFalse()
    {
        bool result = ExternalApiKeyScopeCeiling.AreWithinCeiling(
            ExternalApiKeyOwnerType.Group,
            [ExternalApiKeyScopes.EventsRead, ExternalApiKeyScopes.OrganizationsWrite]);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task AreWithinCeiling_ForTenantWithAdminInstance_ReturnsFalse()
    {
        bool result = ExternalApiKeyScopeCeiling.AreWithinCeiling(
            ExternalApiKeyOwnerType.Tenant,
            [ExternalApiKeyScopes.AdminTenant, ExternalApiKeyScopes.AdminInstance]);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task AreWithinCeiling_ForInstanceAdminWithAllScopes_ReturnsTrue()
    {
        bool result = ExternalApiKeyScopeCeiling.AreWithinCeiling(
            ExternalApiKeyOwnerType.InstanceAdmin,
            ExternalApiKeyScopes.All);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task GetExceeding_ForUserRequestingMixedScopes_ReturnsOnlyOutOfCeilingScopes()
    {
        var exceeding = ExternalApiKeyScopeCeiling.GetExceeding(
            ExternalApiKeyOwnerType.User,
            [
                ExternalApiKeyScopes.EventsRead,
                ExternalApiKeyScopes.OrganizationsWrite,
                ExternalApiKeyScopes.AdminTenant,
                ExternalApiKeyScopes.UsersRead
            ]);

        await Assert.That(exceeding).Contains(ExternalApiKeyScopes.OrganizationsWrite);
        await Assert.That(exceeding).Contains(ExternalApiKeyScopes.AdminTenant);
        await Assert.That(exceeding).DoesNotContain(ExternalApiKeyScopes.EventsRead);
        await Assert.That(exceeding).DoesNotContain(ExternalApiKeyScopes.UsersRead);
    }

    [Test]
    public async Task GetExceeding_ForTenantRequestingAdminInstance_ReturnsInstanceScopeOnly()
    {
        var exceeding = ExternalApiKeyScopeCeiling.GetExceeding(
            ExternalApiKeyOwnerType.Tenant,
            [ExternalApiKeyScopes.AdminInstance]);

        await Assert.That(exceeding.Count).IsEqualTo(1);
        await Assert.That(exceeding).Contains(ExternalApiKeyScopes.AdminInstance);
    }

    [Test]
    public async Task GetExceeding_ForInstanceAdminWithAnyScope_ReturnsEmpty()
    {
        var exceeding = ExternalApiKeyScopeCeiling.GetExceeding(
            ExternalApiKeyOwnerType.InstanceAdmin,
            ExternalApiKeyScopes.All);

        await Assert.That(exceeding.Count).IsEqualTo(0);
    }
}
