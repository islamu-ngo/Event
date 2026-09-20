using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeCustomPropertyGovernanceHttpTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LocalPolicy_InstanceAdminViewParityPreservesOtherActionsAndResources(bool safeMode)
    {
        await using var factory = await NativeCustomPropertyGovernanceFactory.CreateAsync();
        Guid instanceId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var instance = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
            instanceId = instance.UserId;
            var role = await db.Set<Role>().SingleAsync(row => row.MasterCode == "platform.admin");
            db.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = instanceId,
                User = null!,
                RoleId = role.Id,
                Role = role
            });
            await db.SaveChangesAsync();
        }

        using var authorityScope = factory.Services.CreateScope();
        SetPrincipal(authorityScope, instanceId);
        var local = authorityScope.ServiceProvider.GetRequiredService<FallbackAuthorizationService>();
        if (safeMode)
            local.ActivateSafeMode();
        AuthorizationRequest[] checks =
        [
            Check(ResourceKinds.CustomPropertyGovernance, AuthorizationActions.View),
            Check(ResourceKinds.CustomPropertyGovernance, AuthorizationActions.Update),
            Check(ResourceKinds.Notification, AuthorizationActions.View),
            Check(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)
        ];
        await AssertDecisionsAsync(local, checks, [true, false, false, true]);
        await Assert.That(local.SafeMode).IsEqualTo(safeMode);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LocalPolicy_TenantAndMemberAuthoritiesRemainBoundedInSingleAndBatchChecks(bool safeMode)
    {
        await using var factory = await NativeCustomPropertyGovernanceFactory.CreateAsync();
        var data = await SeedAsync(factory);
        foreach (var userId in new[] { data.AdminId, data.MemberId, data.ForeignAdminId })
        {
            using var scope = factory.Services.CreateScope();
            SetPrincipal(scope, userId);
            var local = scope.ServiceProvider.GetRequiredService<FallbackAuthorizationService>();
            if (safeMode)
                local.ActivateSafeMode();
            AuthorizationRequest[] checks =
            [
                Check(ResourceKinds.CustomPropertyGovernance, AuthorizationActions.View),
                Check(ResourceKinds.CustomPropertyGovernance, AuthorizationActions.View, data.ForeignTenantId),
                Check(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)
            ];
            await AssertDecisionsAsync(local, checks, [userId == data.AdminId && !safeMode, false, false]);
            await Assert.That(local.SafeMode).IsEqualTo(safeMode);
        }
    }

    private static AuthorizationRequest Check(string resourceKind, string action, Guid? tenantId = null) =>
        new(AuthorizationCapabilityCatalog.Require(resourceKind, action), "governance-policy-test",
            Facts: new TenantScopedAuthorizationFacts(tenantId ?? PlatformDefaults.DefaultTenantId));

    private static async Task AssertDecisionsAsync(
        IAuthorizationProvider provider, AuthorizationRequest[] checks, bool[] expected)
    {
        var single = new List<bool>();
        foreach (var check in checks)
        {
            var decision = await provider.AuthorizeAsync(check);
            await Assert.That(decision.Provider).IsEqualTo(AuthorizationProviderMetadata.Local);
            single.Add(decision.IsAllowed);
        }
        await Assert.That(single).IsEquivalentTo(expected, CollectionOrdering.Matching);
        // More than two checks exercises the independently optimized local batch path.
        var batch = await provider.AuthorizeBatchAsync(checks);
        await Assert.That(batch.Select(decision => decision.IsAllowed)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(batch.All(decision => decision.Provider == AuthorizationProviderMetadata.Local)).IsTrue();
    }
}
