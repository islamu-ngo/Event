using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using Explore.Persistence;
using ISLAMU.Moderation.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationQualificationPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Native_scalar_and_child_hydration_preserves_checked_qualification_sources()
    {
        await using var database = await QualificationDatabase.Create();
        database.Context.ChangeTracker.Clear();
        var finding = await database.Context.Set<ModerationFinding>().AsNoTracking().SingleAsync();
        finding.ValidatePersistedState();
        await Assert.That(finding.CaseId).IsEqualTo(database.Case.Id);
        await Assert.That(finding.Subject).IsEqualTo(database.Case.Subject);
        await Assert.That(finding.Rule).IsEqualTo(database.Profile.Rule);
        await Assert.That(finding.Reason).IsEqualTo(database.Profile.Reason);
        await Assert.That(finding.Reason == finding.Rule).IsFalse();
        var reviewer = await database.Context.Set<ModerationReviewerAssignment>().AsNoTracking().SingleAsync();
        reviewer.ValidatePersistedState();
        await Assert.That(reviewer.CaseTenantId).IsEqualTo(database.Scope.Id);
        await Assert.That(reviewer.IsQualifiedAt(Now)).IsFalse();
        var policy = await database.Context.ModerationPolicyPublications.AsNoTracking()
            .Include(entity => entity.Profiles).ThenInclude(profile => profile.Scopes)
            .Include(entity => entity.Profiles).ThenInclude(profile => profile.Subjects)
            .Include(entity => entity.Profiles).ThenInclude(profile => profile.Effects)
            .Include(entity => entity.Profiles).ThenInclude(profile => profile.Plans)
            .Include(entity => entity.Profiles).ThenInclude(profile => profile.Consumers)
            .Include(entity => entity.Profiles).ThenInclude(profile => profile.Prerequisites)
            .Include(entity => entity.Classes).Include(entity => entity.Reasons).Include(entity => entity.Modes)
            .SingleAsync();
        var restored = ModerationPolicyPublicationProjection.RestoreHistory(policy);
        await Assert.That(restored).IsNotNull();
        await Assert.That(policy.Jurisdiction).IsEqualTo(database.Scope);
        await Assert.That(restored!.Profiles.Single().Definition.Rule).IsEqualTo(database.Profile.Rule);
        await Assert.That(restored.Profiles.Single().Definition.Reason).IsEqualTo(database.Profile.Reason);
        await Assert.That(policy.PublishedActive).IsFalse();
        await Assert.That(await database.Context.ModerationPolicies.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Same_jurisdiction_cannot_acquire_a_second_policy_head()
    {
        await using var database = await QualificationDatabase.Create();
        var second = database.Owner.Publication(2);
        var result = await database.Owner.PublishAsync(database.Owner.AdministratorId, second);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Conflict);
        await Assert.That(await database.Context.ModerationPolicySelections.CountAsync()).IsEqualTo(1);
        await Assert.That(await database.Context.ModerationPolicyPublications.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Profile_scope_cannot_point_at_a_missing_publication()
    {
        await using var database = await QualificationDatabase.Create();
        database.Context.Add(new ModerationPolicyPublicationScope
        {
            PublicationId = Guid.CreateVersion7(),
            ProfileId = database.Profile.Id,
            Kind = ModerationScopeKind.Tenant
        });
        await Assert.That(() => database.Context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    public async Task Direct_conflict_clearance_cannot_replace_unresolved_controller_evidence()
    {
        await using var database = await QualificationDatabase.Create();
        await Assert.That(() => database.Context.Database.ExecuteSqlRawAsync(
            "UPDATE moderation_reviewer_assignments SET conflict_state = 2 WHERE id = {0}",
            database.Assignment.Id)).Throws<SqliteException>();
        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Set<ModerationReviewerAssignment>().SingleAsync();
        stored.ValidatePersistedState();
        await Assert.That(stored.IsQualifiedAt(Now)).IsFalse();
    }

    [Test]
    public async Task Stale_controller_qualification_cannot_replace_a_committed_source_revision()
    {
        await using var database = await QualificationDatabase.Create();
        await using var staleContext = database.Owner.Context();
        staleContext.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var stale = await staleContext.Set<ModerationReviewerAssignment>().SingleAsync();
        var current = database.Assignment;
        current.QualifyController(1, current.ReviewerHumanId, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Now.AddHours(1), Now);
        await database.Context.SaveChangesAsync();
        stale.QualifyController(1, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Now.AddHours(1), Now);
        await Assert.That(() => staleContext.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Set<ModerationReviewerAssignment>().AsNoTracking().SingleAsync();
        await Assert.That(stored.ControllerHumanId).IsEqualTo(current.ReviewerHumanId);
        await Assert.That(stored.Revision).IsEqualTo(2L);
    }

    private sealed class QualificationDatabase : IAsyncDisposable
    {
        private QualificationDatabase(
            ModerationQualificationWriterTests.SourceDatabase owner, ModerationPolicyProfileDefinition profile)
        {
            Owner = owner;
            Context = owner.Context();
            Scope = owner.Scope;
            Context.ModerationTrustDomainId = Scope.TrustDomainId;
            Case = ModerationCase.Open(Guid.CreateVersion7(),
                SubjectReference.Create(ModerationSubjectKind.Human, owner.SubjectId, owner.Instance),
                Scope, Guid.CreateVersion7(), Now);
            Profile = profile;
            Assignment = ModerationReviewerAssignment.Assign(Guid.CreateVersion7(), Case, 1,
                owner.ReviewerId, owner.AdministratorId, Now);
            Finding = ModerationFinding.Finalize(Guid.CreateVersion7(), Case, 1,
                Case.Scope, null, null, profile.Rule, profile.Rule,
                profile.Reason, profile.Reason,
                SubjectReference.Create(ModerationSubjectKind.Human, owner.ReviewerId, owner.Instance),
                ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), Case.Id, 1, Now),
                "Qualified finding source.", Now);
        }

        public ModerationQualificationWriterTests.SourceDatabase Owner { get; }
        public ExploreDbContext Context { get; }
        public AuthorityScope Scope { get; }
        public ModerationCase Case { get; }
        public ModerationPolicyProfileDefinition Profile { get; }
        public ModerationReviewerAssignment Assignment { get; }
        public ModerationFinding Finding { get; }

        public static async Task<QualificationDatabase> Create()
        {
            var owner = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
            var request = owner.Publication(1);
            var published = await owner.PublishAsync(owner.AdministratorId, request);
            if (published.Outcome != ModerationPolicyPublicationOutcome.Known)
                throw new InvalidOperationException("The qualification hydration fixture requires a real rich publication.");
            var database = new QualificationDatabase(owner, request.Proposed.Profiles.Single());
            database.Context.AddRange(database.Case, database.Assignment, database.Finding);
            await database.Context.SaveChangesAsync();
            return database;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Owner.DisposeAsync();
        }
    }
}
