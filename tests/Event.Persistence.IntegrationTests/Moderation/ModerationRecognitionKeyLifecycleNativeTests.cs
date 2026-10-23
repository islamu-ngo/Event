using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services.Moderation;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task RecognitionLifecycleEmptyCleanupAndPurgeLookupLeaveOrdinaryErasureIndependent()
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
        await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, CancellationToken.None);
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        var clock = new DeploymentClock();
        await using var retained = await RecognitionAuthorityDatabase.CreateAsync(clock);
        using var replica = new ModerationRecognitionKeyLifecycleReplicaOwner();
        var repository = new ModerationRecognitionKeyLifecycleRepository(context, retained.Repository,
            retained.Repository, replica, clock);
        var material = await repository.PruneAsync(null, 100, CancellationToken.None);
        await Assert.That(material.LastInspectedIntentId).IsNull();
        await Assert.That(material.Purged.IsEmpty).IsTrue();
        var empty = await repository.ForgetExpiredReceiptsAsync(null, 100, CancellationToken.None);
        await Assert.That(empty.LastInspectedIntentId).IsNull();
        await Assert.That(empty.ReceiptsRemoved).IsEqualTo(0);
        var fact = await retained.Repository.AppendAsync(PrivacyErasureRequest.Create(Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User, Guid.CreateVersion7(), PrivacyErasureReasonCode.AccountDeletion, 1));
        var proof = await retained.Repository.ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var result = await repository.AcquirePurgeAsync(fact, token);
            await transaction.CommitAsync(token);
            return result;
        }, CancellationToken.None);
        await Assert.That(proof).IsNull();
    }

    [Test]
    public async Task RecognitionLifecycleRequiresDurableOverlapThenIndependentSelectedByteAcknowledgement()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        var originalToken = await scenario.WithRingAsync(ring =>
        {
            using var key = ring.BorrowWritingKey(scenario.Trust, "original", 1);
            return key.Compute(new(AuthenticationProviderKind.Keycloak, "https://issuer.example.invalid|qualified"),
                ModerationReferenceRecognition.SeparatelyQualifiedFinalAction);
        });
        scenario.Overlap();
        var staged = await scenario.Service.StageAsync(scenario.Trust, 1, CancellationToken.None);
        await Assert.That(staged.Keys.Single(row => row.KeyId == "candidate").State)
            .IsEqualTo(ModerationRecognitionKeyState.Candidate);
        await Assert.That(async () => { await scenario.Service.SwitchAsync(scenario.Trust, "candidate", 2, CancellationToken.None); })
            .Throws<InvalidOperationException>();
        await scenario.Service.AcknowledgeAsync(scenario.Trust, 2, CancellationToken.None);
        await scenario.Service.SwitchAsync(scenario.Trust, "candidate", 2, CancellationToken.None);
        // The operator has not switched the selected secret yet. An old ring cannot keep writing.
        await Assert.That(() => scenario.WithRingAsync(ring => ring.RingGeneration)).Throws<InvalidOperationException>();
        scenario.SwitchSelected();
        var readableToken = await scenario.WithRingAsync(ring =>
        {
            using var key = ring.BorrowReadableKey(scenario.Trust, "original", 1);
            return key.Compute(new(AuthenticationProviderKind.Keycloak, "https://issuer.example.invalid|qualified"),
                ModerationReferenceRecognition.SeparatelyQualifiedFinalAction);
        });
        await Assert.That(readableToken).IsEqualTo(originalToken);
        await Assert.That(await scenario.WithRingAsync(ring => ring.Keys.Single(row => row.IsWriting).KeyId))
            .IsEqualTo("candidate");
    }

    [Test]
    public async Task RecognitionLifecycleRejectsSameIdentityWithDifferentSelectedBytes()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        scenario.Overlap();
        scenario.ReplaceOriginalBytes();
        await Assert.That(async () => { await scenario.Service.StageAsync(scenario.Trust, 1, CancellationToken.None); })
            .Throws<InvalidOperationException>();
        await Assert.That(() => scenario.WithRingAsync(ring => ring.RingGeneration))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RecognitionLifecycleExternalSecretChangeAtCommitRollsBackStagedMetadata()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        scenario.Overlap();
        scenario.AfterNextSelectedObservation = scenario.ReplaceCandidateBytes;
        await Assert.That(async () => { await scenario.Service.StageAsync(scenario.Trust, 1, CancellationToken.None); })
            .Throws<InvalidOperationException>().WithMessage("moderation_recognition_selected_source_changed");
        // Repeating the original revision with stable selected bytes succeeds only if the real
        // first primary transaction rolled back; no mock repository supplies this result.
        var staged = await scenario.Service.StageAsync(scenario.Trust, 1, CancellationToken.None);
        await Assert.That(staged.Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task RecognitionLifecycleExactObservationExpiryAtNativeCommitRollsBackStaging()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        scenario.Overlap();
        scenario.AfterNextPrimarySave = () => scenario.Clock.UtcNow = Now.AddMinutes(5);
        await Assert.That(async () => { await scenario.Service.StageAsync(scenario.Trust, 1, CancellationToken.None); })
            .Throws<InvalidOperationException>().WithMessage("moderation_recognition_selected_source_changed");
        var staged = await scenario.Service.StageAsync(scenario.Trust, 1, CancellationToken.None);
        await Assert.That(staged.Revision).IsEqualTo(2L);
    }

    [Test]
    [Arguments("binding")]
    [Arguments("authority")]
    public async Task RecognitionLifecycleOldSelectedAuthorityAcknowledgementCannotAuthorizeSwitch(string change)
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        scenario.Overlap();
        await scenario.Service.StageAsync(scenario.Trust, 1, CancellationToken.None);
        await scenario.Service.AcknowledgeAsync(scenario.Trust, 2, CancellationToken.None);
        if (change == "binding") scenario.BindingRevision = "changed-native-binding";
        else scenario.SelectedSource = SecretSourceType.Infisical;
        await Assert.That(async () => { await scenario.Service.SwitchAsync(scenario.Trust, "candidate", 2, CancellationToken.None); })
            .Throws<InvalidOperationException>();
        await scenario.Service.AcknowledgeAsync(scenario.Trust, 2, CancellationToken.None);
        await Assert.That((await scenario.Service.SwitchAsync(scenario.Trust, "candidate", 2, CancellationToken.None))
            .RingGeneration).IsEqualTo(3L);
    }

    [Test]
    public async Task RecognitionLifecycleRejectsUnacknowledgedNewRuntimeAndSecondStandaloneOwner()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        scenario.Overlap();
        await scenario.Service.StageAsync(scenario.Trust, 1, CancellationToken.None);
        await scenario.Service.AcknowledgeAsync(scenario.Trust, 2, CancellationToken.None);
        using (var competing = new ModerationRecognitionKeyLifecycleReplicaOwner())
        {
            await scenario.Context.Database.OpenConnectionAsync();
            await Assert.That(() => competing.Acquire(scenario.Context)).Throws<InvalidOperationException>()
                .WithMessage("moderation_recognition_topology_unavailable");
            await Assert.That(() => competing.Acquire(scenario.Context)).Throws<InvalidOperationException>()
                .WithMessage("moderation_recognition_topology_unavailable");
        }
        scenario.RestartReplica();
        await Assert.That(async () => { await scenario.Service.SwitchAsync(scenario.Trust, "candidate", 2, CancellationToken.None); })
            .Throws<InvalidOperationException>();
        await scenario.Service.AcknowledgeAsync(scenario.Trust, 2, CancellationToken.None);
        var switched = await scenario.Service.SwitchAsync(scenario.Trust, "candidate", 2, CancellationToken.None);
        await Assert.That(switched.RingGeneration).IsEqualTo(3);
    }

    [Test]
    public async Task RecognitionLifecycleRetirementRequiresActualSelectedErasureReplayFloor()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        await scenario.RotateAsync();
        var request = PrivacyErasureRequest.Create(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(), PrivacyErasureReasonCode.AccountDeletion, 1);
        var fact = await scenario.Retained.Repository.AppendAsync(request, CancellationToken.None);
        await scenario.Service.AcknowledgeAsync(scenario.Trust, 3, CancellationToken.None);
        await Assert.That(async () => { await scenario.Service.RetireAsync(scenario.Trust, "original", 3, CancellationToken.None); })
            .Throws<InvalidOperationException>().WithMessage("moderation_recognition_restore_floor_unavailable");
        await new PrivacyErasureReplayCheckpointRepository(scenario.Context).AppendAsync(
            PrivacyErasureReplayCheckpoint.Start(fact, scenario.Clock.UtcNow.UtcDateTime), CancellationToken.None);
        scenario.Context.ChangeTracker.Clear();
        var retired = await scenario.Service.RetireAsync(scenario.Trust, "original", 3, CancellationToken.None);
        await Assert.That(retired.Keys.Single(row => row.KeyId == "original").State)
            .IsEqualTo(ModerationRecognitionKeyState.Retired);
        await Assert.That(() => scenario.WithRingAsync(ring => ring.RingGeneration)).Throws<InvalidOperationException>();
        scenario.RemoveOriginal();
        await Assert.That(await scenario.WithRingAsync(ring => ring.Keys.Length)).IsEqualTo(1);
    }

    [Test]
    public async Task RecognitionLifecycleCompromiseClosesRecognitionButNotOrdinarySelectedAuthorityErasure()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        await scenario.Service.CompromiseAsync(scenario.Trust, "original", 1, CancellationToken.None);
        await Assert.That(() => scenario.WithRingAsync(ring => ring.RingGeneration)).Throws<InvalidOperationException>();
        var request = PrivacyErasureRequest.Create(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(), PrivacyErasureReasonCode.AccountDeletion, 1);
        var fact = await scenario.Retained.Repository.AppendAsync(request, CancellationToken.None);
        await Assert.That(fact.IntentId).IsEqualTo(request.IntentId);
        await Assert.That((await scenario.Retained.Repository.GetStateAsync()).HighWaterSequence).IsEqualTo(1L);
    }

    [Test]
    public async Task RecognitionLifecycleRequiresVerifiedReplayBaselineButAllowsPriorOrdinaryErasure()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync(initialize: false);
        var fact = await scenario.Retained.Repository.AppendAsync(PrivacyErasureRequest.Create(Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User, Guid.CreateVersion7(), PrivacyErasureReasonCode.AccountDeletion, 1));
        await Assert.That(async () => { await scenario.Service.InitializeAsync(scenario.Trust, CancellationToken.None); })
            .Throws<InvalidOperationException>();
        await new PrivacyErasureReplayCheckpointRepository(scenario.Context).AppendAsync(
            PrivacyErasureReplayCheckpoint.Start(fact, scenario.Clock.UtcNow.UtcDateTime), CancellationToken.None);
        scenario.Context.ChangeTracker.Clear();
        var initialized = await scenario.Service.InitializeAsync(scenario.Trust, CancellationToken.None);
        await Assert.That(initialized.InitialErasureHead).IsEqualTo(1L);
        await Assert.That(initialized.InitialErasureRetainedFloor).IsEqualTo(0L);
    }

    [Test]
    public async Task RecognitionLifecyclePrunesExpiredTokensButPreservesOriginalBackupObligation()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        var captured = await scenario.CaptureAsync();
        await scenario.RotateAsync();
        scenario.Clock.UtcNow = Now.AddDays(2);
        var purged = await scenario.Repository.PruneAsync(null, 1, CancellationToken.None);
        await Assert.That(purged.Purged.Length).IsEqualTo(1);
        await Assert.That(purged.LastInspectedIntentId).IsEqualTo(captured.Pending.IntentId);
        await Assert.That((await scenario.Repository.FindPurgeAsync(captured.Pending.IntentId, CancellationToken.None))!
            .MaterialReceipt).IsEqualTo(captured.Pending.MaterialReceipt);
        await Assert.That(await scenario.Context.Set<ModerationRecognitionCaptureItem>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .AnyAsync(row => row.IntentId == captured.Pending.IntentId)).IsFalse();
        await scenario.Service.AcknowledgeAsync(scenario.Trust, 3, CancellationToken.None);
        await Assert.That(async () => { await scenario.Service.RetireAsync(scenario.Trust, "original", 3, CancellationToken.None); })
            .Throws<InvalidOperationException>();
        scenario.Clock.UtcNow = captured.Item.RetainUntil
            .AddTicks(captured.Source.MaximumBackupHorizonTicks + captured.Source.BackupSafetyMarginTicks);
        await scenario.Service.AcknowledgeAsync(scenario.Trust, 3, CancellationToken.None);
        var retired = await scenario.Service.RetireAsync(scenario.Trust, "original", 3, CancellationToken.None);
        await Assert.That(retired.Keys.Single(row => row.KeyId == "original").State)
            .IsEqualTo(ModerationRecognitionKeyState.Retired);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RecognitionSchedulerPrunesExpiredNativeMaterialWithoutRemovingLiveCapture(bool expired)
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        var captured = await scenario.CaptureAsync();
        if (expired) scenario.Clock.UtcNow = Now.AddDays(2);
        var scheduler = new ModerationRecognitionRecoveryScheduler(
            Substitute.For<IModerationRecognitionErasureRepository>(),
            Substitute.For<IModerationRecognitionRecoveryRepository>(),
            Substitute.For<IModerationRecognitionRecoveryNotifications>(),
            Substitute.For<ISettingMutationLock>(), scenario.Clock, scenario.Repository);

        await scheduler.PruneExpiredMaterialAsync(CancellationToken.None);

        await Assert.That(await scenario.Context.Set<ModerationRecognitionCaptureItem>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .AnyAsync(row => row.Id == captured.Item.Id)).IsEqualTo(!expired);
        var purge = await scenario.Repository.FindPurgeAsync(captured.Pending.IntentId, CancellationToken.None);
        if (expired)
            await Assert.That(purge!.MaterialReceipt).IsEqualTo(captured.Pending.MaterialReceipt);
        else
            await Assert.That(purge).IsNull();
    }

    [Test]
    public async Task RecognitionLifecycleCleanupCursorPassesLiveReceiptsWithoutDeletingThem()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        var captured = await scenario.CaptureAsync();
        var page = await scenario.Repository.PruneAsync(null, 1, CancellationToken.None);
        await Assert.That(page.Purged.Length).IsEqualTo(0);
        await Assert.That(page.LastInspectedIntentId).IsEqualTo(captured.Pending.IntentId);
        var next = await scenario.Repository.PruneAsync(page.LastInspectedIntentId, 1, CancellationToken.None);
        await Assert.That(next.LastInspectedIntentId).IsNull();
        await Assert.That(await scenario.Context.Set<ModerationRecognitionCaptureItem>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .AnyAsync(row => row.Id == captured.Item.Id)).IsTrue();
    }

    [Test]
    public async Task RecognitionLifecycleDoesNotPurgeAncestorWhileDescendantStillReferencesItsExactReceipt()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        var captured = await scenario.CaptureAsync();
        var fresh = Guid.CreateVersion7();
        var binding = ModerationRecognitionContinuityBinding.Bind(Guid.CreateVersion7(), fresh,
            captured.Item, captured.Decision, captured.Sanction, Now);
        var alias = ModerationRecognitionCaptureItem.Recapture(Guid.CreateVersion7(), Guid.CreateVersion7(),
            fresh, captured.Item, binding, captured.Decision, captured.Sanction,
            captured.Item.Token, captured.Item.KeyVerificationTag, 1, Now.AddMinutes(1));
        var pending = ModerationRecognitionPending.Receive(alias.IntentId, fresh, 1, PrivacyErasureReasonCode.AccountDeletion,
            captured.Source.PrivacyOwnerHumanId, captured.Source.RecoveryOwnerHumanId,
            Now.AddMinutes(1), Now.AddHours(1), Now.AddMinutes(5), RandomNumberGenerator.GetBytes(32), Now.AddDays(1));
        pending.Capture([alias], Now.AddMinutes(1));
        scenario.Context.Add(binding);
        scenario.Context.Add(pending);
        await scenario.Context.SaveChangesAsync();
        scenario.Context.ChangeTracker.Clear();
        scenario.Clock.UtcNow = Now.AddDays(2);
        var page = await scenario.Repository.PruneAsync(null, 100, CancellationToken.None);
        await Assert.That(page.Purged.Length).IsEqualTo(0);
        await Assert.That(await scenario.Repository.FindPurgeAsync(captured.Pending.IntentId, CancellationToken.None)).IsNull();
        await Assert.That(await scenario.Context.Set<ModerationRecognitionCaptureItem>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .AnyAsync(row => row.Id == captured.Item.Id)).IsTrue();
    }

    [Test]
    public async Task RecognitionLifecyclePurgeRequiresOriginalObligationsAndAuthenticSelectedFact()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        var captured = await scenario.CaptureAsync();
        scenario.Clock.UtcNow = Now.AddDays(2);
        await scenario.Repository.PruneAsync(null, 100, CancellationToken.None);
        await Assert.That(await scenario.AcquirePurgeAsync(captured.Fact)).IsNotNull();
        var forged = PrivacyErasureIntent.Record(captured.Fact.IntentId, captured.Fact.AuthoritySequence,
            PrivacyErasureSubjectKind.User, Guid.CreateVersion7(), captured.Fact.ReasonCode, captured.Fact.PolicyVersion,
            captured.Fact.RequestedAtUtc, captured.Fact.RecordedAtUtc, captured.Fact.RetentionExpiresAtUtc);
        await Assert.That(() => scenario.AcquirePurgeAsync(forged)).Throws<InvalidOperationException>();
        // Simulate a torn/corrupt restore, not a native expiry. A surviving scalar marker
        // without its independently committed original obligation cannot authorize replay.
        await scenario.Context.Set<KeyObligation>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(row => row.Id == captured.Item.Id).ExecuteDeleteAsync();
        await Assert.That(await scenario.Repository.FindPurgeAsync(captured.Pending.IntentId, CancellationToken.None)).IsNotNull();
        await Assert.That(() => scenario.AcquirePurgeAsync(captured.Fact)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RecognitionLifecycleForgetsLinkedReceiptAtCeilingWithoutLosingOriginalKeyHorizon()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        var captured = await scenario.CaptureAsync(TimeSpan.FromDays(365));
        await scenario.RotateAsync();
        scenario.Clock.UtcNow = Now.AddDays(2);
        var page = await scenario.Repository.PruneAsync(null, 100, CancellationToken.None);
        var receipt = page.Purged.Single();
        await Assert.That(receipt.RetainUntil).IsEqualTo(Now.AddDays(92));
        await Assert.That(receipt.BackupObligationUntil).IsEqualTo(Now.AddDays(402));
        scenario.Clock.UtcNow = receipt.RetainUntil;
        await Assert.That(await scenario.Repository.FindPurgeAsync(captured.Pending.IntentId, CancellationToken.None)).IsNull();
        var forgotten = await scenario.Repository.ForgetExpiredReceiptsAsync(null, 100, CancellationToken.None);
        await Assert.That(forgotten.ReceiptsRemoved).IsEqualTo(1);
        await Assert.That(forgotten.ObligationsRemoved).IsEqualTo(1);
        await Assert.That(await scenario.Context.Set<KeyObligation>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .AnyAsync(row => row.IntentId == captured.Pending.IntentId)).IsFalse();
        await scenario.Service.AcknowledgeAsync(scenario.Trust, 3, CancellationToken.None);
        await Assert.That(async () => { await scenario.Service.RetireAsync(scenario.Trust, "original", 3, CancellationToken.None); })
            .Throws<InvalidOperationException>();
        scenario.Clock.UtcNow = receipt.BackupObligationUntil;
        await scenario.Service.AcknowledgeAsync(scenario.Trust, 3, CancellationToken.None);
        await Assert.That((await scenario.Service.RetireAsync(scenario.Trust, "original", 3, CancellationToken.None))
            .Keys.Single(row => row.KeyId == "original").State).IsEqualTo(ModerationRecognitionKeyState.Retired);
    }

    [Test]
    public async Task RecognitionLifecycleExpiredReceiptCleanupDoesNotRenewDuringAuthorityOutage()
    {
        await using var scenario = await RecognitionKeyScenario.CreateAsync();
        await scenario.CaptureAsync();
        scenario.Clock.UtcNow = Now.AddDays(2);
        var receipt = (await scenario.Repository.PruneAsync(null, 100, CancellationToken.None)).Purged.Single();
        scenario.Clock.UtcNow = receipt.RetainUntil;
        // Reproduce an actual torn authority image: retained facts without their counter.
        // The native provider demonstrably fails, rather than relying on pool/connection timing.
        await using (var brokenAuthority = scenario.Retained.CreateDbContext())
            await brokenAuthority.AuthorityCounters.ExecuteDeleteAsync();
        await Assert.That(async () => { await scenario.Retained.Repository.GetStateAsync(); })
            .Throws<InvalidOperationException>().WithMessage("privacy_identity_fence_authority_state_unavailable");
        var forgotten = await scenario.Repository.ForgetExpiredReceiptsAsync(null, 100, CancellationToken.None);
        await Assert.That(forgotten.ReceiptsRemoved).IsEqualTo(1);
        await Assert.That(forgotten.ObligationsRemoved).IsEqualTo(1);
    }

    private sealed record KeyCapture(
        ModerationRecognitionPending Pending, ModerationRecognitionCaptureItem Item,
        ModerationRecognitionPrivacySource Source, ModerationDecision Decision, ModerationSanction Sanction,
        PrivacyErasureIntent Fact);

    private sealed class RecognitionLifecycleSaveObserver : SaveChangesInterceptor
    {
        public Action? AfterSave { get; set; }
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var after = AfterSave;
            AfterSave = null;
            after?.Invoke();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecognitionKeyScenario : IAsyncDisposable
    {
        private readonly Database database;
        private readonly string path;
        private readonly SqliteConnection connection;
        private readonly RecognitionLifecycleSaveObserver saveObserver;
        private readonly ISecretResolver resolver = Substitute.For<ISecretResolver>();
        private readonly Dictionary<string, (long Generation, byte[] Material, bool Writing)> material = [];
        private ModerationRecognitionKeyLifecycleFixture? composition;
        private long generation = 1;
        public DeploymentClock Clock { get; }
        public ExploreDbContext Context { get; }
        public Guid Trust => database.Scope.TrustDomainId;
        public RecognitionAuthorityDatabase Retained { get; }
        public ModerationRecognitionKeyLifecycleRepository Repository { get; private set; } = null!;
        public ModerationRecognitionKeyRingProvider Provider { get; private set; } = null!;
        public ModerationRecognitionKeyLifecycleService Service { get; private set; } = null!;
        public Action? AfterNextSelectedObservation { get; set; }
        public Action? AfterNextPrimarySave { set => saveObserver.AfterSave = value; }
        public string BindingRevision { get; set; } = "native-selected-binding";
        public SecretSourceType SelectedSource { get; set; } = SecretSourceType.EnvironmentVariable;

        private RecognitionKeyScenario(Database database, string path, SqliteConnection connection,
            ExploreDbContext context, RecognitionAuthorityDatabase retained, DeploymentClock clock,
            RecognitionLifecycleSaveObserver saveObserver)
        {
            this.database = database;
            this.path = path;
            this.connection = connection;
            Context = context;
            Retained = retained;
            Clock = clock;
            this.saveObserver = saveObserver;
            material.Add("original", (1, RandomNumberGenerator.GetBytes(32), true));
            resolver.ResolveAsync(ModerationRecognitionKeyRing.SecretSettingKey, null, Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    var selected = Secret();
                    var after = AfterNextSelectedObservation;
                    AfterNextSelectedObservation = null;
                    after?.Invoke();
                    return SecretResolutionResult.Resolved(selected);
                });
            Compose();
        }

        public static async Task<RecognitionKeyScenario> CreateAsync(bool initialize = true)
        {
            var database = await Database.CreateAsync();
            var path = Path.Combine(Path.GetTempPath(), $"recognition-lifecycle-{Guid.CreateVersion7():N}.db");
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using (var source = database.Context())
                ((SqliteConnection)source.Database.GetDbConnection()).BackupDatabase(connection);
            var saveObserver = new RecognitionLifecycleSaveObserver();
            var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection)
                .UseSnakeCaseNamingConvention().AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance, saveObserver);
            var context = new ExploreDbContext(options.Options) { TenantContext = database };
            var clock = new DeploymentClock();
            var retained = await RecognitionAuthorityDatabase.CreateAsync(clock);
            var scenario = new RecognitionKeyScenario(database, path, connection, context, retained, clock, saveObserver);
            if (initialize) await scenario.Service.InitializeAsync(scenario.Trust, CancellationToken.None);
            return scenario;
        }

        private void Compose()
        {
            composition ??= new(resolver, Clock);
            var services = composition.Bind(Context, Retained.Repository, Retained.Repository);
            Repository = services.Repository;
            Provider = services.Provider;
            Service = services.Lifecycle;
        }
        public void RestartReplica() { composition!.Dispose(); composition = null; Compose(); }
        public void Overlap()
        {
            material.Add("candidate", (2, RandomNumberGenerator.GetBytes(32), false));
            generation = 2;
        }
        public void SwitchSelected()
        {
            material["original"] = (1, material["original"].Material, false);
            material["candidate"] = (2, material["candidate"].Material, true);
            generation = 3;
        }
        public void RemoveOriginal()
        {
            CryptographicOperations.ZeroMemory(material["original"].Material);
            material.Remove("original");
            generation = 4;
        }
        public void ReplaceOriginalBytes()
        {
            CryptographicOperations.ZeroMemory(material["original"].Material);
            material["original"] = (1, RandomNumberGenerator.GetBytes(32), true);
        }
        public void ReplaceCandidateBytes()
        {
            CryptographicOperations.ZeroMemory(material["candidate"].Material);
            material["candidate"] = (2, RandomNumberGenerator.GetBytes(32), false);
        }
        public async Task RotateAsync()
        {
            Overlap();
            await Service.StageAsync(Trust, 1, CancellationToken.None);
            await Service.AcknowledgeAsync(Trust, 2, CancellationToken.None);
            await Service.SwitchAsync(Trust, "candidate", 2, CancellationToken.None);
            SwitchSelected();
        }

        public async Task<KeyCapture> CaptureAsync(TimeSpan? retention = null)
        {
            var request = database.Request();
            request = request with { Proposed = request.Proposed with
            {
                RequestedState = ModerationPolicyRequestedState.Active,
                Profiles = [request.Proposed.Profiles.Single() with
                { Recognition = ModerationReferenceRecognition.SeparatelyQualifiedFinalAction }]
            }};
            var (admin, machine) = DeploymentIdentity(Context, database.AdminId);
            var publications = new ModerationPolicyPublicationRepository(Context, admin, machine, database.Inventory, null, Clock);
            if ((await publications.PrepareAsync(request, Now.AddDays(10))).Outcome != ModerationPolicyPublicationOutcome.Known)
                throw new InvalidOperationException("Native lifecycle source policy preparation failed.");
            var privacy = Guid.CreateVersion7();
            var recovery = Guid.CreateVersion7();
            var subject = Guid.CreateVersion7();
            var role = await Context.Roles.SingleAsync(row =>
                row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
            foreach (var id in new[] { privacy, recovery, subject })
            {
                var user = new User { Id = id, Pii = new UserPii
                { UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Lifecycle", LastName = "Witness" } };
                Context.Users.Add(user);
                if (id != subject) Context.PlatformUserRoles.Add(new PlatformUserRole
                { Id = Guid.CreateVersion7(), UserId = id, User = user, RoleId = role.Id, Role = role });
            }
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            ModerationQualificationRepository Writer(Guid human)
            {
                var actor = DeploymentIdentity(Context, human);
                return new(Context, actor.Admin, actor.Machine, publications,
                    new ModerationNativeActivationQualification(Context, database.Inventory, null, null, Clock),
                    Clock, Retained.Repository, deploymentInventory: database.Inventory);
            }
            var sourceRequest = new RecordModerationRecognitionPrivacySource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2,
                SourceId = Guid.CreateVersion7(), PolicyId = request.Proposed.PolicyId, ExpectedPolicyRevision = 1,
                PublicationId = request.Proposed.PublicationId, ProfileId = request.Proposed.Profiles.Single().Id,
                Mode = ModerationNativeMode.Embedded, ValidUntil = Now.AddDays(8), Terms = RecognitionPrivacyTerms(),
                Ownership = new(privacy, privacy, recovery, recovery, database.AdminId,
                    "Independently reviewed original canonical controllers.",
                    "Appointed privacy owner for finite original recognition.",
                    "Appointed recovery owner for the original backup horizon."),
                Retention = [
                    new(ModerationRecognitionPrivacyRecordClass.PendingComputedRecognition, TimeSpan.FromDays(30),
                        ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                    new(ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial, TimeSpan.FromDays(365),
                        ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                    new(ModerationRecognitionPrivacyRecordClass.ProtectedCorrectionEvidence, TimeSpan.FromDays(366),
                        ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                    new(ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt, TimeSpan.FromDays(90),
                        ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero)]
            };
            if ((await Writer(database.AdminId).ExecuteAsync(sourceRequest)).Outcome != ModerationQualificationWriteOutcome.Known)
                throw new InvalidOperationException("Native lifecycle original source was not recorded.");
            var adoption = Guid.CreateVersion7();
            if ((await Writer(privacy).ExecuteAsync(new AdoptModerationRecognitionPrivacySource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 3,
                SourceId = sourceRequest.SourceId, ExpectedSourceRevision = 1, ExpectedSourceWriterGeneration = 3,
                AdoptionId = adoption, ValidUntil = Now.AddDays(7)
            })).Outcome != ModerationQualificationWriteOutcome.Known)
                throw new InvalidOperationException("Native lifecycle original source was not adopted.");
            var source = await Context.Set<ModerationRecognitionPrivacySource>().Include(row => row.Retention)
                .AsNoTracking().SingleAsync(row => row.Id == sourceRequest.SourceId);
            var instance = database.Scope.Parent!;
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(),
                SubjectReference.Create(ModerationSubjectKind.Human, subject, instance),
                SubjectReference.Create(ModerationSubjectKind.Human, database.AdminId, instance),
                instance, database.Scope, request.Proposed.Profiles.Single().Rule,
                [ModerationCapability.Publish], Now, Now.AddDays(2), null, 1, Now);
            decision.RecordResponse(1, Now);
            decision.AcceptFinding(Guid.CreateVersion7(), 2, Now);
            decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, privacy, instance), instance, 3, Now);
            var sanction = decision.Activate(Guid.CreateVersion7(), 4, Now);
            var pending = ModerationRecognitionPending.Receive(Guid.CreateVersion7(), subject, 1,
                PrivacyErasureReasonCode.AccountDeletion, privacy, recovery, Now, Now.AddHours(1),
                Now.AddMinutes(5), RandomNumberGenerator.GetBytes(32), Now.AddDays(1));
            ModerationRecognitionCaptureItem? item = null;
            await Retained.Repository.ExecuteSerializedAsync(async token =>
            {
                await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                using var ring = await Provider.ResolveAsync(Trust, token);
                using var key = ring.BorrowWritingKey(Trust, "original", 1);
                var purpose = ModerationReferenceRecognition.SeparatelyQualifiedFinalAction;
                item = ModerationRecognitionCaptureItem.Capture(Guid.CreateVersion7(), pending.IntentId, decision,
                    new(key.Compute(new(AuthenticationProviderKind.Keycloak, "https://issuer.example.invalid|original"),
                        purpose), key.VerificationTag,
                        ModerationRecognitionContribution.Capture(decision, sanction, purpose, Now,
                            Now.Add(retention ?? TimeSpan.FromDays(3)))),
                    source.Id, 1, 3, adoption, 1, 4, 1);
                pending.Capture([item], Now);
                Context.Add(pending);
                await Repository.EnlistCaptureAsync(pending, token);
                await Context.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                Context.ChangeTracker.Clear();
                return true;
            }, CancellationToken.None);
            var fact = await Retained.Repository.AppendAsync(PrivacyErasureRequest.Create(pending.IntentId,
                PrivacyErasureSubjectKind.User, subject, PrivacyErasureReasonCode.AccountDeletion, 1));
            await Retained.Repository.ExecuteSerializedAsync(async token =>
            {
                await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var current = await Context.Set<ModerationRecognitionPending>()
                    .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).Include(row => row.Material)
                    .SingleAsync(row => row.IntentId == pending.IntentId, token);
                current.BindAuthority(fact, Now);
                await Repository.EnlistCaptureAsync(current, token);
                Context.PrivacyErasureReplayCheckpoints.Add(PrivacyErasureReplayCheckpoint.Start(fact, Now.UtcDateTime));
                await Context.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                Context.ChangeTracker.Clear();
                return true;
            }, CancellationToken.None);
            return new(pending, item!, source, decision, sanction, fact);
        }
        public Task<ModerationRecognitionKeyLifecyclePurge?> AcquirePurgeAsync(PrivacyErasureIntent fact) =>
            Retained.Repository.ExecuteSerializedAsync(async token =>
            {
                await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var proof = await Repository.AcquirePurgeAsync(fact, token);
                await transaction.CommitAsync(token);
                return proof;
            }, CancellationToken.None);
        public Task<T> WithRingAsync<T>(Func<ModerationRecognitionKeyRing, T> observe) =>
            Retained.Repository.ExecuteSerializedAsync(async token =>
            {
                await using var transaction = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                using var ring = await Provider.ResolveAsync(Trust, token);
                var result = observe(ring);
                await transaction.CommitAsync(token);
                return result;
            }, CancellationToken.None);

        private ResolvedSecret Secret() => new(ModerationRecognitionKeyRing.SecretSettingKey,
            JsonSerializer.Serialize(new
            {
                version = 1, trustDomainId = Trust.ToString("D"), ringGeneration = generation,
                issuedAt = Clock.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                validUntil = Clock.UtcNow.AddMinutes(5).ToString("O", CultureInfo.InvariantCulture),
                keys = material.Select(row => new { kid = row.Key, generation = row.Value.Generation,
                    k = Convert.ToBase64String(row.Value.Material).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                    status = row.Value.Writing ? "active" : "readable" }).ToArray()
            }), SelectedSource, SecretScope.Instance, null, Clock.UtcNow, BindingRevision);

        public async ValueTask DisposeAsync()
        {
            foreach (var key in material.Values) CryptographicOperations.ZeroMemory(key.Material);
            composition?.Dispose();
            await Context.DisposeAsync();
            await connection.DisposeAsync();
            await Retained.DisposeAsync();
            await database.DisposeAsync();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }
}
