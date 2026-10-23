using System.Data;
using System.Data.Common;
using System.Text;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments("adoption")]
    [Arguments("source-receipt")]
    [Arguments("adoption-receipt")]
    public async Task NativeIntakeNeedsAnActuallyAdoptedBudgetAndOriginalWriterReceipts(string missing)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var issued = await database.IssueAsync();
        await using (var writer = database.Context())
        {
            if (missing == "adoption")
                await writer.Set<ModerationOperationalBudgetAdoption>().ExecuteDeleteAsync();
            else
                await writer.ModerationQualificationMutationReceipts.Where(row => row.SourceKind ==
                    (missing == "source-receipt" ? ModerationQualificationSourceKind.OperationalBudgetSource
                        : ModerationQualificationSourceKind.OperationalBudgetAdoption)).ExecuteDeleteAsync();
        }
        await using var context = database.Context();
        var request = BudgetSubmission(issued, database.Request().Material);
        await Assert.That((await Committer(context, database).SubmitAsync(request, default)).Outcome)
            .IsEqualTo(ModerationCaseIntakeCommitOutcome.Unavailable);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationCaseNarratives.AnyAsync()).IsFalse();
        await Assert.That((await context.ModerationCaseTargetReceipts.SingleAsync()).ConsumedCaseId).IsNull();
    }

    [Test]
    public async Task DistinctNativeOccurrencesExhaustTheSameSubjectBudgetWithoutAttributingGuilt()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        await ReplaceNativeBudgetAsync(database, 1);
        var first = await IssueBudgetOccurrenceAsync(database, "Original occurrence.", "First account.");
        var second = await IssueBudgetOccurrenceAsync(database, "A separate occurrence.", "Second account.");
        await using var context = database.Context();
        var committer = Committer(context, database);
        await Assert.That((await committer.SubmitAsync(
            BudgetSubmission(first, Encoding.UTF8.GetBytes("First account.")), default)).Outcome)
            .IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        await Assert.That((await committer.SubmitAsync(
            BudgetSubmission(second, Encoding.UTF8.GetBytes("Second account.")), default)).Outcome)
            .IsEqualTo(ModerationCaseIntakeCommitOutcome.Unavailable);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationSanctions.AnyAsync()).IsFalse();
        await Assert.That((await context.ModerationCaseTargetReceipts.SingleAsync(row => row.Id == second.Receipt.Id))
            .ConsumedCaseId).IsNull();
    }

    [Test]
    public async Task MissingSuccessfulAcceptanceCannotBeMistakenForUnusedBudgetCapacity()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        await ReplaceNativeBudgetAsync(database, 1);
        var first = await IssueBudgetOccurrenceAsync(database, "Original occurrence.", "Original account.");
        await using var context = database.Context();
        var accepted = await Committer(context, database).SubmitAsync(
            BudgetSubmission(first, Encoding.UTF8.GetBytes("Original account.")), default);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        context.ModerationTrustDomainId = database.TrustId;
        await context.ModerationCaseIntakeAcknowledgements.Where(row => row.Id == accepted.Acknowledgement!.Id).ExecuteDeleteAsync();
        var next = await IssueBudgetOccurrenceAsync(database, "Another occurrence.", "Another account.");
        await Assert.That((await Committer(context, database).SubmitAsync(
            BudgetSubmission(next, Encoding.UTF8.GetBytes("Another account.")), default)).Outcome)
            .IsEqualTo(ModerationCaseIntakeCommitOutcome.Unavailable);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.AnyAsync()).IsFalse();
        await Assert.That((await context.ModerationCaseTargetReceipts.SingleAsync(row => row.Id == next.Receipt.Id))
            .ConsumedCaseId).IsNull();
    }

    [Test]
    public async Task ReplacingTheOperatorBudgetCannotResetAlreadyAcceptedOccurrenceUsage()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        await ReplaceNativeBudgetAsync(database, 1);
        var first = await IssueBudgetOccurrenceAsync(database, "Original occurrence.", "Original account.");
        await using var context = database.Context();
        var accepted = await Committer(context, database).SubmitAsync(
            BudgetSubmission(first, Encoding.UTF8.GetBytes("Original account.")), default);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        var replacement = await ReplaceNativeBudgetAsync(database, 1);
        await Assert.That(replacement.SourceId != accepted.Acknowledgement!.BudgetSourceId).IsTrue();
        var another = await IssueBudgetOccurrenceAsync(database, "Another occurrence.", "Another account.");
        await Assert.That((await Committer(context, database).SubmitAsync(
            BudgetSubmission(another, Encoding.UTF8.GetBytes("Another account.")), default)).Outcome)
            .IsEqualTo(ModerationCaseIntakeCommitOutcome.Unavailable);
        context.ModerationTrustDomainId = database.TrustId;
        var retained = await context.ModerationCaseIntakeAcknowledgements.SingleAsync();
        await Assert.That(retained.Id).IsEqualTo(accepted.Acknowledgement.Id);
        await Assert.That(retained.BudgetSourceId).IsEqualTo(accepted.Acknowledgement.BudgetSourceId);
    }

    [Test]
    public async Task NewQualifiedMaterialForTheSameOccurrenceDoesNotRenewItsBudgetTimestamp()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var budget = await ReplaceNativeBudgetAsync(database, 1);
        var first = await IssueBudgetOccurrenceAsync(database, "One occurrence.", "Original account.");
        await using var context = database.Context();
        var accepted = await Committer(context, database).SubmitAsync(
            BudgetSubmission(first, Encoding.UTF8.GetBytes("Original account.")), default);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        var originalAt = accepted.Acknowledgement!.AcceptedAt;
        database.Clock.Now = database.Clock.Now.AddMinutes(30);
        var second = await IssueBudgetOccurrenceAsync(database, "One occurrence.", "Independent additional material.");
        await Assert.That(second.Receipt.OccurrenceId).IsEqualTo(first.Receipt.OccurrenceId);
        var duplicate = await Committer(context, database).SubmitAsync(
            BudgetSubmission(second, Encoding.UTF8.GetBytes("Independent additional material.")), default);
        await Assert.That(duplicate.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        await Assert.That(duplicate.Acknowledgement!.AcceptedAt > originalAt).IsTrue();
        database.Clock.Now = originalAt.AddHours(1).AddSeconds(1);
        var third = await IssueBudgetOccurrenceAsync(database, "Another occurrence.", "A later account.");
        await Assert.That((await Committer(context, database).SubmitAsync(
            BudgetSubmission(third, Encoding.UTF8.GetBytes("A later account.")), default)).Outcome)
            .IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(3);
        var original = await context.ModerationCaseIntakeAcknowledgements.SingleAsync(row => row.Id == accepted.Acknowledgement.Id);
        await Assert.That(original.AcceptedAt).IsEqualTo(originalAt);
        await Assert.That(original.BudgetSourceId).IsEqualTo(budget.SourceId);
        await Assert.That(original.BudgetAdoptionId).IsEqualTo(budget.AdoptionId);
    }

    [Test]
    public async Task InvalidTargetAuthorizationCannotSpendTheSuccessfulSubmissionBudget()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        await ReplaceNativeBudgetAsync(database, 1);
        var issued = await database.IssueAsync();
        var request = BudgetSubmission(issued, database.Request().Material);
        var changed = issued.Token.ToArray();
        changed[0] ^= 1;
        await using var context = database.Context();
        var committer = Committer(context, database);
        await Assert.That((await committer.SubmitAsync(request with { PresentedToken = changed }, default)).Outcome)
            .IsEqualTo(ModerationCaseIntakeCommitOutcome.Unavailable);
        await Assert.That((await committer.SubmitAsync(request, default)).Outcome)
            .IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("expiry")]
    [Arguments("receipt")]
    public async Task BudgetLossAtTheActualCompleteFlushRollsBackIntakeAndItsOnlyCharge(string loss)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var budget = await ReplaceNativeBudgetAsync(database, 1, TimeSpan.FromSeconds(20));
        var issued = await database.IssueAsync();
        var checkpoint = new ReceiptSavedCheckpoint(async context =>
        {
            await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
            await Assert.That(await context.ModerationCaseIntakeAcknowledgements.AnyAsync()).IsTrue();
            if (loss == "expiry") database.Clock.Now = budget.ValidUntil;
            else
                await context.ModerationQualificationMutationReceipts.Where(row =>
                    row.SourceKind == ModerationQualificationSourceKind.OperationalBudgetSource
                    && row.SourceId == budget.SourceId).ExecuteDeleteAsync();
        });
        await using (var context = database.Context(checkpoint))
        {
            await Assert.That((await Committer(context, database).SubmitAsync(
                BudgetSubmission(issued, database.Request().Material), default)).Outcome)
                .IsEqualTo(ModerationCaseIntakeCommitOutcome.Unavailable);
            await Assert.That(checkpoint.ObservedReceiptSave).IsTrue();
        }
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseIntakeAcknowledgements.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseNarratives.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCases.AnyAsync()).IsFalse();
        await Assert.That((await verify.ModerationCaseTargetReceipts.SingleAsync()).ConsumedCaseId).IsNull();
        await Assert.That(await verify.ModerationQualificationMutationReceipts.AnyAsync(row =>
            row.SourceId == budget.SourceId && row.SourceKind == ModerationQualificationSourceKind.OperationalBudgetSource)).IsTrue();
    }

    [Test]
    [Arguments("current")]
    [Arguments("expiry")]
    [Arguments("receipt")]
    public async Task FinalDisclosureAdvancementCannotOutliveTheBudgetThatAcceptedIntake(string boundary)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var budget = await ReplaceNativeBudgetAsync(database, 1, TimeSpan.FromSeconds(20));
        var issued = await database.IssueAsync();
        long? before;
        await using (var original = database.Context())
            before = await original.Set<EventDiscoveryRevision>().AsNoTracking()
                .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
        var finalization = new BudgetDisclosureFinalizationCheckpoint(database.TenantId, before, async context =>
        {
            await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
            if (boundary == "expiry") database.Clock.Now = budget.ValidUntil;
            if (boundary == "receipt")
                await context.ModerationQualificationMutationReceipts.Where(row =>
                    row.SourceKind == ModerationQualificationSourceKind.OperationalBudgetSource
                    && row.SourceId == budget.SourceId).ExecuteDeleteAsync();
        });
        var saved = new ReceiptSavedCheckpoint(context =>
        {
            context.DisclosureMutations.Enlist([database.TenantId]);
            finalization.Armed = true;
            return Task.CompletedTask;
        });
        await using (var context = database.Context(saved, finalization))
        {
            var result = await Committer(context, database).SubmitAsync(
                BudgetSubmission(issued, database.Request().Material), default);
            await Assert.That(result.Outcome).IsEqualTo(boundary == "current"
                ? ModerationCaseIntakeCommitOutcome.Accepted : ModerationCaseIntakeCommitOutcome.Unavailable);
            await Assert.That(saved.ObservedReceiptSave).IsTrue();
            await Assert.That(finalization.ObservedAdvancement).IsTrue();
        }
        await using var verify = database.Context();
        var accepted = boundary == "current";
        await Assert.That(await verify.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(accepted ? 1 : 0);
        await Assert.That(await verify.ModerationCaseNarratives.CountAsync()).IsEqualTo(accepted ? 1 : 0);
        await Assert.That(await verify.ModerationCases.CountAsync()).IsEqualTo(accepted ? 1 : 0);
        await Assert.That((await verify.ModerationCaseTargetReceipts.SingleAsync()).ConsumedCaseId.HasValue)
            .IsEqualTo(accepted);
        var after = await verify.Set<EventDiscoveryRevision>().AsNoTracking()
            .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
        await Assert.That(after).IsEqualTo(accepted ? before.GetValueOrDefault() + 1 : before);
        await Assert.That(await verify.ModerationQualificationMutationReceipts.AnyAsync(row =>
            row.SourceId == budget.SourceId && row.SourceKind == ModerationQualificationSourceKind.OperationalBudgetSource))
            .IsTrue();
    }

    private sealed class BudgetDisclosureFinalizationCheckpoint(
        Guid tenantId, long? originalEpoch, Func<ExploreDbContext, Task> afterAdvancement) : DbCommandInterceptor
    {
        private bool observing;
        public bool Armed { get; set; }
        public bool ObservedAdvancement { get; private set; }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed || observing || ObservedAdvancement || eventData.Context is not ExploreDbContext context)
                return result;
            observing = true;
            try
            {
                var epoch = await context.Set<EventDiscoveryRevision>().AsNoTracking()
                    .Where(row => row.TenantId == tenantId).Select(row => (long?)row.DisclosureEpoch)
                    .SingleOrDefaultAsync(cancellationToken);
                if (epoch > originalEpoch.GetValueOrDefault())
                {
                    ObservedAdvancement = true;
                    await afterAdvancement(context);
                }
            }
            finally { observing = false; }
            return result;
        }
    }

    private static ModerationCaseAccessRequest BudgetSubmission(
        ModerationCaseIssuedReceipt receipt, ReadOnlyMemory<byte> material) => new(
            ModerationCaseAccessPurpose.SubmitAllegation, Guid.Empty, Guid.CreateVersion7(), 0,
            receipt.Receipt.Id, receipt.Token)
        { Narrative = material };

    private static async Task<ModerationCaseIssuedReceipt> IssueBudgetOccurrenceAsync(
        NativeDatabase database, string occurrence, string material)
    {
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = database.Owner(context);
        var issued = await owner.IssueSubmissionAsync(database.Request(material: material) with
        { OccurrenceMaterial = Encoding.UTF8.GetBytes(occurrence) }, default);
        await Assert.That(issued).IsNotNull();
        await context.SaveChangesAsync();
        await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
        await transaction.CommitAsync();
        return issued!;
    }

    private static async Task<(Guid SourceId, Guid AdoptionId, DateTimeOffset ValidUntil)> ReplaceNativeBudgetAsync(
        NativeDatabase database, int maximum, TimeSpan? validity = null)
    {
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        ModerationOperationalBudgetSource previous;
        ModerationHostBinding host;
        long revision;
        await using (var reader = database.Context())
        {
            previous = await reader.Set<ModerationOperationalBudgetSource>().OrderByDescending(row => row.WriterGeneration).FirstAsync();
            host = await reader.ModerationHostBindings.SingleAsync();
            revision = (await reader.ModerationQualificationEpochs.SingleAsync()).Revision;
        }
        var source = new RecordModerationOperationalBudgetSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = AuthorityScope.Tenant(
                AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(database.TrustId),
                    host.SolutionId), host.InstanceId), database.TenantId),
            ExpectedQualificationRevision = revision,
            SourceId = Guid.CreateVersion7(),
            OperatorHumanId = previous.OperatorHumanId,
            Terms = new(ModerationSubjectKind.Human, ModerationOperationalAction.SubmitReport, maximum, TimeSpan.FromHours(1)),
            ValidUntil = database.Clock.Now.Add(validity ?? TimeSpan.FromHours(2))
        };
        await WriteAsync(database.Caller, source);
        var adoptionId = Guid.CreateVersion7();
        await WriteAsync(new NativeCaller(source.OperatorHumanId), new AdoptModerationOperationalBudgetSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = source.Target,
            ExpectedQualificationRevision = revision + 1,
            SourceId = source.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = revision + 1,
            AdoptionId = adoptionId,
            ValidUntil = source.ValidUntil
        });
        return (source.SourceId, adoptionId, source.ValidUntil);

        async Task WriteAsync(NativeCaller caller, ModerationQualificationMutation mutation)
        {
            await using var context = database.Context();
            context.CurrentUserService = caller;
            var writer = NativeNarrativePolicyWriter(context, database, caller, privacy.Authority(context, database.Clock));
            await Assert.That((await writer.ExecuteAsync(mutation)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
    }
}
