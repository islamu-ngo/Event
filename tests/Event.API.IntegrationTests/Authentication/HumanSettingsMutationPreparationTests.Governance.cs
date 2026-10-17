using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("modules", "commit")]
    [Arguments("organization", "commit")]
    [Arguments("branding", "commit")]
    [Arguments("domains", "commit")]
    [Arguments("delegation", "commit")]
    [Arguments("admin-portal", "commit")]
    [Arguments("mcp", "commit")]
    [Arguments("ai", "commit")]
    [Arguments("render-policy", "commit")]
    [Arguments("modules", "unchanged")]
    [Arguments("organization", "unchanged")]
    [Arguments("branding", "unchanged")]
    [Arguments("domains", "unchanged")]
    [Arguments("delegation", "unchanged")]
    [Arguments("admin-portal", "unchanged")]
    [Arguments("mcp", "unchanged")]
    [Arguments("ai", "unchanged")]
    [Arguments("render-policy", "unchanged")]
    [Arguments("modules", "revoked")]
    [Arguments("organization", "revoked")]
    [Arguments("branding", "revoked")]
    [Arguments("domains", "revoked")]
    [Arguments("delegation", "revoked")]
    [Arguments("admin-portal", "revoked")]
    [Arguments("mcp", "revoked")]
    [Arguments("ai", "revoked")]
    [Arguments("render-policy", "revoked")]
    [Arguments("modules", "content")]
    [Arguments("organization", "content")]
    [Arguments("branding", "content")]
    [Arguments("domains", "content")]
    [Arguments("delegation", "content")]
    [Arguments("admin-portal", "content")]
    [Arguments("mcp", "content")]
    [Arguments("ai", "content")]
    [Arguments("render-policy", "content")]
    [Arguments("modules", "expiry")]
    [Arguments("organization", "expiry")]
    [Arguments("branding", "expiry")]
    [Arguments("domains", "expiry")]
    [Arguments("delegation", "expiry")]
    [Arguments("admin-portal", "expiry")]
    [Arguments("mcp", "expiry")]
    [Arguments("ai", "expiry")]
    [Arguments("render-policy", "expiry")]
    [Arguments("modules", "support-active")]
    [Arguments("organization", "support-active")]
    [Arguments("branding", "support-active")]
    [Arguments("domains", "support-active")]
    [Arguments("delegation", "support-active")]
    [Arguments("admin-portal", "support-active")]
    [Arguments("mcp", "support-active")]
    [Arguments("ai", "support-active")]
    [Arguments("render-policy", "support-active")]
    [Arguments("modules", "support-expiry")]
    [Arguments("organization", "support-expiry")]
    [Arguments("branding", "support-expiry")]
    [Arguments("domains", "support-expiry")]
    [Arguments("delegation", "support-expiry")]
    [Arguments("admin-portal", "support-expiry")]
    [Arguments("mcp", "support-expiry")]
    [Arguments("ai", "support-expiry")]
    [Arguments("render-policy", "support-expiry")]
    public async Task OriginalGovernancePatchCommitsOnlyWithItsCompleteCurrentAuthority(string operation, string outcome)
    {
        var observation = new TypedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureTypedObservation(services, observation));
        Guid grant = await SeedPlatformAdministratorAsync(fixture);
        Guid? supportId = null;
        var supportHorizon = fixture.Clock.GetUtcNow().AddMinutes(5);
        var (key, before, after) = GovernanceValue(operation);
        if (outcome == "unchanged") before = after;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedSpecializedSettingAsync(seed, key, before);
            await seed.SystemSettings.Where(row => row.SettingKey == key)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "retained governance metadata"));
            if (outcome.StartsWith("support-", StringComparison.Ordinal))
            {
                await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.SupportAccess.Enabled, "true");
                await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.SupportAccess.AllowWriteMode, "true");
                var support = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                    SupportAccessModeEnum.Write, "maintenance", "Governance control", "governance-control",
                    fixture.Clock.GetUtcNow().AddMinutes(-1), supportHorizon);
                supportId = support.Id;
                seed.SupportAccessSessions.Add(support);
                await seed.SaveChangesAsync();
            }
        }
        await using var request = await Request.CreateAsync(fixture);
        if (supportId.HasValue) request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = supportId.Value.ToString("D");
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var owner = selected.Services.GetRequiredService<ExploreDbContext>();
        var notifications = new List<SettingChangedNotification>();
        observation.BeforeComplete = async services =>
        {
            await Assert.That(ReferenceEquals(owner, services.GetRequiredService<ExploreDbContext>())).IsTrue();
            await Assert.That(notifications.Count).IsEqualTo(0);
            if (outcome == "revoked")
                await owner.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync();
            if (outcome == "content")
                await owner.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "changed after write"));
        };
        observation.AfterTerminalRead = () =>
        {
            if (outcome == "expiry") ExpireMixedCredential(fixture, selected.Services);
            if (outcome == "support-expiry") fixture.Clock.Advance(supportHorizon - fixture.Clock.GetUtcNow());
        };
        var command = GovernanceCommand(operation, fixture.Binding.LocalSubjectId);
        var attempt = ExecuteGovernanceAsync(command, selected.Services, new TypedNotifications(owner, notifications));
        bool success = outcome is "commit" or "unchanged" or "support-active";
        if (success) await Assert.That((await attempt).IsSuccess).IsTrue();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => attempt);
        await using var independent = fixture.Native.CreateDatabase();
        var stored = await independent.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key);
        await Assert.That(stored.Value).IsEqualTo(success ? after : before);
        if (!success) await Assert.That(stored.Description).IsEqualTo("retained governance metadata");
        await Assert.That(await independent.PlatformUserRoles.AnyAsync(row => row.Id == grant)).IsTrue();
        await Assert.That(notifications.Count > 0).IsEqualTo(success);
        await Assert.That(owner.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task GovernanceAiProviderRetainsTheSuppliedModelSnapshot()
    {
        var observation = new TypedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureTypedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        var models = new List<string> { "model-A", "model-B" };
        observation.AfterObservation = () => { models.Clear(); models.Add("substituted"); };
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var command = new UpdateAiAssistantGovernanceSettingsCommand
        {
            UserId = fixture.Binding.LocalSubjectId,
            Patch = new()
            {
                ProviderConfiguration = OptionalUpdate<Explore.Application.DTOs.Instance.AiAssistantProviderConfigurationWriteDto>.Set(new()
                {
                    Provider = "openai-compatible",
                    CredentialScope = "none",
                    EndpointUrl = "https://models.example.test",
                    ModelId = " model-A ",
                    AllowedModelIds = models
                })
            }
        };
        await Assert.That((await ExecuteGovernanceAsync(command, selected.Services,
            new TypedNotifications(selected.Services.GetRequiredService<ExploreDbContext>(), []))).IsSuccess).IsTrue();
        await using var independent = fixture.Native.CreateDatabase();
        await Assert.That(await independent.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.AiAssistant.AllowedModelIds)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("[\"model-A\",\"model-B\"]");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GovernanceOriginalCannotCommitWithoutItsPrivateWriter(bool equivalentRawWrite)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureTypedObservation(services, new()));
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var preparation = selected.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var prepared = await preparation.PrepareAsync((UpdateModuleSettingsCommand)GovernanceCommand("modules", fixture.Binding.LocalSubjectId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.ExecutePreparedAsync(prepared, async (_, token) =>
        {
            if (equivalentRawWrite)
                await selected.Services.GetRequiredService<ISystemSettingRepository>().UpsertAsync(new SystemSetting
                {
                    SettingKey = GovernanceSettingKeys.Modules.TechEnabled,
                    Value = "true",
                    ValueType = SettingValueType.Boolean,
                    CreatedBy = fixture.Binding.LocalSubjectId,
                    UpdatedBy = fixture.Binding.LocalSubjectId
                }, token);
            return true;
        }));
    }

    [Test]
    [Arguments("modules")]
    [Arguments("organization")]
    [Arguments("branding")]
    [Arguments("domains")]
    [Arguments("delegation")]
    [Arguments("admin-portal")]
    [Arguments("mcp")]
    [Arguments("ai")]
    [Arguments("render-policy")]
    public async Task GovernancePatchCannotSubstituteAnOrdinaryInstanceSelection(string operation)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureTypedObservation(services, new()));
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var notices = new List<SettingChangedNotification>();
        var context = selected.Services.GetRequiredService<ExploreDbContext>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ExecuteGovernanceAsync(
            GovernanceCommand(operation, fixture.Binding.LocalSubjectId), selected.Services, new TypedNotifications(context, notices)));
        await Assert.That(notices.Count).IsEqualTo(0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task GovernanceLockOnlyPatchPreservesOriginalStoredBytes()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureTypedObservation(services, new()));
        await SeedPlatformAdministratorAsync(fixture);
        string key = GovernanceSettingKeys.Branding.LogoUrl;
        string value = "\"  https://brand.example.test/original.svg  \"";
        await using (var seed = fixture.Native.CreateDatabase())
            await SeedSpecializedSettingAsync(seed, key, value);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var response = await ExecuteGovernanceAsync(new UpdateBrandingSettingsCommand
        {
            UserId = fixture.Binding.LocalSubjectId,
            Patch = new() { LockTenantBrandLogoUrl = OptionalUpdate<bool>.Set(true) }
        }, selected.Services, new TypedNotifications(selected.Services.GetRequiredService<ExploreDbContext>(), []));
        await Assert.That(response.IsSuccess).IsTrue();
        await using var independent = fixture.Native.CreateDatabase();
        var row = await independent.SystemSettings.AsNoTracking().SingleAsync(setting => setting.SettingKey == key);
        await Assert.That(row.Value).IsEqualTo(value);
        await Assert.That(row.IsLocked).IsTrue();
    }

    private static (string Key, string Before, string After) GovernanceValue(string operation) => operation switch
    {
        "modules" => (GovernanceSettingKeys.Modules.TechEnabled, "false", "true"),
        "organization" => (GovernanceSettingKeys.Organizations.TenantCanOmitVerification, "false", "true"),
        "branding" => (GovernanceSettingKeys.Branding.LogoUrl, "\"old\"", "\"https://brand.example.test/logo.svg\""),
        "domains" => (GovernanceSettingKeys.Domains.AdminHost, "\"old.example.test\"", "\"admin.example.test\""),
        "delegation" => (GovernanceSettingKeys.TenantDelegation.LockStorage, "false", "true"),
        "admin-portal" => (GovernanceSettingKeys.AdminPortal.Enabled, "false", "true"),
        "mcp" => (GovernanceSettingKeys.Mcp.Enabled, "false", "true"),
        "ai" => (GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled, "false", "true"),
        "render-policy" => (GovernanceSettingKeys.Routing.RenderPolicy.AllowTenantOverride, "false", "true"),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static object GovernanceCommand(string operation, Guid actor) => operation switch
    {
        "modules" => new UpdateModuleSettingsCommand { UserId = actor, Patch = new() { EnableTechModule = OptionalUpdate<bool>.Set(true) } },
        "organization" => new UpdateOrganizationPolicyCommand { UserId = actor, Patch = new() { AllowTenantToOmitVerification = OptionalUpdate<bool>.Set(true) } },
        "branding" => new UpdateBrandingSettingsCommand { UserId = actor, Patch = new() { DefaultBrandLogoUrl = OptionalUpdate<string?>.Set(" https://brand.example.test/logo.svg ") } },
        "domains" => new UpdateDomainSettingsCommand { UserId = actor, Patch = new() { AdminHost = OptionalUpdate<string?>.Set(" HTTPS://ADMIN.EXAMPLE.TEST/ ") } },
        "delegation" => new UpdateTenantDelegationSettingsCommand { UserId = actor, Patch = new() { LockTenantStorage = OptionalUpdate<bool>.Set(true) } },
        "admin-portal" => new UpdateAdminPortalSettingsCommand { UserId = actor, Patch = new() { Enabled = OptionalUpdate<bool>.Set(true) } },
        "mcp" => new UpdateMcpGovernanceSettingsCommand { UserId = actor, Patch = new() { Enabled = OptionalUpdate<bool>.Set(true) } },
        "ai" => new UpdateAiAssistantGovernanceSettingsCommand { UserId = actor, Patch = new() { ToolProposalsEnabled = OptionalUpdate<bool>.Set(true) } },
        "render-policy" => new UpdateRenderPolicySettingsCommand { UserId = actor, Patch = new() { AllowTenantRenderPolicyOverride = OptionalUpdate<bool>.Set(true) } },
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static Task<BaseCommandResponse<Guid>> ExecuteGovernanceAsync(object command, IServiceProvider services,
        INotificationHandler<SettingChangedNotification> sink)
    {
        object[] arguments = [(IEnumerable<INotificationHandler<SettingChangedNotification>>)[sink]];
        return command switch
        {
            UpdateModuleSettingsCommand value => ActivatorUtilities.CreateInstance<UpdateModuleSettingsCommandHandler>(services, arguments).ExecuteAsync(value, CancellationToken.None),
            UpdateOrganizationPolicyCommand value => ActivatorUtilities.CreateInstance<UpdateOrganizationPolicyCommandHandler>(services, arguments).ExecuteAsync(value, CancellationToken.None),
            UpdateBrandingSettingsCommand value => ActivatorUtilities.CreateInstance<UpdateBrandingSettingsCommandHandler>(services, arguments).ExecuteAsync(value, CancellationToken.None),
            UpdateDomainSettingsCommand value => ActivatorUtilities.CreateInstance<UpdateDomainSettingsCommandHandler>(services, arguments).ExecuteAsync(value, CancellationToken.None),
            UpdateTenantDelegationSettingsCommand value => ActivatorUtilities.CreateInstance<UpdateTenantDelegationSettingsCommandHandler>(services, arguments).ExecuteAsync(value, CancellationToken.None),
            UpdateAdminPortalSettingsCommand value => ActivatorUtilities.CreateInstance<UpdateAdminPortalSettingsCommandHandler>(services, arguments).ExecuteAsync(value, CancellationToken.None),
            UpdateMcpGovernanceSettingsCommand value => ActivatorUtilities.CreateInstance<UpdateMcpGovernanceSettingsCommandHandler>(services, arguments).ExecuteAsync(value, CancellationToken.None),
            UpdateAiAssistantGovernanceSettingsCommand value => ActivatorUtilities.CreateInstance<UpdateAiAssistantGovernanceSettingsCommandHandler>(services, arguments).ExecuteAsync(value, CancellationToken.None),
            UpdateRenderPolicySettingsCommand value => ActivatorUtilities.CreateInstance<UpdateRenderPolicySettingsCommandHandler>(services, arguments).ExecuteAsync(value, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
    }
}
