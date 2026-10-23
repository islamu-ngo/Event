using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;

namespace Explore.Infrastructure.Tests.Behaviors;

public sealed class HumanPrincipalSourceSerializationTests
{
    [Test]
    public async Task HeldSourcesPreserveGlobalMapsAndOrderedPermissionMultiplicity()
    {
        Guid actor = Guid.NewGuid(), tenant = Guid.NewGuid(), membership = Guid.NewGuid();
        Guid firstOrganization = Guid.NewGuid(), secondOrganization = Guid.NewGuid();
        var facts = new HumanPrincipalSourceFacts(
            actor, Guid.NewGuid(), [new(actor, false)], [],
            [new(Guid.NewGuid(), 901)],
            [new(901, (int)RoleScopeEnum.Platform, "platform.admin", true)],
            [new(membership, tenant, (int)TenantUserStatusEnum.Active, false)],
            [new(Guid.NewGuid(), tenant, membership, (int)RoleEnum.TenantAdmin, 0, null)],
            [new(tenant, (int)TenantStatusEnum.Suspended, false)],
            [new(Guid.NewGuid(), tenant, Guid.NewGuid(), firstOrganization, (int)RoleEnum.OrgAdmin,
                false, false, false, 0)], [],
            [], true, [secondOrganization, firstOrganization, secondOrganization], [], [], [], null);
        var principal = CerbosPrincipalBuilder.BuildPrincipalFromSources(facts);
        var serialized = CerbosPrincipalBuilder.CapturePrincipal(principal);
        await Assert.That(serialized.Attr["isInstanceAdmin"].BoolValue).IsTrue();
        await Assert.That(serialized.Attr["tenantMemberships"].StructValue.Fields.ContainsKey(tenant.ToString())).IsTrue();
        await Assert.That(serialized.Attr["orgMemberships"].StructValue.Fields.ContainsKey(firstOrganization.ToString())).IsTrue();
        var sequence = serialized.Attr["eventCreateOrganizations"].ListValue.Values
            .Select(value => value.StringValue).ToArray();
        await Assert.That(sequence.SequenceEqual(new[]
            { secondOrganization.ToString(), firstOrganization.ToString(), secondOrganization.ToString() })).IsTrue();
        await Assert.That(serialized.Attr.ContainsKey("nowUtc")).IsFalse();
        principal.ToPrincipal().Attr["isInstanceAdmin"].BoolValue = false;
        await Assert.That(serialized.Attr["isInstanceAdmin"].BoolValue).IsTrue();
        await Assert.That(CerbosPrincipalBuilder.CapturePrincipal(principal).Attr["isInstanceAdmin"].BoolValue).IsFalse();
    }
}
