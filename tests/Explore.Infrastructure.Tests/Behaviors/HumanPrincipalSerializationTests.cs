using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Application.Authorization;
using Explore.Infrastructure.Tests.Authorization;

namespace Explore.Infrastructure.Tests.Behaviors;

public sealed class HumanPrincipalSerializationTests
{
    [Test]
    public async Task LocalTenantDeleteRemainsDeniedDespiteInstanceAndTenantAdministratorSources()
    {
        Guid actor = Guid.NewGuid(), tenant = Guid.NewGuid(), membership = Guid.NewGuid();
        var facts = new HumanPrincipalSourceFacts(actor, tenant, [new(actor, false)], [],
            [new(Guid.NewGuid(), 901)], [new(901, (int)RoleScopeEnum.Platform, "platform.admin", true)],
            [new(membership, tenant, (int)TenantUserStatusEnum.Active, false)],
            [new(Guid.NewGuid(), tenant, membership, (int)RoleEnum.TenantAdmin, 0, null)],
            [new(tenant, (int)TenantStatusEnum.Suspended, false)], [], [], [], false, [], [], [], [], null);
        var deletion = TestAuthorizationRequest.Create(ResourceKinds.Tenant, tenant.ToString(),
            AuthorizationActions.Delete, null, null, new TenantScopedAuthorizationFacts(tenant));
        var update = TestAuthorizationRequest.Create(ResourceKinds.Tenant, tenant.ToString(),
            AuthorizationActions.Update, null, null, new TenantScopedAuthorizationFacts(tenant));
        await Assert.That(FallbackAuthorizationService.EvaluatePreparedTenant(facts, deletion, tenant, false)).IsFalse();
        await Assert.That(FallbackAuthorizationService.EvaluatePreparedTenant(facts, update, tenant, false)).IsTrue();
        var tenantOnly = facts with { PlatformGrants = [] };
        await Assert.That(FallbackAuthorizationService.EvaluatePreparedTenant(tenantOnly, update, tenant, false)).IsTrue();
        await Assert.That(FallbackAuthorizationService.EvaluatePreparedTenant(tenantOnly, update, Guid.NewGuid(), false)).IsFalse();
        var revoked = tenantOnly with
        {
            TenantGrants = [tenantOnly.TenantGrants[0] with { RevokedAt = DateTime.UtcNow }]
        };
        await Assert.That(FallbackAuthorizationService.EvaluatePreparedTenant(revoked, update, tenant, false)).IsFalse();
    }

}
