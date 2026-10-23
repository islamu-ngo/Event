using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Event.Api.IntegrationTests.Authentication.TenantDelegationAuthenticationFixture;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class TenantDelegationAuthenticationAiTests
{
    [Test]
    public async Task AiProviderDraft_CanBeSavedBeforeEnablingAndRetainsReferenceWhenDisabled()
    {
        await using var fixture = await TenantDelegationAuthenticationFixture.CreateAsync();
        await using var selected = await fixture.SelectAsync();
        var services = selected.Services;
        var handler = services.GetRequiredService<ICommandHandler<UpdateAiAssistantGovernanceSettingsCommand,
            BaseCommandResponse<Guid>>>();
        var incomplete = await handler.ExecuteAsync(new UpdateAiAssistantGovernanceSettingsCommand
        {
            UserId = fixture.UserId,
            Patch = new PatchAiAssistantGovernanceSettingsDto
            {
                ProviderConfiguration = OptionalUpdate<AiAssistantProviderConfigurationWriteDto>.Set(new()
                {
                    Provider = "anthropic",
                    CredentialScope = "none",
                    ModelId = "model-a"
                })
            }
        }, CancellationToken.None);
        await Assert.That(incomplete.IsSuccess).IsFalse();
        await Assert.That(fixture.Published.Items).IsEmpty();
        var saved = await handler.ExecuteAsync(new UpdateAiAssistantGovernanceSettingsCommand
        {
            UserId = fixture.UserId,
            Patch = new PatchAiAssistantGovernanceSettingsDto
            {
                ProviderConfiguration = OptionalUpdate<AiAssistantProviderConfigurationWriteDto>.Set(new()
                {
                    Provider = "anthropic",
                    CredentialScope = "instance",
                    ModelId = "model-a"
                })
            }
        }, CancellationToken.None);
        await Assert.That(saved.IsSuccess).IsTrue();
        foreach (bool enabled in new[] { false, true, false, true })
        {
            services.GetRequiredService<IHierarchicalSettingsResolver>().InvalidateCache();
            var changed = await handler.ExecuteAsync(new UpdateAiAssistantGovernanceSettingsCommand
            {
                UserId = fixture.UserId,
                Patch = new PatchAiAssistantGovernanceSettingsDto { Enabled = OptionalUpdate<bool>.Set(enabled) }
            }, CancellationToken.None);
            await Assert.That(changed.IsSuccess).IsTrue();
            services.GetRequiredService<IHierarchicalSettingsResolver>().InvalidateCache();
            var settings = await services.GetRequiredService<IInstanceGovernanceSettingService>().ReadSettingsAsync();
            await Assert.That(settings.AiAssistant.Enabled).IsEqualTo(enabled);
            await Assert.That(settings.AiAssistant.Provider).IsEqualTo("anthropic");
            await Assert.That(settings.AiAssistant.CredentialScope).IsEqualTo("instance");
            await Assert.That(settings.AiAssistant.ModelId).IsEqualTo("model-a");
        }
        await Assert.That(fixture.Published.Items.All(notification =>
            notification.ActorUserId == fixture.UserId && notification.TenantId is null)).IsTrue();
    }

    [Test]
    [Arguments("openai", "none", false)]
    [Arguments("anthropic", "none", false)]
    [Arguments("openai", "tenant", false)]
    [Arguments("openai-compatible", "", false)]
    [Arguments("anthropic-compatible", "Instance", false)]
    [Arguments("openai-compatible", "unknown", false)]
    [Arguments("openai", "instance", true)]
    [Arguments("anthropic", "instance", true)]
    [Arguments("openai-compatible", "none", true)]
    [Arguments("anthropic-compatible", "none", true)]
    [Arguments("openai-compatible", "instance", true)]
    [Arguments("anthropic-compatible", "instance", true)]
    [Arguments("fake", "none", true)]
    [Arguments("fake", "instance", false)]
    public async Task AiProviderPatch_ValidatesReferenceBeforePersistingAndPreservesLocks(
        string provider, string credentialScope, bool accepted)
    {
        await using var fixture = await TenantDelegationAuthenticationFixture.CreateAsync();
        await using var selected = await fixture.SelectAsync();
        var services = selected.Services;
        var handler = services.GetRequiredService<ICommandHandler<UpdateAiAssistantGovernanceSettingsCommand,
            BaseCommandResponse<Guid>>>();
        var result = await handler.ExecuteAsync(new UpdateAiAssistantGovernanceSettingsCommand
        {
            UserId = fixture.UserId,
            Patch = new PatchAiAssistantGovernanceSettingsDto
            {
                Enabled = OptionalUpdate<bool>.Set(true),
                ProviderConfiguration = OptionalUpdate<AiAssistantProviderConfigurationWriteDto>.Set(new()
                {
                    Provider = provider,
                    EndpointUrl = "https://ai.example.test/v1",
                    CredentialScope = credentialScope,
                    ModelId = "model-a",
                    AllowedModelIds = ["model-a", "model-b"]
                })
            }
        }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsEqualTo(accepted);
        services.GetRequiredService<IHierarchicalSettingsResolver>().InvalidateCache();
        var settings = await services.GetRequiredService<IInstanceGovernanceSettingService>().ReadSettingsAsync();
        if (!accepted)
        {
            await Assert.That(settings.AiAssistant.Enabled).IsFalse();
            await Assert.That(settings.AiAssistant.CredentialScope).IsEqualTo("none");
            await Assert.That(fixture.Published.Items).IsEmpty();
            return;
        }

        await Assert.That(settings.AiAssistant.Provider).IsEqualTo(provider);
        await Assert.That(settings.AiAssistant.CredentialScope).IsEqualTo(credentialScope);
        await Assert.That(settings.AiAssistant.AllowedModelIds).IsEquivalentTo(
            provider == "fake" ? Array.Empty<string>() : ["model-a", "model-b"]);
        var unlocked = await handler.ExecuteAsync(new UpdateAiAssistantGovernanceSettingsCommand
        {
            UserId = fixture.UserId,
            Patch = new PatchAiAssistantGovernanceSettingsDto
            {
                LockTenantAiAssistant = OptionalUpdate<bool>.Set(false)
            }
        }, CancellationToken.None);
        await Assert.That(unlocked.IsSuccess).IsTrue();
        services.GetRequiredService<IHierarchicalSettingsResolver>().InvalidateCache();
        var retained = await services.GetRequiredService<IInstanceGovernanceSettingService>().ReadSettingsAsync();
        await Assert.That(retained.AiAssistant.CredentialScope).IsEqualTo(credentialScope);
        await Assert.That(retained.AiAssistant.LockTenantAiAssistant).IsFalse();
        var reference = (await services.GetRequiredService<ISystemSettingRepository>()
            .GetByKey(GovernanceSettingKeys.AiAssistant.CredentialScope))!;
        await Assert.That(reference.IsLocked).IsFalse();
        await Assert.That(reference.UpdatedBy).IsEqualTo(fixture.UserId);
        await Assert.That(fixture.Published.Items.All(notification =>
            notification.ActorUserId == fixture.UserId && notification.TenantId is null)).IsTrue();
    }

    [Test]
    [Arguments("openai-compatible", "none", false, false, false, true)]
    [Arguments("anthropic-compatible", "tenant", false, false, false, true)]
    [Arguments("openai", "tenant", false, false, false, true)]
    [Arguments("anthropic", "none", false, false, false, false)]
    [Arguments("openai-compatible", "instance", false, false, false, false)]
    [Arguments("openai-compatible", "instance", true, false, false, false)]
    [Arguments("openai-compatible", "instance", true, true, false, true)]
    [Arguments("openai-compatible", "tenant", false, false, true, false)]
    [Arguments("openai-compatible", "unknown", true, true, true, true)]
    [Arguments("openai-compatible", "", false, false, false, false)]
    [Arguments("openai-compatible", "unknown", false, false, false, false)]
    public async Task TenantAiPolicy_UsesEffectiveLocksAndRejectsInstanceCredentialForTenantTransport(
        string provider, string scope, bool providerLocked, bool endpointLocked, bool credentialLocked, bool accepted)
    {
        await using var fixture = await TenantDelegationAuthenticationFixture.CreateAsync();
        await using (var seed = fixture.CreateIndependentContext())
        {
            // Persisted setup facts are not human writes through a null actor or internal adapter.
            await SeedSettingAsync(seed, GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\"");
            await SeedSettingAsync(seed, GovernanceSettingKeys.TenantDelegation.LockAiAssistant, "false");
            await SeedSettingAsync(seed, GovernanceSettingKeys.AiAssistant.Provider, "\"openai-compatible\"", providerLocked);
            await SeedSettingAsync(seed, GovernanceSettingKeys.AiAssistant.EndpointUrl,
                "\"https://instance.example.test/v1\"", endpointLocked);
            await SeedSettingAsync(seed, GovernanceSettingKeys.AiAssistant.CredentialScope, "\"instance\"", credentialLocked);
            var member = await seed.TenantUsers.SingleOrDefaultAsync(row =>
                row.TenantId == fixture.TenantId && row.UserId == fixture.UserId);
            if (member is null)
            {
                member = new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = fixture.TenantId,
                    Tenant = null!,
                    UserId = fixture.UserId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = DateTime.UtcNow
                };
                seed.TenantUsers.Add(member);
            }
            seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = fixture.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using var selected = await fixture.SelectAsync(tenant: true);
        var services = selected.Services;
        var tenantSettings = services.GetRequiredService<ITenantSettingRepository>();
        var service = services.GetRequiredService<ITenantPolicySettingService>();
        var unit = services.GetRequiredService<IUnitOfWork>();
        var request = new UpdateTenantPolicyRequest
        {
            AiAssistantEnabled = true,
            AiAssistantProvider = provider,
            AiAssistantEndpointUrl = "https://tenant.example.test/v1",
            AiAssistantCredentialScope = scope,
            AiAssistantModelId = "tenant-model",
            AiAssistantAllowedModelIds = ["tenant-model", "other-model"]
        };
        Explore.Application.Exceptions.ValidationException? rejection = null;
        try
        {
            await unit.ExecuteInTransactionAsync(
                async ct => { await service.ApplyTenantSettingsAsync(fixture.TenantId, fixture.UserId, request, ct); },
                CancellationToken.None);
        }
        catch (Explore.Application.Exceptions.ValidationException exception)
        {
            rejection = exception;
        }

        await Assert.That(rejection is null).IsEqualTo(accepted);
        var effective = await service.ReadEffectiveTenantSettingsAsync(fixture.TenantId);
        await Assert.That(effective.AiAssistantEnabled).IsEqualTo(accepted);
        await Assert.That(effective.AiAssistantCredentialScope).IsEqualTo(
            accepted && !credentialLocked ? scope : "instance");
        if (!accepted)
        {
            await Assert.That(await tenantSettings.GetAllForTenant(fixture.TenantId)).IsEmpty();
            return;
        }
        await Assert.That(effective.AiAssistantModelId).IsEqualTo("tenant-model");
        await Assert.That(effective.AiAssistantAllowedModelIds).IsEquivalentTo(["tenant-model", "other-model"]);
        await Assert.That(effective.AiAssistantEndpointUrl).IsEqualTo(endpointLocked
            ? "https://instance.example.test/v1"
            : provider is "openai" or "anthropic" ? "https://instance.example.test/v1" : "https://tenant.example.test/v1");

        // Instance lock changes select their own genuine Standard Global owner, not tenant authority.
        await using (var instance = await fixture.SelectAsync())
            await instance.Services.GetRequiredService<SettingUpsertService>().UpsertValueAsync(
                GovernanceSettingKeys.AiAssistant.CredentialScope, "\"instance\"", true, fixture.UserId);
        var individuallyLocked = await service.ReadEffectiveTenantSettingsAsync(fixture.TenantId);
        await Assert.That(individuallyLocked.CanOverrideAiAssistant).IsTrue();
        await Assert.That(individuallyLocked.AiAssistantCredentialScope).IsEqualTo("instance");

        await using (var instance = await fixture.SelectAsync())
            await instance.Services.GetRequiredService<SettingUpsertService>().UpsertValueAsync(
                GovernanceSettingKeys.TenantDelegation.LockAiAssistant, "true", fixture.UserId);
        var locked = await service.ReadEffectiveTenantSettingsAsync(fixture.TenantId);
        await Assert.That(locked.CanOverrideAiAssistant).IsFalse();
        await Assert.That(locked.AiAssistantCredentialScope).IsEqualTo("instance");
        await unit.ExecuteInTransactionAsync(
            async ct =>
            {
                await service.ApplyTenantSettingsAsync(fixture.TenantId, fixture.UserId,
                    request with { AiAssistantCredentialScope = "unknown" }, ct);
            }, CancellationToken.None);
        var restored = await service.ReadEffectiveTenantSettingsAsync(fixture.TenantId);
        await Assert.That(restored.AiAssistantCredentialScope).IsEqualTo("instance");
        await Assert.That((await tenantSettings.GetAllForTenant(fixture.TenantId))
            .Any(setting => setting.SettingKey.StartsWith("ai_assistant.", StringComparison.Ordinal))).IsFalse();
    }
}
