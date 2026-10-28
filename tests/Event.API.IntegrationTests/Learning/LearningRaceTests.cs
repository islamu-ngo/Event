using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using UserLearningState = Explore.Domain.Learning.UserLearningState;
using Explore.Persistence;
using Explore.Persistence.Privacy;
using Explore.Persistence.Repositories;
using ISLAMU.Wire.Contracts.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.API.IntegrationTests.Learning;

[NotInParallel]
public sealed class LearningRaceTests
{
    [Test]
    public async Task RetainedErasureCommittedAfterHumanPreparationRejectsThePausedOriginalOutcome()
    {
        var barrier = new LearningPreflightBarrier();
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync(barrier, null);
        using var member = await fixture.CreateMemberAsync();
        var before = await LearningOperationTests.Context(member.Client);
        using var claimResponse = await member.Client.PostAsJsonAsync("/api/learning/tours/claim",
            new LearningClaimTourRequest
            {
                ExpectedScopeStamp = before.ScopeStamp, ExpectedRevision = 0, IntentId = Guid.CreateVersion7(),
                ContextKey = "self", Family = ISLAMU.Wire.Contracts.Learning.LearningTourFamily.Shell, MaterialVersion = 1
            });
        await Assert.That(claimResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var claimed = (await claimResponse.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        await using (var metadata = fixture.OpenInspection())
            barrier.Table = metadata.Model.FindEntityType(typeof(UserLearningState))!.GetTableName()!;
        barrier.Armed = true;
        var pending = member.Client.PostAsJsonAsync("/api/learning/tours/outcome",
            new LearningRecordTourOutcomeRequest
            {
                ExpectedScopeStamp = before.ScopeStamp, ExpectedRevision = claimed.LearnerRevision,
                IntentId = Guid.CreateVersion7(), ClaimToken = claimed.Claim!.Token,
                Family = ISLAMU.Wire.Contracts.Learning.LearningTourFamily.Shell, MaterialVersion = 1,
                Outcome = LearningTourOutcome.Completed, StepIndex = 1
            });
        Explore.Domain.PrivacyErasureIntent? fact = null;
        try
        {
            await barrier.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
            fact = await fixture.CommitRetainedErasureAsync(member.UserId);
        }
        finally { barrier.Release.TrySetResult(); }
        using var rejected = await pending;
        await Assert.That(rejected.IsSuccessStatusCode).IsFalse();
        await using var database = fixture.OpenInspection();
        var retained = await database.UserLearningStates.AsNoTracking()
            .SingleAsync(row => row.TenantId == fixture.TenantId && row.UserId == member.UserId);
        await Assert.That(retained.Revision).IsEqualTo(claimed.LearnerRevision);
        await using var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var store = new LearningPrivacyErasureStore(database);
        await store.PurgeAsync(fact!, default);
        await store.RequireAbsentAsync(fact!, default);
        await transaction.CommitAsync();
    }

    [Test]
    public async Task AnAlreadyWrittenOriginalClaimWinsAgainstTheConcurrentStaleTab()
    {
        var barrier = new LearningWriteBarrier();
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync(barrier, null);
        using var member = await fixture.CreateMemberAsync();
        var context = await LearningOperationTests.Context(member.Client);
        var input = new LearningClaimTourRequest
        {
            ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = 0, IntentId = Guid.CreateVersion7(),
            ContextKey = "self", Family = ISLAMU.Wire.Contracts.Learning.LearningTourFamily.Shell, MaterialVersion = 1
        };
        barrier.Armed = true;
        var firstTask = member.Client.PostAsJsonAsync("/api/learning/tours/claim", input);
        Task<HttpResponseMessage>? secondTask = null;
        try
        {
            await barrier.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
            secondTask = member.Client.PostAsJsonAsync("/api/learning/tours/claim",
                input with { IntentId = Guid.CreateVersion7() });
        }
        finally { barrier.Release.TrySetResult(); }
        using var first = await firstTask;
        using var second = await secondTask!;
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var winner = (await first.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        var stored = await LearningOperationTests.Context(member.Client);
        await Assert.That(stored.Claim!.Token).IsEqualTo(winner.Claim!.Token);
        await Assert.That(stored.LearnerRevision).IsEqualTo(winner.LearnerRevision);
        await Assert.That(stored.Tours.Count(row => row.Outcome is not null)).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentRetainedErasurePurgesLearningAfterTheOriginalOwnerSettles()
    {
        var barrier = new LearningWriteBarrier();
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync(barrier, null);
        using var member = await fixture.CreateMemberAsync();
        var context = await LearningOperationTests.Context(member.Client);
        barrier.Armed = true;
        var write = member.Client.PatchAsJsonAsync("/api/learning/preferences", Preferences(context));
        Task<Explore.Domain.PrivacyErasureIntent>? erasure = null;
        var erasureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await barrier.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
            erasure = Task.Run(async () =>
            {
                erasureStarted.TrySetResult();
                return await fixture.CommitRetainedErasureAsync(member.UserId);
            });
            await erasureStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { barrier.Release.TrySetResult(); }
        using var response = await write;
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var fact = await erasure!;
        using var hidden = await member.Client.GetAsync("/api/learning/context");
        await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await using (var database = fixture.OpenInspection())
        {
            await using var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var store = new LearningPrivacyErasureStore(database);
            await store.PurgeAsync(fact, default);
            await store.RequireAbsentAsync(fact, default);
            await transaction.CommitAsync();
        }
        using var retry = await member.Client.PatchAsJsonAsync("/api/learning/preferences", Preferences(context));
        await Assert.That(retry.IsSuccessStatusCode).IsFalse();
        await using var observer = fixture.OpenInspection();
        await Assert.That(await observer.UserLearningStates.AnyAsync(row => row.UserId == member.UserId)).IsFalse();
    }

    [Test]
    public async Task RefusedOriginalWriteReturnsACleanReusedTenantPoolLease()
    {
        var interruption = new CaptureAndRefuse();
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync(interruption, null, pooled: true);
        using var member = await fixture.CreateMemberAsync();
        var context = await LearningOperationTests.Context(member.Client);
        using var initial = await member.Client.PatchAsJsonAsync("/api/learning/preferences", Preferences(context));
        await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
        context = await LearningOperationTests.Context(member.Client);
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ObserveContextReturn((owner, lease) =>
        {
            if (ReferenceEquals(owner, interruption.Original) && lease == interruption.Lease)
                returned.TrySetResult();
        });
        interruption.Armed = true;
        var pending = member.Client.PatchAsJsonAsync("/api/learning/preferences", Preferences(context));
        ExploreDbContext? parked = null;
        try
        {
            await interruption.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(interruption.Lease.Lease).IsGreaterThan(0);
            // Keep the idle pool slot rented until the original request scope returns its own context.
            parked = await fixture.RentTenantAsync(Guid.CreateVersion7());
        }
        finally { interruption.Release.TrySetResult(); }
        await using var parkedLease = parked!;
        using var rejected = await pending;
        await returned.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(interruption.Original).IsNotNull();
        await Assert.That(rejected.IsSuccessStatusCode).IsFalse();
        Guid otherTenant = Guid.CreateVersion7();
        await using (var rented = await fixture.RentTenantAsync(otherTenant))
        {
            await Assert.That(ReferenceEquals(rented, interruption.Original)).IsTrue();
            await Assert.That(rented.ContextId).IsNotEqualTo(interruption.Lease);
            await Assert.That(rented.Database.CurrentTransaction).IsNull();
            await Assert.That(rented.ChangeTracker.Entries()).IsEmpty();
            await Assert.That(rented.TenantFilterTenantId).IsEqualTo(otherTenant);
            await Assert.That(rented.HasHumanCommitContextLease).IsFalse();
            await Assert.That(rented.HumanCommitAuditSubject).IsNull();
            Assert.Throws<UnauthorizedAccessException>(rented.RequireHumanCommitAdmission);
            await Assert.That(await new LearningRepository(rented)
                .GetStateAsync(fixture.TenantId, member.UserId, false, default)).IsNull();
        }
        using var accepted = await member.Client.PatchAsJsonAsync("/api/learning/preferences", Preferences(context));
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await LearningOperationTests.Context(member.Client)).LearnerRevision).IsEqualTo(2);
    }

    private static LearningUpdatePreferencesRequest Preferences(LearningContextDto context) => new()
    {
        ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = context.LearnerRevision,
        SuppressAutomaticTours = true, HideLearning = false, PinLearning = false
    };

    private sealed class LearningWriteBarrier : SaveChangesInterceptor
    {
        private bool _saving;
        internal bool Armed { get; set; }
        internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _saving = Armed && eventData.Context!.ChangeTracker.Entries<UserLearningState>()
                .Any(entry => entry.State is EntityState.Added or EntityState.Modified);
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_saving)
            {
                _saving = false;
                Armed = false;
                if (eventData.Context!.Database.CurrentTransaction is null)
                    throw new InvalidOperationException("The write barrier requires the still-live original transaction.");
                Arrived.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    private sealed class LearningPreflightBarrier : DbCommandInterceptor
    {
        internal string Table { get; set; } = string.Empty;
        internal bool Armed { get; set; }
        internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is ExploreDbContext owner && owner.Database.CurrentTransaction is null
                && command.CommandText.Contains(Table, StringComparison.Ordinal))
            {
                Armed = false;
                if (!owner.HasHumanCommitContextLease)
                    throw new InvalidOperationException("The preflight barrier requires the original prepared human.");
                Arrived.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    private sealed class CaptureAndRefuse : SaveChangesInterceptor
    {
        private bool _saving;
        internal bool Armed { get; set; }
        internal ExploreDbContext? Original { get; private set; }
        internal DbContextId Lease { get; private set; }
        internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _saving = Armed && eventData.Context!.ChangeTracker.Entries<UserLearningState>()
                .Any(entry => entry.State is EntityState.Added or EntityState.Modified);
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_saving)
            {
                _saving = false;
                Armed = false;
                Original = (ExploreDbContext)eventData.Context!;
                Lease = Original.ContextId;
                Arrived.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                var state = Original.ChangeTracker.Entries<UserLearningState>().Single().Entity;
                await Original.UserLearningStates.Where(row => row.Id == state.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision + 1),
                        cancellationToken);
            }
            return result;
        }
    }
}
