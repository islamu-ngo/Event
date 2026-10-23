using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence.Database;
using Explore.Persistence.Entities;
using Explore.Persistence.Security;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class HumanImportPublicationCustodyTests
{
    [Test]
    [Arguments("commit")]
    [Arguments("metadata_drift")]
    [Arguments("late_failure")]
    [Arguments("credential_expiry")]
    [Arguments("before_write_metadata_drift")]
    [Arguments("identity_drift")]
    [Arguments("absence_drift")]
    [Arguments("unselected_key_drift")]
    public async Task OriginalImportPublicationRequiresItsWriterAndUnchangedTerminalSources(string outcome)
    {
        var probe = new AfterPublication(outcome == "before_write_metadata_drift");
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync(probe);
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
        string key = EventSettingDefinitions.RequireApproval.Key;
        await fixture.Context.SystemSettings.Where(row => row.SettingKey == key)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "false")
                .SetProperty(row => row.IsLocked, false));
        await fixture.Context.SystemSettings.Where(row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"light\""));
        var original = await fixture.Context.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key);
        var human = fixture.Services.GetRequiredService<IHumanCommitScopePreparation>();
        await human.PrepareAsync(default);
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
            [key] = true,
            [AppearanceSettingDefinitions.DefaultThemeMode.Key] = "dark"
        };
        var sessions = scope.ServiceProvider.GetRequiredService<ConfigurationImportSessionApplicationService>();
        var created = await sessions.CreateInstanceAsync(JsonSerializer.SerializeToUtf8Bytes(manifest), default);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["instance.settings"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };
        await Assert.That((await sessions.PreviewInstanceAsync(created.SessionId, created.AccessToken, request, default))
            .IsApplyReady).IsTrue();
        probe.Observe = async token =>
        {
            if (outcome == "before_write_metadata_drift")
            {
                await context.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "changed after first observation"), token);
                return;
            }
            await Assert.That(await context.SystemSettings.AsNoTracking()
                .Where(row => row.SettingKey == key).Select(row => row.Value).SingleAsync(token)).IsEqualTo("true");
            // An ordinary publication request cannot borrow the active human import.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                scope.ServiceProvider.GetRequiredService<IPublicationPolicyMutationBoundary>()
                    .ApplyInstanceInCurrentTransactionAsync(new(fixture.UserId, fixture.Clock.GetUtcNow().UtcDateTime,
                        [new(key, PublicationPolicyMutationKind.Set, "true", null, false)]), token));
            if (outcome == "metadata_drift")
                await context.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "changed after native completion"), token);
            if (outcome == "identity_drift")
            {
                Guid replacement = Guid.CreateVersion7();
                await context.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Id, replacement), token);
            }
            if (outcome == "absence_drift")
                await context.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync(token);
            if (outcome == "unselected_key_drift")
                await context.SystemSettings.Where(row => row.SettingKey == PublicationPolicySettingKeys.All[0])
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "changed unselected policy input"), token);
            if (outcome == "late_failure")
                throw new LateFailure();
            if (outcome == "credential_expiry")
                fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
        };
        Task<ConfigurationImportOperationResult> apply = scope.ServiceProvider.GetRequiredService<ConfigurationImportApplyService>()
            .ApplyInstanceAsync(created.SessionId, created.AccessToken, request, human.Subject!, null, null, default);
        if (outcome == "commit")
            await Assert.That((await apply).Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
        else if (outcome == "late_failure")
            await Assert.ThrowsAsync<LateFailure>(() => apply);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => apply);
        await Assert.That(probe.Reached).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.CreateIndependentContext();
        var actual = await observer.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key);
        await Assert.That(actual.Value).IsEqualTo(outcome == "commit" ? "true" : original.Value);
        await Assert.That(actual.Description).IsEqualTo(original.Description);
        await Assert.That(await observer.ConfigurationImportOperations.AnyAsync(row => row.SessionId == created.SessionId))
            .IsEqualTo(outcome == "commit");
        await Assert.That(await observer.Set<ConfigurationImportSession>().Where(row => row.SessionId == created.SessionId)
            .Select(row => row.State).SingleAsync())
            .IsEqualTo(outcome == "commit" ? ConfigurationImportSessionState.Consumed : ConfigurationImportSessionState.PreviewReady);
    }

    private sealed class LateFailure : Exception;

    private sealed class AfterPublication(bool beforeWrite) : SaveChangesInterceptor
    {
        internal Func<CancellationToken, Task>? Observe { get; set; }
        internal bool Reached { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            bool atBoundary = beforeWrite
                ? eventData.Context!.ChangeTracker.Entries<ConfigurationImportStoredArtifact>()
                    .Any(entry => entry.State == EntityState.Added)
                : eventData.Context!.ChangeTracker.Entries<SystemSetting>()
                    .Any(entry => entry.Entity.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key
                        && entry.State is EntityState.Added or EntityState.Modified);
            if (!Reached && Observe is { } observe && atBoundary)
            {
                Reached = true;
                await observe(cancellationToken);
            }
            return result;
        }
    }
}
