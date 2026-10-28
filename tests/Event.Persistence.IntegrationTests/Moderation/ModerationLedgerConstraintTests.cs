using Explore.Persistence.Configurations.Entities;
using Explore.Persistence.Configurations.Moderation;
using ISLAMU.Moderation.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationLedgerConstraintTests
{
    [Test]
    public async Task Target_owned_case_round_trips_retained_material_without_an_attributed_subject()
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString());
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LedgerContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
        await using var context = new LedgerContext(options);
        await context.Database.EnsureCreatedAsync();
        var allegation = TargetCase();
        var first = allegation.Evidence.Single().ToReference(allegation.Target!, allegation.OccurrenceId);
        var second = ModerationEvidenceReference.Create(first.Target, first.SourceOrigin, first.OccurrenceId,
            first.SourceId, first.SourceVersion, Guid.CreateVersion7());
        allegation.RecordEvidence(second, 1, allegation.CreatedAt.AddMinutes(1));
        context.Add(allegation);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var retained = await context.Set<ModerationCase>().Include(row => row.Evidence).SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.Subject).IsNull();
        await Assert.That(retained.Target).IsEqualTo(first.Target);
        await Assert.That(retained.Revision).IsEqualTo(2L);
        await Assert.That(retained.Evidence.Count).IsEqualTo(2);
        await Assert.That(retained.Evidence.Single(row => row.CaseRevision == 1)
            .ToReference(retained.Target!, retained.OccurrenceId)).IsEqualTo(first);
        await Assert.That(retained.Evidence.Single(row => row.CaseRevision == 2)
            .ToReference(retained.Target!, retained.OccurrenceId)).IsEqualTo(second);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Stored_target_case_cannot_acquire_partial_attribution_or_lose_its_target(bool attribution)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString());
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LedgerContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
        await using var context = new LedgerContext(options);
        await context.Database.EnsureCreatedAsync();
        var allegation = TargetCase();
        context.Add(allegation);
        await context.SaveChangesAsync();

        var update = attribution
            ? "UPDATE moderation_cases SET subject_kind = 1 WHERE id = {0}"
            : "UPDATE moderation_cases SET target_version = NULL WHERE id = {0}";
        var rejected = false;
        try
        {
            await context.Database.ExecuteSqlRawAsync(update, allegation.Id);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            rejected = true;
        }
        await Assert.That(rejected).IsTrue();
        context.ChangeTracker.Clear();
        var retained = await context.Set<ModerationCase>().Include(row => row.Evidence).SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.Subject).IsNull();
        await Assert.That(retained.Target).IsEqualTo(allegation.Target);
    }

    [Test]
    public async Task A_stale_case_writer_cannot_overwrite_or_lose_committed_material()
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ":memory:" }.ToString());
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LedgerContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
        await using var firstContext = new LedgerContext(options);
        await firstContext.Database.EnsureCreatedAsync();
        var allegation = TargetCase();
        firstContext.Add(allegation);
        await firstContext.SaveChangesAsync();
        firstContext.ChangeTracker.Clear();
        await using var secondContext = new LedgerContext(options);
        var first = await firstContext.Set<ModerationCase>().Include(row => row.Evidence).SingleAsync();
        var stale = await secondContext.Set<ModerationCase>().Include(row => row.Evidence).SingleAsync();
        var original = first.Evidence.Single().ToReference(first.Target!, first.OccurrenceId);
        var committed = ModerationEvidenceReference.Create(original.Target, original.SourceOrigin,
            original.OccurrenceId, original.SourceId, original.SourceVersion, Guid.CreateVersion7());
        var competing = ModerationEvidenceReference.Create(original.Target, original.SourceOrigin,
            original.OccurrenceId, original.SourceId, original.SourceVersion, Guid.CreateVersion7());
        first.RecordEvidence(committed, 1, first.CreatedAt.AddMinutes(1));
        stale.RecordEvidence(competing, 1, stale.CreatedAt.AddMinutes(1));
        await firstContext.SaveChangesAsync();
        var rejected = false;
        try
        {
            await secondContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            rejected = true;
        }
        await Assert.That(rejected).IsTrue();
        firstContext.ChangeTracker.Clear();
        var retained = await firstContext.Set<ModerationCase>().Include(row => row.Evidence).SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.Revision).IsEqualTo(2L);
        await Assert.That(retained.Evidence.Count).IsEqualTo(2);
        await Assert.That(retained.Evidence.Single(row => row.CaseRevision == 2)
            .ToReference(retained.Target!, retained.OccurrenceId)).IsEqualTo(committed);
    }

    private static ModerationCase TargetCase()
    {
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7());
        var evidence = ModerationEvidenceReference.Create(ModerationCaseTarget.Create(
            ModerationCaseTargetKind.LocalIncident, Guid.CreateVersion7(), scope, Guid.CreateVersion7()), scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        return ModerationCase.OpenAllegation(Guid.CreateVersion7(), scope, evidence,
            new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Invalid_stored_capability_or_partial_approval_cannot_commit(bool invalidCapability)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:"
        }.ToString());
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LedgerContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
        await using var context = new LedgerContext(options);
        await context.Database.EnsureCreatedAsync();
        var now = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7());
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var issuer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);
        var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), now);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer,
            scope, scope, ModerationRuleReference.Create(Guid.CreateVersion7(), "stored-invariant", 1),
            [ModerationCapability.Publish], now, now.AddDays(1), null, 1, now);
        context.AddRange(allegation, decision);
        await context.SaveChangesAsync();
        var rejected = false;
        try
        {
            var update = invalidCapability
                ? "UPDATE moderation_decisions SET capability_mask = 1 WHERE id = {0}"
                : "UPDATE moderation_decisions SET approval1_kind = 1 WHERE id = {0}";
            await context.Database.ExecuteSqlRawAsync(update, decision.Id);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            rejected = true;
        }
        await Assert.That(rejected).IsTrue();
        context.ChangeTracker.Clear();
        var stored = await context.Set<ModerationDecision>().AsNoTracking().SingleAsync();
        stored.ValidatePersistedState();
        await Assert.That(stored.Capabilities.Contains(ModerationCapability.Publish)).IsTrue();
        await Assert.That(stored.Approvals).IsEmpty();
    }

    private sealed class LedgerContext(DbContextOptions<LedgerContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            var ledger = new ModerationLedgerConfiguration();
            builder.ApplyConfiguration<ModerationCase>(ledger);
            builder.ApplyConfiguration<ModerationDecision>(ledger);
            builder.ApplyConfiguration<ModerationSanction>(ledger);
            builder.ApplyConfiguration<ModerationProtectiveMeasure>(ledger);
            var effects = new ModerationEffectIntentConfiguration();
            builder.ApplyConfiguration<Explore.Domain.ModerationEffectIntent>(effects);
            builder.ApplyConfiguration<Explore.Domain.ModerationOperationReceipt>(effects);
        }
    }
}
