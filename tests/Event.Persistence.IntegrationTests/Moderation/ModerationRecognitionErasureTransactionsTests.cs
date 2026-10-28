using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task ModerationErasureWorstBreak_LostAuthorityCommitAcknowledgementRequalifiesExactDurableCapture()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        var retained = fixture.Retained;
        await using var context = fixture.Database.Context();
        var boundary = new RecognitionCommitBoundary(retained.Repository) { LoseAppendAcknowledgement = true };
        var service = new ModerationRecognitionErasure(fixture.Repository(context), boundary, retained.Repository,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        var fact = await service.AppendAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        await Assert.That(fact).IsNotNull();
        await Assert.That((await retained.Repository.ReadAfterAsync(0, 100)).Count).IsEqualTo(1);
        await using var verification = fixture.Database.Context();
        var pending = await fixture.Repository(verification).FindAsync(fixture.Request.IntentId, CancellationToken.None);
        await Assert.That(pending!.AuthoritySequence).IsEqualTo(fact!.AuthoritySequence);
        await Assert.That(pending.HasCompleteMaterial).IsTrue();
    }

    [Test]
    public async Task ModerationErasureWorstBreak_ExactExpiryBetweenCaptureCommitAndAppendReleasesOnlyExpiredContribution()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        var retained = fixture.Retained;
        await using var context = fixture.Database.Context();
        var boundary = new RecognitionCommitBoundary(retained.Repository) { PauseAfterCapture = true };
        var service = new ModerationRecognitionErasure(fixture.Repository(context), boundary, retained.Repository,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        // Subscribe before triggering. Completion proves the real native capture transaction
        // returned, not a SaveChanges callback or a guessed scheduling interval.
        var committed = boundary.CaptureCommitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var append = service.AppendAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        try
        {
            await committed;
            await using var observer = fixture.Database.Context();
            var pending = await fixture.Repository(observer).FindAsync(fixture.Request.IntentId, CancellationToken.None);
            await Assert.That(pending!.HasCompleteMaterial).IsTrue();
            await Assert.That((await retained.Repository.GetStateAsync()).HighWaterSequence).IsEqualTo(0L);
            fixture.Clock.UtcNow = Now.AddDays(2);
        }
        finally { boundary.ContinueCapture.TrySetResult(); }
        var fact = await append.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(fact).IsNotNull();
        await using var verification = fixture.Database.Context();
        var current = await fixture.Repository(verification).FindAsync(fixture.Request.IntentId, CancellationToken.None);
        await Assert.That(current!.Material.Single().DroppedAt).IsEqualTo(Now.AddDays(2));
        await Assert.That(current.Material.Single().EndsAt).IsEqualTo(Now.AddDays(2));
    }

    [Test]
    public async Task ModerationErasureWorstBreak_ReceivedKeyOutageResumesFromDurableOriginalRequest()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        fixture.Keys.Available = false;
        await using (var context = fixture.Database.Context())
        {
            var service = new ModerationRecognitionErasure(fixture.Repository(context),
                fixture.Retained.Repository, fixture.Retained.Repository,
                new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
            await Assert.That(await service.AppendAsync(fixture.Request, fixture.ReceiptHash,
                Now.AddHours(2), CancellationToken.None)).IsNull();
        }
        await Assert.That((await fixture.Retained.Repository.GetStateAsync()).HighWaterSequence).IsEqualTo(0L);
        fixture.Keys.Available = true;
        await using var resumed = fixture.Database.Context();
        var replay = new ModerationRecognitionErasure(fixture.Repository(resumed),
            fixture.Retained.Repository, fixture.Retained.Repository,
            new RelationalSettingMutationLock(resumed, new EfCoreUnitOfWork(resumed)));
        await replay.ReconcileReceivedAsync(CancellationToken.None);
        var received = await replay.FindAsync(fixture.Request.IntentId, CancellationToken.None);
        await Assert.That(received!.AuthoritySequence).IsEqualTo(1L);
        await Assert.That(received.ReceiptExpiresAt).IsEqualTo(Now.AddHours(2));
        await Assert.That(received.SettleBy).IsEqualTo(Now.AddDays(30));
        await Assert.That(received.HasCompleteMaterial).IsTrue();
    }

    [Test]
    public async Task ModerationErasureWorstBreak_UnreceiptedDeadlineCannotBecomeUnlimitedRetention()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(TimeSpan.FromHours(2));
        fixture.Keys.Available = false;
        await using var context = fixture.Database.Context();
        var service = new ModerationRecognitionErasure(fixture.Repository(context),
            fixture.Retained.Repository, fixture.Retained.Repository,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        await service.AppendAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        var received = await service.FindAsync(fixture.Request.IntentId, CancellationToken.None);
        fixture.Clock.UtcNow = received!.SettleBy;
        await service.ReconcileReceivedAsync(CancellationToken.None);
        var overdue = await service.FindAsync(fixture.Request.IntentId, CancellationToken.None);
        await Assert.That(overdue!.SettleBy).IsEqualTo(received.SettleBy);
        await Assert.That(overdue.State).IsEqualTo(ModerationRecognitionPendingState.LawfulDispositionRequired);
        await Assert.That(overdue.HasCompleteMaterial).IsFalse();
    }

    [Test]
    public async Task ModerationErasureWorstBreak_CompleteButUnsettledReceiptStillReachesOriginalDispositionDeadline()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(TimeSpan.FromHours(2));
        ModerationRecognitionPending original;
        await using (var capture = fixture.Database.Context())
        {
            var repository = fixture.Repository(capture);
            await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
            await repository.ComputeAsync(fixture.Request, CancellationToken.None);
            original = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
            await Assert.That(original.HasCompleteMaterial).IsTrue();
        }
        // Restart the primary reader with no key available. Observing the obligation
        // neither recomputes the immutable batch nor waits for another capture failure.
        fixture.Keys.Available = false;
        fixture.Clock.UtcNow = original.EscalateAt;
        await using (var escalation = fixture.Database.Context())
        {
            var repository = fixture.Repository(escalation);
            await repository.ComputeAsync(fixture.Request, CancellationToken.None);
            var pending = await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None);
            await Assert.That(pending!.State).IsEqualTo(ModerationRecognitionPendingState.EscalationRequired);
            await Assert.That(pending.Failure).IsEqualTo(ModerationRecognitionPendingFailure.None);
            await Assert.That(pending.MaterialReceipt).IsEqualTo(original.MaterialReceipt);
        }
        fixture.Clock.UtcNow = original.SettleBy;
        await using var disposition = fixture.Database.Context();
        var resumed = fixture.Repository(disposition);
        await resumed.ComputeAsync(fixture.Request, CancellationToken.None);
        var overdue = await resumed.FindAsync(fixture.Request.IntentId, CancellationToken.None);
        await Assert.That(overdue!.State).IsEqualTo(ModerationRecognitionPendingState.LawfulDispositionRequired);
        await Assert.That(overdue.SettleBy).IsEqualTo(original.SettleBy);
        await Assert.That(overdue.HasCompleteMaterial).IsTrue();
        await Assert.That(overdue.MaterialReceipt).IsEqualTo(original.MaterialReceipt);
        await Assert.That(overdue.AuthoritySequence).IsNull();
        await Assert.That((await fixture.Retained.Repository.GetStateAsync()).HighWaterSequence).IsEqualTo(0L);
        await Assert.That(await disposition.UserExternalLogins.AnyAsync(row => row.UserId == fixture.Subject)).IsTrue();
    }

    [Test]
    public async Task ModerationErasureWorstBreak_ScheduledExpiryDropsEligibilityWithoutKeysOrRewritingSettledReceipt()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        await using (var capture = fixture.Database.Context())
        {
            var service = new ModerationRecognitionErasure(fixture.Repository(capture),
                fixture.Retained.Repository, fixture.Retained.Repository,
                new RelationalSettingMutationLock(capture, new EfCoreUnitOfWork(capture)));
            await Assert.That(await service.AppendAsync(fixture.Request, fixture.ReceiptHash,
                Now.AddHours(2), CancellationToken.None)).IsNotNull();
        }
        await using var resumed = fixture.Database.Context();
        var repository = fixture.Repository(resumed);
        var original = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        fixture.Keys.Available = false;
        // The same bounded pass must leave a still-effective contribution untouched.
        await Assert.That(await repository.ExpireMatchingEligibilityAsync(1, CancellationToken.None)).IsEqualTo(0);
        fixture.Clock.UtcNow = original.Material.Single().EndsAt!.Value.AddTicks(-1);
        await Assert.That(await repository.ExpireMatchingEligibilityAsync(1, CancellationToken.None)).IsEqualTo(0);
        fixture.Clock.UtcNow = original.Material.Single().EndsAt!.Value;
        var replay = new ModerationRecognitionErasure(repository,
            fixture.Retained.Repository, fixture.Retained.Repository,
            new RelationalSettingMutationLock(resumed, new EfCoreUnitOfWork(resumed)));
        // There are no received requests left; ordinary scheduled replay still expires material.
        await replay.ReconcileReceivedAsync(CancellationToken.None);
        var expired = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        await Assert.That(expired.Material.Single().DroppedAt).IsEqualTo(fixture.Clock.UtcNow);
        await Assert.That(expired.Material.Single().EndsAt).IsEqualTo(original.Material.Single().EndsAt);
        await Assert.That(expired.MaterialReceipt).IsEqualTo(original.MaterialReceipt);
        await Assert.That(expired.HasCompleteMaterial).IsTrue();
        await Assert.That(expired.AuthoritySequence).IsEqualTo(original.AuthoritySequence);
        await Assert.That(expired.State).IsEqualTo(ModerationRecognitionPendingState.AuthorityBound);
        await Assert.That(await repository.ExpireMatchingEligibilityAsync(1, CancellationToken.None)).IsEqualTo(0);
    }

    [Test]
    public async Task ModerationErasureWorstBreak_MatchingRetentionCeilingDoesNotRevokeOrExtendCanonicalSanction()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync(sanctionDuration: TimeSpan.FromDays(366));
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        var original = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        fixture.Keys.Available = false;
        fixture.Clock.UtcNow = original.Material.Single().RetainUntil.AddTicks(-1);
        await Assert.That(await repository.ExpireMatchingEligibilityAsync(1, CancellationToken.None)).IsEqualTo(0);
        fixture.Clock.UtcNow = original.Material.Single().RetainUntil;
        await Assert.That(await repository.ExpireMatchingEligibilityAsync(1, CancellationToken.None)).IsEqualTo(1);
        var expired = (await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        await Assert.That(expired.Material.Single().DroppedAt).IsEqualTo(original.Material.Single().RetainUntil);
        await Assert.That(expired.Material.Single().EndsAt).IsEqualTo(Now.AddDays(366));
        await Assert.That(expired.MaterialReceipt).IsEqualTo(original.MaterialReceipt);
        await Assert.That(expired.HasCompleteMaterial).IsTrue();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var sanction = await context.ModerationSanctions.SingleAsync(row => row.Id == fixture.SanctionId);
        await Assert.That(sanction.IsEffectiveAt(fixture.Clock.UtcNow)).IsTrue();
        await Assert.That(sanction.EndsAt).IsEqualTo(Now.AddDays(366));
    }

    [Test]
    public async Task ModerationErasureWorstBreak_FailedEligibilityCommitLeavesReceiptIntactAndCanResume()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        await using (var capture = fixture.Database.Context())
        {
            var repository = fixture.Repository(capture);
            await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
            await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        }
        fixture.Clock.UtcNow = Now.AddDays(2);
        fixture.Keys.Available = false;
        await using var failing = fixture.Database.Context(new RecognitionEligibilityFailure());
        var resumed = fixture.Repository(failing);
        await Assert.ThrowsAsync<IOException>(() => resumed.ExpireMatchingEligibilityAsync(1, CancellationToken.None));
        await using (var verification = fixture.Database.Context())
        {
            var intact = (await fixture.Repository(verification).FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
            await Assert.That(intact.HasCompleteMaterial).IsTrue();
            await Assert.That(intact.Material.Single().DroppedAt).IsNull();
            await Assert.That(intact.AuthoritySequence).IsNull();
        }
        await Assert.That(await resumed.ExpireMatchingEligibilityAsync(1, CancellationToken.None)).IsEqualTo(1);
        var settled = (await resumed.FindAsync(fixture.Request.IntentId, CancellationToken.None))!;
        await Assert.That(settled.Material.Single().DroppedAt).IsEqualTo(fixture.Clock.UtcNow);
        await Assert.That(settled.HasCompleteMaterial).IsTrue();
        await Assert.That(settled.AuthoritySequence).IsNull();
    }

    [Test]
    public async Task ModerationErasureWorstBreak_NativeSourceWithdrawalBetweenStagesPreventsAuthorityAppend()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var boundary = new RecognitionCommitBoundary(fixture.Retained.Repository) { PauseAfterCapture = true };
        var service = new ModerationRecognitionErasure(fixture.Repository(context), boundary, fixture.Retained.Repository,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        var captured = boundary.CaptureCommitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var append = service.AppendAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        try
        {
            await captured;
            await fixture.WithdrawPrivacySourceAsync();
        }
        finally { boundary.ContinueCapture.TrySetResult(); }
        await Assert.That(await append.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
        await Assert.That((await fixture.Retained.Repository.GetStateAsync()).HighWaterSequence).IsEqualTo(0L);
        var pending = await service.FindAsync(fixture.Request.IntentId, CancellationToken.None);
        await Assert.That(pending!.HasCompleteMaterial).IsTrue();
        await Assert.That(pending.Failure).IsEqualTo(ModerationRecognitionPendingFailure.SourceUnavailable);
        await Assert.That(pending.AuthoritySequence).IsNull();
    }

    [Test]
    public async Task ModerationErasureWorstBreak_CaptureStoreFailureKeepsReceivedRequestButNoPartialReceipt()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        await using var context = fixture.Database.Context(new RecognitionCaptureFailure());
        var service = new ModerationRecognitionErasure(fixture.Repository(context),
            fixture.Retained.Repository, fixture.Retained.Repository,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        await Assert.That(await service.AppendAsync(fixture.Request, fixture.ReceiptHash,
            Now.AddHours(2), CancellationToken.None)).IsNull();
        await using var verification = fixture.Database.Context();
        var pending = await fixture.Repository(verification).FindAsync(fixture.Request.IntentId, CancellationToken.None);
        await Assert.That(pending!.HasCompleteMaterial).IsFalse();
        await Assert.That(pending.Material.Count).IsEqualTo(0);
        await Assert.That(pending.Failure).IsEqualTo(ModerationRecognitionPendingFailure.StoreUnavailable);
        await Assert.That((await fixture.Retained.Repository.GetStateAsync()).HighWaterSequence).IsEqualTo(0L);
    }

    [Test]
    public async Task ModerationErasureWorstBreak_UncommittedAuthorityFactCannotAuthorizeInputDestruction()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        await fixture.Retained.Repository.ExecuteSerializedAsync(async ct =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.VerifyPurgeAsync(fixture.Fact(), ct));
            return true;
        }, CancellationToken.None);
        await Assert.That(await context.UserExternalLogins.AnyAsync(row => row.UserId == fixture.Subject)).IsTrue();
    }

    private sealed class RecognitionCaptureFailure : SaveChangesInterceptor
    {
        private bool failed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!failed && eventData.Context!.ChangeTracker.Entries<ModerationRecognitionCaptureItem>()
                .Any(entry => entry.State == EntityState.Added))
            {
                failed = true;
                throw new IOException("Injected native capture store boundary failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecognitionEligibilityFailure : SaveChangesInterceptor
    {
        private bool failed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!failed && eventData.Context!.ChangeTracker.Entries<ModerationRecognitionCaptureItem>()
                .Any(entry => entry.State == EntityState.Modified && entry.Entity.DroppedAt is not null))
            {
                failed = true;
                throw new IOException("Injected native matching eligibility write failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecognitionCommitBoundary(IPrivacyIdentityFenceAuthority inner) : IPrivacyIdentityFenceAuthority
    {
        private int stages;
        public bool LoseAppendAcknowledgement { get; set; }
        public bool PauseAfterCapture { get; init; }
        public TaskCompletionSource CaptureCommitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueCapture { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<T> ExecuteSerializedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token)
        {
            var result = await inner.ExecuteSerializedAsync(operation, token);
            stages++;
            if (stages == 2 && PauseAfterCapture)
            {
                CaptureCommitted.TrySetResult();
                await ContinueCapture.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            }
            if (stages == 3 && LoseAppendAcknowledgement)
            {
                LoseAppendAcknowledgement = false;
                throw new IOException("Injected loss after the native retained authority commit.");
            }
            return result;
        }

        public Task ValidateKeyAsync(string keyId, string tag, CancellationToken token) => inner.ValidateKeyAsync(keyId, tag, token);
        public Task<T> ExecuteEnrollmentSerializedAsync<T>(Func<CancellationToken, Task<T>> operation,
            Func<CancellationToken, Task> beforePrimaryCommit, CancellationToken token) =>
            inner.ExecuteEnrollmentSerializedAsync(operation, beforePrimaryCommit, token);
        public Task<PrivacyErasureIntent?> FindAsync(PrivacyIdentityFingerprint fingerprint, CancellationToken token) =>
            inner.FindAsync(fingerprint, token);
        public Task<bool> IsSubjectFencedAsync(Guid subject, CancellationToken token) => inner.IsSubjectFencedAsync(subject, token);
    }

    private sealed class RecognitionAuthorityDatabase : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection anchor;
        public EmbeddedPrivacyErasureAuthorityRepository Repository { get; }
        private RecognitionAuthorityDatabase(SqliteConnection anchor, DeploymentClock clock)
        {
            this.anchor = anchor;
            Repository = new(this, clock, Options.Create(new PrivacyErasureOptions()));
        }
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(anchor.ConnectionString).UseSnakeCaseNamingConvention().Options);
        public Task<EmbeddedPrivacyErasureAuthorityDbContext> CreateDbContextAsync(CancellationToken token = default) =>
            Task.FromResult(CreateDbContext());
        public static async Task<RecognitionAuthorityDatabase> CreateAsync(DeploymentClock clock)
        {
            var database = new RecognitionAuthorityDatabase(await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync(), clock);
            await using var context = database.CreateDbContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }
        public ValueTask DisposeAsync() => anchor.DisposeAsync();
    }
}
