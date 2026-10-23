using System.Data;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class EventInitialCreationMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Initial_Published_without_adoption_survives_only_its_own_qualification_epoch_increment(bool absentHost)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var creation = (await PrepareAsync(database, context, "organization", absentHost: absentHost))
            with { RequestedStatus = EventStatusEnum.Published };
        var session = Session(database, context);

        var result = await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token),
            (creator, token) => InsertAsync(context, creation, creator, token), default);

        await Assert.That(result.IsSuccess).IsTrue();
        await using var inspect = database.Context();
        await AssertCommittedAsync(inspect, creation, absentHost);
        await Assert.That(await inspect.Events.Where(row => row.Id == creation.EventId)
            .Select(row => row.EventStatusId).SingleAsync()).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Actual_creator_Human_Publish_restriction_blocks_initial_Published_before_callback(
        bool protective, bool instanceScope)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var clock = new InitialPublicationClock();
        var creation = (await PrepareAsync(database, context, "organization"))
            with { RequestedStatus = EventStatusEnum.Published, CreatedAtUtc = clock.Now.UtcDateTime };
        var publication = await AdoptInitialPublicationAsync(database);
        await StoreInitialPublicationRestrictionAsync(database, publication, clock.Now, protective,
            scope: instanceScope ? database.Instance : database.Scope);
        var entered = false;
        var session = Session(database, context, clock: clock);

        var result = await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token), (creator, token) =>
            {
                entered = true;
                return InsertAsync(context, creation, creator, token);
            }, default);

        await Assert.That(entered).IsFalse();
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("event_publish_restricted");
        await AssertInitialPublicationStateAsync(database, creation, 2, committed: false);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Initial_Draft_bypasses_Human_Publish_reader_even_when_restricted_or_history_is_unavailable(bool malformed)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var clock = new InitialPublicationClock();
        var creation = (await PrepareAsync(database, context, "group")) with { CreatedAtUtc = clock.Now.UtcDateTime };
        var publication = await AdoptInitialPublicationAsync(database);
        if (malformed) await RemoveInitialPublicationHistoryAsync(database, publication.Id, "effect");
        else await StoreInitialPublicationRestrictionAsync(database, publication, clock.Now);
        var session = Session(database, context, clock: clock);

        var result = await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token),
            (creator, token) => InsertAsync(context, creation, creator, token), default);

        await Assert.That(result.IsSuccess).IsTrue();
        await AssertInitialPublicationStateAsync(database, creation, 3, committed: true);
    }

    [Test]
    public async Task Restricting_another_Human_does_not_replace_the_actual_initial_creator_identity()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var clock = new InitialPublicationClock();
        var creation = (await PrepareAsync(database, context, "organization"))
            with { RequestedStatus = EventStatusEnum.Published, CreatedAtUtc = clock.Now.UtcDateTime };
        var publication = await AdoptInitialPublicationAsync(database);
        await StoreInitialPublicationRestrictionAsync(database, publication, clock.Now,
            subjectId: database.AdministratorId);
        var session = Session(database, context, clock: clock);

        var result = await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token),
            (creator, token) => InsertAsync(context, creation, creator, token), default);

        await Assert.That(result.IsSuccess).IsTrue();
        await AssertInitialPublicationStateAsync(database, creation, 3, committed: true);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Retiring_profile_support_and_selection_cannot_erase_initial_creator_retained_restriction(bool protective)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var clock = new InitialPublicationClock();
        var creation = (await PrepareAsync(database, context, "organization"))
            with { RequestedStatus = EventStatusEnum.Published, CreatedAtUtc = clock.Now.UtcDateTime };
        var publication = await AdoptInitialPublicationAsync(database);
        await StoreInitialPublicationRestrictionAsync(database, publication, clock.Now, protective);
        var declared = ModerationPolicyPublicationProjection.RestoreHistory(publication)!.Proposed;
        var retired = await database.PublishAsync(database.AdministratorId, database.Publication(2) with
        {
            ExpectedPublicationId = publication.Id,
            Proposed = declared with
            {
                PublicationId = Guid.CreateVersion7(), ExpectedRevision = publication.Revision, SupportedProfiles = []
            }
        });
        await Assert.That(retired.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Instance.TrustDomainId;
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await writer.ModerationPolicySelections.Where(row => row.Id == publication.PolicyId).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
        var session = Session(database, context, clock: clock);
        var entered = false;

        var result = await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token), (creator, token) =>
            {
                entered = true;
                return InsertAsync(context, creation, creator, token);
            }, default);

        await Assert.That(entered).IsFalse();
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("event_publish_restricted");
        await AssertInitialPublicationStateAsync(database, creation, 3, committed: false);
    }

    [Test]
    [Arguments("effect")]
    [Arguments("base-plan")]
    public async Task Malformed_selected_history_blocks_initial_Published_before_callback(string missing)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var creation = (await PrepareAsync(database, context, "organization"))
            with { RequestedStatus = EventStatusEnum.Published };
        var publication = await AdoptInitialPublicationAsync(database);
        await RemoveInitialPublicationHistoryAsync(database, publication.Id, missing);
        var entered = false;
        var session = Session(database, context);

        var result = await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token), (creator, token) =>
            {
                entered = true;
                return InsertAsync(context, creation, creator, token);
            }, default);

        await Assert.That(entered).IsFalse();
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("event_publish_authority_unavailable");
        await AssertInitialPublicationStateAsync(database, creation, 2, committed: false);
    }

    [Test]
    public async Task Callback_cannot_smuggle_Published_through_a_Draft_initial_creation_request()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var clock = new InitialPublicationClock();
        var creation = (await PrepareAsync(database, context, "organization")) with { CreatedAtUtc = clock.Now.UtcDateTime };
        var publication = await AdoptInitialPublicationAsync(database);
        await StoreInitialPublicationRestrictionAsync(database, publication, clock.Now);
        var session = Session(database, context, clock: clock);
        var flushedPublished = false;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () => await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token), async (creator, token) =>
            {
                var result = await InsertAsync(context, creation with { RequestedStatus = EventStatusEnum.Published }, creator, token);
                flushedPublished = await context.Events.AsNoTracking().AnyAsync(row => row.Id == creation.EventId
                    && row.EventStatusId == (int)EventStatusEnum.Published, token);
                return result;
            }, default));

        await Assert.That(flushedPublished).IsTrue();
        await AssertInitialPublicationStateAsync(database, creation, 2, committed: false);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Restriction_starting_on_initial_epoch_save_rolls_back_parent_participation_owner_and_epoch(bool colocated)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var setup = database.Context();
        var clock = new InitialPublicationClock();
        var creation = (await PrepareAsync(database, setup, "organization"))
            with { RequestedStatus = EventStatusEnum.Published, CreatedAtUtc = clock.Now.UtcDateTime };
        var publication = await AdoptInitialPublicationAsync(database);
        var startsAt = clock.Now.AddMinutes(1);
        await StoreInitialPublicationRestrictionAsync(database, publication, startsAt);
        var crossing = new InitialPublicationStartsOnEpochSave(creation.EventId, clock, startsAt);
        await using var context = database.Context(crossing);
        await using var retained = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        var session = Session(database, context, retained?.Authority(context, clock), clock);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () => await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token),
            (creator, token) => InsertAsync(context, creation, creator, token), default));

        await Assert.That(crossing.ObservedPublishedGraph).IsTrue();
        await Assert.That(crossing.PrivacyOwned).IsEqualTo(colocated);
        await AssertInitialPublicationStateAsync(database, creation, 2, committed: false);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.IdentityFenceOwnsTransaction).IsFalse();
    }

    private static async Task<ModerationPolicyPublication> AdoptInitialPublicationAsync(SourceDatabase database)
    {
        var result = await database.PublishAsync(database.AdministratorId,
            database.Publication(1, ModerationReferenceProfileId.CFG06));
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        return result.Publication!;
    }

    private static async Task StoreInitialPublicationRestrictionAsync(SourceDatabase database,
        ModerationPolicyPublication publication, DateTimeOffset startsAt, bool protective = false,
        Guid? subjectId = null, AuthorityScope? scope = null)
    {
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, subjectId ?? database.SubjectId, database.Instance);
        var issuer = SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance);
        scope ??= database.Scope;
        var recordedAt = startsAt.AddMinutes(-1);
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (protective)
        {
            var measure = ModerationProtectiveMeasure.Create(Guid.CreateVersion7(), subject, issuer, scope, scope,
                [ModerationCapability.Publish], "Bounded initial publication protection.", startsAt,
                startsAt.AddHours(1), startsAt.AddMinutes(30));
            measure.ValidatePersistedState();
            context.ModerationProtectiveMeasures.Add(measure);
        }
        else
        {
            var controller = SubjectReference.Create(ModerationSubjectKind.Human, database.ReviewerId, database.Instance);
            var profile = publication.Profiles.Single();
            var rule = ModerationRuleReference.Create(publication.Jurisdiction.Id, profile.RuleCode, profile.RuleRevision);
            var reason = ModerationRuleReference.Create(publication.Jurisdiction.Id, profile.ReasonCode, profile.ReasonRevision);
            var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), recordedAt);
            var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1, scope, null, null,
                rule, rule, reason, reason, controller,
                ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), allegation.Id, 1, recordedAt),
                "Retained canonical initial publication finding.", recordedAt);
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer,
                scope, scope, rule, [ModerationCapability.Publish], startsAt, startsAt.AddHours(1), null, 1, recordedAt);
            decision.RecordResponse(decision.Revision, recordedAt);
            decision.AcceptFinding(finding.Id, decision.Revision, recordedAt);
            decision.Approve(controller, scope, decision.Revision, recordedAt);
            var sanction = decision.Activate(Guid.CreateVersion7(), decision.Revision, recordedAt);
            decision.ValidatePersistedState();
            sanction.ValidatePersistedState();
            context.AddRange(allegation, finding, decision, sanction);
        }
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static async Task RemoveInitialPublicationHistoryAsync(SourceDatabase database, Guid publicationId, string missing)
    {
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (missing == "effect")
            await context.Set<ModerationPolicyPublicationEffect>().Where(row => row.PublicationId == publicationId).ExecuteDeleteAsync();
        else
            await context.Set<ModerationPolicyPublicationPlan>().Where(row => row.PublicationId == publicationId).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }

    private static async Task AssertInitialPublicationStateAsync(SourceDatabase database, EventInitialCreation creation,
        long epoch, bool committed)
    {
        await using var context = database.Context();
        var parent = await context.Events.AsNoTracking().SingleOrDefaultAsync(row => row.Id == creation.EventId);
        await Assert.That(parent is not null).IsEqualTo(committed);
        await Assert.That(await context.Set<EventParticipationConfiguration>().AnyAsync(row => row.Id == creation.EventId))
            .IsEqualTo(committed);
        var owners = await context.EventRoleAssignments.AsNoTracking().Where(row => row.EventId == creation.EventId).ToArrayAsync();
        await Assert.That(owners.Length).IsEqualTo(committed ? 1 : 0);
        if (committed)
        {
            await Assert.That(parent!.EventStatusId).IsEqualTo((int)creation.RequestedStatus);
            await Assert.That(parent.CreatedBy).IsEqualTo((Guid?)creation.CreatorUserId);
            await Assert.That(parent.ActorId).IsEqualTo(creation.PublisherActorId);
            await Assert.That(owners.Single().UserId).IsEqualTo(creation.CreatorUserId);
            await Assert.That(owners.Single().RoleId).IsEqualTo((int)RoleEnum.EventOwner);
        }
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(epoch);
        if (!committed)
        {
            await Assert.That(await context.OutboxMessages.AnyAsync(row => row.AggregateId == creation.EventId)).IsFalse();
            await Assert.That(await context.PdsSyncOutbox.AnyAsync(row => row.SourceEntityId == creation.EventId)).IsFalse();
        }
    }

    private sealed class InitialPublicationClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class InitialPublicationStartsOnEpochSave(Guid eventId, InitialPublicationClock clock,
        DateTimeOffset startsAt) : SaveChangesInterceptor
    {
        private bool _pending;
        public bool ObservedPublishedGraph { get; private set; }
        public bool PrivacyOwned { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!ObservedPublishedGraph && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationQualificationEpoch>().Any(entry => entry.State == EntityState.Modified))
            {
                _pending = true;
                PrivacyOwned = context.IdentityFenceOwnsTransaction;
            }
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (_pending && eventData.Context is ExploreDbContext context)
            {
                _pending = false;
                ObservedPublishedGraph = await context.Events.AsNoTracking().AnyAsync(row => row.Id == eventId
                    && row.EventStatusId == (int)EventStatusEnum.Published, cancellationToken)
                    && await context.Set<EventParticipationConfiguration>().AnyAsync(row => row.Id == eventId, cancellationToken)
                    && await context.EventRoleAssignments.AnyAsync(row => row.EventId == eventId, cancellationToken);
                clock.Now = startsAt;
            }
            return result;
        }
    }
}
