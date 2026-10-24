using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Explore.Persistence;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    public enum ClassificationControlChange
    {
        None, WrongSourceRevision, WrongSourceWriter, WrongReviewRevision, WrongReviewWriter,
        WrongGeneration, WrongGenerationRevision, WrongGenerationWriter, WrongContributionIdentity,
        MissingEffect, MissingBasePlan, MissingStrategy, HumanSubject, ResourceScope, SanctionWindow,
        OwnerAuthor, ReviewerAuthor, OwnerAdopter, ReviewerAdopter, AuthorAdopter, WrongOrdinal,
        NewUnacceptedMaterialHead, EndedReview, ExpiredReview, MaterialChanged, AuthorRoleLost,
        ReceiptLost, ReplayChanged, WrongProfile, TenantTarget, ResourceTarget, DifferentEventTarget
    }

    [Test]
    [Arguments(ClassificationControlChange.None)]
    [Arguments(ClassificationControlChange.WrongSourceRevision)]
    [Arguments(ClassificationControlChange.WrongSourceWriter)]
    [Arguments(ClassificationControlChange.WrongReviewRevision)]
    [Arguments(ClassificationControlChange.WrongReviewWriter)]
    [Arguments(ClassificationControlChange.WrongGeneration)]
    [Arguments(ClassificationControlChange.WrongGenerationRevision)]
    [Arguments(ClassificationControlChange.WrongGenerationWriter)]
    [Arguments(ClassificationControlChange.WrongContributionIdentity)]
    [Arguments(ClassificationControlChange.MissingEffect)]
    [Arguments(ClassificationControlChange.MissingBasePlan)]
    [Arguments(ClassificationControlChange.MissingStrategy)]
    [Arguments(ClassificationControlChange.HumanSubject)]
    [Arguments(ClassificationControlChange.ResourceScope)]
    [Arguments(ClassificationControlChange.SanctionWindow)]
    [Arguments(ClassificationControlChange.OwnerAuthor)]
    [Arguments(ClassificationControlChange.ReviewerAuthor)]
    [Arguments(ClassificationControlChange.OwnerAdopter)]
    [Arguments(ClassificationControlChange.ReviewerAdopter)]
    [Arguments(ClassificationControlChange.AuthorAdopter)]
    [Arguments(ClassificationControlChange.WrongOrdinal)]
    [Arguments(ClassificationControlChange.NewUnacceptedMaterialHead)]
    [Arguments(ClassificationControlChange.EndedReview)]
    [Arguments(ClassificationControlChange.ExpiredReview)]
    [Arguments(ClassificationControlChange.MaterialChanged)]
    [Arguments(ClassificationControlChange.AuthorRoleLost)]
    [Arguments(ClassificationControlChange.ReceiptLost)]
    [Arguments(ClassificationControlChange.ReplayChanged)]
    [Arguments(ClassificationControlChange.WrongProfile)]
    [Arguments(ClassificationControlChange.TenantTarget)]
    [Arguments(ClassificationControlChange.ResourceTarget)]
    [Arguments(ClassificationControlChange.DifferentEventTarget)]
    public async Task ClassificationControl_RequiresExactCompleteIndependentNativeMaterial(ClassificationControlChange change)
    {
        await RunClassificationMaterialAsync(MaterialChange.None,
            change == ClassificationControlChange.WrongProfile ? ModerationReferenceProfileId.CFG28 : ModerationReferenceProfileId.CFG29,
            async fixture =>
            {
                var context = fixture.Context;
                var selection = await ClassificationSelectionRequest(fixture);
                var contribution = selection.Contribution;
                var resource = AuthorityScope.Resource(fixture.Database.Scope, fixture.Generation.EventId);
                selection = change switch
                {
                    ClassificationControlChange.TenantTarget => selection with { Target = fixture.Database.Scope },
                    ClassificationControlChange.ResourceTarget => selection with { Target = resource },
                    ClassificationControlChange.DifferentEventTarget => selection with
                    { Target = AuthorityScope.Event(fixture.Database.Scope, Guid.NewGuid()) },
                    ClassificationControlChange.WrongSourceRevision => selection with { ExpectedMaterialSourceRevision = 2 },
                    ClassificationControlChange.WrongSourceWriter => selection with { ExpectedMaterialWriterGeneration = 1 },
                    ClassificationControlChange.WrongReviewRevision => selection with { ExpectedMaterialReviewRevision = 2 },
                    ClassificationControlChange.WrongReviewWriter => selection with { ExpectedReviewWriterGeneration = 1 },
                    ClassificationControlChange.WrongGeneration => selection with { EventGenerationId = Guid.CreateVersion7() },
                    ClassificationControlChange.WrongGenerationRevision => selection with { ExpectedEventGenerationRevision = 2 },
                    ClassificationControlChange.WrongGenerationWriter => selection with { ExpectedEventWriterGeneration = 1 },
                    ClassificationControlChange.WrongContributionIdentity => selection with
                    { Contribution = contribution with { Id = Guid.CreateVersion7() } },
                    ClassificationControlChange.MissingEffect => selection with
                    { Contribution = contribution with { Effects = contribution.Effects.RemoveAt(0) } },
                    ClassificationControlChange.MissingBasePlan => selection with
                    { Contribution = contribution with { Consequences = contribution.Consequences.Where(row => row.Strategy is not null).ToImmutableArray() } },
                    ClassificationControlChange.MissingStrategy => selection with
                    { Contribution = contribution with { Consequences = contribution.Consequences.Where(row => row.Strategy is null).ToImmutableArray() } },
                    ClassificationControlChange.HumanSubject => selection with
                    { Contribution = contribution with { SubjectKind = ModerationSubjectKind.Human, SubjectId = fixture.Owner } },
                    ClassificationControlChange.ResourceScope => selection with
                    {
                        Contribution = contribution with
                        {
                            Scope = resource,
                            Effects = contribution.Effects.Select(row => row with { Target = row.Target with { Scope = resource } }).ToImmutableArray(),
                            Consequences = contribution.Consequences.Select(row => row with { Target = row.Target with { Scope = resource } }).ToImmutableArray()
                        }
                    },
                    ClassificationControlChange.SanctionWindow => selection with
                    { Contribution = contribution with { EventWindowEnd = Now.AddMinutes(1) } },
                    _ => selection
                };
                var author = change == ClassificationControlChange.OwnerAuthor ? fixture.Owner
                    : change == ClassificationControlChange.ReviewerAuthor ? fixture.Reviewer : fixture.Database.AdminId;
                var epoch = selection.ExpectedQualificationRevision;
                var recorded = await fixture.Writer(author).ExecuteAsync(selection);
                var rejectsSelection = change is ClassificationControlChange.WrongSourceRevision
                    or ClassificationControlChange.WrongSourceWriter or ClassificationControlChange.WrongReviewRevision
                    or ClassificationControlChange.WrongReviewWriter or ClassificationControlChange.WrongGeneration
                    or ClassificationControlChange.WrongGenerationRevision or ClassificationControlChange.WrongGenerationWriter
                    or ClassificationControlChange.WrongContributionIdentity or ClassificationControlChange.MissingEffect
                    or ClassificationControlChange.MissingBasePlan or ClassificationControlChange.MissingStrategy
                    or ClassificationControlChange.HumanSubject or ClassificationControlChange.ResourceScope
                    or ClassificationControlChange.SanctionWindow or ClassificationControlChange.OwnerAuthor
                    or ClassificationControlChange.ReviewerAuthor or ClassificationControlChange.WrongProfile
                    or ClassificationControlChange.TenantTarget or ClassificationControlChange.ResourceTarget
                    or ClassificationControlChange.DifferentEventTarget;
                if (rejectsSelection)
                {
                    await Assert.That(recorded.Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
                    await AssertClassificationControlRollback(fixture, selection.OperationId, epoch, selection.SelectionId, null);
                    return;
                }
                await Assert.That(recorded.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
                epoch = recorded.Receipt!.QualificationRevision;
                if (change == ClassificationControlChange.ReplayChanged)
                {
                    var replay = await fixture.Writer(author).ExecuteAsync(selection);
                    await Assert.That(replay.Receipt!.QualificationRevision).IsEqualTo(epoch);
                    await Assert.That((await fixture.Writer(author).ExecuteAsync(selection with
                    { ValidUntil = selection.ValidUntil.AddTicks(-1) })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
                    return;
                }
                if (change == ClassificationControlChange.NewUnacceptedMaterialHead)
                {
                    var next = fixture.Source with
                    {
                        OperationId = Guid.CreateVersion7(),
                        SourceId = Guid.CreateVersion7(),
                        ExpectedQualificationRevision = epoch,
                        ExpectedMaterialGeneration = fixture.Review.ExpectedSourceWriterGeneration,
                        Materials = fixture.Source.Materials.Select(row => row with { ReviewId = Guid.CreateVersion7() }).ToImmutableArray()
                    };
                    var result = await fixture.Writer(fixture.Owner).ExecuteAsync(next);
                    await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
                    epoch = result.Receipt!.QualificationRevision;
                }
                if (change == ClassificationControlChange.EndedReview)
                {
                    var result = await fixture.Writer(author).ExecuteAsync(new EndModerationClassificationMaterialReview
                    {
                        OperationId = Guid.CreateVersion7(),
                        Target = fixture.Database.Scope,
                        ExpectedQualificationRevision = epoch,
                        ReviewId = fixture.Review.ReviewId,
                        ExpectedReviewRevision = 1,
                        End = ModerationClassificationMaterialEnd.Withdrawn,
                        Grounds = "Independent acceptance withdrawn."
                    });
                    await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
                    epoch = result.Receipt!.QualificationRevision;
                }
                if (change == ClassificationControlChange.ExpiredReview) fixture.Clock.UtcNow = Now.AddMinutes(2);
                if (change == ClassificationControlChange.MaterialChanged)
                    await context.EventSessions.Where(row => row.EventId == fixture.Generation.EventId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Title, "Unreviewed native text"));
                if (change == ClassificationControlChange.AuthorRoleLost)
                    await context.PlatformUserRoles.Where(row => row.UserId == author).ExecuteDeleteAsync();
                if (change == ClassificationControlChange.ReceiptLost)
                    await context.ModerationQualificationMutationReceipts.Where(row => row.SourceId == selection.SelectionId).ExecuteDeleteAsync();
                var adopter = change == ClassificationControlChange.OwnerAdopter ? fixture.Owner
                    : change == ClassificationControlChange.ReviewerAdopter ? fixture.Reviewer
                    : change == ClassificationControlChange.AuthorAdopter ? author : fixture.Adopter;
                var adopt = new AdoptModerationClassificationEventTarget
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = selection.Target,
                    ExpectedQualificationRevision = epoch,
                    AdoptionId = Guid.CreateVersion7(),
                    SelectionId = selection.SelectionId,
                    ExpectedSelectionRevision = 1,
                    BindingOrdinal = change == ClassificationControlChange.WrongOrdinal ? 63 : 0,
                    EventGenerationId = fixture.Generation.SourceId,
                    ExpectedEventGenerationRevision = 1,
                    ValidUntil = Now.AddMinutes(1)
                };
                var adopted = await fixture.Writer(adopter).ExecuteAsync(adopt);
                if (change != ClassificationControlChange.None)
                {
                    await Assert.That(adopted.Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
                    await AssertClassificationControlRollback(fixture, adopt.OperationId, epoch, null, adopt.AdoptionId);
                    return;
                }
                await Assert.That(adopted.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
                epoch = adopted.Receipt!.QualificationRevision;
                // Every complete binding has its own native independent adoption, not just the first effect.
                for (var ordinal = 1; ordinal < 4; ordinal++)
                {
                    var result = await fixture.Writer(adopter).ExecuteAsync(adopt with
                    {
                        OperationId = Guid.CreateVersion7(),
                        AdoptionId = Guid.CreateVersion7(),
                        ExpectedQualificationRevision = epoch,
                        BindingOrdinal = ordinal
                    });
                    await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
                    epoch = result.Receipt!.QualificationRevision;
                }
                await Assert.That(await context.Set<ModerationClassificationEventTargetAdoption>().CountAsync()).IsEqualTo(4);
                await Assert.That(await context.ModerationSanctions.CountAsync()).IsEqualTo(0);
                await Assert.That(await context.ModerationContributionSelections.CountAsync()).IsEqualTo(0);
            });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ClassificationControl_RevocationRetainsAccountabilityWithoutLiveMaterial(bool expired)
    {
        await RunClassificationMaterialAsync(MaterialChange.None, ModerationReferenceProfileId.CFG29, async fixture =>
        {
            var selection = await ClassificationSelectionRequest(fixture);
            var recorded = await fixture.Writer(fixture.Database.AdminId).ExecuteAsync(selection);
            await Assert.That(recorded.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            var adopt = new AdoptModerationClassificationEventTarget
            {
                OperationId = Guid.CreateVersion7(),
                Target = selection.Target,
                ExpectedQualificationRevision = recorded.Receipt!.QualificationRevision,
                AdoptionId = Guid.CreateVersion7(),
                SelectionId = selection.SelectionId,
                ExpectedSelectionRevision = 1,
                BindingOrdinal = 0,
                EventGenerationId = fixture.Generation.SourceId,
                ExpectedEventGenerationRevision = 1,
                ValidUntil = Now.AddMinutes(1)
            };
            var adopted = await fixture.Writer(fixture.Adopter).ExecuteAsync(adopt);
            await Assert.That(adopted.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            if (expired) fixture.Clock.UtcNow = Now.AddMinutes(11);
            else await fixture.Context.Events.Where(row => row.Id == fixture.Generation.EventId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
            var revoke = new RevokeModerationClassificationEventTarget
            {
                OperationId = Guid.CreateVersion7(),
                Target = selection.Target,
                ExpectedQualificationRevision = adopted.Receipt!.QualificationRevision,
                AdoptionId = adopt.AdoptionId,
                ExpectedAdoptionRevision = 1,
                Grounds = "End retained material control without granting replacement authority."
            };
            var result = await fixture.Writer(fixture.Database.AdminId).ExecuteAsync(revoke);
            await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            var retained = await fixture.Context.Set<ModerationClassificationEventTargetAdoption>().AsNoTracking().SingleAsync();
            await Assert.That(retained.Revision).IsEqualTo(2);
            await Assert.That(retained.RevocationGrounds).IsEqualTo(revoke.Grounds);
            await Assert.That(retained.RevokedByHumanId).IsEqualTo(fixture.Database.AdminId);
            await Assert.That((await fixture.Writer(fixture.Database.AdminId).ExecuteAsync(revoke)).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await fixture.Writer(fixture.Database.AdminId).ExecuteAsync(revoke with
            { Grounds = "A different correction cannot reuse the immutable operation." })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        });
    }

    private static async Task<RecordModerationClassificationControlSelection> ClassificationSelectionRequest(ClassificationMaterialFixture fixture)
    {
        var profile = fixture.Policy.Profiles.Single().Definition;
        var scope = AuthorityScope.Event(fixture.Database.Scope, fixture.Generation.EventId);
        var target = new ModerationPolicyTarget(scope, ModerationSubjectKind.Content, fixture.Generation.EventId,
            fixture.Generation.EventId, 1, "classification.material", null);
        var id = Guid.CreateVersion7();
        return new()
        {
            OperationId = Guid.CreateVersion7(),
            Target = scope,
            ExpectedQualificationRevision = await fixture.Context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync(),
            SelectionId = id,
            MaterialSourceId = fixture.Source.SourceId,
            ExpectedMaterialSourceRevision = 1,
            ExpectedMaterialWriterGeneration = fixture.Review.ExpectedSourceWriterGeneration,
            MaterialReviewId = fixture.Review.ReviewId,
            ExpectedMaterialReviewRevision = 1,
            ExpectedReviewWriterGeneration = fixture.Review.ExpectedQualificationRevision + 1,
            EventGenerationId = fixture.Generation.SourceId,
            ExpectedEventGenerationRevision = 1,
            ExpectedEventWriterGeneration = fixture.Generation.ExpectedQualificationRevision + 1,
            Contribution = new(id, profile.Rule, scope, ModerationSubjectKind.Content, fixture.Generation.EventId, Now, null,
                profile.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, target)).ToImmutableArray(),
                new ModerationAdditiveStrategyKind?[] { null }.Concat(profile.Strategies.Select(strategy => (ModerationAdditiveStrategyKind?)strategy))
                    .Select(strategy => new ModerationPolicyConsequenceTarget(strategy, target)).ToImmutableArray()),
            ValidUntil = Now.AddMinutes(1)
        };
    }

    public enum ClassificationControlFlushChange { Receipt, Material, Epoch, Role, Expiry, FinalFlushReceipt }

    [Test]
    [Arguments(ClassificationControlFlushChange.Receipt, false)]
    [Arguments(ClassificationControlFlushChange.Material, false)]
    [Arguments(ClassificationControlFlushChange.Epoch, false)]
    [Arguments(ClassificationControlFlushChange.Role, false)]
    [Arguments(ClassificationControlFlushChange.Expiry, false)]
    [Arguments(ClassificationControlFlushChange.FinalFlushReceipt, false)]
    [Arguments(ClassificationControlFlushChange.Receipt, true)]
    [Arguments(ClassificationControlFlushChange.Material, true)]
    [Arguments(ClassificationControlFlushChange.Epoch, true)]
    [Arguments(ClassificationControlFlushChange.Role, true)]
    [Arguments(ClassificationControlFlushChange.Expiry, true)]
    [Arguments(ClassificationControlFlushChange.FinalFlushReceipt, true)]
    public async Task ClassificationControl_FinalNativeRecheckRollsBackWholeMutation(ClassificationControlFlushChange change, bool adoption)
    {
        var finalFlush = new MaterialFinalFlush();
        var observer = new ClassificationControlFlushObserver(change, adoption, finalFlush);
        await RunClassificationMaterialAsync(MaterialChange.None, ModerationReferenceProfileId.CFG29, async fixture =>
        {
            observer.Clock = fixture.Clock;
            var selection = await ClassificationSelectionRequest(fixture);
            observer.Enabled = !adoption;
            var result = await fixture.Writer(fixture.Database.AdminId).ExecuteAsync(selection);
            if (!adoption)
            {
                await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
                await AssertClassificationControlRollback(fixture, selection.OperationId, selection.ExpectedQualificationRevision,
                    selection.SelectionId, null);
            }
            else
            {
                await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
                observer.Enabled = true;
                var adopt = new AdoptModerationClassificationEventTarget
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = selection.Target,
                    ExpectedQualificationRevision = result.Receipt!.QualificationRevision,
                    AdoptionId = Guid.CreateVersion7(),
                    SelectionId = selection.SelectionId,
                    ExpectedSelectionRevision = 1,
                    BindingOrdinal = 0,
                    EventGenerationId = fixture.Generation.SourceId,
                    ExpectedEventGenerationRevision = 1,
                    ValidUntil = Now.AddMinutes(1)
                };
                var accepted = await fixture.Writer(fixture.Adopter).ExecuteAsync(adopt);
                await Assert.That(accepted.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
                await AssertClassificationControlRollback(fixture, adopt.OperationId, adopt.ExpectedQualificationRevision,
                    null, adopt.AdoptionId);
            }
            await Assert.That(observer.Observed).IsTrue();
            if (change == ClassificationControlFlushChange.FinalFlushReceipt)
                await Assert.That(finalFlush.Observed).IsTrue();
        }, observer, finalFlush);
    }

    private sealed class ClassificationControlFlushObserver(
        ClassificationControlFlushChange change, bool adoption, MaterialFinalFlush finalFlush) : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public bool Observed { get; private set; }
        public DeploymentClock Clock { get; set; } = null!;

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Enabled || Observed || eventData.Context is not ExploreDbContext primary) return result;
            var selected = primary.ChangeTracker.Entries<ModerationClassificationControlSelection>()
                .Select(row => row.Entity).SingleOrDefault();
            var adopted = primary.ChangeTracker.Entries<ModerationClassificationEventTargetAdoption>()
                .Select(row => row.Entity).SingleOrDefault();
            var id = adoption ? adopted?.Id : selected?.Id;
            if (id is null) return result;
            Observed = true;
            if (change == ClassificationControlFlushChange.Receipt)
                await primary.ModerationQualificationMutationReceipts.Where(row => row.SourceId == id).ExecuteDeleteAsync(cancellationToken);
            if (change == ClassificationControlFlushChange.Material)
                await primary.EventSessions.Where(row => row.EventId == (adoption ? adopted!.EventId : selected!.EventId))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Title, "Changed after native save"), cancellationToken);
            if (change == ClassificationControlFlushChange.Epoch)
                await primary.ModerationQualificationEpochs.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
            if (change == ClassificationControlFlushChange.Role)
                await primary.PlatformUserRoles.Where(row => row.UserId == (adoption ? adopted!.AdoptedByHumanId : selected!.AuthoredByHumanId))
                    .ExecuteDeleteAsync(cancellationToken);
            if (change == ClassificationControlFlushChange.Expiry) Clock.UtcNow = Now.AddMinutes(1);
            if (change == ClassificationControlFlushChange.FinalFlushReceipt)
            {
                primary.DisclosureMutations.Enlist([adoption ? adopted!.TenantId : selected!.TenantId]);
                finalFlush.ReviewId = id;
                finalFlush.Transaction = primary.Database.CurrentTransaction!.GetDbTransaction();
            }
            return result;
        }
    }

    private static async Task AssertClassificationControlRollback(ClassificationMaterialFixture fixture, Guid operationId,
        long epoch, Guid? selectionId, Guid? adoptionId)
    {
        fixture.Context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await fixture.Context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(epoch);
        await Assert.That(await fixture.Context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == operationId)).IsFalse();
        if (selectionId is { } selected)
            await Assert.That(await fixture.Context.Set<ModerationClassificationControlSelection>().AnyAsync(row => row.Id == selected)).IsFalse();
        if (adoptionId is { } adopted)
            await Assert.That(await fixture.Context.Set<ModerationClassificationEventTargetAdoption>().AnyAsync(row => row.Id == adopted)).IsFalse();
    }
}
