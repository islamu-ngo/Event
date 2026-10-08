using Explore.Persistence.Configurations.Entities;
using Explore.Persistence.Configurations.Moderation;
using ISLAMU.Moderation.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationLedgerConstraintTests
{
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
