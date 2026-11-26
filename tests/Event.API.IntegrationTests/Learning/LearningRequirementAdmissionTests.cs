using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Domain.Learning;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Privacy;
using Explore.Persistence.Repositories;
using ISLAMU.Wire.Contracts.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.API.IntegrationTests.Learning;

[NotInParallel]
public sealed class LearningRequirementAdmissionTests
{
    private const string Route = "/api/learning/requirements/tenant-admin-orientation/acknowledge";
    private const string PolicyRoute = "/api/tenant/settings/documents/learning-guidance";

    [Test]
    public async Task ExplicitAcknowledgementIsPrivateReplayableAndIndependentOfSuppressionOrSharedSpace()
    {
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync();
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        using var otherAdministrator = await fixture.CreateMemberAsync(administrator: true);
        await SetRequiredPolicyAsync(administrator.Client);
        var initial = await LearningOperationTests.Context(administrator.Client);
        await Assert.That(initial.Requirement.Status).IsEqualTo(LearningRequirementStatus.Pending);
        await Assert.That(initial.StateId).IsNull();
        using var preferences = await administrator.Client.PatchAsJsonAsync("/api/learning/preferences",
            Preferences(initial));
        await Assert.That(preferences.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var pending = await LearningOperationTests.Context(administrator.Client);
        await Assert.That(pending.Requirement.Status).IsEqualTo(LearningRequirementStatus.Pending);
        await AssertLinkAsync(administrator.Client, expected: true);
        var input = Acknowledge(pending);

        using var accepted = await administrator.Client.PostAsJsonAsync(Route, input);

        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(accepted.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(accepted.Headers.CacheControl?.NoStore).IsTrue();
        var receipt = (await accepted.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        await Assert.That(receipt.LearnerRevision).IsEqualTo(pending.LearnerRevision + 1);
        await Assert.That(receipt.State!.Requirement.Status).IsEqualTo(LearningRequirementStatus.Satisfied);
        await Assert.That(receipt.State.Preferences.SuppressAutomaticTours).IsTrue();
        await Assert.That(receipt.State.Preferences.HideLearning).IsTrue();
        await Assert.That(receipt.State.Claim).IsNull();
        await Assert.That(receipt.State.OwnedPractices).IsEmpty();
        await AssertLinkAsync(administrator.Client, expected: false);

        using var replay = await administrator.Client.PostAsJsonAsync(Route, input);
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var repeated = (await replay.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        await Assert.That(repeated.Id).IsEqualTo(receipt.Id);
        await Assert.That(repeated.LearnerRevision).IsEqualTo(receipt.LearnerRevision);
        await Assert.That(repeated.State!.Requirement.AcknowledgedAtUtc)
            .IsEqualTo(receipt.State.Requirement.AcknowledgedAtUtc);
        var other = await LearningOperationTests.Context(otherAdministrator.Client);
        await Assert.That(other.Requirement.Status).IsEqualTo(LearningRequirementStatus.Pending);
        await Assert.That(other.StateId).IsNull();
        await using var observer = fixture.OpenInspection();
        var state = (await new LearningRepository(observer)
            .GetStateAsync(fixture.TenantId, administrator.UserId, false, default))!;
        await Assert.That(state.RequirementAcknowledgements.Count).IsEqualTo(1);
        await Assert.That(state.RequirementAcknowledgements.Single().Id).IsEqualTo(receipt.Id);
        await Assert.That(await observer.TenantLearningSpaces.AnyAsync(row => row.TenantId == fixture.TenantId)).IsFalse();
        await Assert.That(await observer.UserLearningStates.CountAsync(row =>
            row.TenantId == fixture.TenantId && row.UserId == administrator.UserId)).IsEqualTo(1);
    }

    [Test]
    public async Task LaterPolicyChangePreservesMaterialSatisfactionButRejectsAStaleSubmission()
    {
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync();
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await SetRequiredPolicyAsync(administrator.Client);
        var before = await LearningOperationTests.Context(administrator.Client);
        var input = Acknowledge(before);
        using var accepted = await administrator.Client.PostAsJsonAsync(Route, input);
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var original = await LearningOperationTests.Context(administrator.Client);

        await SetRequiredPolicyAsync(administrator.Client, automaticInvitations: false);

        var changed = await LearningOperationTests.Context(administrator.Client);
        await Assert.That(changed.Requirement.Status).IsEqualTo(LearningRequirementStatus.Satisfied);
        await Assert.That(changed.Requirement.PolicySourceStamp).IsNotEqualTo(before.Requirement.PolicySourceStamp);
        await Assert.That(changed.Requirement.AcknowledgedAtUtc).IsEqualTo(original.Requirement.AcknowledgedAtUtc);
        await Assert.That(changed.LearnerRevision).IsEqualTo(original.LearnerRevision);
        using var stale = await administrator.Client.PostAsJsonAsync(Route, input);
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await using var observer = fixture.OpenInspection();
        var retained = (await new LearningRepository(observer)
            .GetStateAsync(fixture.TenantId, administrator.UserId, false, default))!;
        await Assert.That(retained.RequirementAcknowledgements.Single().PolicySourceStamp)
            .IsEqualTo(before.Requirement.PolicySourceStamp);
    }

    [Test]
    public async Task AcknowledgementDoesNotRecreateOrChangeARemovedSharedSpace()
    {
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync();
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await SetRequiredPolicyAsync(administrator.Client);
        var initial = await LearningOperationTests.Context(administrator.Client);
        using var removal = await administrator.Client.PostAsJsonAsync("/api/learning/space/actions", new
        {
            expectedScopeStamp = initial.ScopeStamp,
            expectedRevision = initial.ResourceRevision,
            action = "Remove"
        });
        await Assert.That(removal.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var removed = await LearningOperationTests.Context(administrator.Client);
        await Assert.That(removed.SpaceStatus).IsEqualTo(LearningSpaceStatus.Removed);

        using var response = await administrator.Client.PostAsJsonAsync(Route, Acknowledge(removed));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = await LearningOperationTests.Context(administrator.Client);
        await Assert.That(result.Requirement.Status).IsEqualTo(LearningRequirementStatus.Satisfied);
        await Assert.That(result.SpaceId).IsEqualTo(removed.SpaceId);
        await Assert.That(result.ResourceRevision).IsEqualTo(removed.ResourceRevision);
        await Assert.That(result.SpaceStatus).IsEqualTo(LearningSpaceStatus.Removed);
    }

    [Test]
    public async Task AcknowledgementPreservesAnActiveTourAndRejectsChangedOrStaleIntents()
    {
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync(null, new AcknowledgementClock());
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await SetRequiredPolicyAsync(administrator.Client);
        var plan = await LearningOperationTests.TourPlan(
            administrator.Client, ISLAMU.Wire.Contracts.Learning.LearningTourFamily.Shell);
        using var claimed = await administrator.Client.PostAsJsonAsync("/api/learning/tours/claim",
            LearningOperationTests.TourClaim(plan, replay: true));
        await Assert.That(claimed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var before = await LearningOperationTests.Context(administrator.Client);
        await Assert.That(before.Claim).IsNotNull();
        var input = Acknowledge(before);

        using var acknowledged = await administrator.Client.PostAsJsonAsync(Route, input);

        await Assert.That(acknowledged.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var after = await LearningOperationTests.Context(administrator.Client);
        await Assert.That(after.Claim!.Token).IsEqualTo(before.Claim!.Token);
        await Assert.That(after.Claim.ExpiresAtUtc).IsEqualTo(before.Claim.ExpiresAtUtc);
        await Assert.That(after.Claim.PlanFingerprint).IsEqualTo(before.Claim.PlanFingerprint);
        await Assert.That(after.Claim.PolicySourceStamp).IsEqualTo(before.Claim.PolicySourceStamp);
        await Assert.That(after.Tours.Select(row => (row.Family, row.MaterialVersion, row.Outcome)))
            .IsEquivalentTo(before.Tours.Select(row => (row.Family, row.MaterialVersion, row.Outcome)));
        using var changed = await administrator.Client.PostAsJsonAsync(Route,
            input with { ExpectedRevision = after.LearnerRevision });
        await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var stale = await administrator.Client.PostAsJsonAsync(Route,
            input with { IntentId = Guid.CreateVersion7() });
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var retained = await LearningOperationTests.Context(administrator.Client);
        await Assert.That(retained.LearnerRevision).IsEqualTo(after.LearnerRevision);
        await Assert.That(retained.Requirement.AcknowledgedAtUtc).IsEqualTo(after.Requirement.AcknowledgedAtUtc);
    }

    [Test]
    public async Task BodyCannotSupplyAnotherUserOrActorContextForAcknowledgement()
    {
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync();
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await SetRequiredPolicyAsync(administrator.Client);
        var before = await LearningOperationTests.Context(administrator.Client);
        var input = Acknowledge(before);

        using var response = await administrator.Client.PostAsJsonAsync(Route, new
        {
            input.IntentId,
            input.ExpectedScopeStamp,
            input.ExpectedRevision,
            input.ExpectedPolicySourceStamp,
            input.MaterialVersion,
            userId = Guid.CreateVersion7(),
            contextKey = $"actor:{Guid.CreateVersion7():D}"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertAbsentAsync(fixture, administrator.UserId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SameOwnerEvidenceInjectedBeforeExpectedImageCaptureIsRejected(bool acknowledge)
    {
        var interception = new BeforeLearningStateRead();
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync(interception, null);
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await SetRequiredPolicyAsync(administrator.Client);
        var initial = await LearningOperationTests.Context(administrator.Client);
        using var preferences = await administrator.Client.PatchAsJsonAsync("/api/learning/preferences", Preferences(initial));
        await Assert.That(preferences.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var before = await LearningOperationTests.Context(administrator.Client);
        await using (var inspection = fixture.OpenInspection())
        {
            interception.Table = inspection.Model.FindEntityType(typeof(UserLearningState))!.GetTableName()!;
            var substituted = (await new LearningRepository(inspection)
                .GetStateAsync(fixture.TenantId, administrator.UserId, false, default))!;
            substituted.AcknowledgeRequirement(Guid.CreateVersion7(), LearningRequirementDefinition.Current,
                before.Requirement.PolicySourceStamp, Guid.CreateVersion7(), substituted.Revision,
                DateTime.SpecifyKind(substituted.UpdatedAtUtc, DateTimeKind.Utc));
            interception.Substituted = substituted;
        }
        interception.Armed = true;

        using var response = acknowledge
            ? await administrator.Client.PostAsJsonAsync(Route, Acknowledge(before))
            : await administrator.Client.PatchAsJsonAsync("/api/learning/preferences", Preferences(before));

        await Assert.That(interception.Observed).IsTrue();
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await using var observer = fixture.OpenInspection();
        var retained = (await new LearningRepository(observer)
            .GetStateAsync(fixture.TenantId, administrator.UserId, false, default))!;
        await Assert.That(retained.Revision).IsEqualTo(before.LearnerRevision);
        await Assert.That(retained.RequirementAcknowledgements).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExtraAcknowledgementDuringSavingRollsBackBothAcknowledgementAndOtherIntents(bool acknowledge)
    {
        bool evidenceWasWritten = false;
        Guid injectedId = Guid.Empty;
        var interception = new DuringLearningSave(async (database, state, token) =>
        {
            evidenceWasWritten = await database.LearningRequirementAcknowledgements
                .AnyAsync(row => row.Id == injectedId && row.UserLearningStateId == state.Id, token);
        }, (database, state) =>
        {
            var current = LearningRequirementDefinition.Current;
            injectedId = Guid.CreateVersion7();
            state.AcknowledgeRequirement(injectedId,
                new LearningRequirementDefinition(current.RequirementKey, current.MaterialVersion + (acknowledge ? 1 : 0)),
                new string('E', 64), Guid.CreateVersion7(), state.Revision, state.UpdatedAtUtc);
            database.ChangeTracker.DetectChanges();
        });
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync(interception, null);
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await SetRequiredPolicyAsync(administrator.Client);
        var before = await LearningOperationTests.Context(administrator.Client);
        interception.Armed = true;

        using var response = acknowledge
            ? await administrator.Client.PostAsJsonAsync(Route, Acknowledge(before))
            : await administrator.Client.PatchAsJsonAsync("/api/learning/preferences", Preferences(before));

        await Assert.That(interception.Observed).IsTrue();
        await Assert.That(evidenceWasWritten).IsTrue();
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await AssertAbsentAsync(fixture, administrator.UserId);
    }

    [Test]
    public async Task PersistedAcknowledgementDriftCannotHideBehindAnUnchangedTracker()
    {
        int changed = 0;
        var interception = new DuringLearningSave(async (database, state, token) =>
        {
            changed = await database.LearningRequirementAcknowledgements
                .Where(row => row.UserLearningStateId == state.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.PolicySourceStamp, new string('F', 64)), token);
        });
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync(interception, null);
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await SetRequiredPolicyAsync(administrator.Client);
        var before = await LearningOperationTests.Context(administrator.Client);
        interception.Armed = true;

        using var response = await administrator.Client.PostAsJsonAsync(Route, Acknowledge(before));

        await Assert.That(interception.Observed).IsTrue();
        await Assert.That(changed).IsEqualTo(1);
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await AssertAbsentAsync(fixture, administrator.UserId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PolicyOrAdministratorRevocationAtFinalAdmissionRollsBackTheAcknowledgement(bool revokeAdministrator)
    {
        int changed = 0;
        var interception = new DuringLearningSave(async (database, state, token) =>
        {
            changed = revokeAdministrator
                ? await database.TenantUserRoleGrants.Where(row => row.TenantId == state.TenantId
                    && row.TenantUser.UserId == state.UserId).ExecuteDeleteAsync(token)
                : await database.TenantSettingsDocuments.Where(row => row.TenantId == state.TenantId
                    && row.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()), token);
        });
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync(interception, null);
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await SetRequiredPolicyAsync(administrator.Client);
        var before = await LearningOperationTests.Context(administrator.Client);
        interception.Armed = true;

        using var response = await administrator.Client.PostAsJsonAsync(Route, Acknowledge(before));

        await Assert.That(interception.Observed).IsTrue();
        await Assert.That(changed).IsEqualTo(1);
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await AssertAbsentAsync(fixture, administrator.UserId);
        var retained = await LearningOperationTests.Context(administrator.Client);
        await Assert.That(retained.Requirement.Status).IsEqualTo(LearningRequirementStatus.Pending);
        await Assert.That(retained.Requirement.PolicySourceStamp).IsEqualTo(before.Requirement.PolicySourceStamp);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RetainedErasureAndAcknowledgementRespectBothOriginalOwnerOrders(bool eraseFirst)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beforeRead = new BeforePolicyRead(reached, release);
        var afterWrite = new DuringLearningSave(async (_, _, token) =>
        {
            reached.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        });
        await using var fixture = new LearningHttpFixture();
        await fixture.InitializeAsync(eraseFirst ? beforeRead : afterWrite, null);
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await SetRequiredPolicyAsync(administrator.Client);
        var context = await LearningOperationTests.Context(administrator.Client);
        var input = Acknowledge(context);
        await using (var metadata = fixture.OpenInspection())
            beforeRead.Table = metadata.Model.FindEntityType(typeof(TenantSettingsDocument))!.GetTableName()!;
        beforeRead.Armed = eraseFirst;
        afterWrite.Armed = !eraseFirst;
        var pending = administrator.Client.PostAsJsonAsync(Route, input);
        Task<Explore.Domain.PrivacyErasureIntent>? erasure = null;
        TaskCompletionSource? erasureStarted = eraseFirst
            ? null
            : new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await Task.WhenAny(reached.Task, pending).WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(reached.Task.IsCompletedSuccessfully).IsTrue();
            erasure = eraseFirst
                ? fixture.CommitRetainedErasureAsync(administrator.UserId)
                : Task.Run(async () =>
                {
                    erasureStarted!.TrySetResult();
                    return await fixture.CommitRetainedErasureAsync(administrator.UserId);
                });
            if (eraseFirst)
                await erasure.WaitAsync(TimeSpan.FromSeconds(30));
            else
                await erasureStarted!.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { release.TrySetResult(); }

        using var response = await pending;
        await Assert.That(response.IsSuccessStatusCode).IsEqualTo(!eraseFirst);
        var fact = await erasure!.WaitAsync(TimeSpan.FromSeconds(30));
        using var hidden = await administrator.Client.GetAsync("/api/learning/context");
        await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await using (var purge = fixture.OpenInspection())
        {
            await using var transaction = await purge.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var store = new LearningPrivacyErasureStore(purge);
            await store.PurgeAsync(fact, default);
            await store.RequireAbsentAsync(fact, default);
            await transaction.CommitAsync();
        }
        using var replay = await administrator.Client.PostAsJsonAsync(Route, input);
        await Assert.That(replay.IsSuccessStatusCode).IsFalse();
        await AssertAbsentAsync(fixture, administrator.UserId);
    }

    private static LearningAcknowledgeRequirementRequest Acknowledge(LearningContextDto context) => new()
    {
        IntentId = Guid.CreateVersion7(),
        ExpectedScopeStamp = context.ScopeStamp,
        ExpectedRevision = context.LearnerRevision,
        ExpectedPolicySourceStamp = context.Requirement.PolicySourceStamp,
        MaterialVersion = context.Requirement.MaterialVersion
    };

    private static LearningUpdatePreferencesRequest Preferences(LearningContextDto context) => new()
    {
        ExpectedScopeStamp = context.ScopeStamp,
        ExpectedRevision = context.LearnerRevision,
        SuppressAutomaticTours = true,
        HideLearning = true,
        PinLearning = false
    };

    private static async Task SetRequiredPolicyAsync(HttpClient client, bool automaticInvitations = true)
    {
        using var current = await client.GetAsync(PolicyRoute);
        await Assert.That(current.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
        var stamp = body.RootElement.GetProperty("concurrencyStamp");
        using var response = await client.PatchAsJsonAsync(PolicyRoute, new
        {
            expectedConcurrencyStamp = stamp.ValueKind == JsonValueKind.Null ? (Guid?)null : stamp.GetGuid(),
            policy = new
            {
                enabled = true,
                automaticInvitations,
                invitationAudience = "ActiveMembers",
                manualAccessAudience = "ActiveMembers",
                requirementPolicy = "TenantAdminOrientationAcknowledgement",
                familyOverrides = Array.Empty<object>()
            }
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task AssertLinkAsync(HttpClient client, bool expected)
    {
        using var response = await client.GetAsync("/api/learning/context");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("_links").TryGetProperty("acknowledge-requirement", out _))
            .IsEqualTo(expected);
    }

    private static async Task AssertAbsentAsync(LearningHttpFixture fixture, Guid userId)
    {
        await using var observer = fixture.OpenInspection();
        await Assert.That(await observer.UserLearningStates.AnyAsync(row => row.UserId == userId)).IsFalse();
        await Assert.That(await observer.LearningRequirementAcknowledgements
            .AnyAsync(row => row.TenantId == fixture.TenantId)).IsFalse();
    }

    private sealed class AcknowledgementClock : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class BeforeLearningStateRead : DbCommandInterceptor
    {
        internal string Table { get; set; } = string.Empty;
        internal UserLearningState Substituted { get; set; } = null!;
        internal bool Armed { get; set; }
        internal bool Observed { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is ExploreDbContext { HasHumanCommitContextLease: true } owner
                && owner.Database.CurrentTransaction is not null
                && command.CommandText.Contains(Table, StringComparison.Ordinal))
            {
                Armed = false;
                Observed = true;
                owner.Attach(Substituted);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DuringLearningSave(
        Func<ExploreDbContext, UserLearningState, CancellationToken, Task> afterWrite,
        Action<ExploreDbContext, UserLearningState>? beforeSave = null) : SaveChangesInterceptor
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
                    beforeSave?.Invoke((ExploreDbContext)eventData.Context, _state);
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
                var owner = (ExploreDbContext)eventData.Context!;
                if (owner.Database.CurrentTransaction is null)
                    throw new InvalidOperationException("Acknowledgement must retain its original physical owner.");
                await afterWrite(owner, state, cancellationToken);
            }
            return result;
        }
    }

    private sealed class BeforePolicyRead(TaskCompletionSource reached, TaskCompletionSource release) : DbCommandInterceptor
    {
        internal string Table { get; set; } = string.Empty;
        internal bool Armed { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is ExploreDbContext { HasHumanCommitContextLease: true } owner
                && owner.Database.CurrentTransaction is null
                && command.CommandText.Contains(Table, StringComparison.Ordinal))
            {
                Armed = false;
                reached.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }
}
