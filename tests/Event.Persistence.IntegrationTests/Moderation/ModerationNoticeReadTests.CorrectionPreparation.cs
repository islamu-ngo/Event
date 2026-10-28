using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    public async Task Correction_output_cannot_borrow_an_unqualified_callers_existing_transaction()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture);
        await using var context = fixture.Database.Context();
        var (identity, machine) = CorrectionIdentity(context, fixture.Database.ReviewerId);
        var activation = new ModerationNativeActivationQualification(context, fixture.Database.Inventory, null,
            new ModerationQualificationPolicySourceOwner(context, fixture.Clock), fixture.Clock);
        var repository = new ModerationNoticeReadRepository(context, identity, machine,
            new ModerationNoticeRecipientRepository(context), activation, fixture.Clock,
            fixture.Privacy.Authority(context, fixture.Clock));
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        bool called = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DiscloseCorrectionAsync(
            CorrectionPreparationRequest(pending, grant), (_, _) =>
            {
                called = true;
                return Task.CompletedTask;
            }));

        await Assert.That(called).IsFalse();
        await Assert.That(ReferenceEquals(context.Database.CurrentTransaction, transaction)).IsTrue();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Correction_preparation_uses_the_original_recipients_real_case_grant_not_current_reviewer_assignment(
        bool colocated)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var primary = fixture.Database.Context();
        await using var privacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(primary) : null;
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture, privacy);
        var replacement = grant with
        {
            OperationId = Guid.CreateVersion7(),
            GranteeUserId = fixture.Database.UrgencyAssessorId
        };
        await IssueCorrectionPreparationGrantAsync(fixture, replacement, privacy);
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        var request = CorrectionPreparationRequest(pending, grant);

        // The creating activation is factory-seeded; only Revoke, text approval and these grants are native writes.
        await Assert.That(await PrepareCorrectionAsAsync(fixture, fixture.Database.ReviewerId, request, privacy)).IsTrue();
        await Assert.That(await PrepareCorrectionAsAsync(fixture, fixture.Database.UrgencyAssessorId,
            request with { GrantId = replacement.OperationId }, privacy)).IsFalse();
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    [Arguments("absent")]
    [Arguments("revoked")]
    [Arguments("subject")]
    public async Task Original_notice_recipient_identity_cannot_replace_a_current_personal_case_grant(string permission)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture, issueGrant: permission != "absent");
        var request = CorrectionPreparationRequest(pending, grant);
        var caller = fixture.Database.ReviewerId;
        if (permission == "absent")
            request = request with { GrantId = Guid.CreateVersion7() };
        else if (permission == "revoked")
        {
            await using var writer = fixture.Database.Context();
            await Assert.That(await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId)
                .RevokeAsync(grant.OperationId, Guid.CreateVersion7(), 1)).IsNotNull();
        }
        else
        {
            caller = fixture.Database.SubjectId;
            request = request with { OriginalNoticeId = pending.Original.SubjectNotice.Id };
        }
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        if (permission == "absent")
            await Assert.That(before.State.ReadGrants).IsEqualTo(0);

        await Assert.That(await PrepareCorrectionAsAsync(fixture, caller, request)).IsFalse();

        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Correction_preparation_requires_live_unrevoked_native_text_approval(bool revoke)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture);
        if (revoke)
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
        else
            fixture.Clock.UtcNow = pending.Request.Intent.ValidUntil;
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);

        await Assert.That(await PrepareCorrectionAsAsync(fixture, fixture.Database.ReviewerId,
            CorrectionPreparationRequest(pending, grant))).IsFalse();

        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    [Arguments("caseGeneration")]
    [Arguments("approvalDecisionGeneration")]
    [Arguments("originalSnapshotHash")]
    [Arguments("originalBindingHash")]
    public async Task Correction_preparation_rejects_generation_and_original_custody_drift_after_real_approval_and_grant(
        string drift)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture);
        await fixture.MutateAsync(async context =>
        {
            switch (drift)
            {
                case "caseGeneration":
                    (await context.ModerationCases.SingleAsync(row => row.Id == grant.CaseId))
                        .Close(grant.ExpectedCaseRevision, fixture.Clock.GetUtcNow());
                    break;
                case "approvalDecisionGeneration":
                    await context.ModerationNoticeDisclosureApprovals
                        .Where(row => row.Id == pending.Request.Intent.ApprovalId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.DecisionRevision,
                            pending.Request.Intent.ExpectedDecisionRevision + 1));
                    break;
                case "originalSnapshotHash":
                    await context.ModerationNoticeSources.Where(row => row.EffectId == pending.Original.Effect.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SnapshotHash, new string('0', 64)));
                    break;
                case "originalBindingHash":
                    await context.ModerationNoticeSources.Where(row => row.EffectId == pending.Original.Effect.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.EffectBindingHash, new string('0', 64)));
                    break;
            }
        });
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);

        await Assert.That(await PrepareCorrectionAsAsync(fixture, fixture.Database.ReviewerId,
            CorrectionPreparationRequest(pending, grant))).IsFalse();

        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Correction_preparation_cannot_return_true_when_an_acquired_grant_expires_or_loses_its_issuing_role(
        bool expireGrant)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture, shortGrant: true);
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        var probe = new InvalidateCorrectionPreparation(fixture, pending, grant, expireGrant ? "grantExpiry" : "issuerRole");

        await Assert.That(await PrepareCorrectionAsAsync(fixture, fixture.Database.ReviewerId,
            CorrectionPreparationRequest(pending, grant), interceptor: probe)).IsFalse();

        await Assert.That(probe.Observed).IsTrue();
        await Assert.That(probe.HeldPrimaryTransaction).IsTrue();
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    public async Task Correction_preparation_does_not_read_private_source_or_approval_bodies_after_original_custody_expires()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture);
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        var probe = new InvalidateCorrectionPreparation(fixture, pending, grant, "originalCustody");

        await Assert.That(await PrepareCorrectionAsAsync(fixture, fixture.Database.ReviewerId,
            CorrectionPreparationRequest(pending, grant), interceptor: probe)).IsFalse();

        await Assert.That(probe.Observed).IsTrue();
        await Assert.That(probe.HeldPrimaryTransaction).IsTrue();
        await Assert.That(probe.ExpiredPrivateQuery).IsFalse();
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Preparation_requalification_keeps_the_approved_text_deadline_before_another_private_audience_read(
        bool colocated)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var primary = fixture.Database.Context();
        await using var privacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(primary) : null;
        var (pending, grant) = await ApprovedCorrectionPreparationAsync(fixture, privacy);
        var before = await CorrectionPreparationLedgerAsync(fixture, pending);
        var probe = new ExpirePreparationDuringAudienceRequalification(fixture, pending);

        await Assert.That(await PrepareCorrectionAsAsync(fixture, fixture.Database.ReviewerId,
            CorrectionPreparationRequest(pending, grant), privacy, probe)).IsFalse();

        await Assert.That(probe.InitialApprovalReceiptObserved).IsTrue();
        await Assert.That(probe.RequalificationEnvelopeObserved).IsTrue();
        await Assert.That(probe.ExpiredPrivateQuery).IsFalse();
        await Assert.That(await CorrectionPreparationLedgerAsync(fixture, pending)).IsEqualTo(before);
        await AssertCorrectionApprovalHasNoDeliveryAsync(fixture, pending);
    }

    private static async Task<bool> PrepareCorrectionAsAsync(NoticeDatabase fixture, Guid caller,
        ModerationNoticeCorrectionPreparationRequest request, ModerationNoticePrivacyFixture? privacy = null,
        IInterceptor? interceptor = null)
    {
        await using var context = fixture.Database.Context(interceptor);
        var (identity, machine) = CorrectionIdentity(context, caller);
        var activation = new ModerationNativeActivationQualification(context, fixture.Database.Inventory, null,
            new ModerationQualificationPolicySourceOwner(context, fixture.Clock), fixture.Clock);
        var repository = new ModerationNoticeReadRepository(context, identity, machine,
            new ModerationNoticeRecipientRepository(context), activation, fixture.Clock,
            (privacy ?? fixture.Privacy).Authority(context, fixture.Clock));

        var result = await repository.PrepareCorrectionAsync(request);

        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        return result;
    }

    private sealed class ExpirePreparationDuringAudienceRequalification(
        NoticeDatabase fixture, CorrectionApprovalPending pending) : DbCommandInterceptor
    {
        private int _envelopesAfterInitialQualification;
        public bool InitialApprovalReceiptObserved { get; private set; }
        public bool RequalificationEnvelopeObserved { get; private set; }
        public bool ExpiredPrivateQuery { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not ExploreDbContext context) return ValueTask.FromResult(result);
            if (!InitialApprovalReceiptObserved && Reads<ModerationQualificationMutationReceipt>(context, command)
                && command.Parameters.Cast<DbParameter>().Any(parameter =>
                    Guid.TryParse(parameter.Value?.ToString(), out var id) && id == pending.Request.Intent.ApprovalId))
                InitialApprovalReceiptObserved = true;
            else if (InitialApprovalReceiptObserved && !RequalificationEnvelopeObserved
                && Reads<ModerationNoticeSource>(context, command)
                && !Selects<ModerationNoticeSource>(context, command, nameof(ModerationNoticeSource.SnapshotJson)))
            {
                // The first envelope belongs to bounded grant revalidation; the next is the audience requalification.
                if (++_envelopesAfterInitialQualification == 2)
                {
                    RequalificationEnvelopeObserved = context.Database.CurrentTransaction is not null;
                    fixture.Clock.UtcNow = pending.Request.Intent.ValidUntil;
                }
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (RequalificationEnvelopeObserved && eventData.Context is ExploreDbContext context
                && (Reads<ModerationNoticeSource>(context, command)
                    && Selects<ModerationNoticeSource>(context, command, nameof(ModerationNoticeSource.SnapshotJson))
                    || Reads<ModerationNoticeDisclosureApproval>(context, command)
                    && Selects<ModerationNoticeDisclosureApproval>(context, command, nameof(ModerationNoticeDisclosureApproval.FactsJson))))
            {
                ExpiredPrivateQuery = true;
                throw new InvalidOperationException("Expired correction approval cannot authorize another private source read.");
            }
            return ValueTask.FromResult(result);
        }

        private static bool Reads<T>(ExploreDbContext context, DbCommand command)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains($"FROM {context.GetService<ISqlGenerationHelper>()
                    .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())}", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Selects<T>(ExploreDbContext context, DbCommand command, string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return command.CommandText.Contains(context.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(table)!), StringComparison.Ordinal);
        }
    }

    private sealed class InvalidateCorrectionPreparation(NoticeDatabase fixture, CorrectionApprovalPending pending,
        GrantModerationAdjudicatedCaseRead grant, string boundary) : DbCommandInterceptor
    {
        private bool _grantFenced;
        public bool Observed { get; private set; }
        public bool HeldPrimaryTransaction { get; private set; }
        public bool ExpiredPrivateQuery { get; private set; }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!_grantFenced && eventData.Context is ExploreDbContext context
                && command.CommandText.TrimStart().StartsWith(
                    $"UPDATE {PreparationTable<ModerationAdjudicatedCaseReadGrant>(context)}", StringComparison.OrdinalIgnoreCase))
            {
                _grantFenced = true;
                if (boundary == "originalCustody")
                {
                    Observe(context);
                    fixture.Clock.UtcNow = pending.Original.Source.RetainUntil!.Value;
                }
            }
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (_grantFenced && !Observed && eventData.Context is ExploreDbContext context
                && PreparationReadsBody<ModerationNoticeDisclosureApproval>(context, command,
                    nameof(ModerationNoticeDisclosureApproval.FactsJson)))
            {
                Observe(context);
                if (boundary == "grantExpiry")
                    fixture.Clock.UtcNow = grant.ValidUntil;
                else if (boundary == "issuerRole")
                    await context.PlatformUserRoles.Where(row => row.UserId == fixture.Database.AdministratorId)
                        .ExecuteDeleteAsync(cancellationToken);
            }
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (boundary == "originalCustody" && Observed && eventData.Context is ExploreDbContext context
                && (PreparationReadsBody<ModerationNoticeSource>(context, command, nameof(ModerationNoticeSource.SnapshotJson))
                    || PreparationReadsBody<ModerationNoticeDisclosureApproval>(context, command,
                        nameof(ModerationNoticeDisclosureApproval.FactsJson))))
            {
                ExpiredPrivateQuery = true;
                throw new InvalidOperationException("Expired original custody must not be read for correction preparation.");
            }
            return ValueTask.FromResult(result);
        }

        private void Observe(ExploreDbContext context)
        {
            Observed = true;
            HeldPrimaryTransaction = context.Database.CurrentTransaction is not null;
        }

        private static bool PreparationReadsBody<T>(ExploreDbContext context, DbCommand command, string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var column = entity.FindProperty(property)!.GetColumnName(table)!;
            return command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains($"FROM {PreparationTable<T>(context)}", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(context.GetService<ISqlGenerationHelper>().DelimitIdentifier(column),
                    StringComparison.Ordinal);
        }

        private static string PreparationTable<T>(ExploreDbContext context)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        }
    }
}
