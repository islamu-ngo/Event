using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class AgentBrowserPersonaStartupTests
{
    [After(Class)]
    public static async Task DisposeDatabase() => await AgentBrowserPersonaFixture.DisposeDatabaseAsync();

    [Test]
    [Arguments("marker")]
    [Arguments("receipt")]
    [Arguments("graph")]
    [Arguments("activation")]
    [Arguments("replacement")]
    public async Task InterruptedPersistedBoundariesResumeOnlyTheRecordedLifecycle(string boundary)
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        fixture.InterruptAt(boundary);
        if (boundary == "replacement") await fixture.RunAsync();
        else await Assert.That(() => fixture.RunAsync()).Throws<AgentBrowserPersonaFixture.InjectedBoundaryFailure>();
        await Assert.That(fixture.InterruptionObserved).IsTrue();
        await fixture.RunAsync();
        await fixture.AssertReadyAsync();
    }

    [Test]
    public async Task NativeProvisioningCompletesSixExactBindingsAndReplayDoesNotRegrantOrReset()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        await fixture.AssertReadyAsync();
        await fixture.ChangePasswordProfileAndRevokeGrantAsync();
        fixture.RemoveInitializationSecrets();
        await fixture.RunAsync();
        await fixture.AssertReadyAsync();
        await fixture.AssertChangesPreservedAsync();
    }

    [Test]
    public async Task FreshFoundationEnablesTenantHostsWithoutResettingOperatorChangesOnReplay()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        await using (var database = fixture.CreateDatabase())
        {
            var baseDomain = await database.Set<SystemSetting>().SingleAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Domains.InstanceBaseDomain);
            var subdomain = await database.Set<SystemSetting>().SingleAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Routing.ResolverSubdomainEnabled);
            var positiveHost = await database.TenantSettingOverrides.SingleAsync(row =>
                row.TenantId == AgentBrowserPersonaCatalog.TenantId
                && row.SettingKey == GovernanceSettingKeys.Domains.TenantSubdomain);
            var negativeHost = await database.TenantSettingOverrides.SingleAsync(row =>
                row.TenantId == AgentBrowserPersonaCatalog.NegativeTenantId
                && row.SettingKey == GovernanceSettingKeys.Domains.TenantSubdomain);
            await Assert.That(baseDomain.Value).IsEqualTo("\"localhost\"");
            await Assert.That(subdomain.Value).IsEqualTo("true");
            await Assert.That(positiveHost.Value).IsEqualTo("\"default\"");
            await Assert.That(negativeHost.Value).IsEqualTo("\"agent-negative\"");
            baseDomain.Value = "\"operator.localhost\"";
            subdomain.Value = "false";
            positiveHost.Value = "\"operator-default\"";
            await database.SaveChangesAsync();
        }

        fixture.RemoveInitializationSecrets();
        await fixture.RunAsync();
        await using var replay = fixture.CreateDatabase();
        await Assert.That((await replay.Set<SystemSetting>().SingleAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Domains.InstanceBaseDomain)).Value)
            .IsEqualTo("\"operator.localhost\"");
        await Assert.That((await replay.Set<SystemSetting>().SingleAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Routing.ResolverSubdomainEnabled)).Value)
            .IsEqualTo("false");
        await Assert.That((await replay.TenantSettingOverrides.SingleAsync(row =>
            row.TenantId == AgentBrowserPersonaCatalog.TenantId
            && row.SettingKey == GovernanceSettingKeys.Domains.TenantSubdomain)).Value)
            .IsEqualTo("\"operator-default\"");
    }

    [Test]
    [Arguments("persona")]
    [Arguments("bootstrap")]
    [Arguments("signing")]
    [Arguments("malformed-signing")]
    [Arguments("mismatched-signing")]
    public async Task AllRequiredSecretsAreCheckedBeforeOwnershipMarkerOrGrantWrites(string fault)
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        fixture.BreakSecret(fault);
        await Assert.That(() => fixture.RunAsync()).Throws<InvalidOperationException>();
        await using var database = fixture.CreateDatabase();
        await Assert.That(await database.InstanceBootstrapStates.AnyAsync()).IsFalse();
        await Assert.That(await database.Users.AnyAsync()).IsFalse();
        await Assert.That(await database.PlatformUserRoles.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task PendingMarkerCannotAdoptForeignOrdinaryContent()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        fixture.InterruptAt("marker");
        await Assert.That(() => fixture.RunAsync()).Throws<AgentBrowserPersonaFixture.InjectedBoundaryFailure>();
        await fixture.InsertForeignUserAsync();
        await Assert.That(() => fixture.RunAsync()).Throws<InvalidOperationException>();
        await using var database = fixture.CreateDatabase();
        await Assert.That((await database.InstanceBootstrapStates.SingleAsync()).Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(await database.Users.CountAsync()).IsEqualTo(1);
        await Assert.That(await database.PlatformUserRoles.AnyAsync()).IsFalse();
        await Assert.That(await database.Tenants.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ForeignDatabaseWithoutDefaultTenantIsNotAdopted()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.InsertForeignUserAsync();
        await fixture.MigrateAsync();
        await Assert.That(() => fixture.RunAsync()).Throws<InvalidOperationException>();
        await using var database = fixture.CreateDatabase();
        await Assert.That(await database.Users.CountAsync()).IsEqualTo(1);
        await Assert.That(await database.InstanceBootstrapStates.AnyAsync()).IsFalse();
        await Assert.That(await database.PlatformUserRoles.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task CompetingSessionLockFailsWithoutMutation()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await using var competingLock = await fixture.AcquireProvisioningLockAsync();
        await Assert.That(() => fixture.RunAsync()).Throws<InvalidOperationException>();
        await using var database = fixture.CreateDatabase();
        await Assert.That(await database.InstanceBootstrapStates.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task MissingCompletedActorCannotResurrectCredentialOrGrants()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        await using (var database = fixture.CreateDatabase())
        {
            var actor = await database.Actors.SingleAsync(row => row.UserId == AgentBrowserPersonaCatalog.Attendee.SubjectId);
            actor.IsDeleted = true;
            await database.SaveChangesAsync();
        }
        fixture.RemoveInitializationSecrets();
        await Assert.That(() => fixture.RunAsync()).Throws<InvalidOperationException>();
    }
}
