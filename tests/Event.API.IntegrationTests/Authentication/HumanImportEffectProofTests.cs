using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class HumanImportEffectProofTests
{
    [Test]
    public async Task PublicAcquisitionDoesNotIssueAnApplyEffectProof()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await GrantInstanceAsync(fixture);
        await using var selected = await fixture.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var human = selected.Services.GetRequiredService<IHumanCommitScopePreparation>();
        var issuer = selected.Services.GetRequiredService<IHumanConfigurationImportAdmission>();
        var prepared = await issuer.PrepareAsync(human.Subject!, ConfigurationImportTarget.ForInstance(),
            null, CancellationToken.None);
        var effect = ThemeEffect();
        await selected.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(token);
            await Assert.That(issuer.CurrentProof).IsNull();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                issuer.RequireOwnedAsync(new Counterfeit(), effect, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                issuer.RequireCurrentOwnedAsync(effect.Scope, null, null, effect.Key, effect.Kind, effect.Value,
                    cancellationToken: token));
            return true;
        });
    }

    [Test]
    [Arguments("commit")]
    [Arguments("storage_failure")]
    [Arguments("credential_expiry")]
    public async Task ActualApplyIssuesOnlyExactEffectsAndRevokesProofOnExit(string outcome)
    {
        var probe = new ApplyProbe();
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync(probe);
        await GrantInstanceAsync(fixture);
        var originalTheme = await fixture.Context.SystemSettings.SingleAsync(
            row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key);
        originalTheme.Value = "\"light\"";
        await fixture.Context.SaveChangesAsync();
        var human = fixture.Services.GetRequiredService<IHumanCommitScopePreparation>();
        await human.PrepareAsync(CancellationToken.None);
        await using var scope = fixture.CreateScope();
        var context = await fixture.Services.GetRequiredService<IGlobalExploreDbContextFactory>().CreateDbContextAsync();
        context.BindConfigurationInstanceImportOwner();
        var lease = scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.Bind(context, null);
        lease.BindHuman(human.Subject!, HumanAdministratorCommitPurpose.ConfigurationImport, null, fixture.Lifetime);
        var snapshot = await scope.ServiceProvider.GetRequiredService<ConfigurationManifestCurrentStateReader>()
            .ReadImportSnapshotAsync(ConfigurationManifestExportView.Portable, null, default);
        var manifest = JsonNode.Parse(snapshot.Utf8Json.Span)!;
        manifest["spec"]!["instance"]!["settings"] = new JsonObject
        {
            [AppearanceSettingDefinitions.DefaultThemeMode.Key] = "dark"
        };
        var sessions = scope.ServiceProvider.GetRequiredService<ConfigurationImportSessionApplicationService>();
        byte[] artifact = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var created = await sessions.CreateInstanceAsync(artifact, default);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["instance.settings"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };
        await Assert.That((await sessions.PreviewInstanceAsync(created.SessionId, created.AccessToken, request, default))
            .IsApplyReady).IsTrue();
        var issuer = scope.ServiceProvider.GetRequiredService<IHumanConfigurationImportAdmission>();
        var foreign = fixture.Services.GetRequiredService<IHumanConfigurationImportAdmission>();
        var parser = scope.ServiceProvider.GetRequiredService<ConfigurationImportArtifactParser>();
        await Assert.ThrowsAsync<ConfigurationImportSessionException>(() => scope.ServiceProvider
            .GetRequiredService<ConfigurationImportSectionApplier>().ApplyAsync(
                ConfigurationImportTarget.ForInstance(), artifact, request, human.Subject!, fixture.UserId,
                fixture.Clock.GetUtcNow().UtcDateTime, parser.Parse(snapshot.Utf8Json).Sha256Digest, parser, default));
        await Assert.That(issuer.CurrentProof).IsNull();
        var effect = ThemeEffect();
        IHumanConfigurationImportEffectProof? retained = null;
        IHumanConfigurationImportEffectProof? child = null;
        var ownerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? escapedCheck = null;
        probe.Observe = async token =>
        {
            retained = issuer.CurrentProof;
            await Assert.That(retained).IsNotNull();
            await issuer.RequireOwnedAsync(retained!, effect, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                foreign.RequireOwnedAsync(retained!, effect, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                issuer.RequireOwnedAsync(new Counterfeit(), effect, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                issuer.RequireOwnedAsync(retained!, effect with { Value = "\"light\"" }, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                issuer.RequireOwnedAsync(retained!, effect with { Kind = HumanSettingsMutationKind.Remove }, token));
            await issuer.RequireOwnedAsync(retained!, effect with
            { Kind = HumanSettingsMutationKind.SetLock, Value = null, IsLocked = false }, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                issuer.RequireOwnedAsync(retained!, effect with
                { Kind = HumanSettingsMutationKind.SetLock, Value = null, IsLocked = true }, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                issuer.RequireOwnedAsync(retained!, effect with
                { Key = GovernanceSettingKeys.EventResources.MaxActiveResources }, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                issuer.RequireOwnedAsync(retained!, effect with { Scope = SettingScope.Tenant, TenantId = fixture.TenantId }, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                issuer.ExecuteJoinedAsync(SettingsMutationOperation.ConfigurationTenantImport, [effect],
                    (_, _) => Task.FromResult(true), token));
            await issuer.ExecuteJoinedAsync(SettingsMutationOperation.ConfigurationInstanceImport, [effect],
                async (proof, joinedToken) =>
                {
                    child = proof;
                    await issuer.RequireOwnedAsync(proof, effect, joinedToken);
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                        issuer.RequireOwnedAsync(proof, effect with
                        { Kind = HumanSettingsMutationKind.SetLock, Value = null, IsLocked = false }, joinedToken));
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                        issuer.RequireOwnedAsync(retained!, effect, joinedToken));
                    return true;
                }, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                issuer.RequireOwnedAsync(child!, effect, token));
            // Inherit the actual active AsyncLocal proof, then wait for the real owner to exit.
            // A copied execution context must not keep a capability alive after callback disposal.
            escapedCheck = Task.Run(async () =>
            {
                await ownerExited.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await Assert.That(issuer.CurrentProof).IsNull();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    issuer.RequireOwnedAsync(retained!, effect));
            });
            if (outcome == "credential_expiry")
            {
                fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
                await issuer.RequireOwnedAsync(retained!, effect, token);
            }
            if (outcome == "storage_failure")
                throw new RejectedStorageWrite();
        };
        var apply = scope.ServiceProvider.GetRequiredService<ConfigurationImportApplyService>()
            .ApplyInstanceAsync(created.SessionId, created.AccessToken, request, human.Subject!, null, null, default);
        try
        {
            if (outcome == "storage_failure")
                await Assert.ThrowsAsync<RejectedStorageWrite>(() => apply);
            else if (outcome == "credential_expiry")
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => apply);
            else
                await Assert.That((await apply).Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
        }
        finally { ownerExited.TrySetResult(); }
        await Assert.That(probe.Reached).IsTrue();
        await (escapedCheck ?? throw new InvalidOperationException("The admitted callback was not reached."))
            .WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(issuer.CurrentProof).IsNull();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            issuer.RequireOwnedAsync(retained!, effect));
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.ConfigurationImportOperations.AnyAsync(row => row.SessionId == created.SessionId))
            .IsEqualTo(outcome == "commit");
        await Assert.That(await observer.Set<ConfigurationImportSession>()
            .Where(row => row.SessionId == created.SessionId).Select(row => row.State).SingleAsync())
            .IsEqualTo(outcome == "commit" ? ConfigurationImportSessionState.Consumed : ConfigurationImportSessionState.PreviewReady);
        await Assert.That(await observer.SystemSettings.Where(
            row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key).Select(row => row.Value).SingleAsync())
            .IsEqualTo(outcome == "commit" ? "\"dark\"" : "\"light\"");
    }

    private static HumanConfigurationImportSettingEffect ThemeEffect() =>
        new(SettingScope.Instance, null, AppearanceSettingDefinitions.DefaultThemeMode.Key,
            HumanSettingsMutationKind.SetValue, "\"dark\"");

    private static async Task GrantInstanceAsync(ResourceVisitorAuthenticatedFixture fixture)
    {
        fixture.Context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        if (!await fixture.Context.PaidEventPolicyVersions.AnyAsync())
            fixture.Context.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
        await fixture.Context.SaveChangesAsync();
    }

    private sealed class Counterfeit : IHumanConfigurationImportEffectProof;
    private sealed class RejectedStorageWrite : Exception;

    private sealed class ApplyProbe : SaveChangesInterceptor
    {
        internal Func<CancellationToken, Task>? Observe { get; set; }
        internal bool Reached { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Reached && Observe is { } observe && eventData.Context!.ChangeTracker.Entries<SystemSetting>()
                .Any(entry => entry.Entity.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key
                    && entry.State is EntityState.Added or EntityState.Modified))
            {
                Reached = true;
                await observe(cancellationToken);
            }
            return result;
        }
    }
}
