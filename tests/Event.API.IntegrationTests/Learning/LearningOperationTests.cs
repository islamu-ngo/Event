using System.Net;
using System.Net.Http.Json;
using Explore.Domain.Learning;
using Explore.Persistence;
using ISLAMU.Wire.Contracts.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WireAction = ISLAMU.Wire.Contracts.Learning.LearningPracticeAction;
using WireFamily = ISLAMU.Wire.Contracts.Learning.LearningTourFamily;
using WireOutcome = ISLAMU.Wire.Contracts.Learning.LearningTourOutcome;

namespace Event.API.IntegrationTests.Learning;

[ClassDataSource<LearningHttpFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class LearningOperationTests(LearningHttpFixture fixture)
{
    [Test]
    [Arguments("event-details", "online", 2)]
    [Arguments("sessions-program", "two-sessions", 2)]
    [Arguments("ticket-capacity", "capacity-12", 12)]
    [Arguments("attendees-team", "helper", 2)]
    [Arguments("publication-lifecycle", "preview-publication", 1)]
    [Arguments("ai-example-review", "reject-example", 0)]
    public async Task AuthoredRecipeCompletesWithoutProductionEffects(string recipe, string choice, int value)
    {
        using LearningHttpMember member = await fixture.CreateMemberAsync();
        var context = await Context(member.Client);
        await using var before = fixture.OpenInspection();
        var counts = await Effects(before);
        var start = new LearningApplyPracticeActionRequest
        {
            ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = context.LearnerRevision,
            ExpectedPracticeRevision = 0, IntentId = Guid.CreateVersion7(), ContextKey = "self",
            RecipeKey = recipe, MaterialVersion = 1, Action = WireAction.Start
        };
        var started = await Apply(member.Client, start);
        var repeated = await Apply(member.Client, start);
        await Assert.That(repeated.Practice!.Id).IsEqualTo(started.Practice!.Id);
        await Assert.That(repeated.LearnerRevision).IsEqualTo(started.LearnerRevision);
        var choose = start with
        {
            IntentId = Guid.CreateVersion7(), PracticeId = started.Practice.Id,
            ExpectedRevision = started.LearnerRevision, ExpectedPracticeRevision = started.Practice.Revision,
            Action = WireAction.Choose, ChoiceKey = choice
        };
        var chosen = await Apply(member.Client, choose);
        await Assert.That(chosen.Practice!.Simulation.Value).IsEqualTo(value);
        var confirmed = await Apply(member.Client, choose with
        {
            IntentId = Guid.CreateVersion7(), ExpectedRevision = chosen.LearnerRevision,
            ExpectedPracticeRevision = chosen.Practice.Revision, Action = WireAction.Confirm, ChoiceKey = null
        });
        await Assert.That(confirmed.Practice!.Simulation.Status).IsEqualTo(LearningSimulationStatus.Simulated);
        await Assert.That(confirmed.Practice.Simulation.Step).IsEqualTo(2);
        await Assert.That(confirmed.Practice.Simulation.Receipt!.ProductionEffects).IsFalse();
        await Assert.That(confirmed.Practice.Simulation.Receipt.IsSimulation).IsTrue();
        using HttpResponseMessage resumedResponse = await member.Client.GetAsync(
            $"/api/learning/practice/{confirmed.Practice.Id:D}");
        await Assert.That(resumedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var resumed = (await resumedResponse.Content.ReadFromJsonAsync<LearningPracticeDto>())!;
        await Assert.That(resumed.Simulation.ChoiceKey).IsEqualTo(choice);
        await Assert.That(resumed.Simulation.Value).IsEqualTo(value);
        await Assert.That(resumedResponse.Headers.CacheControl?.NoStore).IsTrue();
        await using var after = fixture.OpenInspection();
        await Assert.That(await Effects(after)).IsEqualTo(counts);
    }

    [Test]
    public async Task AnotherInternalOwnerCannotReadOrMutatePrivatePractice()
    {
        using LearningHttpMember owner = await fixture.CreateMemberAsync();
        using LearningHttpMember stranger = await fixture.CreateMemberAsync();
        var context = await Context(owner.Client);
        var started = await Apply(owner.Client, new()
        {
            ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = 0,
            ExpectedPracticeRevision = 0, IntentId = Guid.CreateVersion7(), ContextKey = "self",
            RecipeKey = "event-details", MaterialVersion = 1, Action = WireAction.Start
        });
        using var read = await stranger.Client.GetAsync($"/api/learning/practice/{started.Practice!.Id:D}");
        await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var foreign = await Context(stranger.Client);
        using var mutation = await stranger.Client.PostAsJsonAsync("/api/learning/practice/actions",
            new LearningApplyPracticeActionRequest
            {
                ExpectedScopeStamp = foreign.ScopeStamp, ExpectedRevision = 0, ExpectedPracticeRevision = 1,
                PracticeId = started.Practice.Id, IntentId = Guid.CreateVersion7(), ContextKey = "self",
                RecipeKey = "event-details", MaterialVersion = 1, Action = WireAction.Clear
            });
        await Assert.That(mutation.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await Context(owner.Client)).OwnedPractices.Single().Id).IsEqualTo(started.Practice.Id);
        await Assert.That((await Context(stranger.Client)).StateId).IsNull();
    }

    [Test]
    public async Task ChangedIntentAndStaleRevisionCannotOverwriteTheWinningPractice()
    {
        using LearningHttpMember member = await fixture.CreateMemberAsync();
        var context = await Context(member.Client);
        var start = new LearningApplyPracticeActionRequest
        {
            ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = 0,
            ExpectedPracticeRevision = 0, IntentId = Guid.CreateVersion7(), ContextKey = "self",
            RecipeKey = "event-details", MaterialVersion = 1, Action = WireAction.Start
        };
        var started = await Apply(member.Client, start);
        using var changed = await member.Client.PostAsJsonAsync("/api/learning/practice/actions",
            start with { RecipeKey = "ticket-capacity" });
        await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var stale = await member.Client.PostAsJsonAsync("/api/learning/practice/actions", start with
        {
            IntentId = Guid.CreateVersion7(), PracticeId = started.Practice!.Id,
            ExpectedPracticeRevision = 1, Action = WireAction.Choose, ChoiceKey = "online"
        });
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var retained = await Context(member.Client);
        await Assert.That(retained.LearnerRevision).IsEqualTo(started.LearnerRevision);
        await Assert.That(retained.OwnedPractices.Count).IsEqualTo(1);
    }

    [Test]
    public async Task OneTourClaimAndItsTerminalRetryPreserveTheOriginalOutcome()
    {
        using LearningHttpMember member = await fixture.CreateMemberAsync();
        var context = await Context(member.Client);
        var claim = new LearningClaimTourRequest
        {
            ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = 0, IntentId = Guid.CreateVersion7(),
            ContextKey = "self", Family = WireFamily.Shell, MaterialVersion = 1
        };
        using var claimedResponse = await member.Client.PostAsJsonAsync("/api/learning/tours/claim", claim);
        await Assert.That(claimedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var claimed = (await claimedResponse.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        using var lost = await member.Client.PostAsJsonAsync("/api/learning/tours/claim", claim with
        {
            IntentId = Guid.CreateVersion7(), ExpectedRevision = claimed.LearnerRevision
        });
        await Assert.That(lost.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var outcome = new LearningRecordTourOutcomeRequest
        {
            ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = claimed.LearnerRevision,
            IntentId = Guid.CreateVersion7(), ClaimToken = claimed.Claim!.Token,
            Family = WireFamily.Shell, MaterialVersion = 1, Outcome = WireOutcome.Completed, StepIndex = 1
        };
        using var completedResponse = await member.Client.PostAsJsonAsync("/api/learning/tours/outcome", outcome);
        await Assert.That(completedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var completed = (await completedResponse.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        using var repeated = await member.Client.PostAsJsonAsync("/api/learning/tours/outcome", outcome);
        await Assert.That(repeated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var retried = (await repeated.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        await Assert.That(retried.LearnerRevision).IsEqualTo(completed.LearnerRevision);
        var final = await Context(member.Client);
        await Assert.That(final.Claim).IsNull();
        await Assert.That(final.Tours.Single(row => row.Family == WireFamily.Shell).Outcome)
            .IsEqualTo(WireOutcome.Completed);
        using var changedFamily = await member.Client.PostAsJsonAsync("/api/learning/tours/outcome",
            outcome with { Family = WireFamily.Studio });
        await Assert.That(changedFamily.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    internal static async Task<LearningContextDto> Context(HttpClient client)
    {
        using var response = await client.GetAsync("/api/learning/context");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LearningContextDto>())!;
    }

    private static async Task<LearningMutationReceiptDto> Apply(
        HttpClient client, LearningApplyPracticeActionRequest input)
    {
        using var response = await client.PostAsJsonAsync("/api/learning/practice/actions", input);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        return (await response.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
    }

    private static async Task<(int Events, int Orders, int Participants, int Ai, int Pds, int Email, int Integrations)> Effects(
        ExploreDbContext database) => (await database.Events.CountAsync(), await database.RegistrationOrders.CountAsync(),
        await database.RegistrationParticipants.CountAsync(), await database.AiConversations.CountAsync(),
        await database.PdsSyncOutbox.CountAsync(), await database.EmailDispatchOutbox.CountAsync(),
        await database.IntegrationSyncOutbox.CountAsync());
}

[NotInParallel]
public sealed class LearningFinalAdmissionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RetainedErasureWinsBeforeWriteOrPurgesTheAlreadyCommittedWriter(bool eraseFirst)
    {
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync();
        using var member = await fixture.CreateMemberAsync();
        var context = await LearningOperationTests.Context(member.Client);
        var input = new LearningUpdatePreferencesRequest
        {
            ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = 0,
            SuppressAutomaticTours = true, HideLearning = false, PinLearning = false
        };
        Explore.Domain.PrivacyErasureIntent fact;
        if (eraseFirst)
        {
            fact = await fixture.CommitRetainedErasureAsync(member.UserId);
            using var rejected = await member.Client.PatchAsJsonAsync("/api/learning/preferences", input);
            await Assert.That(rejected.IsSuccessStatusCode).IsFalse();
        }
        else
        {
            using var written = await member.Client.PatchAsJsonAsync("/api/learning/preferences", input);
            await Assert.That(written.StatusCode).IsEqualTo(HttpStatusCode.OK);
            fact = await fixture.CommitRetainedErasureAsync(member.UserId);
        }
        using var hidden = await member.Client.GetAsync("/api/learning/context");
        await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(hidden.Headers.CacheControl?.NoStore).IsTrue();
        await using (var purge = fixture.OpenInspection())
        {
            await using var transaction = await purge.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var store = new Explore.Persistence.Privacy.LearningPrivacyErasureStore(purge);
            await store.PurgeAsync(fact, default);
            await store.RequireAbsentAsync(fact, default);
            await transaction.CommitAsync();
        }
        using var resurrect = await member.Client.PatchAsJsonAsync("/api/learning/preferences", input);
        await Assert.That(resurrect.IsSuccessStatusCode).IsFalse();
        await using var observer = fixture.OpenInspection();
        await Assert.That(await observer.UserLearningStates.AnyAsync(row => row.UserId == member.UserId)).IsFalse();
    }

    [Test]
    public async Task SavingAnotherOwnersRootCannotEscapeTheOriginalWriteInventory()
    {
        Guid strangerId = Guid.Empty;
        var interception = new AfterLearningSaved((_, _, _) => Task.CompletedTask, database =>
        {
            var original = database.ChangeTracker.Entries<UserLearningState>()
                .Single(entry => entry.State == EntityState.Added).Entity;
            database.UserLearningStates.Add(UserLearningState.Create(Guid.CreateVersion7(),
                original.TenantId, strangerId, original.UpdatedAtUtc));
        });
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync(interception, null);
        using var member = await fixture.CreateMemberAsync();
        using var stranger = await fixture.CreateMemberAsync();
        strangerId = stranger.UserId;
        var context = await LearningOperationTests.Context(member.Client);
        interception.Armed = true;
        using var response = await member.Client.PatchAsJsonAsync("/api/learning/preferences",
            new LearningUpdatePreferencesRequest
            {
                ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = 0,
                SuppressAutomaticTours = true, HideLearning = false, PinLearning = false
            });
        await Assert.That(interception.Observed).IsTrue();
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await using var observer = fixture.OpenInspection();
        await Assert.That(await observer.UserLearningStates.AnyAsync(row =>
            row.UserId == member.UserId || row.UserId == strangerId)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CredentialExpiryOrOriginalPrincipalReplacementAfterWriteCannotCommit(bool replacePrincipal)
    {
        var clock = new LearningClock();
        LearningHttpFixture? active = null;
        var interception = new AfterLearningSaved((_, _, _) =>
        {
            if (replacePrincipal)
            {
                var request = active!.RequestAccessor.HttpContext!;
                request.User = new System.Security.Claims.ClaimsPrincipal(request.User);
            }
            else
                clock.Advance(TimeSpan.FromDays(2));
            return Task.CompletedTask;
        });
        await using var fixture = new LearningHttpFixture();
        active = fixture;
        await fixture.InitializeAsync(interception, clock);
        using var member = await fixture.CreateMemberAsync();
        var context = await LearningOperationTests.Context(member.Client);
        interception.Armed = true;
        using var response = await member.Client.PatchAsJsonAsync("/api/learning/preferences",
            new LearningUpdatePreferencesRequest
            {
                ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = 0,
                SuppressAutomaticTours = true, HideLearning = false, PinLearning = false
            });
        await Assert.That(interception.Observed).IsTrue();
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await using var observer = fixture.OpenInspection();
        await Assert.That(await observer.UserLearningStates.AnyAsync(row =>
            row.TenantId == fixture.TenantId && row.UserId == member.UserId)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AfterWriteImageOrMembershipChangeRollsBackTheWholeLearningEffect(bool changeMembership)
    {
        var interception = new AfterLearningSaved(async (database, state, token) =>
        {
            if (changeMembership)
                await database.TenantUsers.Where(row => row.TenantId == state.TenantId && row.UserId == state.UserId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true), token);
            else
                await database.UserLearningStates.Where(row => row.Id == state.Id && row.TenantId == state.TenantId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision + 1), token);
        });
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync(interception, null);
        using var member = await fixture.CreateMemberAsync();
        var context = await LearningOperationTests.Context(member.Client);
        interception.Armed = true;
        using var response = await member.Client.PatchAsJsonAsync("/api/learning/preferences",
            new LearningUpdatePreferencesRequest
            {
                ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = 0,
                SuppressAutomaticTours = true, HideLearning = true, PinLearning = false
            });
        await Assert.That(interception.Observed).IsTrue();
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await using var observer = fixture.OpenInspection();
        await Assert.That(await observer.UserLearningStates.AnyAsync(row =>
            row.TenantId == fixture.TenantId && row.UserId == member.UserId)).IsFalse();
        await Assert.That(await observer.TenantUsers.AnyAsync(row =>
            row.TenantId == fixture.TenantId && row.UserId == member.UserId && !row.IsDeleted)).IsTrue();
    }

    private sealed class AfterLearningSaved(
        Func<ExploreDbContext, UserLearningState, CancellationToken, Task> afterWrite,
        Action<ExploreDbContext>? beforeSave = null) : SaveChangesInterceptor
    {
        private UserLearningState? _state;
        internal bool Armed { get; set; }
        internal bool Observed { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                _state = eventData.Context!.ChangeTracker.Entries<UserLearningState>()
                    .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
                    .Select(entry => entry.Entity).SingleOrDefault();
                if (_state is not null)
                    beforeSave?.Invoke((ExploreDbContext)eventData.Context);
            }
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_state is { } state)
            {
                _state = null;
                Armed = false;
                Observed = true;
                if (eventData.Context!.Database.CurrentTransaction is null)
                    throw new InvalidOperationException("Learning must still be inside its original physical owner.");
                await afterWrite((ExploreDbContext)eventData.Context, state, cancellationToken);
            }
            return result;
        }
    }

    private sealed class LearningClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now += duration;
    }
}
