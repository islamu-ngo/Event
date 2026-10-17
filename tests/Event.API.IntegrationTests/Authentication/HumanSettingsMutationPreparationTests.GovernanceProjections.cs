using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Notifications;
using Explore.Domain.Constants;
using Explore.Domain.Modules;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("commit")]
    [Arguments("projection-corrupt")]
    [Arguments("expiry")]
    public async Task GovernanceModulePatchOwnsItsOriginalCapabilityProjection(string outcome)
    {
        var observation = new TypedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureTypedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        Guid moduleId;
        Guid capabilityId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.Modules.TechEnabled, "false");
            var module = await seed.Set<ModuleDefinition>().SingleOrDefaultAsync(row => row.ModuleKey == "Mod_Tech");
            if (module is null)
            {
                module = new() { Id = Guid.CreateVersion7(), ModuleKey = "Mod_Tech", Name = "Technology", IsActive = true };
                seed.Add(module);
            }
            moduleId = module.Id;
            var capability = await seed.Set<TenantCapability>().SingleOrDefaultAsync(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && row.ModuleId == moduleId);
            if (capability is null)
            {
                capability = new()
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    ModuleId = moduleId,
                    Module = null!,
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                    EnabledAt = fixture.Clock.GetUtcNow().AddDays(-1).UtcDateTime
                };
                seed.Add(capability);
            }
            capability.IsEnabled = false;
            capability.ConfigurationJson = "{\"retained\":true}";
            capabilityId = capability.Id;
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var owner = selected.Services.GetRequiredService<ExploreDbContext>();
        var notices = new List<SettingChangedNotification>();
        observation.BeforeComplete = async _ =>
        {
            await Assert.That(await owner.Set<TenantCapability>().AsNoTracking()
                .Where(row => row.Id == capabilityId).Select(row => row.IsEnabled).SingleAsync()).IsTrue();
            if (outcome == "projection-corrupt")
                await owner.Set<TenantCapability>().Where(row => row.Id == capabilityId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConfigurationJson, "{\"retained\":false}"));
        };
        observation.AfterTerminalRead = () =>
        {
            if (outcome == "expiry") ExpireMixedCredential(fixture, selected.Services);
        };
        var operation = ExecuteGovernanceAsync(new UpdateModuleSettingsCommand
        {
            UserId = fixture.Binding.LocalSubjectId,
            Patch = new() { EnableTechModule = OptionalUpdate<bool>.Set(true) }
        }, selected.Services, new TypedNotifications(owner, notices));
        if (outcome == "commit") await Assert.That((await operation).IsSuccess).IsTrue();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
        await using var independent = fixture.Native.CreateDatabase();
        var stored = await independent.Set<TenantCapability>().AsNoTracking().SingleAsync(row => row.Id == capabilityId);
        await Assert.That(stored.IsEnabled).IsEqualTo(outcome == "commit");
        await Assert.That(stored.ModuleId).IsEqualTo(moduleId);
        await Assert.That(stored.ConfigurationJson).IsEqualTo("{\"retained\":true}");
        await Assert.That(await independent.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Modules.TechEnabled)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(outcome == "commit" ? "true" : "false");
        await Assert.That(notices.Count).IsEqualTo(outcome == "commit" ? 1 : 0);
    }

    [Test]
    [Arguments(false, "commit")]
    [Arguments(true, "commit")]
    [Arguments(false, "projection-corrupt")]
    [Arguments(true, "expiry")]
    public async Task GovernanceBrandingPatchOwnsTheOriginalDefaultTenantDocument(bool absent, string outcome)
    {
        var observation = new TypedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureTypedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.Branding.DisplayName, "\"Before\"");
            await seed.Set<TenantSettingsDocument>().Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding).ExecuteDeleteAsync();
            if (!absent)
            {
                var document = TenantBrandingSettingsDocumentDefaults.Create(PlatformDefaults.DefaultTenantId, "Before");
                document.UpdatePayload(document.SchemaVersion, document.DefaultsVersion,
                    "{\"displayName\":\"Before\",\"logoUrl\":\"https://brand.example.test/kept.svg\",\"faviconUrl\":null,\"customCssUrl\":null}");
                seed.Add(document);
                await seed.SaveChangesAsync();
            }
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var owner = selected.Services.GetRequiredService<ExploreDbContext>();
        var notices = new List<SettingChangedNotification>();
        observation.BeforeComplete = async _ =>
        {
            var payload = await owner.Set<TenantSettingsDocument>().AsNoTracking().Where(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding)
                .Select(row => row.PayloadJson).SingleAsync();
            using var current = System.Text.Json.JsonDocument.Parse(payload);
            await Assert.That(current.RootElement.GetProperty("displayName").GetString()).IsEqualTo("After");
            await Assert.That(notices.Count).IsEqualTo(0);
            if (outcome == "projection-corrupt")
                await owner.Set<TenantSettingsDocument>().Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                    && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.PayloadJson, "{\"displayName\":\"Substituted\"}"));
        };
        observation.AfterTerminalRead = () =>
        {
            if (outcome == "expiry") ExpireMixedCredential(fixture, selected.Services);
        };
        var operation = ExecuteGovernanceAsync(new UpdateBrandingSettingsCommand
        {
            UserId = fixture.Binding.LocalSubjectId,
            Patch = new() { DefaultBrandDisplayName = OptionalUpdate<string?>.Set(" After ") }
        }, selected.Services, new TypedNotifications(owner, notices));
        if (outcome == "commit") await Assert.That((await operation).IsSuccess).IsTrue();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
        await using var independent = fixture.Native.CreateDatabase();
        var stored = await independent.Set<TenantSettingsDocument>().AsNoTracking().SingleOrDefaultAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding);
        await Assert.That(stored is not null).IsEqualTo(!absent || outcome == "commit");
        if (stored is not null)
        {
            using var payload = System.Text.Json.JsonDocument.Parse(stored.PayloadJson);
            await Assert.That(payload.RootElement.GetProperty("displayName").GetString()).IsEqualTo(outcome == "commit" ? "After" : "Before");
            if (!absent)
                await Assert.That(payload.RootElement.GetProperty("logoUrl").GetString()).IsEqualTo("https://brand.example.test/kept.svg");
        }
        await Assert.That(await independent.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Branding.DisplayName)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(outcome == "commit" ? "\"After\"" : "\"Before\"");
        await Assert.That(notices.Count).IsEqualTo(outcome == "commit" ? 1 : 0);
    }

    [Test]
    [Arguments("commit")]
    [Arguments("unchanged")]
    [Arguments("revision-corrupt")]
    [Arguments("expiry")]
    public async Task GovernanceSmtpDelegationAndOrdinarySettingsSettleOnOneOriginalOwner(string outcome)
    {
        var observation = new TypedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureTypedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.TenantDelegation.LockSmtp, outcome == "unchanged" ? "true" : "false");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.TenantDelegation.LockStorage, outcome == "unchanged" ? "true" : "false");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.Email.DeliveryEnabled, "false");
        }
        long previous;
        await using (var before = fixture.Native.CreateDatabase())
            previous = await before.EmailDispatchProcessorStates.Where(row => row.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode)
                .Select(row => (long?)row.DeliveryPolicyRevision).SingleOrDefaultAsync() ?? 0;
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var owner = selected.Services.GetRequiredService<ExploreDbContext>();
        var notices = new List<SettingChangedNotification>();
        observation.BeforeComplete = async _ =>
        {
            await Assert.That(notices.Count).IsEqualTo(0);
            await Assert.That(await owner.EmailDispatchProcessorStates.AsNoTracking()
                .Where(row => row.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode)
                .Select(row => (long?)row.DeliveryPolicyRevision).SingleOrDefaultAsync() ?? 0)
                .IsEqualTo(outcome == "unchanged" ? previous : previous + 1);
            if (outcome == "revision-corrupt")
                await owner.EmailDispatchProcessorStates.Where(row => row.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.DeliveryPolicyRevision, previous + 2));
        };
        observation.AfterTerminalRead = () =>
        {
            if (outcome == "expiry") ExpireMixedCredential(fixture, selected.Services);
        };
        var operation = ExecuteGovernanceAsync(new UpdateTenantDelegationSettingsCommand
        {
            UserId = fixture.Binding.LocalSubjectId,
            Patch = new() { LockTenantSmtp = OptionalUpdate<bool>.Set(true), LockTenantStorage = OptionalUpdate<bool>.Set(true) }
        }, selected.Services, new TypedNotifications(owner, notices));
        bool success = outcome is "commit" or "unchanged";
        if (success) await Assert.That((await operation).IsSuccess).IsTrue();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
        await using var independent = fixture.Native.CreateDatabase();
        foreach (string key in new[] { GovernanceSettingKeys.TenantDelegation.LockSmtp, GovernanceSettingKeys.TenantDelegation.LockStorage })
            await Assert.That(await independent.SystemSettings.Where(row => row.SettingKey == key).Select(row => row.Value).SingleAsync())
                .IsEqualTo(success ? "true" : "false");
        await Assert.That(await independent.EmailDispatchProcessorStates
            .Where(row => row.ProcessorCode == EmailDispatchOutboxRepository.SmtpProcessorCode)
            .Select(row => (long?)row.DeliveryPolicyRevision).SingleOrDefaultAsync() ?? 0).IsEqualTo(outcome == "commit" ? previous + 1 : previous);
        await Assert.That(notices.Count).IsEqualTo(outcome == "commit" ? 2 : outcome == "unchanged" ? 1 : 0);
    }
}
