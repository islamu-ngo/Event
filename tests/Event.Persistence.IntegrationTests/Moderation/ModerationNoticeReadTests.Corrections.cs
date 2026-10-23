using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CorrectionObligationsRetainWithdrawnOriginalReviewerNeverCurrentReplacement(bool content)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: content);
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        await ReplaceCorrectionReviewerAsync(fixture);

        var obligations = await ReadCorrectionObligationsAsync(fixture, original);

        await AssertCorrectionObligationsAsync(fixture, original, obligations);
        await Assert.That(obligations!.Recipients.Any(row =>
            row.RecipientSubjectId == fixture.Database.UrgencyAssessorId)).IsFalse();
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That((await inspect.ModerationNoticeSources.SingleAsync(row =>
            row.EffectId == original.Effect.Id)).SnapshotHash).IsEqualTo(original.Source.SnapshotHash);
        await Assert.That(await inspect.NotificationIntents.AnyAsync()).IsFalse();
        await Assert.That(await inspect.OutboxMessages.AnyAsync()).IsFalse();
        await Assert.That((await inspect.ModerationEffectIntents.SingleAsync(row =>
            row.Id == original.Effect.Id)).CompletedAt).IsNull();
    }

    [Test]
    public async Task Nullable_original_source_projection_keeps_its_exact_native_writer_and_audience()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture, nullableTenantProjection: true);
        await Assert.That(original.Source.TenantId).IsNull();
        await Assert.That(original.Capture.WriterTenantId).IsEqualTo(fixture.Database.TenantId);

        await AssertCorrectionObligationsAsync(fixture, original,
            await ReadCorrectionObligationsAsync(fixture, original));
    }

    [Test]
    public async Task ExpiredOriginalReviewerQualificationDoesNotEraseHistoricalCorrectionObligation()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        fixture.Clock.UtcNow = original.Capture.Reviewers.Single().QualifiedUntil;
        await Assert.That(fixture.Clock.GetUtcNow() > original.Capture.ValidUntil).IsTrue();
        await using (var inspect = fixture.Database.Context())
        {
            inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            await Assert.That((await inspect.ModerationReviewerAssignments.SingleAsync())
                .IsQualifiedAt(fixture.Clock.GetUtcNow())).IsFalse();
        }

        await AssertCorrectionObligationsAsync(fixture, original,
            await ReadCorrectionObligationsAsync(fixture, original));
    }

    [Test]
    [Arguments("hash")]
    [Arguments("missing")]
    [Arguments("extra")]
    [Arguments("ambiguous")]
    public async Task CorrectionAudienceRejectsCorruptOrIncompleteOriginalCreatingLedger(string drift)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        if (drift == "ambiguous")
            await SeedCreatingNoticeLedgerAsync(fixture);
        else
            await fixture.MutateAsync(async context =>
            {
                if (drift == "hash")
                    await context.ModerationNoticeSources.Where(row => row.EffectId == original.Effect.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SnapshotHash, new string('0', 64)));
                else if (drift == "missing")
                    await context.ModerationNotices.Where(row => row.Id == original.ReviewerNotice.Id).ExecuteDeleteAsync();
                else
                    context.ModerationNotices.Add(ModerationNotice.Create(Guid.CreateVersion7(), original.Source,
                        ModerationNoticeAudience.Reviewer, (int)ModerationSubjectKind.Human,
                        fixture.Database.AdministratorId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable));
            });

        await Assert.That(await ReadCorrectionObligationsAsync(fixture, original)).IsNull();
    }

    [Test]
    public async Task OriginalRetentionBoundaryDeniesCorrectionWithoutExtendingCustody()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        fixture.Clock.UtcNow = original.Source.RetainUntil!.Value;

        await Assert.That(await ReadCorrectionObligationsAsync(fixture, original)).IsNull();

        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That((await inspect.ModerationNoticeSources.SingleAsync(row =>
            row.EffectId == original.Effect.Id)).RetainUntil).IsEqualTo(original.Source.RetainUntil);
        await Assert.That(await inspect.ModerationNoticeSources.CountAsync()).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Original_custody_body_is_not_read_at_expiry_even_when_a_fence_wait_crosses_the_boundary(bool crossAtFence)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var until = original.Source.RetainUntil!.Value;
        var probe = new CorrectionCustodyReadBoundary(fixture.Clock, original.Effect.Id, until, crossAtFence);
        if (!crossAtFence) fixture.Clock.UtcNow = until;

        await Assert.That(await ReadCorrectionObligationsAsync(fixture, original, probe)).IsNull();
        await Assert.That(probe.OriginalEffectFenced).IsTrue();
        await Assert.That(probe.CustodyBodyRead).IsFalse();
    }

    [Test]
    public async Task Original_recipient_read_crossing_the_custody_ceiling_cannot_prepare_a_correction()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var probe = new ExpireAfterOriginalRecipientRead(fixture.Clock, original.Source.RetainUntil!.Value);

        await Assert.That(await ReadCorrectionObligationsAsync(fixture, original, probe)).IsNull();

        await Assert.That(probe.Observed).IsTrue();
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.ModerationNoticeSources.CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task HistoricalCorrectionCoordinatesGrantNeitherWithdrawnNorReplacementReaderAccess()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        await ReplaceCorrectionReviewerAsync(fixture);
        var obligations = await ReadCorrectionObligationsAsync(fixture, original);
        await AssertCorrectionObligationsAsync(fixture, original, obligations);
        var reviewerObligation = obligations!.Recipients.Single(row => row.Audience == ModerationNoticeAudience.Reviewer);

        foreach (var caller in new[] { fixture.Database.ReviewerId, fixture.Database.UrgencyAssessorId })
        {
            await using var reader = fixture.Reader(caller);
            await Assert.That(await reader.DeliverDetail(reviewerObligation.OriginalNoticeId))
                .IsEqualTo(ModerationNoticeReadOutcome.NotFound);
            var emptyCoordinates = await reader.Detail(Guid.Empty);
            await Assert.That(emptyCoordinates.Outcome == ModerationNoticeReadOutcome.Allowed).IsFalse();
            await Assert.That(emptyCoordinates.Projection).IsNull();
        }
    }

    [Test]
    public async Task NativeRevokeCommitsOnlyNamedSanctionWithPendingUnnotifiedCorrectionAndNoOutgoingGraph()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        Guid acknowledgementId = Guid.CreateVersion7();
        await using (var reader = fixture.Reader(fixture.Database.SubjectId))
            await Assert.That((await reader.Acknowledge(original.SubjectNotice.Id,
                acknowledgementId, original.Source.SnapshotHash)).IsSuccess).IsTrue();

        Guid otherSanctionId;
        ModerationOperation operation;
        await using (var seed = fixture.Database.Context())
        {
            seed.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var decision = await seed.ModerationDecisions.SingleAsync(row => row.Id == fixture.DecisionId);
            var other = ModerationDecision.Propose(Guid.CreateVersion7(), decision.CaseId, decision.Subject,
                decision.Issuer, decision.IssuingScope, decision.Scope, decision.Rule, decision.Capabilities,
                decision.StartsAt, decision.EndsAt, decision.ReviewAt, 1, Now);
            other.RecordResponse(1, Now);
            other.AcceptFinding(decision.FindingId!.Value, 2, Now);
            other.Approve(SubjectReference.Create(ModerationSubjectKind.Human, fixture.Database.ReviewerId,
                fixture.Database.Instance), decision.Scope, 3, Now);
            var independent = other.Activate(Guid.CreateVersion7(), 4, Now);
            otherSanctionId = independent.Id;
            seed.AddRange(other, independent);
            await seed.SaveChangesAsync();
            operation = ModerationOperation.Transition(ModerationOperationKind.Revoke, decision.Subject, decision.Scope,
                Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, original.Effect.SanctionRevision!.Value,
                original.Effect.PolicyRevision, original.Effect.SanctionId!.Value);
        }
        await ReplaceCorrectionReviewerAsync(fixture);
        await using var context = fixture.Database.Context();
        var service = CorrectionService(context, fixture);

        var result = await service.ExecuteAsync(operation);

        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        var replay = await service.ExecuteAsync(operation);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(replay.Revision).IsEqualTo(result.Revision);
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var revoked = await inspect.ModerationSanctions.SingleAsync(row => row.Id == original.Effect.SanctionId);
        await Assert.That(revoked.State).IsEqualTo(ModerationSanctionState.Revoked);
        await Assert.That(revoked.Revision).IsEqualTo(original.Effect.SanctionRevision!.Value + 1);
        var independentAfter = await inspect.ModerationSanctions.SingleAsync(row => row.Id == otherSanctionId);
        await Assert.That(independentAfter.State).IsEqualTo(ModerationSanctionState.Active);
        await Assert.That(independentAfter.Revision).IsEqualTo(1L);
        var effect = await inspect.ModerationEffectIntents.SingleAsync(row => row.Id == operation.EffectId);
        var source = await inspect.ModerationNoticeSources.SingleAsync(row => row.EffectId == operation.EffectId);
        var pending = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
        await Assert.That(source.Matches(effect)).IsTrue();
        await Assert.That(pending.Matches(effect)).IsTrue();
        await Assert.That(pending.HasNoSubstantiveWork).IsFalse();
        await Assert.That(pending.Notice).IsNull();
        await Assert.That(pending.Finding).IsNull();
        await Assert.That(pending.Reviewers.IsEmpty).IsTrue();
        await AssertCorrectionObligationsAsync(fixture, original, pending.PendingCorrection);
        var defaultRecipients = pending with
        {
            PendingCorrection = pending.PendingCorrection! with { Recipients = default }
        };
        await Assert.That(defaultRecipients.Matches(effect)).IsFalse();
        var defaultDisposition = pending with
        {
            PendingCorrection = pending.PendingCorrection! with
            {
                Recipients = [.. pending.PendingCorrection!.Recipients.Select(row => row with { Disposition = default })]
            }
        };
        await Assert.That(defaultDisposition.Matches(effect)).IsFalse();
        await Assert.That(source.RetainUntil).IsEqualTo(original.Source.RetainUntil);
        await Assert.That((await inspect.ModerationNoticeSources.SingleAsync(row =>
            row.EffectId == original.Effect.Id)).SnapshotHash).IsEqualTo(original.Source.SnapshotHash);
        await Assert.That(effect.CompletedAt).IsNull();
        var correctionOccurrences = await inspect.ModerationNotices.Where(row => row.EffectId == operation.EffectId).ToArrayAsync();
        await Assert.That(correctionOccurrences.Length).IsEqualTo(original.Notices.Length);
        await Assert.That(correctionOccurrences.All(row => row.DeliveryDisposition == ModerationNoticeDeliveryDisposition.RecipientUnavailable
            && row.NativeIntentId is null)).IsTrue();
        foreach (var occurrence in correctionOccurrences.Where(row => row.RecipientSubjectKind == (int)ModerationSubjectKind.Human))
        {
            await using var reader = fixture.Reader(occurrence.RecipientSubjectId);
            await Assert.That(await reader.DeliverDetail(occurrence.Id)).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
            await Assert.That((await reader.Acknowledge(occurrence.Id, Guid.CreateVersion7(), source.SnapshotHash)).IsSuccess).IsFalse();
        }
        await Assert.That(await inspect.NotificationIntents.AnyAsync()).IsFalse();
        await Assert.That(await inspect.Notifications.AnyAsync()).IsFalse();
        await Assert.That(await inspect.NotificationDeliveries.AnyAsync()).IsFalse();
        await Assert.That(await inspect.EmailDispatchOutbox.AnyAsync()).IsFalse();
        await Assert.That(await inspect.OutboxMessages.CountAsync()).IsEqualTo(1);
        var acknowledged = await inspect.Set<ModerationNoticeNotificationReceipt>().SingleAsync();
        await Assert.That(acknowledged.Id).IsEqualTo(acknowledgementId);
        await Assert.That(acknowledged.EffectId).IsEqualTo(original.Effect.Id);
        await Assert.That(acknowledged.NoticeId).IsEqualTo(original.SubjectNotice.Id);
        await Assert.That(acknowledged.RetainUntil).IsEqualTo(original.Source.RetainUntil!.Value);
        await Assert.That(context.Database.CurrentTransaction).IsNull();

        // Receipt replay must prove every pending obligation, not only the subject occurrence.
        await fixture.MutateAsync(async tamper => await tamper.ModerationNotices
            .Where(row => row.EffectId == operation.EffectId && row.Audience == ModerationNoticeAudience.Reviewer)
            .ExecuteDeleteAsync());
        await Assert.That((await service.ExecuteAsync(operation)).Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That((await inspect.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == operation.EffectId))
            .CompletedAt).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Missing_pending_recipient_after_flush_rolls_back_revocation_and_its_entire_prefix(bool colocated)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        await using var primary = fixture.Database.Context();
        primary.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await using var sharedPrivacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(primary) : null;
        var decision = await primary.ModerationDecisions.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.DecisionId);
        var operation = ModerationOperation.Transition(ModerationOperationKind.Revoke, decision.Subject, decision.Scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, original.Effect.SanctionRevision!.Value,
            original.Effect.PolicyRevision, original.Effect.SanctionId!.Value);
        var probe = new RemovePendingReviewerAfterSave(operation.EffectId);
        await using var context = fixture.Database.Context(probe);

        var result = await CorrectionService(context, fixture, sharedPrivacy).ExecuteAsync(operation);

        await Assert.That(probe.Observed).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That((await inspect.ModerationSanctions.SingleAsync(row => row.Id == original.Effect.SanctionId)).State)
            .IsEqualTo(ModerationSanctionState.Active);
        await Assert.That(await inspect.ModerationEffectIntents.AnyAsync(row => row.Id == operation.EffectId)).IsFalse();
        await Assert.That(await inspect.ModerationNoticeSources.AnyAsync(row => row.EffectId == operation.EffectId)).IsFalse();
        await Assert.That(await inspect.ModerationNotices.AnyAsync(row => row.EffectId == operation.EffectId)).IsFalse();
        await Assert.That(await inspect.ModerationOperationReceipts.AnyAsync(row => row.Id == operation.OperationId)).IsFalse();
        await Assert.That(await inspect.OutboxMessages.AnyAsync(row => row.Id == operation.EffectId)).IsFalse();
    }

    // This independently seeded retained ledger is not measured evidence of the activation writer.
    private static async Task<CorrectionCreatingLedger> SeedCreatingNoticeLedgerAsync(NoticeDatabase fixture,
        bool nullableTenantProjection = false)
    {
        await using var context = fixture.Database.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var templateEffect = await context.ModerationEffectIntents.SingleAsync(row => row.Id == fixture.EffectId);
        await Assert.That(templateEffect.OperationKind).IsEqualTo((int)ModerationOperationKind.RecordResponse);
        var templateSource = await context.ModerationNoticeSources.SingleAsync(row => row.EffectId == templateEffect.Id);
        var template = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(templateSource.SnapshotJson)!;
        var capturedAt = fixture.Clock.GetUtcNow().AddSeconds(1);
        var effect = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            templateEffect.DecisionId, templateEffect.DecisionRevision, templateEffect.SanctionId, templateEffect.SanctionRevision,
            templateEffect.TrustDomainId, templateEffect.ScopeKind, templateEffect.ScopeId,
            templateEffect.SubjectKind, templateEffect.SubjectId, (int)ModerationOperationKind.Activate,
            templateEffect.PolicyRevision, templateEffect.QualificationRevision, capturedAt, null, null);
        var approval = ModerationNoticeDisclosureApproval.Approve(fixture.Approval.Intent with
        {
            ApprovalId = Guid.CreateVersion7(), EffectId = effect.Id
        }, fixture.Database.HostId, effect.TrustDomainId, fixture.Database.TenantId,
            fixture.Database.AdministratorId, 1, capturedAt);
        var capture = template with
        {
            Notice = template.Notice! with
            {
                EffectId = effect.Id, OperationId = effect.OperationId,
                OperationKind = (int)ModerationOperationKind.Activate, CapturedAt = capturedAt, ApprovalId = approval.Id
            }
        };
        var source = ModerationNoticeSource.Capture(effect, nullableTenantProjection ? null : fixture.Database.TenantId,
            JsonSerializer.Serialize(capture), templateSource.RetainUntil);
        var notices = new List<ModerationNotice>
        {
            ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Subject,
                effect.SubjectKind, effect.SubjectId, null, effect.SubjectKind == (int)ModerationSubjectKind.Content
                    ? ModerationNoticeDeliveryDisposition.NoHumanProjection : ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable),
            ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Reviewer,
                (int)ModerationSubjectKind.Human, fixture.Database.ReviewerId, null,
                ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable)
        };
        notices.AddRange(capture.ContentRepresentatives.Select(proof => ModerationNotice.Create(
            Guid.CreateVersion7(), source, ModerationNoticeAudience.ContentRepresentative,
            (int)ModerationSubjectKind.Human, proof.HumanId, null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable)));
        context.AddRange(effect, approval, source);
        context.AddRange(notices);
        await context.SaveChangesAsync();
        await Assert.That(source.Matches(effect)).IsTrue();
        await Assert.That(capture.Matches(effect)).IsTrue();
        fixture.Clock.UtcNow = capturedAt.AddSeconds(1);
        return new(effect, source, capture, notices.ToArray());
    }

    private static async Task<ModerationNoticeCorrectionObligations?> ReadCorrectionObligationsAsync(
        NoticeDatabase fixture, CorrectionCreatingLedger original, IInterceptor? interceptor = null)
    {
        await using var context = fixture.Database.Context(interceptor);
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var decision = await context.ModerationDecisions.SingleAsync(row => row.Id == original.Effect.DecisionId);
        var now = fixture.Clock.GetUtcNow();
        var sanction = await context.ModerationSanctions.SingleAsync(row => row.Id == original.Effect.SanctionId);
        sanction.Revoke(sanction.Revision, now);
        await context.SaveChangesAsync();
        var revoke = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, decision.Revision,
            original.Effect.SanctionId, original.Effect.SanctionRevision!.Value + 1,
            original.Effect.TrustDomainId, original.Effect.ScopeKind, original.Effect.ScopeId,
            original.Effect.SubjectKind, original.Effect.SubjectId, (int)ModerationOperationKind.Revoke,
            original.Effect.PolicyRevision, original.Effect.QualificationRevision, now, null, null);
        var obligations = await ModerationNoticeCorrectionAudience.ReadAsync(context, revoke, decision, fixture.Clock, CancellationToken.None);
        await Assert.That(context.Database.CurrentTransaction).IsEqualTo(transaction);
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        return obligations;
    }

    private sealed class CorrectionCustodyReadBoundary(TestClock clock, Guid originalEffectId,
        DateTimeOffset until, bool crossAtFence) : DbCommandInterceptor
    {
        public bool OriginalEffectFenced { get; private set; }
        public bool CustodyBodyRead { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context)
            {
                var entity = context.Model.FindEntityType(typeof(ModerationEffectIntent))!;
                var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
                if (command.CommandText.TrimStart().StartsWith($"UPDATE {table}", StringComparison.OrdinalIgnoreCase)
                    && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid id && id == originalEffectId))
                {
                    OriginalEffectFenced = true;
                    if (crossAtFence) clock.UtcNow = until;
                }
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context)
            {
                var entity = context.Model.FindEntityType(typeof(ModerationNoticeSource))!;
                var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
                var column = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
                    entity.FindProperty(nameof(ModerationNoticeSource.SnapshotJson))!.GetColumnName(store)!);
                if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    && command.CommandText.Contains(column, StringComparison.Ordinal))
                {
                    CustodyBodyRead = true;
                    if (clock.GetUtcNow() >= until)
                        throw new InvalidOperationException("Expired original custody cannot be read to prepare a correction.");
                }
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RemovePendingReviewerAfterSave(Guid effectId) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is ExploreDbContext context
                && context.ModerationNotices.Local.Any(row => row.EffectId == effectId))
            {
                Observed = true;
                await context.ModerationNotices.Where(row => row.EffectId == effectId
                    && row.Audience == ModerationNoticeAudience.Reviewer).ExecuteDeleteAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class ExpireAfterOriginalRecipientRead(TestClock clock, DateTimeOffset until) : DbCommandInterceptor
    {
        public bool Observed { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is { } context)
            {
                var entity = context.Model.FindEntityType(typeof(ModerationNotice))!;
                var helper = context.GetService<ISqlGenerationHelper>();
                var table = helper.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
                var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
                var disposition = helper.DelimitIdentifier(entity.FindProperty(nameof(ModerationNotice.DeliveryDisposition))!
                    .GetColumnName(store)!);
                if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    && command.CommandText.Contains($"FROM {table}", StringComparison.Ordinal)
                    && command.CommandText.Contains(disposition, StringComparison.Ordinal))
                {
                    Observed = true;
                    clock.UtcNow = until;
                }
            }
            return ValueTask.FromResult(result);
        }
    }

}
