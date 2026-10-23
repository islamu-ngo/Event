using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Correction_disclosure_borrows_only_approved_explanation_for_the_withdrawn_original_reviewer(
        bool colocated)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Database.Context();
        await using var colocatedPrivacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(reader) : null;
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture, colocatedPrivacy);
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        var privacy = (colocatedPrivacy ?? fixture.Privacy).Authority(reader, fixture.Clock);
        var repository = CorrectionDisclosureReader(reader, fixture, fixture.Database.ReviewerId, privacy);
        ReadOnlyMemory<byte> borrowed = default;
        var calls = 0;

        // This is a direct borrowed callback, not HTTP, email or delivery to the entire original audience.
        var disclosed = await repository.DiscloseCorrectionAsync(CorrectionPreparationRequest(pending, grant), async (bytes, token) =>
        {
            calls++;
            borrowed = bytes;
            await Task.Yield();
            await Assert.That(reader.Database.CurrentTransaction).IsNotNull();
            await Assert.That(reader.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                .IsEqualTo(IsolationLevel.Serializable);
            await Assert.That(reader.IdentityFenceOwnsTransaction).IsEqualTo(colocated);
            await Assert.That(await privacy.IsSubjectFencedAsync(fixture.Database.ReviewerId, token)).IsFalse();
            await AssertCorrectionExplanationAsync(bytes, pending.Request.Intent);
        });

        await Assert.That(disclosed).IsTrue();
        await Assert.That(calls).IsEqualTo(1);
        await AssertCorrectionDisclosureBufferClearedAsync(borrowed);
        await AssertCorrectionDisclosureReleasedAsync(reader, privacy, fixture.Database.ReviewerId);
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    [Arguments("replacement")]
    [Arguments("absentGrant")]
    [Arguments("revokedGrant")]
    [Arguments("revokedText")]
    [Arguments("expiredText")]
    [Arguments("otherOriginal")]
    public async Task Correction_disclosure_denies_before_output_without_exact_original_identity_and_current_authority(
        string loss)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture, issueGrant: loss != "absentGrant");
        var request = CorrectionPreparationRequest(pending, grant);
        var caller = fixture.Database.ReviewerId;
        if (loss == "replacement")
        {
            caller = fixture.Database.UrgencyAssessorId;
            var replacement = grant with { OperationId = Guid.CreateVersion7(), GranteeUserId = caller };
            await IssueCorrectionPreparationGrantAsync(fixture, replacement);
            request = request with { GrantId = replacement.OperationId };
        }
        else if (loss == "revokedGrant")
        {
            await using var writer = fixture.Database.Context();
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .RevokeAsync(grant.OperationId, Guid.CreateVersion7(), 1)).IsNotNull();
        }
        else if (loss == "revokedText")
        {
            var state = await CorrectionApprovalStateAsync(fixture, pending);
            var revoked = await WriteCorrectionApprovalAsync(fixture, new RevokeModerationNoticeDisclosureSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = pending.Request.Target,
                ExpectedQualificationRevision = state.QualificationRevision,
                ApprovalId = pending.Request.Intent.ApprovalId,
                CaseId = pending.Request.Intent.CaseId,
                ExpectedCaseRevision = pending.Request.Intent.ExpectedCaseRevision,
                ExpectedApprovalRevision = 1
            });
            await Assert.That(revoked.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        else if (loss == "expiredText") fixture.Clock.UtcNow = pending.Request.Intent.ValidUntil;
        else if (loss == "otherOriginal") request = request with { OriginalNoticeId = pending.Original.SubjectNotice.Id };
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        await using var reader = fixture.Database.Context();
        var privacy = fixture.Privacy.Authority(reader, fixture.Clock);
        var repository = CorrectionDisclosureReader(reader, fixture, caller, privacy);
        var calls = 0;

        var disclosed = await repository.DiscloseCorrectionAsync(request, (_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        });

        await Assert.That(disclosed).IsFalse();
        await Assert.That(calls).IsEqualTo(0);
        await AssertCorrectionDisclosureReleasedAsync(reader, privacy, caller);
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Correction_disclosure_suppresses_output_when_retained_source_or_text_expires_during_requalification(
        bool expireText)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture);
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        var clockProbe = new ExpirePreparationDuringAudienceRequalification(fixture, pending);
        var sourceProbe = new ExpireCorrectionDisclosureSource(fixture, pending);
        await using var reader = fixture.Database.Context(expireText ? clockProbe : sourceProbe);
        var privacy = fixture.Privacy.Authority(reader, fixture.Clock);
        var repository = CorrectionDisclosureReader(reader, fixture, fixture.Database.ReviewerId, privacy);
        var calls = 0;

        var disclosed = await repository.DiscloseCorrectionAsync(CorrectionPreparationRequest(pending, grant), (_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        });

        await Assert.That(disclosed).IsFalse();
        await Assert.That(calls).IsEqualTo(0);
        if (expireText)
        {
            await Assert.That(clockProbe.InitialApprovalReceiptObserved).IsTrue();
            await Assert.That(clockProbe.RequalificationEnvelopeObserved).IsTrue();
            await Assert.That(clockProbe.ExpiredPrivateQuery).IsFalse();
        }
        else
        {
            await Assert.That(sourceProbe.Observed).IsTrue();
            await Assert.That(sourceProbe.HeldPrimaryTransaction).IsTrue();
            await Assert.That(sourceProbe.RowsExpired).IsEqualTo(1);
        }
        await AssertCorrectionDisclosureReleasedAsync(reader, privacy, fixture.Database.ReviewerId);
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Correction_disclosure_propagates_consumer_failure_or_cancellation_without_replay_and_clears_borrowed_bytes(
        bool cancel, bool colocated)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var reader = fixture.Database.Context();
        await using var colocatedPrivacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(reader) : null;
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture, colocatedPrivacy);
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        var privacy = (colocatedPrivacy ?? fixture.Privacy).Authority(reader, fixture.Clock);
        var repository = CorrectionDisclosureReader(reader, fixture, fixture.Database.ReviewerId, privacy);
        using var cancellation = new CancellationTokenSource();
        Exception failure = cancel ? new OperationCanceledException(cancellation.Token)
            : new IOException("Consumer failed after observing correction bytes.");
        Exception? observed = null;
        ReadOnlyMemory<byte> borrowed = default;
        var calls = 0;

        try
        {
            await repository.DiscloseCorrectionAsync(CorrectionPreparationRequest(pending, grant), async (bytes, token) =>
            {
                calls++;
                borrowed = bytes;
                await AssertCorrectionExplanationAsync(bytes, pending.Request.Intent);
                await Assert.That(reader.Database.CurrentTransaction).IsNotNull();
                await Assert.That(reader.IdentityFenceOwnsTransaction).IsEqualTo(colocated);
                await Assert.That(await privacy.IsSubjectFencedAsync(fixture.Database.ReviewerId, token)).IsFalse();
                if (cancel)
                {
                    cancellation.Cancel();
                    await Assert.That(token.IsCancellationRequested).IsTrue();
                }
                throw failure;
            }, cancellation.Token);
        }
        catch (Exception exception) { observed = exception; }

        await Assert.That(observed).IsSameReferenceAs(failure);
        await Assert.That(calls).IsEqualTo(1);
        await AssertCorrectionDisclosureBufferClearedAsync(borrowed);
        await AssertCorrectionDisclosureReleasedAsync(reader, privacy, fixture.Database.ReviewerId);
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Correction_disclosure_never_replays_output_after_retryable_commit_failure_or_lost_commit_acknowledgement(
        bool colocatedLostCommit)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var probe = new FailCorrectionDisclosureCommit(colocatedLostCommit);
        await using var template = fixture.Database.Context();
        var options = (DbContextOptions<ExploreDbContext>)template.GetService<IDbContextOptions>();
        await using var reader = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>(options)
            .AddInterceptors(probe).ReplaceService<IExecutionStrategyFactory, CorrectionDisclosureRetryStrategyFactory>().Options)
        { TenantContext = fixture.Database };
        await using var colocatedPrivacy = colocatedLostCommit
            ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(reader, probe) : null;
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture, colocatedPrivacy);
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        var privacy = (colocatedPrivacy ?? fixture.Privacy).Authority(reader, fixture.Clock);
        var repository = CorrectionDisclosureReader(reader, fixture, fixture.Database.ReviewerId, privacy);
        Exception? observed = null;
        ReadOnlyMemory<byte> borrowed = default;
        var calls = 0;

        try
        {
            await repository.DiscloseCorrectionAsync(CorrectionPreparationRequest(pending, grant), async (bytes, token) =>
            {
                calls++;
                borrowed = bytes;
                await AssertCorrectionExplanationAsync(bytes, pending.Request.Intent);
                await Assert.That(reader.Database.CurrentTransaction).IsNotNull();
                await Assert.That(await privacy.IsSubjectFencedAsync(fixture.Database.ReviewerId, token)).IsFalse();
                probe.Armed = true;
            });
        }
        catch (Exception exception) { observed = exception; }

        await Assert.That(probe.Injected).IsTrue();
        await Assert.That(observed).IsSameReferenceAs(probe.Failure);
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(probe.CommitAttempts).IsEqualTo(1);
        await AssertCorrectionDisclosureBufferClearedAsync(borrowed);
        await AssertCorrectionDisclosureReleasedAsync(reader, privacy, fixture.Database.ReviewerId);
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    private sealed class ExpireCorrectionDisclosureSource(NoticeDatabase fixture, CorrectionApprovalPending pending)
        : DbCommandInterceptor
    {
        private bool _initialApprovalReceiptObserved;
        public bool Observed { get; private set; }
        public bool HeldPrimaryTransaction { get; private set; }
        public int RowsExpired { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not ExploreDbContext context || Observed) return result;
            if (!_initialApprovalReceiptObserved && DisclosureReads<ModerationQualificationMutationReceipt>(context, command)
                && command.Parameters.Cast<DbParameter>().Any(parameter =>
                    Guid.TryParse(parameter.Value?.ToString(), out var id) && id == pending.Request.Intent.ApprovalId))
                _initialApprovalReceiptObserved = true;
            else if (_initialApprovalReceiptObserved && DisclosureReads<ModerationNoticeSource>(context, command))
            {
                // Drift is injected only after the initial native approval proof, inside the actual read transaction.
                Observed = true;
                HeldPrimaryTransaction = context.Database.CurrentTransaction is not null;
                fixture.Clock.UtcNow = fixture.Clock.GetUtcNow().AddSeconds(1);
                RowsExpired = await context.ModerationNoticeSources.Where(row => row.EffectId == pending.Effect.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.RetainUntil, fixture.Clock.GetUtcNow()), cancellationToken);
            }
            return result;
        }

        private static bool DisclosureReads<T>(ExploreDbContext context, DbCommand command)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains($"FROM {context.GetService<ISqlGenerationHelper>()
                    .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())}", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FailCorrectionDisclosureCommit(bool lostCommit) : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public bool Injected { get; private set; }
        public int CommitAttempts { get; private set; }
        public SqliteException Failure { get; } = new("Injected retryable correction disclosure commit failure.", 5);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                CommitAttempts++;
                if (!lostCommit && !Injected)
                {
                    Injected = true;
                    throw Failure;
                }
            }
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Armed && lostCommit && !Injected)
            {
                Injected = true;
                throw Failure;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class CorrectionDisclosureRetryStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new CorrectionDisclosureRetryStrategy(dependencies);
    }

    private sealed class CorrectionDisclosureRetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is SqliteException;
    }
}
