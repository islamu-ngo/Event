using Explore.API.Configuration;
using Explore.API.Mcp;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;
using Microsoft.Extensions.Options;
using NSubstitute;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class McpRuntimeStateServiceTests
{
    [Test]
    public async Task GetAsync_WhenStartupDisabled_BlocksEffectiveRuntimeEvenIfDbEnabled()
    {
        var service = CreateService(startupEnabled: false, instanceEnabled: true, lockTenantMcp: false);

        var state = await service.GetAsync();

        await Assert.That(state.StartupEnabled).IsFalse();
        await Assert.That(state.RuntimeEnabled).IsTrue();
        await Assert.That(state.EffectiveEnabled).IsFalse();
    }

    [Test]
    public async Task GetAsync_WhenTenantOverrideUnlocked_UsesTenantRuntimeValue()
    {
        var tenantId = Guid.NewGuid();
        var service = CreateService(
            startupEnabled: true,
            instanceEnabled: true,
            lockTenantMcp: false,
            tenantId: tenantId,
            tenantEnabled: false);

        var state = await service.GetAsync();

        await Assert.That(state.TenantOverrideAllowed).IsTrue();
        await Assert.That(state.RuntimeEnabled).IsFalse();
        await Assert.That(state.EffectiveEnabled).IsFalse();
    }

    [Test]
    public async Task GetAsync_WhenTenantOverrideLocked_UsesInstanceRuntimeValue()
    {
        var tenantId = Guid.NewGuid();
        var service = CreateService(
            startupEnabled: true,
            instanceEnabled: true,
            lockTenantMcp: true,
            tenantId: tenantId,
            tenantEnabled: false);

        var state = await service.GetAsync();

        await Assert.That(state.TenantOverrideAllowed).IsFalse();
        await Assert.That(state.RuntimeEnabled).IsTrue();
        await Assert.That(state.EffectiveEnabled).IsTrue();
    }

    [Test]
    public async Task GetAsync_KeepsLegacySseRuntimeUnavailableEvenWhenRequested()
    {
        var service = CreateService(
            startupEnabled: true,
            startupLegacySseCeiling: true,
            instanceEnabled: true,
            instanceLegacySseRequested: true,
            lockTenantMcp: true,
            lockTenantLegacySse: true);

        var state = await service.GetAsync();

        await Assert.That(state.StartupLegacySseCeiling).IsTrue();
        await Assert.That(state.RuntimeLegacySseRequested).IsTrue();
        await Assert.That(state.LegacySseRuntimeEnabled).IsFalse();
    }

    private static McpRuntimeStateService CreateService(
        bool startupEnabled,
        bool instanceEnabled,
        bool lockTenantMcp,
        bool startupLegacySseCeiling = false,
        bool instanceLegacySseRequested = false,
        bool lockTenantLegacySse = true,
        Guid? tenantId = null,
        bool? tenantEnabled = null)
    {
        var resolver = Substitute.For<IHierarchicalSettingsResolver>();
        resolver.ResolveGroupAsync<McpSettingGroup>(
                Arg.Is<SettingContext>(c => c.TenantId == null),
                Arg.Any<CancellationToken>())
            .Returns(CreateMcpGroup(instanceEnabled, instanceLegacySseRequested));
        resolver.ResolveGroupAsync<TenantDelegationSettingGroup>(
                Arg.Is<SettingContext>(c => c.TenantId == null),
                Arg.Any<CancellationToken>())
            .Returns(CreateDelegationGroup(lockTenantMcp, lockTenantLegacySse));

        if (tenantId.HasValue)
        {
            resolver.ResolveGroupAsync<McpSettingGroup>(
                    Arg.Is<SettingContext>(c => c.TenantId == tenantId.Value),
                    Arg.Any<CancellationToken>())
                .Returns(CreateMcpGroup(tenantEnabled ?? instanceEnabled, instanceLegacySseRequested));
        }

        var tenantAccessor = Substitute.For<ITenantContextAccessor>();
        tenantAccessor.TenantId.Returns(tenantId);
        var deploymentModeProvider = Substitute.For<IDeploymentModeProvider>();
        deploymentModeProvider.IsSingleTenantAsync(Arg.Any<CancellationToken>()).Returns(false);

        return new McpRuntimeStateService(
            Options.Create(new McpAdapterSettings
            {
                Enabled = startupEnabled,
                EnableLegacySse = startupLegacySseCeiling,
                EndpointPath = "/mcp",
                Stateless = true
            }),
            resolver,
            tenantAccessor,
            deploymentModeProvider);
    }

    private static McpSettingGroup CreateMcpGroup(bool enabled, bool legacySse)
    {
        var group = new McpSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>
        {
            [GovernanceSettingKeys.Mcp.Enabled] = new() { Value = enabled ? "true" : "false" },
            [GovernanceSettingKeys.Mcp.EnableLegacySse] = new() { Value = legacySse ? "true" : "false" }
        });
        return group;
    }

    private static TenantDelegationSettingGroup CreateDelegationGroup(bool lockMcp, bool lockLegacySse)
    {
        var group = new TenantDelegationSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>
        {
            [GovernanceSettingKeys.TenantDelegation.LockMcp] = new() { Value = lockMcp ? "true" : "false" },
            [GovernanceSettingKeys.TenantDelegation.LockMcpLegacySse] = new() { Value = lockLegacySse ? "true" : "false" }
        });
        return group;
    }
}
