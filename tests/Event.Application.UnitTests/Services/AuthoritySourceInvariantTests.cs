using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Tenants;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Services;

/// <summary>Current source facts must still match the original human before completion.</summary>
public sealed class AuthoritySourceInvariantTests
{
    [Test]
    public async Task DeletedHumanCannotCompleteUnderAnEarlierSourceImage()
    {
        var original = Facts();
        var revoked = original with { Users = [new(original.PrincipalId, true)] };

        await Assert.That(() => HumanTenantCreationPrincipalProjection.RequireUnchanged(original, revoked))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task RevokedTenantGrantCannotBeHiddenByTheUnchangedMembership()
    {
        var original = Facts();
        var revoked = original with
        {
            TenantGrants = [original.TenantGrants.Single() with { RevokedAt = DateTime.UnixEpoch.AddDays(1) }]
        };

        await Assert.That(() => HumanTenantCreationPrincipalProjection.RequireUnchanged(original, revoked))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task AnotherTenantCannotReuseTheSameHumanAndRoleFacts()
    {
        var original = Facts();
        var foreign = original with { SelectedTenantId = Guid.CreateVersion7() };

        await Assert.That(() => HumanTenantCreationPrincipalProjection.RequireUnchanged(original, foreign))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task RemovedPermissionCannotBeReplacedByAnUnrelatedActivePermission()
    {
        var original = Facts();
        var changed = original with
        {
            Permissions = [new((int)RoleEnum.TenantAdmin, 902, "event.read", true, false, false)]
        };

        await Assert.That(() => HumanTenantCreationPrincipalProjection.RequireUnchanged(original, changed))
            .Throws<UnauthorizedAccessException>();
    }

    private static HumanPrincipalSourceFacts Facts()
    {
        var human = Guid.CreateVersion7();
        var tenant = Guid.CreateVersion7();
        var membership = Guid.CreateVersion7();
        return new(human, tenant, [new(human, false)], [], [],
            [new((int)RoleEnum.TenantAdmin, (int)RoleScopeEnum.Tenant, "tenant.admin", false)],
            [new(membership, tenant, (int)TenantUserStatusEnum.Active, false)],
            [new(Guid.CreateVersion7(), tenant, membership, (int)RoleEnum.TenantAdmin, (int)RoleScopeEnum.Tenant, null)],
            [new(tenant, (int)TenantStatusEnum.Active, true)], [], [],
            [new((int)RoleEnum.TenantAdmin, 901, "event.create", true, true, false)],
            true, [], [], [], [], null);
    }
}
