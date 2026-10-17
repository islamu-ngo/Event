using EventVisitorCapabilitySqliteFixture = Event.Api.IntegrationTests.Authentication.ResourceVisitorAuthenticatedFixture;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence.Database;
using Explore.Persistence.Entities;
using Explore.Persistence;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class ConfigurationImportHumanCommitAuthorityTests
{
    [Test]
    public async Task StaleHumanImportWritesOnlyFailedHistoryAndRemainsConsumable()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
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
        var executor = fixture.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
        var snapshot = await executor.ReadSnapshotAsync(ConfigurationManifestExportView.Portable, default);
        var created = await executor.CreateAsync(snapshot.Utf8Json, default);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["instance.settings"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };
        await Assert.That((await executor.PreviewAsync(created.SessionId, created.AccessToken, request, default))
            .IsApplyReady).IsTrue();
        var theme = await fixture.Context.SystemSettings.SingleAsync(
            row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key);
        theme.Value = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<string>(theme.Value) == "dark" ? "light" : "dark");
        await fixture.Context.SaveChangesAsync();
        string changedTheme = theme.Value;
        await Assert.ThrowsAsync<ConfigurationImportSessionException>(() =>
            executor.ApplyAsync(created.SessionId, created.AccessToken, request, null, null, default));
        await using (var observer = fixture.CreateIndependentContext())
        {
            var failure = await observer.ConfigurationImportOperations.AsNoTracking()
                .SingleAsync(row => row.SessionId == created.SessionId);
            await Assert.That(failure.Status).IsEqualTo(ConfigurationImportOperationStatus.Failed);
            await Assert.That(failure.FailureCode).IsEqualTo(ConfigurationImportFailureCodes.StalePreview);
            await Assert.That(failure.ActorUserId).IsEqualTo(fixture.UserId);
            await Assert.That(failure.EffectOutboxId).IsNull();
            await Assert.That(failure.SnapshotArtifactHandleId).IsNull();
            await Assert.That(await observer.SystemSettings.Where(
                row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key)
                .Select(row => row.Value).SingleAsync()).IsEqualTo(changedTheme);
            await Assert.That(await observer.Set<ConfigurationImportSession>()
                .Where(row => row.SessionId == created.SessionId).Select(row => row.State).SingleAsync())
                .IsEqualTo(ConfigurationImportSessionState.PreviewReady);
        }
        await executor.PreviewAsync(created.SessionId, created.AccessToken, request, default);
        var receipt = await executor.ApplyAsync(created.SessionId, created.AccessToken, request, null, null, default);
        await Assert.That(receipt.Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
        await using var finalObserver = fixture.CreateIndependentContext();
        await Assert.That(await finalObserver.ConfigurationImportOperations.CountAsync(
            row => row.SessionId == created.SessionId)).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HumanImportConsumesOnlyOriginalAcknowledgedCommitWitness(bool counterfeit)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
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
        var subject = (await HumanCommitSubject.ResolveAsync(
            fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            CancellationToken.None))!;
        await using var scope = fixture.CreateScope();
        var context = await fixture.Services.GetRequiredService<IGlobalExploreDbContextFactory>().CreateDbContextAsync();
        context.BindConfigurationInstanceImportOwner();
        var lease = scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.Bind(context, null);
        lease.BindHuman(subject, Explore.Persistence.Security.HumanAdministratorCommitPurpose.ConfigurationImport,
            null, fixture.Lifetime);
        var snapshot = await scope.ServiceProvider.GetRequiredService<ConfigurationManifestCurrentStateReader>()
            .ReadImportSnapshotAsync(ConfigurationManifestExportView.Portable, null, default);
        var sessions = scope.ServiceProvider.GetRequiredService<ConfigurationImportSessionApplicationService>();
        var created = await sessions.CreateInstanceAsync(snapshot.Utf8Json, default);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["instance.settings"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };
        await Assert.That((await sessions.PreviewInstanceAsync(created.SessionId, created.AccessToken, request, default))
            .IsApplyReady).IsTrue();
        var importOwners = (IHumanConfigurationImportOwnerFactory)scope.ServiceProvider
            .GetRequiredService<IPolicyMutationExecutionScopeFactory>();
        var boundary = new CommitWitnessBoundary(
            importOwners.GetImportSettlementBoundary(fixture.Lifetime), counterfeit);
        var apply = ActivatorUtilities.CreateInstance<ConfigurationImportApplyService>(
            scope.ServiceProvider, new WitnessServices(scope.ServiceProvider, boundary));
        var operation = apply.ApplyInstanceAsync(created.SessionId, created.AccessToken, request, subject, null, null, default);
        if (counterfeit)
            await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        else
            await Assert.That((await operation).Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
        await Assert.That(boundary.ConsumeReached).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.CreateIndependentContext();
        var persisted = await observer.ConfigurationImportOperations.AsNoTracking()
            .Where(row => row.SessionId == created.SessionId).ToArrayAsync();
        await Assert.That(persisted.Length).IsEqualTo(1);
        await Assert.That(persisted.Single().Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
        // A refused receipt cannot retroactively turn an acknowledged commit into failed history.
    }

    private sealed class WitnessServices(IServiceProvider inner, IOriginalOwnerSettlementBoundary boundary)
        : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IOriginalOwnerSettlementBoundary) ? boundary
            : serviceType == typeof(IPolicyMutationExecutionScopeFactory)
                ? new WitnessOwnerFactory(inner.GetRequiredService<IPolicyMutationExecutionScopeFactory>(), boundary)
                : inner.GetService(serviceType);
    }

    private sealed class WitnessOwnerFactory(
        IPolicyMutationExecutionScopeFactory inner, IOriginalOwnerSettlementBoundary boundary)
        : IPolicyMutationExecutionScopeFactory, IHumanConfigurationImportOwnerFactory
    {
        public ValueTask<IPolicyMutationExecutionScope> CreateAsync(
            PolicyMutationScope selection, CancellationToken cancellationToken) =>
            inner.CreateAsync(selection, cancellationToken);
        public ValueTask<IPolicyMutationExecutionScope> CreateFreshImportOwnerAsync(
            HumanConfigurationImportAuthority original, CancellationToken cancellationToken) =>
            ((IHumanConfigurationImportOwnerFactory)inner).CreateFreshImportOwnerAsync(original, cancellationToken);
        public IOriginalOwnerSettlementBoundary GetImportSettlementBoundary(
            Explore.Application.Authentication.HumanAuthenticationLifetime lifetime) => boundary;
        public IHumanConfigurationImportSourceReader GetImportSourceReader() =>
            ((IHumanConfigurationImportOwnerFactory)inner).GetImportSourceReader();
    }

    private sealed class CommitWitnessBoundary(IOriginalOwnerSettlementBoundary inner, bool counterfeit)
        : IOriginalOwnerSettlementBoundary
    {
        internal bool ConsumeReached { get; private set; }
        internal Func<Task>? AfterRollback { get; set; }
        internal bool ThrowUnclassifiedFailure { get; set; }
        public async Task<IOriginalOwnerSettlement<T>> ExecuteAsync<T>(object invocation,
            Func<CancellationToken, Task<OriginalOwnerDecision<T>>> operation, CancellationToken cancellationToken = default)
        {
            var witness = await inner.ExecuteAsync(invocation, async token =>
            {
                if (ThrowUnclassifiedFailure)
                    throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ApplyBlocked);
                return await operation(token);
            }, cancellationToken);
            if (witness.Disposition == OriginalOwnerDisposition.WholeTransactionRolledBack && AfterRollback is { } callback)
                await callback();
            return counterfeit ? new Counterfeit<T>(witness.Disposition) : witness;
        }
        public T Consume<T>(object invocation, IOriginalOwnerSettlement<T> settlement, OriginalOwnerDisposition expectedDisposition)
        {
            ConsumeReached = true;
            return inner.Consume(invocation, settlement, expectedDisposition);
        }
        private sealed class Counterfeit<T>(OriginalOwnerDisposition disposition) : IOriginalOwnerSettlement<T>
        {
            public OriginalOwnerDisposition Disposition { get; } = disposition;
        }
    }

    [Test]
    [Arguments("genuine")]
    [Arguments("counterfeit")]
    [Arguments("unclassified")]
    [Arguments("revoked_gap")]
    [Arguments("expired_gap")]
    [Arguments("retention_gap")]
    [Arguments("own_revocation")]
    [Arguments("own_session_cancel")]
    [Arguments("final_expiry")]
    public async Task HumanRejectedHistoryRequiresActualRollbackAndFreshOriginalAdmission(string control)
    {
        var interceptor = new ReceiptAdmission();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(interceptor);
        var grant = new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        };
        fixture.Context.PlatformUserRoles.Add(grant);
        if (!await fixture.Context.PaidEventPolicyVersions.AnyAsync())
            fixture.Context.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
        await fixture.Context.SaveChangesAsync();
        var subject = (await HumanCommitSubject.ResolveAsync(
            fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(), default))!;
        await using var scope = fixture.CreateScope();
        var context = await fixture.Services.GetRequiredService<IGlobalExploreDbContextFactory>().CreateDbContextAsync();
        context.BindConfigurationInstanceImportOwner();
        var lease = scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.Bind(context, null);
        lease.BindHuman(subject, Explore.Persistence.Security.HumanAdministratorCommitPurpose.ConfigurationImport,
            null, fixture.Lifetime);
        var snapshot = await scope.ServiceProvider.GetRequiredService<ConfigurationManifestCurrentStateReader>()
            .ReadImportSnapshotAsync(ConfigurationManifestExportView.Portable, null, default);
        var sessions = scope.ServiceProvider.GetRequiredService<ConfigurationImportSessionApplicationService>();
        var created = await sessions.CreateInstanceAsync(snapshot.Utf8Json, default);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["instance.settings"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };
        await Assert.That((await sessions.PreviewInstanceAsync(created.SessionId, created.AccessToken, request, default))
            .IsApplyReady).IsTrue();
        var theme = await fixture.Context.SystemSettings.SingleAsync(
            row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key);
        theme.Value = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<string>(theme.Value) == "dark" ? "light" : "dark");
        await fixture.Context.SaveChangesAsync();
        string retainedTheme = theme.Value;
        var importOwners = (IHumanConfigurationImportOwnerFactory)scope.ServiceProvider
            .GetRequiredService<IPolicyMutationExecutionScopeFactory>();
        var boundary = new CommitWitnessBoundary(
            importOwners.GetImportSettlementBoundary(fixture.Lifetime), control == "counterfeit")
        {
            ThrowUnclassifiedFailure = control == "unclassified",
            AfterRollback = async () =>
            {
                if (control == "revoked_gap")
                    await fixture.Context.PlatformUserRoles.Where(row => row.Id == grant.Id).ExecuteDeleteAsync();
                if (control == "expired_gap")
                    fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1)
                        - fixture.Clock.GetUtcNow());
                if (control == "retention_gap")
                    await fixture.Context.Set<ConfigurationImportSession>()
                        .Where(row => row.SessionId == created.SessionId).ExecuteDeleteAsync();
            }
        };
        interceptor.BeforeFailedReceipt = async (database, token) =>
        {
            if (control == "own_revocation")
                await database.PlatformUserRoles.Where(row => row.Id == grant.Id).ExecuteDeleteAsync(token);
            if (control == "own_session_cancel")
                await database.Set<ConfigurationImportSession>().Where(row => row.SessionId == created.SessionId)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(row => row.State, ConfigurationImportSessionState.Cancelled)
                        .SetProperty(row => row.CancelledAt, fixture.Clock.GetUtcNow().UtcDateTime)
                        .SetProperty(row => row.Revision, row => row.Revision + 1), token);
            if (control == "final_expiry")
                fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
        };
        var apply = ActivatorUtilities.CreateInstance<ConfigurationImportApplyService>(
            scope.ServiceProvider, new WitnessServices(scope.ServiceProvider, boundary));
        Exception? failure = null;
        try
        {
            await apply.ApplyInstanceAsync(created.SessionId, created.AccessToken, request, subject, null, null, default);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(boundary.ConsumeReached).IsEqualTo(control != "unclassified");
        if (control is "own_revocation" or "own_session_cancel" or "final_expiry")
            await Assert.That(interceptor.BeforeFailedReceipt).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.CreateIndependentContext();
        var receipts = await observer.ConfigurationImportOperations.AsNoTracking()
            .Where(row => row.SessionId == created.SessionId).ToArrayAsync();
        await Assert.That(receipts.Length).IsEqualTo(control == "genuine" ? 1 : 0);
        if (control == "genuine")
        {
            await Assert.That(receipts.Single().FailureCode).IsEqualTo(ConfigurationImportFailureCodes.StalePreview);
            await Assert.That(receipts.Single().ActorUserId).IsEqualTo(fixture.UserId);
            await Assert.That(receipts.Single().EffectOutboxId).IsNull();
            await Assert.That(((ConfigurationImportSessionException)failure!).FailureCode)
                .IsEqualTo(ConfigurationImportFailureCodes.StalePreview);
        }
        await Assert.That(await observer.SystemSettings.Where(
            row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(retainedTheme);
        await Assert.That(await observer.OutboxMessages.AnyAsync(
            row => row.AggregateType == nameof(ConfigurationImportOperation))).IsFalse();
        if (control != "retention_gap")
            await Assert.That(await observer.Set<ConfigurationImportSession>()
                .Where(row => row.SessionId == created.SessionId).Select(row => row.State).SingleAsync())
                .IsEqualTo(ConfigurationImportSessionState.PreviewReady);
        if (control == "own_revocation")
            await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.Id == grant.Id)).IsTrue();
    }

    [Test]
    public async Task LateFidelityRefusalRollsBackBusinessStateBeforeOneFailedReceipt()
    {
        var divergence = new FidelityDivergence();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(divergence);
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
        var executor = fixture.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
        var snapshot = await executor.ReadSnapshotAsync(ConfigurationManifestExportView.Portable, default);
        var artifact = JsonNode.Parse(snapshot.Utf8Json.Span)!;
        string originalTheme = await fixture.Context.SystemSettings.AsNoTracking()
            .Where(row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key)
            .Select(row => row.Value).SingleAsync();
        string desiredTheme = JsonSerializer.Deserialize<string>(originalTheme) == "dark" ? "light" : "dark";
        artifact["spec"]!["instance"]!["settings"]![AppearanceSettingDefinitions.DefaultThemeMode.Key] = desiredTheme;
        var created = await executor.CreateAsync(Encoding.UTF8.GetBytes(artifact.ToJsonString()), default);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["instance.settings"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };
        await Assert.That((await executor.PreviewAsync(created.SessionId, created.AccessToken, request, default))
            .IsApplyReady).IsTrue();
        int artifactCount = await fixture.Context.Set<ConfigurationImportStoredArtifact>().CountAsync();
        divergence.Desired = JsonSerializer.Serialize(desiredTheme);
        divergence.Original = originalTheme;
        divergence.SessionId = created.SessionId;
        await Assert.ThrowsAsync<ConfigurationImportSessionException>(() =>
            executor.ApplyAsync(created.SessionId, created.AccessToken, request, null, null, default));
        await Assert.That(divergence.Reached).IsTrue();
        await using var observer = fixture.CreateIndependentContext();
        var history = await observer.ConfigurationImportOperations.AsNoTracking()
            .SingleAsync(row => row.SessionId == created.SessionId);
        await Assert.That(history.Status).IsEqualTo(ConfigurationImportOperationStatus.Failed);
        await Assert.That(history.FailureCode).IsEqualTo(ConfigurationImportFailureCodes.ApplyFailed);
        await Assert.That(history.ActorUserId).IsEqualTo(fixture.UserId);
        await Assert.That(history.SnapshotArtifactHandleId).IsNull();
        await Assert.That(history.EffectOutboxId).IsNull();
        await Assert.That(await observer.SystemSettings.Where(
            row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(originalTheme);
        await Assert.That(await observer.Set<ConfigurationImportSession>()
            .Where(row => row.SessionId == created.SessionId).Select(row => row.State).SingleAsync())
            .IsEqualTo(ConfigurationImportSessionState.PreviewReady);
        await Assert.That(await observer.Set<ConfigurationImportStoredArtifact>().CountAsync()).IsEqualTo(artifactCount);
        await Assert.That(await observer.OutboxMessages.AnyAsync(
            row => row.AggregateType == nameof(ConfigurationImportOperation))).IsFalse();
    }

    private sealed class FidelityDivergence : SaveChangesInterceptor
    {
        internal Guid SessionId { get; set; }
        internal string? Desired { get; set; }
        internal string? Original { get; set; }
        internal bool Reached { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Reached && Desired is not null && Original is not null && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ConfigurationImportSession>().Any(entry =>
                    entry.Entity.SessionId == SessionId && entry.Entity.State == ConfigurationImportSessionState.Consumed)
                && await context.SystemSettings.AsNoTracking().AnyAsync(row =>
                    row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key && row.Value == Desired,
                    cancellationToken))
            {
                Reached = true;
                await context.SystemSettings.Where(row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, Original), cancellationToken);
            }
            return result;
        }
    }

    [Test]
    public async Task Import_RoutesResourceSettingsOnlyThroughCoordinatedWriter()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
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
        var executor = fixture.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
        var snapshot = await executor.ReadSnapshotAsync(ConfigurationManifestExportView.Portable, default);
        var artifact = JsonNode.Parse(snapshot.Utf8Json.Span)!;
        var settings = artifact["spec"]!["instance"]!["settings"]!;
        settings[EventResourceSettingDefinitions.MaxActiveResources.Key] = 100;
        settings[AppearanceSettingDefinitions.DefaultThemeMode.Key] = "system";
        var session = await executor.CreateAsync(Encoding.UTF8.GetBytes(artifact.ToJsonString()), default);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["instance.settings"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };
        await Assert.That((await executor.PreviewAsync(session.SessionId, session.AccessToken, request, default)).IsApplyReady)
            .IsTrue();
        var receipt = await executor.ApplyAsync(session.SessionId, session.AccessToken, request, null, null, default);
        await Assert.That(receipt.SessionId).IsEqualTo(session.SessionId);
        await Assert.That(receipt.Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
        await Assert.That(receipt.FidelityVerified).IsTrue();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == EventResourceSettingDefinitions.MaxActiveResources.Key)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("100");
        string theme = await observer.SystemSettings.Where(row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key)
            .Select(row => row.Value).SingleAsync();
        await Assert.That(JsonSerializer.Deserialize<string>(theme)).IsEqualTo("system");
        var operation = await observer.ConfigurationImportOperations.SingleAsync(row => row.Id == receipt.OperationId);
        var effect = await observer.OutboxMessages.SingleAsync(row => row.Id == operation.EffectOutboxId);
        await Assert.That(ConfigurationImportEffectOutbox.ReadNotifications(effect.Payload)
            .Any(notification => notification.Key == EventResourceSettingDefinitions.MaxActiveResources.Key
                && notification.NewValue == "100")).IsTrue();
    }

    [Test]
    [Arguments("unchanged")]
    [Arguments("revoked_before_acquisition")]
    [Arguments("own_revocation")]
    [Arguments("credential_expiry")]
    public async Task InstanceImportRefencesAdministrationEvenForUnchangedSections(string change)
    {
        var interceptor = new ReceiptAdmission();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(interceptor);
        var grant = new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        };
        fixture.Context.PlatformUserRoles.Add(grant);
        if (!await fixture.Context.PaidEventPolicyVersions.AnyAsync())
            fixture.Context.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
        await fixture.Context.SaveChangesAsync();
        var subject = (await HumanCommitSubject.ResolveAsync(
            fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            CancellationToken.None))!;

        await using var owner = fixture.CreateScope();
        var context = await fixture.Services.GetRequiredService<IGlobalExploreDbContextFactory>()
            .CreateDbContextAsync();
        context.BindConfigurationInstanceImportOwner();
        var lease = owner.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.Bind(context, null);
        lease.BindHuman(subject, Explore.Persistence.Security.HumanAdministratorCommitPurpose.ConfigurationImport,
            null, fixture.Lifetime);
        var reader = owner.ServiceProvider.GetRequiredService<ConfigurationManifestCurrentStateReader>();
        var snapshot = await reader.ReadImportSnapshotAsync(ConfigurationManifestExportView.Portable, null, default);
        var sessions = owner.ServiceProvider.GetRequiredService<ConfigurationImportSessionApplicationService>();
        var created = await sessions.CreateInstanceAsync(snapshot.Utf8Json, default);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["instance.settings"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };
        var preview = await sessions.PreviewInstanceAsync(created.SessionId, created.AccessToken, request, default);
        await Assert.That(preview.IsApplyReady).IsTrue();
        var before = await context.SystemSettings.AsNoTracking().OrderBy(row => row.SettingKey)
            .Select(row => new { row.SettingKey, row.Value }).ToArrayAsync();
        var parser = owner.ServiceProvider.GetRequiredService<ConfigurationImportArtifactParser>();
        string settingsDigest = ConfigurationImportArtifactSnapshotFactory
            .FromManifest(parser.Parse(snapshot.Utf8Json).Manifest)
            .Single(section => section.SectionKey == "instance.settings").NormalizedDigest;
        if (change == "revoked_before_acquisition")
            await fixture.Context.PlatformUserRoles.Where(row => row.Id == grant.Id).ExecuteDeleteAsync();
        interceptor.BeforeReceipt = async (database, token) =>
        {
            if (change == "own_revocation")
                await database.PlatformUserRoles.Where(row => row.Id == grant.Id).ExecuteDeleteAsync(token);
            if (change == "credential_expiry")
                owner.ServiceProvider.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await database.PlatformUserRoles.AsNoTracking().SingleAsync(row => row.Id == grant.Id, finalToken);
                    fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1)
                        - fixture.Clock.GetUtcNow());
                }, _ => { });
        };
        var apply = owner.ServiceProvider.GetRequiredService<ConfigurationImportApplyService>();
        if (change != "unchanged")
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => apply.ApplyInstanceAsync(
                created.SessionId, created.AccessToken, request, subject, null, null, default));
            var session = await owner.ServiceProvider.GetRequiredService<IConfigurationImportSessionRepository>()
                .GetForUpdateAsync(created.SessionId, ConfigurationImportTarget.ForInstance(), default);
            await Assert.That(session!.State).IsEqualTo(ConfigurationImportSessionState.PreviewReady);
            await Assert.That(await context.ConfigurationImportOperations.AsNoTracking()
                .AnyAsync(row => row.SessionId == created.SessionId)).IsFalse();
            await Assert.That(await context.OutboxMessages.AsNoTracking()
                .AnyAsync(row => row.AggregateType == nameof(ConfigurationImportOperation))).IsFalse();
            var afterDenial = await context.SystemSettings.AsNoTracking().OrderBy(row => row.SettingKey)
                .Select(row => new { row.SettingKey, row.Value }).ToArrayAsync();
            await Assert.That(afterDenial).IsEquivalentTo(before);
        }
        else
        {
            var receipt = await apply.ApplyInstanceAsync(
                created.SessionId, created.AccessToken, request, subject, null, null, default);
            await Assert.That(receipt.Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
            await Assert.That(receipt.FidelityVerified).IsTrue();
            await Assert.That(await context.ConfigurationImportOperations.AsNoTracking()
                .CountAsync(row => row.SessionId == created.SessionId)).IsEqualTo(1);
        }
        var after = await reader.ReadImportSnapshotAsync(ConfigurationManifestExportView.Portable, null, default);
        await Assert.That(ConfigurationImportArtifactSnapshotFactory
            .FromManifest(parser.Parse(after.Utf8Json).Manifest)
            .Single(section => section.SectionKey == "instance.settings").NormalizedDigest)
            .IsEqualTo(settingsDigest);
    }

    private sealed class ReceiptAdmission : SaveChangesInterceptor
    {
        internal Func<ExploreDbContext, CancellationToken, Task>? BeforeReceipt { get; set; }
        internal Func<ExploreDbContext, CancellationToken, Task>? BeforeFailedReceipt { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext context && BeforeReceipt is { } callback
                && context.ChangeTracker.Entries<ConfigurationImportOperation>()
                    .Any(entry => entry.State == EntityState.Added
                        && entry.Entity.Status == ConfigurationImportOperationStatus.Applied))
            {
                BeforeReceipt = null;
                await callback(context, cancellationToken);
            }
            if (eventData.Context is ExploreDbContext failedContext && BeforeFailedReceipt is { } failedCallback
                && failedContext.ChangeTracker.Entries<ConfigurationImportOperation>()
                    .Any(entry => entry.State == EntityState.Added
                        && entry.Entity.Status == ConfigurationImportOperationStatus.Failed))
            {
                BeforeFailedReceipt = null;
                await failedCallback(failedContext, cancellationToken);
            }
            return result;
        }
    }
}
