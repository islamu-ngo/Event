using System.Data;
using System.Data.Common;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    public async Task Borrowed_adjudicated_read_keeps_the_native_transaction_and_epoch_without_saving()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using (var writer = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .GrantAsync(request)).IsNotNull();
        await using var reader = fixture.Database.Context(new RejectBorrowedReadSave());
        var privacy = fixture.Privacy.Authority(reader, fixture.Clock);
        var (identity, machine) = CorrectionIdentity(reader, request.GranteeUserId);
        var repository = new ModerationAdjudicatedCaseReadGrantRepository(reader, privacy, identity, machine, fixture.Clock);
        await privacy.ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var epoch = await reader.ModerationQualificationEpochs.AsNoTracking().Select(row => row.Revision).SingleAsync(token);
            var acquired = await repository.AcquireCurrentInTransactionAsync(request.OperationId, request.CaseId,
                request.ExpectedCaseRevision, token);
            await Assert.That(acquired).IsNotNull();
            await Assert.That(acquired!.Grant.Id).IsEqualTo(request.OperationId);
            await Assert.That(acquired.Authority.UserId).IsEqualTo(request.GranteeUserId);
            await Assert.That(await acquired.RevalidateAsync(token)).IsTrue();
            await Assert.That(ReferenceEquals(reader.Database.CurrentTransaction, transaction)).IsTrue();
            await Assert.That(reader.ChangeTracker.HasChanges()).IsFalse();
            await Assert.That(await reader.ModerationQualificationEpochs.AsNoTracking()
                .Select(row => row.Revision).SingleAsync(token)).IsEqualTo(epoch);
            await Assert.That(async () => await repository.ReadCurrentAsync(request.OperationId, request.CaseId,
                request.ExpectedCaseRevision, token)).Throws<InvalidOperationException>();
            await Assert.That(reader.ModerationTrustDomainId).IsEqualTo(fixture.Database.Scope.TrustDomainId);
            return true;
        }, default);
        await AssertOriginalAdjudicatedLedgerUnchangedAsync(fixture, original);
    }

    [Test]
    [Arguments("caller")]
    [Arguments("revoked")]
    [Arguments("deadline")]
    [Arguments("revision")]
    [Arguments("erased")]
    public async Task Borrowed_adjudicated_acquisition_fails_closed_for_lost_read_authority(string loss)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using (var writer = fixture.Database.Context())
        {
            var issuer = AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId);
            await Assert.That(await issuer.GrantAsync(request)).IsNotNull();
            if (loss == "revoked")
                await Assert.That(await issuer.RevokeAsync(request.OperationId, Guid.CreateVersion7(), 1)).IsNotNull();
        }
        if (loss == "deadline") fixture.Clock.UtcNow = request.ValidUntil;
        await using var reader = fixture.Database.Context(new RejectBorrowedReadSave());
        var privacy = fixture.Privacy.Authority(reader, fixture.Clock);
        if (loss == "erased")
            await privacy.AppendAsync(new(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
                fixture.Database.SubjectId, PrivacyErasureReasonCode.AccountDeletion, 1));
        var (identity, machine) = CorrectionIdentity(reader,
            loss == "caller" ? fixture.Database.AdministratorId : request.GranteeUserId);
        var repository = new ModerationAdjudicatedCaseReadGrantRepository(reader, privacy, identity, machine, fixture.Clock);
        await privacy.ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await Assert.That(await repository.AcquireCurrentInTransactionAsync(request.OperationId, request.CaseId,
                request.ExpectedCaseRevision + (loss == "revision" ? 1 : 0), token)).IsNull();
            await Assert.That(ReferenceEquals(reader.Database.CurrentTransaction, transaction)).IsTrue();
            return true;
        }, default);
    }

    [Test]
    [Arguments("deadline")]
    [Arguments("original-deadline")]
    [Arguments("rollback-clock")]
    [Arguments("grant")]
    [Arguments("role")]
    [Arguments("epoch")]
    [Arguments("trust")]
    public async Task Borrowed_adjudicated_revalidation_rejects_drift_inside_its_native_transaction(string loss)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using (var writer = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .GrantAsync(request)).IsNotNull();
        await using var reader = fixture.Database.Context(new RejectBorrowedReadSave());
        var privacy = fixture.Privacy.Authority(reader, fixture.Clock);
        var (identity, machine) = CorrectionIdentity(reader, request.GranteeUserId);
        var repository = new ModerationAdjudicatedCaseReadGrantRepository(reader, privacy, identity, machine, fixture.Clock);
        await privacy.ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var acquired = await repository.AcquireCurrentInTransactionAsync(request.OperationId, request.CaseId,
                request.ExpectedCaseRevision, token);
            await Assert.That(acquired).IsNotNull();
            if (loss == "deadline") fixture.Clock.UtcNow = request.ValidUntil;
            else if (loss == "original-deadline") fixture.Clock.UtcNow = original.Source.RetainUntil!.Value;
            else if (loss == "rollback-clock") fixture.Clock.UtcNow = fixture.Clock.GetUtcNow().AddTicks(-1);
            else if (loss == "grant")
                await reader.Set<ModerationAdjudicatedCaseReadGrant>().Where(row => row.Id == request.OperationId)
                    .ExecuteDeleteAsync(token);
            else if (loss == "role")
                await reader.PlatformUserRoles.Where(row => row.Id == acquired!.Grant.IssuerRoleGrantId).ExecuteDeleteAsync(token);
            else if (loss == "epoch")
                await reader.ModerationQualificationEpochs.Where(row => row.Id == acquired!.Authority.Epoch.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), token);
            else reader.ModerationTrustDomainId = null;
            await Assert.That(await acquired!.RevalidateAsync(token)).IsFalse();
            return true;
        }, default);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Borrowed_adjudicated_acquisition_cannot_outlive_actual_native_settlement(bool commit)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using (var writer = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .GrantAsync(request)).IsNotNull();
        await using var reader = fixture.Database.Context(new RejectBorrowedReadSave());
        var privacy = fixture.Privacy.Authority(reader, fixture.Clock);
        var (identity, machine) = CorrectionIdentity(reader, request.GranteeUserId);
        var repository = new ModerationAdjudicatedCaseReadGrantRepository(reader, privacy, identity, machine, fixture.Clock);
        ModerationAdjudicatedCaseReadGrantRepository.ReadAcquisition? escaped = null;
        await privacy.ExecuteSerializedAsync(async token =>
        {
            var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            escaped = await repository.AcquireCurrentInTransactionAsync(request.OperationId, request.CaseId,
                request.ExpectedCaseRevision, token);
            await Assert.That(escaped).IsNotNull();
            if (commit) await transaction.CommitAsync(token);
            else await transaction.DisposeAsync();
            await Assert.That(await escaped!.RevalidateAsync(token)).IsFalse();
            if (commit) await transaction.DisposeAsync();
            await using var replacement = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await Assert.That(await escaped.RevalidateAsync(token)).IsFalse();
            return true;
        }, default);
        await Assert.That(await escaped!.RevalidateAsync(default)).IsFalse();
    }

    [Test]
    public async Task Borrowed_read_rejects_raw_deadline_crossing_during_awaited_original_custody_checks()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var clock = new AdvancingAdjudicatedIssueClock(fixture.Clock.GetUtcNow());
        var request = AdjudicatedGrantRequest(fixture, original) with { ValidUntil = clock.Start.AddMicroseconds(2) };
        await using (var writer = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .GrantAsync(request)).IsNotNull();
        await using var reader = fixture.Database.Context(new StartAdjudicatedIssueClock(clock));
        var privacy = fixture.Privacy.Authority(reader, clock);
        var (identity, machine) = CorrectionIdentity(reader, request.GranteeUserId);
        var repository = new ModerationAdjudicatedCaseReadGrantRepository(reader, privacy, identity, machine, clock);
        await privacy.ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await Assert.That(await repository.AcquireCurrentInTransactionAsync(request.OperationId, request.CaseId,
                request.ExpectedCaseRevision, token)).IsNull();
            await Assert.That(clock.Started).IsTrue();
            await Assert.That(clock.GetUtcNow() >= request.ValidUntil).IsTrue();
            await Assert.That(ReferenceEquals(reader.Database.CurrentTransaction, transaction)).IsTrue();
            return true;
        }, default);
    }

    [Test]
    public async Task Borrowed_read_does_not_fetch_private_custody_after_the_grant_expires_at_its_envelope()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using (var writer = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .GrantAsync(request)).IsNotNull();
        var probe = new ExpireBorrowedCustodyEnvelope(fixture, request.ValidUntil);
        await using var reader = fixture.Database.Context(probe);
        var privacy = fixture.Privacy.Authority(reader, fixture.Clock);
        var (identity, machine) = CorrectionIdentity(reader, request.GranteeUserId);
        var repository = new ModerationAdjudicatedCaseReadGrantRepository(reader, privacy, identity, machine, fixture.Clock);
        await privacy.ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await Assert.That(await repository.AcquireCurrentInTransactionAsync(request.OperationId, request.CaseId,
                request.ExpectedCaseRevision, token)).IsNull();
            await Assert.That(probe.Observed).IsTrue();
            await Assert.That(probe.ExpiredBodyRead).IsFalse();
            return true;
        }, default);
    }

    [Test]
    public async Task Borrowed_revalidation_respects_the_consumers_shorter_approved_read_deadline()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        await using (var writer = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .GrantAsync(request)).IsNotNull();
        var approvalUntil = fixture.Clock.GetUtcNow().AddSeconds(5);
        var probe = new ExpireBorrowedCustodyEnvelope(fixture, approvalUntil) { Armed = false };
        await using var reader = fixture.Database.Context(probe);
        var privacy = fixture.Privacy.Authority(reader, fixture.Clock);
        var (identity, machine) = CorrectionIdentity(reader, request.GranteeUserId);
        var repository = new ModerationAdjudicatedCaseReadGrantRepository(reader, privacy, identity, machine, fixture.Clock);
        await privacy.ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var acquired = await repository.AcquireCurrentInTransactionAsync(request.OperationId, request.CaseId,
                request.ExpectedCaseRevision, token);
            await Assert.That(acquired).IsNotNull();
            probe.Armed = true;
            await Assert.That(await acquired!.RevalidateAsync(token, approvalUntil)).IsFalse();
            await Assert.That(probe.Observed).IsTrue();
            await Assert.That(probe.ExpiredBodyRead).IsFalse();
            await Assert.That(fixture.Clock.GetUtcNow() < request.ValidUntil).IsTrue();
            return true;
        }, default);
    }

    private sealed class ExpireBorrowedCustodyEnvelope(NoticeDatabase fixture, DateTimeOffset expiresAt) : DbCommandInterceptor
    {
        public bool Armed { get; set; } = true;
        public bool Observed { get; private set; }
        public bool ExpiredBodyRead { get; private set; }

        private static bool ReadsSource(DbCommand command, ExploreDbContext context) =>
            command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains($"FROM {PreparationSourceTable(context)}", StringComparison.OrdinalIgnoreCase);

        private static string PreparationSourceTable(ExploreDbContext context) =>
            context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
                context.Model.FindEntityType(typeof(ModerationNoticeSource))!.GetTableName()!);

        private static bool ReadsBody(DbCommand command, ExploreDbContext context)
        {
            var entity = context.Model.FindEntityType(typeof(ModerationNoticeSource))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return command.CommandText.Contains(context.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(entity.FindProperty(nameof(ModerationNoticeSource.SnapshotJson))!.GetColumnName(table)!), StringComparison.Ordinal);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed && !Observed && eventData.Context is ExploreDbContext context && ReadsSource(command, context) && !ReadsBody(command, context))
            {
                Observed = true;
                fixture.Clock.UtcNow = expiresAt;
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Observed && eventData.Context is ExploreDbContext context && ReadsSource(command, context) && ReadsBody(command, context))
            {
                ExpiredBodyRead = true;
                throw new InvalidOperationException("Expired personal permission must not fetch retained private custody.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RejectBorrowedReadSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A borrowed metadata read must not save caller-owned work.");
    }
}
