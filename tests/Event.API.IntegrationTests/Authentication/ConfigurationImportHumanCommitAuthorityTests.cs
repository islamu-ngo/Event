using EventVisitorCapabilitySqliteFixture = Event.Api.IntegrationTests.Authentication.ResourceVisitorAuthenticatedFixture;
using System.Collections.Immutable;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.SetupLive;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Features.SetupLive;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence.Database;
using Explore.Persistence.Entities;
using Explore.Persistence.QueryFilters;
using Explore.Persistence;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class ConfigurationImportHumanCommitAuthorityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task SetupImportLostCommitAcknowledgementNeverReportsFailedHistoryOrReplays(bool failGenerationRelease) =>
        VerifySetupCommitAsync(loseCommitAcknowledgement: true, failGenerationRelease);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task SetupImportAcknowledgedCommitRetainsAppliedHistoryWhenCoordinationReleaseFails(bool failGenerationRelease) =>
        VerifySetupCommitAsync(loseCommitAcknowledgement: false, failGenerationRelease);

    private static async Task VerifySetupCommitAsync(bool loseCommitAcknowledgement, bool failGenerationRelease)
    {
        var lostAcknowledgement = new SetupImportLostAcknowledgement();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(lostAcknowledgement);
        fixture.Context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        var membership = await fixture.Context.TenantUsers.SingleAsync(row => row.UserId == fixture.UserId);
        membership.ActorId = fixture.ActorId;
        fixture.Context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            TenantUserId = membership.Id,
            TenantUser = null!,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime,
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        if (!await fixture.Context.PaidEventPolicyVersions.AnyAsync())
            fixture.Context.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
        await fixture.Context.SaveChangesAsync();
        var enrollment = await fixture.Services.GetRequiredService<SetupLiveApplicationService>().CreateAsync(
            fixture.TenantId, fixture.UserId, Guid.CreateVersion7(),
            new ISLAMU.Wire.Contracts.SetupLive.CreateSetupTargetEnrollmentRequest
            {
                ClientChallenge = ISLAMU.Wire.Contracts.SetupLive.SetupClientChallenge.FromBytes(RandomNumberGenerator.GetBytes(32)),
                RequestedScopes = [ISLAMU.Wire.Contracts.SetupLive.SetupEnrollmentScope.TargetRead,
                    ISLAMU.Wire.Contracts.SetupLive.SetupEnrollmentScope.ConfigurationImport]
            }, default);
        await Assert.That(enrollment.Status).IsEqualTo(SetupLiveApplicationStatus.Created);
        Guid enrollmentId = enrollment.Data!.EnrollmentId;
        string capability = enrollment.Capability!.ToHeaderValue();
        const string desiredName = "Committed setup import";
        var package = new TenantConfigurationPackageV1Alpha2
        {
            Schema = TenantConfigurationPackageContractMetadata.SchemaId,
            ApiVersion = TenantConfigurationPackageContractMetadata.ApiVersion,
            Kind = TenantConfigurationPackageContractMetadata.Kind,
            Metadata = new TenantConfigurationPackageMetadataV1Alpha2
            {
                Name = "source-community",
                Source = new TenantConfigurationPackageSourceV1Alpha2 { TenantName = "source-community" }
            },
            Spec = new TenantConfigurationPackageSpecV1Alpha2
            {
                DisplayName = desiredName,
                Settings = new Dictionary<string, JsonElement>(),
                Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>()
            }
        };
        var coordinator = new GenerationReleaseFault(
            fixture.Services.GetRequiredService<ISetupSecretBindingOperationCoordinator>(),
            failGenerationRelease);
        var enrollmentService = ActivatorUtilities.CreateInstance<SetupLiveApplicationService>(
            fixture.Services, coordinator);
        var settingRelease = new SettingReleaseFault(fixture.Services.GetRequiredService<ISettingMutationLock>());
        var apply = ActivatorUtilities.CreateInstance<ConfigurationImportApplyService>(fixture.Services, settingRelease);
        var setup = ActivatorUtilities.CreateInstance<SetupConfigurationImportApplicationService>(
            fixture.Services, enrollmentService, apply);
        var created = await setup.CreateAsync(fixture.TenantId, enrollmentId, fixture.UserId, capability,
            JsonSerializer.SerializeToUtf8Bytes(package,
                ConfigurationPortabilityJsonContext.Default.TenantConfigurationPackageV1Alpha2), default);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["tenant.settings"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };
        await Assert.That((await setup.PreviewAsync(fixture.TenantId, enrollmentId, fixture.UserId,
            capability, created.SessionId, created.AccessToken, request, default)).IsApplyReady).IsTrue();
        lostAcknowledgement.Armed = loseCommitAcknowledgement;
        coordinator.Armed = true;
        settingRelease.Armed = !loseCommitAcknowledgement && !failGenerationRelease;
        Exception? failure = null;
        try
        {
            await setup.ApplyAsync(fixture.TenantId, enrollmentId, fixture.UserId, capability,
                created.SessionId, created.AccessToken, request, default);
        }
        catch (Exception observed) { failure = observed; }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure is HumanMutationOutcomeUnknownException).IsEqualTo(loseCommitAcknowledgement);
        await Assert.That(lostAcknowledgement.FaultCount).IsEqualTo(loseCommitAcknowledgement ? 1 : 0);
        await Assert.That(ContainsCause(failure!, lostAcknowledgement.Failure)).IsEqualTo(loseCommitAcknowledgement);
        await Assert.That(ContainsCause(failure!, coordinator.Failure)).IsEqualTo(failGenerationRelease);
        await Assert.That(ContainsCause(failure!, settingRelease.Failure)).IsEqualTo(settingRelease.Armed);
        await using var observer = fixture.CreateIndependentContext();
        var operation = await observer.ConfigurationImportOperations.AsNoTracking()
            .SingleAsync(row => row.SessionId == created.SessionId);
        await Assert.That(operation.Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
        await Assert.That(operation.EffectOutboxId.HasValue).IsTrue();
        await Assert.That(await observer.OutboxMessages.Where(row => row.Id == operation.EffectOutboxId)
            .Select(row => row.Status).SingleAsync()).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(await observer.Set<ConfigurationImportSession>()
            .Where(row => row.SessionId == created.SessionId).Select(row => row.State).SingleAsync())
            .IsEqualTo(ConfigurationImportSessionState.Consumed);
        await Assert.That(await observer.Tenants.Where(row => row.Id == fixture.TenantId)
            .Select(row => row.FullName).SingleAsync()).IsEqualTo(desiredName);
    }

    private sealed class SetupImportLostAcknowledgement : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal int FaultCount { get; private set; }
        internal IOException Failure { get; } = new("Lost setup import commit acknowledgement after physical commit.");

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context
                || !context.ChangeTracker.Entries<ConfigurationImportOperation>()
                    .Any(entry => entry.Entity.Status == ConfigurationImportOperationStatus.Applied))
                return Task.CompletedTask;
            Armed = false;
            FaultCount++;
            throw Failure;
        }
    }

    private static bool ContainsCause(Exception failure, Exception expected) =>
        ReferenceEquals(failure, expected)
        || (failure is AggregateException aggregate
            ? aggregate.InnerExceptions.Any(inner => ContainsCause(inner, expected))
            : failure.InnerException is { } inner && ContainsCause(inner, expected));

    private sealed class GenerationReleaseFault(
        ISetupSecretBindingOperationCoordinator original,
        bool enabled) : ISetupSecretBindingOperationCoordinator
    {
        private int injected;
        internal bool Armed { get; set; }
        internal IOException Failure { get; } = new("Generation coordination release failed after physical commit.");

        private void FailReleaseIfArmed()
        {
            if (enabled && Armed && Interlocked.Exchange(ref injected, 1) == 0)
                throw Failure;
        }

        public async Task<IAsyncDisposable> AcquireAsync(
            SetupSecretBindingCoordinationRequest request, CancellationToken cancellationToken) =>
            new Lease(await original.AcquireAsync(request, cancellationToken), this);

        private sealed class Lease(IAsyncDisposable original, GenerationReleaseFault owner) : IAsyncDisposable
        {
            public async ValueTask DisposeAsync()
            {
                await original.DisposeAsync();
                owner.FailReleaseIfArmed();
            }
        }
    }

    private sealed class SettingReleaseFault(ISettingMutationLock original) : ISettingMutationLock
    {
        internal bool Armed { get; set; }
        internal IOException Failure { get; } = new("Setting coordination release failed after acknowledged commit.");

        public Task<T> ExecuteAsync<T>(string settingKey, Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) => original.ExecuteAsync(settingKey, operation, cancellationToken);

        public Task<T> ExecuteManyAsync<T>(IEnumerable<string> settingKeys, Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) => original.ExecuteManyAsync(settingKeys, operation, cancellationToken);

        public Task<T> ExecuteOrderedGroupsAsync<T>(IEnumerable<IEnumerable<string>> settingKeyGroups,
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            original.ExecuteOrderedGroupsAsync(settingKeyGroups, operation, cancellationToken);

        public async Task<T> ExecuteOrderedGroupsOnceAsync<T>(IEnumerable<IEnumerable<string>> settingKeyGroups,
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            var result = await original.ExecuteOrderedGroupsOnceAsync(settingKeyGroups, operation, cancellationToken);
            if (Armed) throw Failure;
            return result;
        }
    }

    private static Task SeedThemeAsync(EventVisitorCapabilitySqliteFixture fixture) =>
        WriteThemeAsync(fixture, AppearanceSettingDefinitions.DefaultThemeMode.DefaultValue);

    private static async Task WriteThemeAsync(EventVisitorCapabilitySqliteFixture fixture, string value)
    {
        var definition = AppearanceSettingDefinitions.DefaultThemeMode;
        await using var context = fixture.CreateIndependentContext();
        context.BindTenantLease();
        await using var human = new HumanSecuritySourceGate(context);
        await using var heldHuman = await human.AcquireAsync(default);
        await using var discovery = new EventDiscoveryEnrollmentGate(context);
        await using var heldDiscovery = await discovery.AcquireAsync(default);
        await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            var row = await context.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == definition.Key, token);
            if (row is null)
                context.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = definition.Key,
                    Value = value,
                    ValueType = definition.ValueType,
                    Category = definition.Category,
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
                });
            else row.Value = value;
            await context.SaveChangesAsync(token);
            return true;
        });
    }

    [Test]
    public async Task HumanImportRetainsOriginalDiscoveryCoordinationThroughCommit()
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
        await using var owner = fixture.CreateScope();
        var context = await fixture.Services.GetRequiredService<IGlobalExploreDbContextFactory>().CreateDbContextAsync();
        context.BindConfigurationInstanceImportOwner();
        var lease = owner.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.Bind(context, null);
        lease.BindHuman(subject, HumanAdministratorCommitPurpose.ConfigurationImport, null, fixture.Lifetime);

        var tenant = await context.Tenants.AsNoTracking().SingleAsync(row => row.Id == fixture.TenantId);
        var revision = await context.Set<EventDiscoveryRevision>().AsNoTracking()
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .SingleAsync(row => row.TenantId == fixture.TenantId);
        string desiredName = tenant.FullName + " imported";
        var snapshot = await owner.ServiceProvider.GetRequiredService<ConfigurationManifestCurrentStateReader>()
            .ReadImportSnapshotAsync(ConfigurationManifestExportView.Portable, null, default);
        var artifact = JsonNode.Parse(snapshot.Utf8Json.Span)!;
        var selectedTenant = artifact["spec"]!["tenants"]!.AsArray()
            .Single(node => node!["metadata"]!["name"]!.GetValue<string>() == tenant.Slug)!.DeepClone();
        selectedTenant["spec"]!["displayName"] = desiredName;
        artifact["spec"]!["tenants"] = new JsonArray(selectedTenant);
        var sessions = owner.ServiceProvider.GetRequiredService<ConfigurationImportSessionApplicationService>();
        var created = await sessions.CreateInstanceAsync(Encoding.UTF8.GetBytes(artifact.ToJsonString()), default);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["tenant.settings"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };
        await Assert.That((await sessions.PreviewInstanceAsync(created.SessionId, created.AccessToken, request, default))
            .IsApplyReady).IsTrue();
        var actualFactory = owner.ServiceProvider.GetRequiredService<IPolicyMutationExecutionScopeFactory>();
        var observation = new DiscoveryImportOwnerObservation(actualFactory, context,
            ((IHumanConfigurationImportOwnerFactory)actualFactory).GetImportSettlementBoundary(fixture.Lifetime));
        var apply = ActivatorUtilities.CreateInstance<ConfigurationImportApplyService>(owner.ServiceProvider, observation);
        var receipt = await apply.ApplyInstanceAsync(created.SessionId, created.AccessToken, request,
            subject, null, null, default);

        await Assert.That(receipt.Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
        await Assert.That(receipt.FidelityVerified).IsTrue();
        await Assert.That(observation.HeldBeforeBegin).IsTrue();
        await Assert.That(observation.HeldInsideOriginalTransaction).IsTrue();
        await Assert.That(observation.HeldAfterPhysicalSettlement).IsTrue();
        await Assert.That(observation.ConsumedOriginalCommit).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var independent = fixture.CreateIndependentContext();
        await Assert.That(await independent.Tenants.Where(row => row.Id == fixture.TenantId)
            .Select(row => row.FullName).SingleAsync()).IsEqualTo(desiredName);
        var changedRevision = await independent.Set<EventDiscoveryRevision>().AsNoTracking()
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .SingleAsync(row => row.TenantId == fixture.TenantId);
        await Assert.That(changedRevision.Id).IsEqualTo(revision.Id);
        await Assert.That(changedRevision.IdentityEpoch).IsEqualTo(revision.IdentityEpoch);
        await Assert.That(changedRevision.DisclosureEpoch).IsEqualTo(revision.DisclosureEpoch + 1);
        var operation = await independent.ConfigurationImportOperations.AsNoTracking()
            .SingleAsync(row => row.SessionId == created.SessionId);
        await Assert.That(operation.Id).IsEqualTo(receipt.OperationId);
        await Assert.That(operation.Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
        await Assert.That(operation.EffectOutboxId.HasValue).IsTrue();
        await Assert.That(await independent.OutboxMessages.CountAsync(row => row.Id == operation.EffectOutboxId))
            .IsEqualTo(1);
        await Assert.That(await independent.Set<ConfigurationImportSession>()
            .Where(row => row.SessionId == created.SessionId).Select(row => row.State).SingleAsync())
            .IsEqualTo(ConfigurationImportSessionState.Consumed);
    }

    // SQLite's identity context begins and commits the shared physical transaction.
    // Observe its real settlement boundary; never manufacture or replace its receipt.
    private sealed class DiscoveryImportOwnerObservation(
        IPolicyMutationExecutionScopeFactory factory, ExploreDbContext context, IOriginalOwnerSettlementBoundary boundary)
        : IPolicyMutationExecutionScopeFactory, IHumanConfigurationImportOwnerFactory,
            IHumanConfigurationImportPublicationFactory, IOriginalOwnerSettlementBoundary
    {
        internal bool HeldBeforeBegin { get; private set; }
        internal bool HeldInsideOriginalTransaction { get; private set; }
        internal bool HeldAfterPhysicalSettlement { get; private set; }
        internal bool ConsumedOriginalCommit { get; private set; }

        public ValueTask<IPolicyMutationExecutionScope> CreateAsync(PolicyMutationScope selection, CancellationToken token) =>
            factory.CreateAsync(selection, token);
        public ValueTask<IPolicyMutationExecutionScope> CreateFreshImportOwnerAsync(
            HumanConfigurationImportAuthority original, CancellationToken token) =>
            ((IHumanConfigurationImportOwnerFactory)factory).CreateFreshImportOwnerAsync(original, token);
        public IOriginalOwnerSettlementBoundary GetImportSettlementBoundary(
            Explore.Application.Authentication.HumanAuthenticationLifetime lifetime)
        {
            if (!ReferenceEquals(((IHumanConfigurationImportOwnerFactory)factory).GetImportSettlementBoundary(lifetime), boundary))
                throw new InvalidOperationException("The observation requires the original import boundary.");
            return this;
        }
        public IHumanConfigurationImportSourceReader GetImportSourceReader() =>
            ((IHumanConfigurationImportOwnerFactory)factory).GetImportSourceReader();
        public Task<IHumanConfigurationImportPublicationStore> ObservePublicationAsync(
            HumanConfigurationImportAuthority authority, Guid operationId, string artifactDigest,
            ImmutableArray<HumanConfigurationImportSettingEffect> effects, DateTime occurredAt, CancellationToken token) =>
            ((IHumanConfigurationImportPublicationFactory)factory).ObservePublicationAsync(
                authority, operationId, artifactDigest, effects, occurredAt, token);

        public async Task<IOriginalOwnerSettlement<T>> ExecuteAsync<T>(object invocation,
            Func<CancellationToken, Task<OriginalOwnerDecision<T>>> operation, CancellationToken cancellationToken = default)
        {
            var contextId = context.ContextId;
            DbConnection connection = context.Database.GetDbConnection();
            HeldBeforeBegin = context.Database.CurrentTransaction is null && Held();
            var settled = await boundary.ExecuteAsync(invocation, async token =>
            {
                DbTransaction transaction = context.Database.CurrentTransaction!.GetDbTransaction();
                bool original = context.ContextId == contextId && ReferenceEquals(transaction.Connection, connection)
                    && Held();
                var decision = await operation(token);
                HeldInsideOriginalTransaction = original && context.ContextId == contextId
                    && ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction) && Held();
                return decision;
            }, cancellationToken);
            HeldAfterPhysicalSettlement = context.ContextId == contextId && context.Database.CurrentTransaction is null
                && ReferenceEquals(context.Database.GetDbConnection(), connection) && Held();
            return settled;
        }

        public T Consume<T>(object invocation, IOriginalOwnerSettlement<T> settlement,
            OriginalOwnerDisposition expectedDisposition)
        {
            T result = boundary.Consume(invocation, settlement, expectedDisposition);
            ConsumedOriginalCommit = settlement.Disposition == OriginalOwnerDisposition.Committed
                && expectedDisposition == OriginalOwnerDisposition.Committed;
            return result;
        }

        private bool Held() => HumanSecuritySourceGate.TryRequireCurrent(context)
            && EventDiscoveryEnrollmentGate.TryRequireCurrent(context);
    }

    [Test]
    public async Task StaleHumanImportWritesOnlyFailedHistoryAndRemainsConsumable()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await SeedThemeAsync(fixture);
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
        var theme = await fixture.Context.SystemSettings.AsNoTracking().SingleAsync(
            row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key);
        theme.Value = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<string>(theme.Value) == "dark" ? "light" : "dark");
        await WriteThemeAsync(fixture, theme.Value);
        string changedTheme = theme.Value;
        bool rejected = false;
        try
        {
            await executor.ApplyAsync(created.SessionId, created.AccessToken, request, null, null, default);
        }
        catch (ConfigurationImportSessionException failure)
        {
            await Assert.That(failure.FailureCode).IsEqualTo(ConfigurationImportFailureCodes.StalePreview);
            rejected = true;
        }
        await Assert.That(rejected).IsTrue();
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
        await SeedThemeAsync(fixture);
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
        var theme = await fixture.Context.SystemSettings.AsNoTracking().SingleAsync(
            row => row.SettingKey == AppearanceSettingDefinitions.DefaultThemeMode.Key);
        theme.Value = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<string>(theme.Value) == "dark" ? "light" : "dark");
        await WriteThemeAsync(fixture, theme.Value);
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
        await SeedThemeAsync(fixture);
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
