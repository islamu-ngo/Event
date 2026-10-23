using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineReleaseTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeTenantAttributionSupportsFreshReviewWithoutRevivingDisclosure(bool storedFile)
    {
        await using var fixture = await Fixture.CreateAsync(storedFile: storedFile, prepare: false, nativeAttribution: true);
        await fixture.RevokeAttributionDisclosureAsync();
        await fixture.PrepareAsync(fixture.CauseId);
        var result = await fixture.ReleaseAsync(fixture.ReleaseIntent());
        await Assert.That(result.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Known);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var owner = await reader.ModerationCases.SingleAsync(row => row.Id == fixture.CaseId);
        await Assert.That(owner.Scope).IsEqualTo(fixture.Database.Scope);
        await Assert.That(owner.AttributionSourceId).IsEqualTo((Guid?)fixture.AttributionId);
        var grant = await reader.ModerationCaseReadGrants.SingleAsync(row => row.Id == fixture.AttributionGrantId);
        await Assert.That(grant.IsCurrentAt(fixture.Clock.Now)).IsFalse();
        await Assert.That(await reader.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.Set<EventResourceModerationQuarantineReview>().CountAsync()).IsEqualTo(4);
        await Assert.That((await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId))
            .ModerationQuarantineCount).IsEqualTo(1);
        await Assert.That((await reader.EventResourceModerationQuarantines.SingleAsync(row => row.Id == fixture.OtherCauseId))
            .ReleaseId).IsNull();
    }

    [Test]
    public async Task TenantAttributionOfAnotherResourceInTheSameEventCannotAuthorizeReview()
    {
        await using var fixture = await Fixture.CreateAsync(
            prepare: false, nativeAttribution: true, attributeSiblingResource: true);
        var intent = fixture.Intent(fixture.Author, ResourceSafetyReviewStep.ProposePurpose, null) with
        {
            RequiredApprovals = 1, MaximumEvidenceLifetime = TimeSpan.FromMinutes(10)
        };
        await Assert.That((await fixture.ReviewAsync(fixture.Author, intent)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.Set<EventResourceModerationQuarantineReview>().AnyAsync()).IsFalse();
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    public async Task GenuineExpectedHeadRenewalSupportsANewIndependentSafetyPurpose()
    {
        await using var fixture = await Fixture.CreateAsync(prepare: false, nativeAttribution: true);
        var opening = fixture.AttributionId;
        await fixture.RenewExpiredAttributionAsync();
        await fixture.RevokeAttributionDisclosureAsync();
        await fixture.PrepareAsync(fixture.CauseId);
        await Assert.That((await fixture.ReleaseAsync(fixture.ReleaseIntent())).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Known);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var head = await reader.ModerationCaseAttributionHeads.SingleAsync(row => row.CaseId == fixture.CaseId);
        await Assert.That(head.OpeningSourceId).IsEqualTo(opening);
        await Assert.That(head.SourceId).IsEqualTo(fixture.AttributionId);
        await Assert.That(head.Generation).IsEqualTo(2L);
        await Assert.That(await reader.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(2);
        await Assert.That(await reader.ModerationCaseAttributionRenewalReceipts.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("resource")]
    [Arguments("material")]
    [Arguments("attribution")]
    [Arguments("reviewer")]
    [Arguments("policy")]
    [Arguments("deadline")]
    public async Task TenantAttributionDoesNotAuthorizeSubstitutedFreshReview(string defect)
    {
        await using var fixture = await Fixture.CreateAsync(prepare: false, nativeAttribution: true);
        var intent = fixture.Intent(fixture.Author, ResourceSafetyReviewStep.ProposePurpose, null) with
        {
            RequiredApprovals = 1, MaximumEvidenceLifetime = TimeSpan.FromMinutes(10)
        };
        if (defect == "resource") intent = intent with { ResourceId = Guid.CreateVersion7() };
        else if (defect == "material") intent = intent with { MaterialVersionId = Guid.CreateVersion7() };
        else if (defect == "reviewer") intent = intent with { ReviewerAssignmentId = fixture.Assignments[fixture.Approver] };
        else if (defect == "deadline") intent = intent with { ValidUntil = fixture.AttributionDeadline.AddTicks(10) };
        else
        {
            await using var mutation = fixture.Context();
            mutation.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            if (defect == "attribution")
                await mutation.ModerationCaseAttributionHeads.Where(row => row.CaseId == fixture.CaseId)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.SubjectId, fixture.Database.SubjectId));
            else
                await mutation.ModerationQualificationPolicyAdoptions.Where(row => row.Id == fixture.PolicyAdoptionId)
                    .ExecuteDeleteAsync();
        }
        await Assert.That((await fixture.ReviewAsync(fixture.Author, intent)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await using var reader = fixture.Context();
        await Assert.That(await reader.Set<EventResourceModerationQuarantineReview>().AnyAsync()).IsFalse();
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    [Arguments("head")]
    [Arguments("source")]
    [Arguments("qualifier")]
    [Arguments("material")]
    [Arguments("reviewer")]
    [Arguments("policy")]
    [Arguments("deadline")]
    [Arguments("purpose")]
    public async Task NativeAttributionLossAfterReleaseFlushRollsBackCauseAndReceipt(string defect)
    {
        await using var fixture = await Fixture.CreateAsync(nativeAttribution: true);
        var checkpoint = new AttributionSafetyCheckpoint(async context =>
        {
            context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            fixture.Clock.Now = fixture.Clock.Now.AddSeconds(1);
            switch (defect)
            {
                case "head":
                    await context.ModerationCaseAttributionHeads.Where(row => row.CaseId == fixture.CaseId)
                        .ExecuteUpdateAsync(set => set.SetProperty(row => row.SubjectId, fixture.Database.SubjectId));
                    break;
                case "source":
                    await context.ModerationCaseAttributionSources.Where(row => row.Id == fixture.AttributionId)
                        .ExecuteUpdateAsync(set => set.SetProperty(row => row.ValidUntil, fixture.Clock.Now));
                    break;
                case "qualifier":
                    await context.ModerationCaseAttributionSources.Where(row => row.Id == fixture.AttributionId)
                        .ExecuteUpdateAsync(set => set.SetProperty(row => row.QualifiedByHumanId, fixture.Database.SubjectId));
                    break;
                case "material":
                    await context.ModerationCaseTargetSources.Where(row => row.Id == fixture.AttributionMaterialId)
                        .ExecuteUpdateAsync(set => set.SetProperty(row => row.NativeTargetId, Guid.CreateVersion7()));
                    break;
                case "reviewer":
                    await context.PlatformUserRoles.Where(row => row.UserId == fixture.Approver).ExecuteDeleteAsync();
                    break;
                case "policy":
                    await context.ModerationQualificationPolicyAdoptions.Where(row => row.Id == fixture.PolicyAdoptionId)
                        .ExecuteDeleteAsync();
                    break;
                case "deadline": fixture.Clock.Now = fixture.AttributionDeadline; break;
                case "purpose":
                    await context.Set<EventResourceModerationQuarantineReview>().Where(row => row.Id == fixture.PurposeId)
                        .ExecuteUpdateAsync(set => set.SetProperty(row => row.ValidUntil, fixture.Clock.Now));
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(defect));
            }
        });
        await Assert.That((await fixture.ReleaseAsync(fixture.ReleaseIntent(), checkpoint)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await Assert.That(checkpoint.Observed).IsTrue();
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeAttributionLossAfterReviewFlushRollsBackTheFreshPurpose(bool expire)
    {
        await using var fixture = await Fixture.CreateAsync(prepare: false, nativeAttribution: true);
        var intent = fixture.Intent(fixture.Author, ResourceSafetyReviewStep.ProposePurpose, null) with
        {
            RequiredApprovals = 1, MaximumEvidenceLifetime = TimeSpan.FromMinutes(10)
        };
        var checkpoint = new AttributionSafetyCheckpoint(async context =>
        {
            fixture.Clock.Now = fixture.Clock.Now.AddSeconds(1);
            if (expire)
                await context.ModerationCaseAttributionSources.Where(row => row.Id == fixture.AttributionId)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.ValidUntil, fixture.Clock.Now));
            else
                await context.ModerationCaseAttributionHeads.Where(row => row.CaseId == fixture.CaseId)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.SubjectId, fixture.Database.SubjectId));
        }, review: true);
        await Assert.That((await fixture.ReviewAsync(fixture.Author, intent, checkpoint)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await Assert.That(checkpoint.Observed).IsTrue();
        await using var reader = fixture.Context();
        await Assert.That(await reader.Set<EventResourceModerationQuarantineReview>().AnyAsync()).IsFalse();
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    public async Task PolicyLostAfterFinalQualificationStillDeniesTheCommit()
    {
        await using var fixture = await Fixture.CreateAsync(nativeAttribution: true);
        var checkpoint = new AttributionFinalCountCheckpoint(fixture.PolicyAdoptionId);
        await Assert.That((await fixture.ReleaseAsync(fixture.ReleaseIntent(), checkpoint)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await Assert.That(checkpoint.Observed).IsTrue();
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    public async Task ReturningToTheExpiredOpeningHeadCannotSubstituteForGenuineRenewal()
    {
        await using var fixture = await Fixture.CreateAsync(prepare: false, nativeAttribution: true);
        var opening = fixture.AttributionId;
        await fixture.RenewExpiredAttributionAsync();
        await using (var mutation = fixture.Context())
        {
            mutation.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            await mutation.ModerationCaseAttributionHeads.Where(row => row.CaseId == fixture.CaseId)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.SourceId, opening)
                    .SetProperty(row => row.Generation, 1).SetProperty(row => row.Revision, 1));
        }
        var intent = fixture.Intent(fixture.Author, ResourceSafetyReviewStep.ProposePurpose, null) with
        {
            RequiredApprovals = 1, MaximumEvidenceLifetime = TimeSpan.FromMinutes(10)
        };
        await Assert.That((await fixture.ReviewAsync(fixture.Author, intent)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    public async Task NewDisclosureGrantCannotExtendExpiredAttributionMetadata()
    {
        await using var fixture = await Fixture.CreateAsync(prepare: false, nativeAttribution: true);
        fixture.Clock.Now = fixture.AttributionDeadline;
        await fixture.GrantAttributionReadAsync(fixture.Clock.Now.AddMinutes(15));
        var intent = fixture.Intent(fixture.Author, ResourceSafetyReviewStep.ProposePurpose, null) with
        {
            RequiredApprovals = 1, MaximumEvidenceLifetime = TimeSpan.FromMinutes(10),
            ValidUntil = fixture.Clock.Now.AddMinutes(5)
        };
        await Assert.That((await fixture.ReviewAsync(fixture.Author, intent)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationCaseAttributionSources.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationCaseAttributionHeads.SingleAsync()).Generation).IsEqualTo(1L);
        await RequireUnreleasedAsync(fixture);
    }

    private sealed class AttributionSafetyCheckpoint(Func<ExploreDbContext, Task> mutate, bool review = false) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is ExploreDbContext context
                && (review ? context.ChangeTracker.Entries<EventResourceModerationQuarantineReview>().Any()
                    : context.ChangeTracker.Entries<EventResourceModerationQuarantineRelease>().Any()))
            {
                Observed = true;
                await mutate(context);
            }
            return result;
        }
    }

    private sealed class AttributionFinalCountCheckpoint(Guid adoptionId) : DbCommandInterceptor
    {
        public bool Observed { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!Observed && command.CommandText.Contains("COUNT(*)", StringComparison.Ordinal)
                && command.CommandText.Contains("event_resource_moderation_quarantines", StringComparison.Ordinal)
                && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<EventResourceModerationQuarantineRelease>().Any())
            {
                Observed = true;
                await using var mutation = command.Connection!.CreateCommand();
                mutation.Transaction = command.Transaction;
                mutation.CommandText = "DELETE FROM moderation_qualification_policy_adoptions WHERE id = @id";
                var parameter = mutation.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = adoptionId;
                mutation.Parameters.Add(parameter);
                await Assert.That(await mutation.ExecuteNonQueryAsync(cancellationToken)).IsEqualTo(1);
            }
            return result;
        }
    }
}
