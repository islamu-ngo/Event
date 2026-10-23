using System.Text.Json;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed partial class LocalBootstrapConvergenceTests
{
    [Test]
    public async Task InvalidDeploymentAddress_DisablesBackgroundLinksWithoutBlockingProfileSave()
    {
        await using var scenario = await CompletionFixture.CreateAsync(interactive: true);
        scenario.Configuration["PUBLIC_BASE_URL"] = "ftp://invalid.example.test";
        var response = await scenario.SaveProfileAsync(new SelfHostOnboardingProfileDto
        {
            SiteName = "Community",
            PublicUrl = "https://request.example.test/community"
        });

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(await PublicAddressResolver.ResolveAsync(
            scenario.Configuration, scenario.Settings, CancellationToken.None)).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProfileWrites_DeploymentUrlOverridesSubmittedDomain(bool complete)
    {
        await using var scenario = await CompletionFixture.CreateAsync(interactive: true);
        scenario.Configuration["PUBLIC_BASE_URL"] = "https://platform.example.test:9443/community";
        var profile = new SelfHostOnboardingProfileDto { SiteName = "Public site", PublicUrl = "https://other.example.test" };
        var command = await scenario.InteractiveCommandAsync();
        string originalDomain = JsonSerializer.Serialize(
            await scenario.Settings.GetByKey(GovernanceSettingKeys.Domains.InstanceBaseDomain));
        var response = complete
            ? await scenario.CompleteInteractiveAsync(command with { Settings = command.Settings with { SiteProfile = profile } })
            : await scenario.SaveProfileAsync(profile);

        await Assert.That(response.IsSuccess).IsTrue();
        scenario.Configuration["PUBLIC_BASE_URL"] = "";
        scenario.Configuration["PublicBaseUrl"] = "";
        await Assert.That((await scenario.Generation.ReadSnapshotAsync(CancellationToken.None)).Profile.PublicUrl)
            .IsEqualTo("https://platform.example.test:9443/community");
        await Assert.That(JsonSerializer.Serialize(
            await scenario.Settings.GetByKey(GovernanceSettingKeys.Domains.InstanceBaseDomain))).IsEqualTo(originalDomain);
    }

    [Test]
    public async Task ConfiguredCompletion_CommitsRolesTenantSettingsAndBootstrapAsOneStateChange()
    {
        await using var scenario = await CompletionFixture.CreateAsync();
        var response = await scenario.ClaimAsync();

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That((await scenario.ReadBootstrapAsync())!.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        foreach (string category in new[] { "tenant", "platform-role", "tenant-role", "system-setting", "bootstrap" })
            await Assert.That(scenario.CommittedWrites).Contains(category);
        await Assert.That(await scenario.Users.GetById(scenario.UserId)).IsNotNull();
        await Assert.That(await new PlatformUserRoleRepository(scenario.Context).IsUserPlatformAdmin(scenario.UserId)).IsTrue();
        await Assert.That(await new TenantUserRoleGrantRepository(scenario.Context)
            .GetByTenantAndUser(PlatformDefaults.DefaultTenantId, scenario.UserId)).IsNotNull();
        await Assert.That((await scenario.Settings.GetByKey(GovernanceSettingKeys.Deployment.Mode))!.IsLocked).IsTrue();
        await Assert.That(scenario.PostCommitEffects).IsEquivalentTo(["secret-lock", "deployment-cache", "jwt-reload", "audit"]);
    }

    [Test]
    [Arguments(1)]
    [Arguments(6)]
    public async Task ConfiguredCompletion_FailureBeforeOrAfterIntermediateWritesRollsBackEverything(int failingWrite)
    {
        await using var scenario = await CompletionFixture.CreateAsync();
        string originalSettings = await scenario.ReadSettingsAsync();
        scenario.FailAtWrite = failingWrite;

        await Assert.That(async () => { await scenario.ClaimAsync(); }).Throws<InjectedBootstrapFailure>();

        await Assert.That((await scenario.ReadBootstrapAsync())!.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        if (failingWrite == 6)
        {
            foreach (string category in new[] { "system-setting", "tenant", "user", "actor", "external-login", "platform-role" })
                await Assert.That(scenario.AttemptedWrites).Contains(category);
        }
        await Assert.That(await scenario.Context.Users.AnyAsync()).IsFalse();
        await Assert.That(await scenario.Context.Actors.AnyAsync()).IsFalse();
        await Assert.That(await scenario.Context.UserExternalLogins.AnyAsync()).IsFalse();
        await Assert.That(await scenario.Context.PlatformUserRoles.AnyAsync()).IsFalse();
        await Assert.That(await scenario.Context.TenantUserRoleGrants.AnyAsync()).IsFalse();
        await Assert.That(await scenario.ReadTenantAsync()).IsNull();
        await Assert.That(await scenario.ReadSettingsAsync()).IsEqualTo(originalSettings);
        await Assert.That(scenario.PostCommitEffects).IsEmpty();
    }

    [Test]
    [Arguments("instance_operator_identity_incomplete", "instance_operator_identity_legal_name_missing")]
    [Arguments("instance_operator_identity_missing", null)]
    [Arguments("instance_operator_identity_integrity_error", null)]
    public async Task CompletionWithoutLegalIdentity_CreatesPrivateAdministration(string failureCode, string? reasonCode)
    {
        await using var scenario = await CompletionFixture.CreateAsync(legalIdentity: false);
        if (failureCode != "instance_operator_identity_missing")
        {
            string payload = failureCode == "instance_operator_identity_integrity_error" ? "{invalid" :
                JsonSerializer.Serialize(new InstanceOperatorIdentitySettings
                {
                    OperatorId = Guid.CreateVersion7(),
                    Revision = Guid.CreateVersion7(),
                    PublicName = "Draft operator"
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            scenario.Context.SystemSettings.Add(new SystemSetting
            {
                SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                Value = payload
            });
            await scenario.Context.SaveChangesAsync();
        }
        var readiness = await new InstanceOperatorIdentityService(scenario.Settings,
            scenario.RequestServices.GetRequiredService<IPolicyMutationExecutionScopeFactory>(),
            scenario.RequestServices.GetRequiredService<TimeProvider>())
            .EvaluateAsync(InstanceOperatorIdentityCapability.PaidCommerce);
        await Assert.That(readiness.FailureCode).IsEqualTo(failureCode);
        if (reasonCode is not null) await Assert.That(readiness.ReasonCodes).Contains(reasonCode);
        scenario.ResetObservation();

        var response = await scenario.ClaimAsync();

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That((await scenario.ReadBootstrapAsync())!.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        await Assert.That((await scenario.ReadTenantAsync())!.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Provisioning);
        await Assert.That(await scenario.Users.GetById(scenario.UserId)).IsNotNull();
    }

    [Test]
    public async Task InteractiveCompletion_RequiresNoDirectoryLegalIdentity()
    {
        await using var scenario = await CompletionFixture.CreateAsync(interactive: true);
        var command = await scenario.InteractiveCommandAsync();
        var response = await scenario.CompleteInteractiveAsync(command with
        {
            Settings = command.Settings with { DirectoryOperatorIdentity = null }
        });
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That((await scenario.ReadTenantAsync())!.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Provisioning);
    }

    [Test]
    public async Task InteractiveCompletion_DoesNotEvaluateExternalReadinessInsideTransaction()
    {
        await using var scenario = await CompletionFixture.CreateAsync(interactive: true);
        var response = await scenario.CompleteInteractiveAsync(await scenario.InteractiveCommandAsync());
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(scenario.ExternalReadinessCallsOutsideTransaction).IsGreaterThan(0);
        await Assert.That(scenario.ExternalReadinessCallsInsideTransaction).IsEqualTo(0);
        await Assert.That((await scenario.ReadBootstrapAsync())!.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
    }

    [Test]
    public async Task ConfiguredCompletion_WithoutIdentity_CreatesAuthoritativeDrafts()
    {
        await using var scenario = await CompletionFixture.CreateAsync(legalIdentity: false);
        var response = await scenario.ClaimAsync();
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That((await scenario.ReadTenantAsync())!.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Provisioning);
        var document = await new TenantSettingsDocumentRepository(scenario.Context).GetByTenantAndDocumentKey(
            PlatformDefaults.DefaultTenantId, SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity);
        await Assert.That(document!.PayloadJson).IsEqualTo(
            TenantDirectoryOperatorIdentityDocumentDefaults.Create(PlatformDefaults.DefaultTenantId).PayloadJson);
    }

    [Test]
    public async Task ConfiguredMultiTenantCompletion_CreatesNoDefaultTenant()
    {
        await using var scenario = await CompletionFixture.CreateAsync(legalIdentity: false, mode: DeploymentMode.MultiTenant);
        var response = await scenario.ClaimAsync();
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(await scenario.ReadTenantAsync()).IsNull();
        await Assert.That(scenario.CommittedWrites).DoesNotContain("tenant-role");
        await Assert.That(await scenario.Context.TenantUserRoleGrants.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ExistingActiveDefault_IsPreservedWithoutReplacingItsDocuments()
    {
        await using var scenario = await CompletionFixture.CreateAsync();
        scenario.Context.Tenants.Add(new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId,
            FullName = "Existing",
            Slug = "existing",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        });
        var branding = TenantBrandingSettingsDocumentDefaults.Create(PlatformDefaults.DefaultTenantId, "Existing brand");
        var identity = TenantDirectoryOperatorIdentityDocumentDefaults.Create(PlatformDefaults.DefaultTenantId);
        scenario.Context.Set<TenantSettingsDocument>().AddRange(branding, identity);
        await scenario.Context.SaveChangesAsync();
        scenario.Context.ChangeTracker.Clear();
        scenario.ResetObservation();

        var response = await scenario.ClaimAsync();

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That((await scenario.ReadTenantAsync())!.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Active);
        await Assert.That(scenario.CommittedWrites).DoesNotContain("tenant");
        foreach (var original in new[] { branding, identity })
        {
            var stored = await new TenantSettingsDocumentRepository(scenario.Context)
                .GetByTenantAndDocumentKey(original.TenantId, original.DocumentKey);
            await Assert.That(stored!.Id).IsEqualTo(original.Id);
            await Assert.That(stored.ConcurrencyStamp).IsEqualTo(original.ConcurrencyStamp);
            await Assert.That(stored.PayloadJson).IsEqualTo(original.PayloadJson);
            await Assert.That(scenario.CommittedWrites).DoesNotContain(original.DocumentKey);
        }
    }

    [Test]
    public async Task PostCommitEffects_AreNotVisibleUntilTheTransactionCommits()
    {
        await using var scenario = await CompletionFixture.CreateAsync();
        bool observedCommit = false;
        scenario.BeforeCommit = () =>
        {
            observedCommit = true;
            if (scenario.PostCommitEffects.Count != 0)
                throw new InvalidOperationException("A post-commit effect escaped before commit.");
        };
        var response = await scenario.ClaimAsync();
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(observedCommit).IsTrue();
        await Assert.That(scenario.EventSequence).IsEquivalentTo(["commit", "secret-lock", "deployment-cache", "jwt-reload", "audit"]);
    }

    [Test]
    public async Task ExactCompletedConfiguredReplay_ReconcilesMandatoryPostCommitEffectsOnce()
    {
        await using var scenario = await CompletionFixture.CreateAsync();
        var first = await scenario.ClaimAsync();
        scenario.ResetObservation();
        var replay = await scenario.ClaimAsync();
        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(first.Id);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.PostCommitEffects).IsEquivalentTo(["secret-lock", "deployment-cache", "jwt-reload", "audit"]);
    }

    [Test]
    public async Task PostCommitReconciliation_DoesNotUseCanceledRequestToken()
    {
        await using var scenario = await CompletionFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        // Cancel at the final physical commit acknowledgement, not before a cancellable
        // relational commit. Both the enrollment and privacy gate have now committed.
        scenario.AfterCommit = cancellation.Cancel;
        var response = await scenario.ClaimAsync(cancellationToken: cancellation.Token);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(scenario.JwtCancellationWasRequested).IsFalse();
    }

    [Test]
    public async Task InteractiveAndConfiguredCommands_ReachTheSameAtomicCompletionOperation()
    {
        await using var configured = await CompletionFixture.CreateAsync();
        var configuredResponse = await configured.ClaimAsync();
        await using var interactive = await CompletionFixture.CreateAsync(interactive: true);
        var interactiveResponse = await interactive.CompleteInteractiveAsync(await interactive.InteractiveCommandAsync());
        await Assert.That(configuredResponse.IsSuccess).IsTrue();
        await Assert.That(interactiveResponse.IsSuccess).IsTrue();
        await Assert.That(configured.CommittedWrites).Contains("bootstrap");
        await Assert.That(interactive.CommittedWrites).Contains("bootstrap");
        await Assert.That(configured.PostCommitEffects).IsEquivalentTo(interactive.PostCommitEffects);
    }

    [Test]
    public async Task CompletedConfiguredClaim_DifferentActorCannotAcquireAuthority()
    {
        await using var scenario = await CompletionFixture.CreateAsync();
        var initial = await scenario.ClaimAsync();
        int effects = scenario.PostCommitEffects.Count;
        Guid attacker = Guid.CreateVersion7();
        var rejected = await scenario.ClaimAsync(attacker);
        await Assert.That(initial.IsSuccess).IsTrue();
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.FailureCode).IsEqualTo("configured_administrator_claim_conflict");
        await Assert.That((await scenario.ReadBootstrapAsync())!.CompletedByUserId).IsEqualTo(scenario.UserId);
        await Assert.That(await scenario.Users.GetById(attacker)).IsNull();
        await Assert.That(scenario.PostCommitEffects.Count).IsEqualTo(effects);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
    }

    [Test]
    public async Task ExactProviderAccountGenerationAndFingerprintCompleteWithExactEvidence()
    {
        await using var scenario = await CompletionFixture.CreateAsync();
        var pending = (await scenario.ReadBootstrapAsync())!;
        var response = await scenario.ClaimAsync();
        var completed = (await scenario.ReadBootstrapAsync())!;
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(completed.ProviderKind).IsEqualTo(AuthenticationProviderKind.Keycloak);
        await Assert.That(completed.Generation).IsEqualTo(7);
        await Assert.That(completed.CompletedIdentityFingerprint).IsEqualTo(pending.SelectorFingerprint);
        await Assert.That(completed.CompletedByUserId).IsEqualTo(scenario.UserId);
        var login = await new UserExternalLoginRepository(scenario.Context).GetByProviderAndKey(scenario.Account);
        await Assert.That(login!.UserId).IsEqualTo(scenario.UserId);
    }

    [Test]
    public async Task LocalIdentityProviderCanClaimConfiguredAdministratorExactlyOnce()
    {
        await using var scenario = await CompletionFixture.CreateAsync(providerKind: AuthenticationProviderKind.Local);
        var unprovisioned = await scenario.ClaimAsync();
        await Assert.That(unprovisioned.IsSuccess).IsFalse();
        await Assert.That(unprovisioned.FailureCode).IsEqualTo("local_bootstrap_authority_invalid");
        await Assert.That((await scenario.ReadBootstrapAsync())!.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.PostCommitEffects).IsEmpty();
        scenario.ResetObservation();
        scenario.BeforeCommit = () =>
        {
            if (scenario.PostCommitEffects.Count != 0)
                throw new InvalidOperationException("Local post-commit effects escaped before commit.");
        };
        bool observedPendingCredentialAfterApplicationCommit = false;
        scenario.OnMetadataRefresh = async () =>
        {
            var committed = (await scenario.ReadBootstrapAsync())!;
            await Assert.That(committed.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
            await Assert.That((await scenario.ReadLocalProvisioningAsync(committed.Id))!.Receipt.Stage)
                .IsEqualTo(Explore.Application.Contracts.Identity.LocalCredentialOperationStage.ProvisioningPending);
            observedPendingCredentialAfterApplicationCommit = true;
        };

        var response = await scenario.CompleteProvisionedLocalAsync();
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(observedPendingCredentialAfterApplicationCommit).IsTrue();
        await scenario.AssertLocalCompletedAsync();
        var completed = (await scenario.ReadBootstrapAsync())!;
        await Assert.That(completed.ProviderKind).IsEqualTo(AuthenticationProviderKind.Local);
        await Assert.That(completed.CompletedByUserId).IsEqualTo(scenario.UserId);
        // Native activation is asserted from its durable receipt, not a fabricated event.
        var receipt = await scenario.ReadLocalProvisioningAsync(completed.Id);
        await Assert.That(receipt!.Receipt.Stage).IsEqualTo(Explore.Application.Contracts.Identity.LocalCredentialOperationStage.ChangeRequired);
        await Assert.That(scenario.EventSequence.Last()).IsEqualTo("credential-activation");
        scenario.ResetObservation();
        scenario.BeforeCommit = null;
        scenario.OnMetadataRefresh = null;
        var replay = await scenario.ClaimAsync();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(response.Id);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(await scenario.Context.Users.Select(user => user.Id).ToListAsync()).IsEquivalentTo([scenario.UserId]);
        await Assert.That(scenario.PostCommitEffects).IsEquivalentTo(["secret-lock", "deployment-cache", "jwt-reload", "audit"]);
    }

    [Test]
    public async Task ExactCompletedReplayReturnsOriginalBootstrapWithoutWritesAndReconcilesEffects()
    {
        await using var scenario = await CompletionFixture.CreateAsync();
        var initial = await scenario.ClaimAsync();
        await Assert.That(initial.IsSuccess).IsTrue();
        Guid bootstrapId = (await scenario.ReadBootstrapAsync())!.Id;
        scenario.ResetObservation();
        var response = await scenario.ClaimAsync();
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsEqualTo(bootstrapId);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.PostCommitEffects).IsEquivalentTo(["secret-lock", "deployment-cache", "jwt-reload", "audit"]);
    }
}
