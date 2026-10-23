using System.Data;
using System.Data.Common;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineExecutionTests
{
    [Test]
    [Arguments("live")]
    [Arguments("expired-retention")]
    [Arguments("retained-erasure")]
    public async Task Inert_dispatch_probe_never_reads_a_private_substantive_original(string custody)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (custody == "expired-retention") fixture.Clock.UtcNow = Now.AddYears(1);
        if (custody == "retained-erasure") await EraseOriginalParticipantAsync(fixture, "representative");
        var reads = new CaptureReads();
        await using var context = fixture.Context(reads);
        var identity = await fixture.AuthenticateAsync(context);
        var result = await new ModerationEffectLeaseRepository(context, identity.Machine, identity.Bindings,
            new ExternalApiKeyRepository(context), fixture.Clock)
            .ReadWithoutSubstantiveWorkAsync(fixture.EffectId);
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Unavailable);
        await Assert.That(result.UnavailableReason)
            .IsEqualTo(ModerationEffectLeaseUnavailableReason.InvalidOrSubstantiveOriginalCapture);
        await Assert.That(result.Effect).IsNull();
        await Assert.That(reads.Count).IsEqualTo(0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await AssertState(fixture, 0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Adopted_original_quarantine_commit_rechecks_after_actual_disclosure_flush(bool expireAtFlush)
    {
        await using var fixture = await Fixture.CreateAsync();
        var checkpoint = new OriginalCommitCheckpoint(fixture, expireAtFlush);
        await using var context = fixture.Context(new ObserveOriginalSave(checkpoint), checkpoint);
        var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
            .UseSqlite(context.Database.GetDbConnection()).UseSnakeCaseNamingConvention().Options;
        await using (var schema = new EmbeddedPrivacyErasureAuthorityDbContext(options))
            await schema.Database.ExecuteSqlRawAsync(schema.Database.GenerateCreateScript());
        var authorityOptions = new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>(options)
            .AddInterceptors(new ObserveOriginalCommit(checkpoint)).Options;
        var privacy = new EmbeddedPrivacyErasureAuthorityRepository(new OriginalAuthorityFactory(authorityOptions),
            fixture.Clock, Options.Create(new PrivacyErasureOptions()), applicationContext: context);
        var identity = await fixture.AuthenticateAsync(context);
        var owner = new ModerationResourceQuarantineRepository(context, identity.Machine, identity.Bindings,
            new ExternalApiKeyRepository(context), fixture.Clock, privacy);
        var result = await owner.ExecuteAsync(fixture.EffectId, fixture.LeaseId, fixture.LeaseRevision,
            0, fixture.ResourceId, fixture.MaterialId);
        await Assert.That(checkpoint.QuarantineSaved).IsTrue();
        await Assert.That(checkpoint.FlushCount).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await Assert.That(result.Outcome).IsEqualTo(expireAtFlush
            ? ModerationResourceQuarantineOutcome.Unavailable : ModerationResourceQuarantineOutcome.Applied);
        await Assert.That(checkpoint.Committed.Count).IsEqualTo(expireAtFlush ? 0 : 1);
        if (!expireAtFlush)
        {
            await Assert.That(ReferenceEquals(checkpoint.Committed.Single(), checkpoint.QuarantineTransaction)).IsTrue();
            var replay = await owner.ExecuteAsync(fixture.EffectId, fixture.LeaseId, fixture.LeaseRevision,
                0, fixture.ResourceId, fixture.MaterialId);
            await Assert.That(replay.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.AlreadyApplied);
            await Assert.That(replay.Receipt!.Id).IsEqualTo(result.Receipt!.Id);
            await Assert.That(replay.Receipt.AppliedAt).IsEqualTo(result.Receipt.AppliedAt);
            await Assert.That(checkpoint.FlushCount).IsEqualTo(1);
        }
        await AssertState(fixture, expireAtFlush ? 0 : 1);
        await using var reader = fixture.Context();
        await Assert.That((await reader.Set<EventDiscoveryRevision>().SingleAsync()).DisclosureEpoch)
            .IsEqualTo(checkpoint.DisclosureBefore + (expireAtFlush ? 0 : 1));
        if (expireAtFlush)
            await Assert.That((await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId))
                .HasPublishablePayload()).IsTrue();
    }

    [Test]
    [Arguments("representative", false)]
    [Arguments("reviewer", false)]
    [Arguments("approver", false)]
    [Arguments("representative", true)]
    [Arguments("reviewer", true)]
    [Arguments("approver", true)]
    public async Task Retained_participant_erasure_rejects_original_execution_before_private_capture(
        string participant, bool discoverOriginal)
    {
        await using var fixture = await Fixture.CreateAsync();
        await EraseOriginalParticipantAsync(fixture, participant);
        var reads = new CaptureReads();
        if (discoverOriginal)
        {
            await using var context = fixture.Context(reads);
            var identity = await fixture.AuthenticateAsync(context);
            var result = await new ModerationResourceQuarantineRepository(context, identity.Machine,
                identity.Bindings, new ExternalApiKeyRepository(context), fixture.Clock,
                fixture.Database.Privacy.Authority(context, fixture.Clock))
                .ExecuteOriginalAsync(fixture.EffectId, fixture.LeaseId, fixture.LeaseRevision);
            await Assert.That(result.Outcome).IsEqualTo(ModerationOriginalQuarantineOutcome.Unavailable);
            await Assert.That(result.ExpectedCauseCount).IsEqualTo(0);
            await Assert.That(result.Receipts.Count).IsEqualTo(0);
        }
        else
        {
            var result = await fixture.ExecuteAsync(interceptors: [reads]);
            await Assert.That(result.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Unavailable);
            await Assert.That(result.Receipt).IsNull();
        }
        await Assert.That(reads.Count).IsEqualTo(0);
        await AssertState(fixture, 0);
        await using var reader = fixture.Context();
        await Assert.That(await reader.PrivacyErasureSagas.AnyAsync()).IsFalse();
        await Assert.That((await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId))
            .HasPublishablePayload()).IsTrue();
    }

    [Test]
    [Arguments("representative")]
    [Arguments("reviewer")]
    [Arguments("approver")]
    public async Task Original_native_cause_is_not_replay_authority_after_retained_participant_erasure(string participant)
    {
        await using var fixture = await Fixture.CreateAsync();
        var applied = await fixture.ExecuteAsync();
        await Assert.That(applied.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        await EraseOriginalParticipantAsync(fixture, participant);
        var reads = new CaptureReads();
        var replay = await fixture.ExecuteAsync(interceptors: [reads]);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Unavailable);
        await Assert.That(replay.Receipt).IsNull();
        await Assert.That(reads.Count).IsEqualTo(0);
        await AssertState(fixture, 1);
        await using var reader = fixture.Context();
        var cause = await reader.EventResourceModerationQuarantines.SingleAsync(row => row.EffectId == fixture.EffectId);
        await Assert.That(cause.Id).IsEqualTo(applied.Receipt!.Id);
        await Assert.That(cause.AppliedAt).IsEqualTo(applied.Receipt.AppliedAt);
        await Assert.That(cause.ReleaseId).IsNull();
        await Assert.That(await reader.PrivacyErasureSagas.AnyAsync()).IsFalse();
    }

    private static async Task EraseOriginalParticipantAsync(Fixture fixture, string participant)
    {
        var humanId = participant switch
        {
            "representative" => fixture.Database.SubjectId,
            "reviewer" => fixture.Database.ReviewerId,
            "approver" => fixture.Database.AdministratorId,
            _ => throw new ArgumentOutOfRangeException(nameof(participant))
        };
        await using var context = fixture.Context();
        var fact = await fixture.Database.Privacy.Authority(context, fixture.Clock).AppendAsync(
            PrivacyErasureRequest.Create(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
                humanId, PrivacyErasureReasonCode.AccountDeletion, 1));
        await Assert.That(fact.SubjectId).IsEqualTo(humanId);
        await Assert.That(fact.AuthoritySequence).IsEqualTo(1L);
        await Assert.That(await context.PrivacyErasureSagas.AnyAsync()).IsFalse();
    }

    private sealed class OriginalAuthorityFactory(DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
    }

    private sealed class ObserveOriginalSave(OriginalCommitCheckpoint checkpoint) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (checkpoint.QuarantineSaved || eventData.Context is not ExploreDbContext primary
                || !primary.ChangeTracker.Entries<EventResourceModerationQuarantine>().Any()) return result;
            checkpoint.QuarantineSaved = true;
            checkpoint.QuarantineTransaction = primary.Database.CurrentTransaction!.GetDbTransaction();
            if (!primary.IdentityFenceOwnsTransaction
                || !ReferenceEquals(checkpoint.AuthorityTransaction, checkpoint.QuarantineTransaction))
                throw new InvalidOperationException("The selected authority must own original quarantine adoption.");
            checkpoint.DisclosureBefore = (await primary.Set<EventDiscoveryRevision>().SingleAsync(cancellationToken))
                .DisclosureEpoch;
            return result;
        }
    }

    private sealed class OriginalCommitCheckpoint(Fixture fixture, bool expireAtFlush) : DbCommandInterceptor
    {
        public bool QuarantineSaved { get; set; }
        public long DisclosureBefore { get; set; }
        public int FlushCount { get; private set; }
        public DbTransaction? AuthorityTransaction { get; set; }
        public DbTransaction? QuarantineTransaction { get; set; }
        public List<DbTransaction> Committed { get; } = [];

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!QuarantineSaved || eventData.Context is not ExploreDbContext primary) return ValueTask.FromResult(result);
            var entity = primary.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = primary.GetService<ISqlGenerationHelper>();
            var column = sql.DelimitIdentifier(entity.FindProperty(nameof(EventDiscoveryRevision.DisclosureEpoch))!
                .GetColumnName(table)!);
            var update = $"UPDATE {sql.DelimitIdentifier(table.Name, table.Schema)} SET {column} = ";
            // Observe the executed disclosure advancement, not the one-parameter no-op row fence.
            if (command.Parameters.Count == 3 && command.CommandText.StartsWith(update, StringComparison.Ordinal))
            {
                if (result != 1 || !primary.IdentityFenceOwnsTransaction
                    || !ReferenceEquals(command.Transaction, QuarantineTransaction) || Committed.Count != 0)
                    throw new InvalidOperationException("Original disclosure must flush before the retained owner's commit.");
                FlushCount++;
                if (expireAtFlush) fixture.Clock.UtcNow = fixture.PurposeDeadline;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ObserveOriginalCommit(OriginalCommitCheckpoint checkpoint) : DbTransactionInterceptor
    {
        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not EmbeddedPrivacyErasureAuthorityDbContext
                || result.IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("Original custody requires the native serializable authority.");
            checkpoint.AuthorityTransaction = result;
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(transaction, checkpoint.AuthorityTransaction))
                throw new InvalidOperationException("Only the selected retained owner may commit.");
            checkpoint.Committed.Add(transaction);
            return Task.CompletedTask;
        }
    }
}
