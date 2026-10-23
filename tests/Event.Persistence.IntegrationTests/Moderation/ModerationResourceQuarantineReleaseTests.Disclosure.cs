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
    [Arguments(false, "current")]
    [Arguments(false, "deadline")]
    [Arguments(false, "human")]
    [Arguments(true, "current")]
    [Arguments(true, "deadline")]
    [Arguments(true, "human")]
    public async Task FreshSafetyWritesRequalifyAfterPersistedDisclosureFinalization(bool release, string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        long? originalEpoch;
        Guid originalStamp;
        int originalReviews;
        int originalCompletions;
        await using (var before = fixture.Context())
        {
            originalEpoch = await before.Set<EventDiscoveryRevision>().AsNoTracking()
                .Where(row => row.TenantId == fixture.Database.TenantId)
                .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
            originalStamp = (await before.EventResources.AsNoTracking()
                .SingleAsync(row => row.Id == fixture.ResourceId)).ConcurrencyStamp;
            originalReviews = await before.Set<EventResourceModerationQuarantineReview>().CountAsync();
            originalCompletions = await before.Set<EventResourceModerationReviewCompletion>().CountAsync();
        }
        var human = release ? fixture.Approver : fixture.Database.AdministratorId;
        var recordedAt = fixture.Clock.Now;
        var review = fixture.Intent(human, ResourceSafetyReviewStep.ProposePurpose, null) with
        {
            RequiredApprovals = 1, MaximumEvidenceLifetime = TimeSpan.FromMinutes(1)
        };
        var removal = fixture.ReleaseIntent();
        var operationId = release ? removal.OperationId : review.OperationId;
        var advancement = new SafetyDisclosureAdvancement(fixture.Database.TenantId, originalEpoch, async context =>
        {
            if (change == "deadline")
                fixture.Clock.Now = fixture.Deadline;
            else if (change == "human")
                await context.Users.Where(row => row.Id == human)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        });
        var saved = new EnlistSafetyDisclosure(operationId, fixture.Database.TenantId, advancement);
        ModerationResourceSafetyOutcome outcome;
        Guid? receiptId;
        if (release)
        {
            var result = await fixture.ReleaseAsync(removal, saved, advancement);
            outcome = result.Outcome;
            receiptId = result.Receipt?.Id;
        }
        else
        {
            var result = await fixture.ReviewAsync(human, review, saved, advancement);
            outcome = result.Outcome;
            receiptId = result.Receipt?.Id;
        }
        var current = change == "current";
        await Assert.That(saved.ObservedWrite).IsTrue();
        await Assert.That(advancement.ObservedAdvancement).IsTrue();
        await Assert.That(outcome).IsEqualTo(current
            ? ModerationResourceSafetyOutcome.Known : ModerationResourceSafetyOutcome.Unavailable);
        await Assert.That(receiptId).IsEqualTo(current ? operationId : (Guid?)null);

        await using var inspect = fixture.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var resource = await inspect.EventResources.AsNoTracking().SingleAsync(row => row.Id == fixture.ResourceId);
        var cause = await inspect.EventResourceModerationQuarantines.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.CauseId);
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(current && release ? 1 : 2);
        await Assert.That(resource.HasPublishablePayload()).IsFalse();
        await Assert.That(current && release ? resource.ConcurrencyStamp != originalStamp
            : resource.ConcurrencyStamp == originalStamp).IsTrue();
        await Assert.That(cause.ReleaseId).IsEqualTo(current && release ? operationId : (Guid?)null);
        await Assert.That((await inspect.EventResourceModerationQuarantines.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.OtherCauseId)).ReleaseId).IsNull();
        var persistedRelease = await inspect.Set<EventResourceModerationQuarantineRelease>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == operationId);
        var persistedReview = await inspect.Set<EventResourceModerationQuarantineReview>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == operationId);
        if (current && release)
        {
            await Assert.That(persistedRelease!.ReleasedAt).IsEqualTo(recordedAt);
            await Assert.That(persistedRelease.OriginallyAppliedAt).IsEqualTo(OriginalTime);
            await Assert.That(persistedRelease.Owns(cause)).IsTrue();
        }
        else
            await Assert.That(persistedRelease).IsNull();
        if (current && !release)
        {
            await Assert.That(persistedReview!.RecordedAt).IsEqualTo(recordedAt);
            await Assert.That(persistedReview.ValidUntil).IsEqualTo(fixture.Deadline);
        }
        else
            await Assert.That(persistedReview).IsNull();
        await Assert.That(await inspect.Set<EventResourceModerationQuarantineReview>().CountAsync())
            .IsEqualTo(originalReviews + (current && !release ? 1 : 0));
        await Assert.That(await inspect.Set<EventResourceModerationReviewCompletion>().CountAsync())
            .IsEqualTo(originalCompletions);
        var finalEpoch = await inspect.Set<EventDiscoveryRevision>().AsNoTracking()
            .Where(row => row.TenantId == fixture.Database.TenantId)
            .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
        await Assert.That(current ? finalEpoch > originalEpoch.GetValueOrDefault() : finalEpoch == originalEpoch).IsTrue();
        await Assert.That((await inspect.Users.IgnoreQueryFilters(["SoftDelete"]).AsNoTracking()
            .SingleAsync(row => row.Id == human)).IsDeleted).IsFalse();
        var history = await inspect.ModerationFindings.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.HistoricalFindingId);
        await Assert.That(history.Revision).IsEqualTo(1L);
        await Assert.That(history.ReversalReferenceId).IsNull();
        await Assert.That(await inspect.ModerationNoticeSources.CountAsync()).IsEqualTo(0);
    }

    private sealed class EnlistSafetyDisclosure(
        Guid operationId, Guid tenantId, SafetyDisclosureAdvancement advancement) : SaveChangesInterceptor
    {
        public bool ObservedWrite { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!ObservedWrite && eventData.Context is ExploreDbContext context
                && !context.ChangeTracker.HasChanges()
                && (context.ChangeTracker.Entries<EventResourceModerationQuarantineReview>()
                    .Any(row => row.Entity.Id == operationId)
                    || context.ChangeTracker.Entries<EventResourceModerationQuarantineRelease>()
                        .Any(row => row.Entity.Id == operationId)))
            {
                ObservedWrite = true;
                context.DisclosureMutations.Enlist([tenantId]);
                advancement.Armed = true;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SafetyDisclosureAdvancement(
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
}
