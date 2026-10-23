using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Identity;
using Explore.Application.Constants;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventReporting.Handlers.Commands;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Domain.Constants;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Security;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("commit")]
    [Arguments("corrupt")]
    [Arguments("credential-after-terminal-read")]
    [Arguments("support-after-terminal-read")]
    [Arguments("actor-substitution")]
    [Arguments("target-substitution")]
    public async Task ActualReportingOriginalRollsBackAtItsOwnCredentialAndSupportHorizon(string outcome)
    {
        var observation = new TypedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureTypedObservation(services, observation));
        await SeedMixedTenantAdministratorAsync(fixture);
        await SeedPlatformAdministratorAsync(fixture);
        Guid tenant = PlatformDefaults.DefaultTenantId;
        Guid supportId;
        var horizon = fixture.Clock.GetUtcNow().AddMinutes(5);
        string key = GovernanceSettingKeys.EventReporting.IntakeEnabled;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.Events.RequireApproval, "true");
            await SeedSpecializedSettingAsync(seed, key, "true");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.SupportAccess.Enabled, "true");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.SupportAccess.AllowWriteMode, "true");
            await seed.TenantSettingOverrides.Where(row => row.TenantId == tenant && row.SettingKey == key).ExecuteDeleteAsync();
            var support = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, tenant,
                SupportAccessModeEnum.Write, "maintenance", "Typed reporting control", "typed-reporting-control",
                fixture.Clock.GetUtcNow().AddMinutes(-1), horizon);
            supportId = support.Id;
            seed.SupportAccessSessions.Add(support);
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = supportId.ToString("D");
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsTenant(tenant), CancellationToken.None);
        bool observedWrite = false;
        observation.BeforeComplete = async services =>
        {
            var owner = services.GetRequiredService<ExploreDbContext>();
            observedWrite = await owner.TenantSettingOverrides.AnyAsync(row => row.TenantId == tenant && row.SettingKey == key && row.Value == "false");
            if (outcome == "corrupt")
                await owner.TenantSettingOverrides.Where(row => row.TenantId == tenant && row.SettingKey == key)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "true"));
        };
        observation.AfterTerminalRead = () =>
        {
            if (outcome == "credential-after-terminal-read") ExpireMixedCredential(fixture, selected.Services);
            if (outcome == "support-after-terminal-read")
                fixture.Clock.Advance(horizon.AddTicks(1) - fixture.Clock.GetUtcNow());
        };
        var handler = ActivatorUtilities.CreateInstance<UpdateTenantReportingIntakePolicyCommandHandler>(
            selected.Services, (IEnumerable<INotificationHandler<SettingChangedNotification>>)[]);
        var command = new UpdateTenantReportingIntakePolicyCommand(
            outcome == "target-substitution" ? Guid.CreateVersion7() : tenant,
            outcome == "actor-substitution" ? Guid.CreateVersion7() : fixture.Binding.LocalSubjectId,
            new UpdateTenantReportingIntakePolicyDto { Enabled = false });
        if (outcome is "commit" or "target-substitution")
            await Assert.That((await handler.ExecuteAsync(command, CancellationToken.None)).IsSuccess).IsEqualTo(outcome == "commit");
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await Assert.That(observedWrite).IsEqualTo(outcome is not ("actor-substitution" or "target-substitution"));
        await using var independent = fixture.Native.CreateDatabase();
        await Assert.That(await independent.TenantSettingOverrides.AsNoTracking()
                .Where(row => row.TenantId == tenant && row.SettingKey == key).Select(row => row.Value).SingleOrDefaultAsync())
            .IsEqualTo(outcome == "commit" ? "false" : null);
        await Assert.That(selected.Services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("platform")]
    [Arguments("suspended")]
    public async Task OriginalReportingUpdateUsesTenantAuthorityRatherThanPlatformShortcut(string authority)
    {
        bool tenantAdministrator = authority == "tenant";
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => services.AddHumanMixedSettingsSources(IdentityDatabaseTopology.Colocated));
        if (authority != "platform")
            await SeedMixedTenantAdministratorAsync(fixture);
        else
            await SeedPlatformAdministratorAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.Events.RequireApproval, "true");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.EventReporting.IntakeEnabled, "true");
            await seed.TenantSettingOverrides.Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                && row.SettingKey == GovernanceSettingKeys.EventReporting.IntakeEnabled).ExecuteDeleteAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsTenant(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        if (authority == "suspended")
        {
            await using var seed = fixture.Native.CreateDatabase();
            await seed.Tenants.Where(tenant => tenant.Id == PlatformDefaults.DefaultTenantId)
                .ExecuteUpdateAsync(update => update.SetProperty(tenant => tenant.TenantStatusId, (int)TenantStatusEnum.Suspended));
        }
        var handler = ActivatorUtilities.CreateInstance<UpdateTenantReportingIntakePolicyCommandHandler>(
            selected.Services, (IEnumerable<INotificationHandler<SettingChangedNotification>>)[]);
        var command = new UpdateTenantReportingIntakePolicyCommand(PlatformDefaults.DefaultTenantId,
            fixture.Binding.LocalSubjectId, new UpdateTenantReportingIntakePolicyDto { Enabled = false });
        if (tenantAdministrator)
            await Assert.That((await handler.ExecuteAsync(command, CancellationToken.None)).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantSettingOverrides.AsNoTracking().Where(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId
                && row.SettingKey == GovernanceSettingKeys.EventReporting.IntakeEnabled)
            .Select(row => row.Value).SingleOrDefaultAsync()).IsEqualTo(tenantAdministrator ? "false" : null);
        await Assert.That(selected.Services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
    }
}
