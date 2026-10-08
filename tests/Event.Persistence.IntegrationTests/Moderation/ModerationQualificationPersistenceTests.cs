using Explore.Persistence.Configurations.Moderation;
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
        var reviewer = await database.Context.Set<ModerationReviewerAssignment>().AsNoTracking().SingleAsync();
        reviewer.ValidatePersistedState();
        await Assert.That(reviewer.CaseTenantId).IsEqualTo(database.Scope.Id);
        await Assert.That(reviewer.IsQualifiedAt(Now)).IsFalse();
        var policy = await database.Context.Set<ModerationPolicy>().AsNoTracking()
            .Include(entity => entity.Rules).SingleAsync();
        policy.ValidatePersistedState();
        await Assert.That(policy.Jurisdiction).IsEqualTo(database.Scope);
        await Assert.That(policy.JurisdictionId).IsEqualTo(database.Scope.Id);
        await Assert.That(policy.Rules.Single().Rule).IsEqualTo(database.Policy.Rules.Single().Rule);
        await Assert.That(policy.IsActive).IsFalse();
    }

    [Test]
    public async Task Same_jurisdiction_cannot_acquire_a_second_policy_head()
    {
        await using var database = await QualificationDatabase.Create();
        database.Context.Add(Policy(database.Scope));
        await Assert.That(() => database.Context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    public async Task Rule_cannot_point_at_a_missing_policy()
    {
        await using var database = await QualificationDatabase.Create();
        var missing = Policy(database.Scope);
        database.Context.Add(missing.Rules.Single());
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
        await using var staleContext = new QualificationContext(database.Options);
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

    private static ModerationPolicy Policy(AuthorityScope scope)
    {
        var rule = ModerationPolicyRule.Define(Guid.CreateVersion7(),
            ModerationRuleReference.Create(scope.Id, "source-ownership", 1), "Checked source ownership.",
            [ModerationPolicyOperationPurpose.Propose, ModerationPolicyOperationPurpose.Activate],
            [ModerationCapability.Publish], TimeSpan.FromDays(1), false, TimeSpan.FromHours(1), 1, 2);
        return ModerationPolicy.Adopt(Guid.CreateVersion7(), scope, [rule], Guid.CreateVersion7(), Now);
    }

    private sealed class QualificationContext(DbContextOptions<QualificationContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ApplyConfiguration<ModerationCase>(new ModerationLedgerConfiguration());
            var sources = new ModerationQualificationConfiguration();
            builder.ApplyConfiguration<ModerationFinding>(sources);
            builder.ApplyConfiguration<ModerationReviewerAssignment>(sources);
            builder.ApplyConfiguration<ModerationPolicy>(sources);
            builder.ApplyConfiguration<ModerationPolicyRule>(sources);
        }
    }

    private sealed class QualificationDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private QualificationDatabase(SqliteConnection connection)
        {
            _connection = connection;
            Options = new DbContextOptionsBuilder<QualificationContext>()
                .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
            Context = new QualificationContext(Options);
            Scope = AuthorityScope.Tenant(AuthorityScope.Instance(
                AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
                Guid.CreateVersion7()), Guid.CreateVersion7());
            Case = ModerationCase.Open(Guid.CreateVersion7(),
                SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), Scope),
                Scope, Guid.CreateVersion7(), Now);
            Policy = ModerationQualificationPersistenceTests.Policy(Scope);
            Assignment = ModerationReviewerAssignment.Assign(Guid.CreateVersion7(), Case, 1,
                Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
            var rule = Policy.Rules.Single().Rule;
            Finding = ModerationFinding.Finalize(Guid.CreateVersion7(), Case, 1, rule, rule,
                SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), Scope),
                ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), Case.Id, 1, Now),
                "Qualified finding source.", Now);
        }

        public DbContextOptions<QualificationContext> Options { get; }
        public QualificationContext Context { get; }
        public AuthorityScope Scope { get; }
        public ModerationCase Case { get; }
        public ModerationPolicy Policy { get; }
        public ModerationReviewerAssignment Assignment { get; }
        public ModerationFinding Finding { get; }

        public static async Task<QualificationDatabase> Create()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString());
            await connection.OpenAsync();
            var database = new QualificationDatabase(connection);
            await database.Context.Database.EnsureCreatedAsync();
            database.Context.AddRange(database.Case, database.Policy, database.Assignment, database.Finding);
            await database.Context.SaveChangesAsync();
            return database;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
