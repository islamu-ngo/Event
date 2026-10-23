using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Actual primary SQLite owner witnesses; authored without running programme assurance.</summary>
public sealed partial class ModerationResourceQuarantineExecutionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactNativeReplayPreservesCauseAndTimestampWithoutCompletingOtherRecipeSteps(bool storedFile)
    {
        await using var fixture = await Fixture.CreateAsync(storedFile);
        var first = await fixture.ExecuteAsync();
        await Assert.That(first.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        await Assert.That(first.Receipt).IsNotNull();
        await Assert.That(first.Receipt!.EffectId).IsEqualTo(fixture.EffectId);
        await Assert.That(first.Receipt.BindingOrdinal).IsEqualTo(0);
        await Assert.That(first.Receipt.MaterialVersionId).IsEqualTo(fixture.MaterialId);
        await Assert.That(first.Receipt.AppliedAt).IsEqualTo(Now);
        fixture.Clock.UtcNow = Now.AddSeconds(10);
        var replay = await fixture.ExecuteAsync();
        await Assert.That(replay.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.AlreadyApplied);
        await Assert.That(replay.Receipt!.Id).IsEqualTo(first.Receipt.Id);
        await Assert.That(replay.Receipt.AppliedAt).IsEqualTo(first.Receipt.AppliedAt);
        await Assert.That((await fixture.ExecuteAsync(resourceId: Guid.CreateVersion7())).Receipt).IsNull();
        await Assert.That((await fixture.ExecuteAsync(materialId: Guid.CreateVersion7())).Receipt).IsNull();
        await Assert.That((await fixture.ExecuteAsync(ordinal: 1)).Receipt).IsNull();
        await AssertState(fixture, 1);
        await using var reader = fixture.Context();
        var resource = await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId);
        await Assert.That(resource.ModerationQuarantines.Count).IsEqualTo(0);
        await Assert.That(resource.HasPublishablePayload()).IsFalse();
        await Assert.That(resource.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Draft);
    }

    [Test]
    [Arguments("lease")]
    [Arguments("revision")]
    [Arguments("lease-deadline")]
    [Arguments("purpose-deadline")]
    [Arguments("tenant")]
    [Arguments("inactive-tenant")]
    [Arguments("target")]
    [Arguments("material-intent")]
    [Arguments("material-source")]
    [Arguments("ordinary-resource-change")]
    [Arguments("target-adoption")]
    [Arguments("profile-adoption")]
    [Arguments("role")]
    [Arguments("key")]
    [Arguments("ancestry")]
    [Arguments("generation-receipt")]
    [Arguments("selection-receipt")]
    [Arguments("adoption-receipt")]
    [Arguments("profile-receipt")]
    [Arguments("original-missing")]
    [Arguments("original-hash")]
    [Arguments("original-version")]
    [Arguments("original-inactive")]
    [Arguments("original-mode")]
    [Arguments("new-generation")]
    public async Task UnqualifiedOriginalOrCurrentCoordinatesNeverMutateOrAcknowledge(string substitution)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (substitution == "lease-deadline") fixture.Clock.UtcNow = Now.AddMinutes(2);
        if (substitution == "purpose-deadline") fixture.Clock.UtcNow = fixture.PurposeDeadline;
        if (substitution == "ancestry")
        {
            var replacementId = await ModerationResourceGenerationWriterTests.SeedAsync(fixture.Database);
            await using var writer = fixture.Context();
            var originalEventId = await writer.EventResources.Where(row => row.Id == fixture.ResourceId)
                .Select(row => row.EventId).SingleAsync();
            var replacementEventId = await writer.EventResources.Where(row => row.Id == replacementId)
                .Select(row => row.EventId).SingleAsync();
            var replacementActorId = await writer.Events.Where(row => row.Id == replacementEventId)
                .Select(row => row.ActorId).SingleAsync();
            await writer.Events.Where(row => row.Id == originalEventId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ActorId, replacementActorId));
        }
        await using (var writer = fixture.Context())
        {
            writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            if (substitution == "material-source")
                await writer.EventResources.Where(row => row.Id == fixture.ResourceId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.MaterialVersionId, Guid.CreateVersion7()));
            if (substitution == "ordinary-resource-change")
                await writer.EventResources.Where(row => row.Id == fixture.ResourceId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid()));
            if (substitution == "target-adoption")
                (await writer.ModerationResourceTargetAdoptions.SingleAsync(row => row.Id == fixture.AdoptionId))
                    .Revoke(1, fixture.Database.AdministratorId, Now);
            if (substitution == "profile-adoption")
                (await writer.ModerationProfileAdoptions.SingleAsync()).Revoke(1, fixture.Database.AdministratorId, Now);
            if (substitution == "role")
                writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(
                    row => row.UserId == fixture.Database.UrgencyAssessorId));
            if (substitution == "inactive-tenant")
            {
                var inactive = await writer.TenantStatuses.FirstAsync(row => !row.IsActiveState);
                await writer.Tenants.Where(row => row.Id == fixture.Database.TenantId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.TenantStatusId, inactive.Id));
            }
            if (substitution.EndsWith("-receipt", StringComparison.Ordinal))
            {
                var kind = substitution switch
                {
                    "generation-receipt" => ModerationQualificationSourceKind.ResourceGeneration,
                    "selection-receipt" => ModerationQualificationSourceKind.ContributionSelection,
                    "profile-receipt" => ModerationQualificationSourceKind.ProfileAdoption,
                    _ => ModerationQualificationSourceKind.ResourceTargetAdoption
                };
                writer.ModerationQualificationMutationReceipts.RemoveRange(
                    await writer.ModerationQualificationMutationReceipts.Where(row => row.SourceKind == kind).ToListAsync());
            }
            if (substitution == "original-missing")
                writer.ModerationNoticeSources.Remove(await writer.ModerationNoticeSources.SingleAsync());
            if (substitution == "original-hash")
                await writer.ModerationNoticeSources.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.SnapshotHash, new string('c', 64)));
            if (substitution is "original-version" or "original-inactive" or "original-mode")
            {
                var original = await writer.ModerationNoticeSources.SingleAsync();
                var effect = await writer.ModerationEffectIntents.AsNoTracking().SingleAsync();
                var capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(original.SnapshotJson)!;
                capture = substitution switch
                {
                    "original-version" => capture with { Version = 999 },
                    "original-inactive" => capture with
                    {
                        Decision = capture.Decision with { State = ISLAMU.Moderation.Domain.ModerationDecisionState.AwaitingApproval }
                    },
                    _ => capture with { Notice = capture.Notice! with
                    {
                        Effects = [capture.Notice.Effects[0] with { Effect = capture.Notice.Effects[0].Effect with
                        {
                            Mode = ISLAMU.Moderation.Domain.Policies.ModerationReferenceEffectMode.Notice
                        }}]
                    }}
                };
                writer.ModerationNoticeSources.Remove(original);
                await writer.SaveChangesAsync();
                writer.ModerationNoticeSources.Add(ModerationNoticeSource.Capture(effect, fixture.Database.TenantId,
                    JsonSerializer.Serialize(capture), capture.Notice!.RetainUntil));
            }
            await writer.SaveChangesAsync();
        }
        if (substitution == "new-generation")
        {
            await using var reader = fixture.Context();
            reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var epoch = await reader.ModerationQualificationEpochs.AsNoTracking()
                .SingleAsync(row => row.TenantId == fixture.Database.TenantId);
            await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId,
                new RecordModerationResourceGenerationSource
                {
                    OperationId = Guid.CreateVersion7(), Target = fixture.Database.Scope,
                    ExpectedQualificationRevision = epoch.Revision, SourceId = Guid.CreateVersion7(),
                    ResourceId = fixture.ResourceId, ExpectedResourceRevision = 1, ValidUntil = Now.AddMinutes(10)
                })).IsSuccess).IsTrue();
        }
        var result = await fixture.ExecuteAsync(
            resourceId: substitution == "target" ? Guid.CreateVersion7() : null,
            materialId: substitution == "material-intent" ? Guid.CreateVersion7() : null,
            leaseId: substitution == "lease" ? Guid.CreateVersion7() : null,
            revision: substitution == "revision" ? fixture.LeaseRevision + 1 : null,
            tenantId: substitution == "tenant" ? fixture.OtherTenantId : null,
            revokeKeyAfterAuthentication: substitution == "key");
        await Assert.That(result.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Unavailable);
        await Assert.That(result.Receipt).IsNull();
        await AssertState(fixture, 0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactPurposeCutoffDuringNativeFlushRollsBackCauseAndGuard(bool expireDuringFlush)
    {
        await using var fixture = await Fixture.CreateAsync();
        var interceptor = new FinalFlushDeadline(fixture, expireDuringFlush);
        var result = await fixture.ExecuteAsync(interceptors: [interceptor]);
        await Assert.That(interceptor.SawQuarantineFlush).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(expireDuringFlush
            ? ModerationResourceQuarantineOutcome.Unavailable : ModerationResourceQuarantineOutcome.Applied);
        await AssertState(fixture, expireDuringFlush ? 0 : 1);
    }

    [Test]
    [Arguments("current")]
    [Arguments("purpose-deadline")]
    [Arguments("credential-expiry")]
    public async Task QuarantineRechecksAuthorityAfterDisclosureFinalization(string boundary)
    {
        await using var fixture = await Fixture.CreateAsync();
        long? originalEpoch;
        Guid originalStamp;
        DateTime? originalKeyExpiry;
        await using (var reader = fixture.Context())
        {
            originalEpoch = await reader.Set<EventDiscoveryRevision>().AsNoTracking()
                .Where(row => row.TenantId == fixture.Database.TenantId)
                .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
            originalStamp = await reader.EventResources.Where(row => row.Id == fixture.ResourceId)
                .Select(row => row.ConcurrencyStamp).SingleAsync();
            originalKeyExpiry = await reader.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId)
                .Select(row => row.ExpiresAt).SingleAsync();
        }
        var advancement = new QuarantineDisclosureAdvancement(fixture.Database.TenantId, originalEpoch,
            async context =>
            {
                if (boundary == "purpose-deadline") fixture.Clock.UtcNow = fixture.PurposeDeadline;
                if (boundary == "credential-expiry")
                    await context.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, Now.UtcDateTime));
            });
        var saved = new EnlistQuarantineDisclosure(fixture.Database.TenantId, advancement);
        var result = await fixture.ExecuteAsync(interceptors: [saved, advancement]);
        await Assert.That(saved.ObservedQuarantine).IsTrue();
        await Assert.That(advancement.ObservedAdvancement).IsTrue();
        var applied = boundary == "current";
        await Assert.That(result.Outcome).IsEqualTo(applied
            ? ModerationResourceQuarantineOutcome.Applied : ModerationResourceQuarantineOutcome.Unavailable);
        await Assert.That(result.Receipt is not null).IsEqualTo(applied);
        await AssertState(fixture, applied ? 1 : 0);
        await using var verify = fixture.Context();
        var epoch = await verify.Set<EventDiscoveryRevision>().AsNoTracking()
            .Where(row => row.TenantId == fixture.Database.TenantId)
            .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
        await Assert.That(epoch).IsEqualTo(applied ? originalEpoch.GetValueOrDefault() + 1 : originalEpoch);
        var resource = await verify.EventResources.SingleAsync(row => row.Id == fixture.ResourceId);
        await Assert.That(resource.ConcurrencyStamp == originalStamp).IsEqualTo(!applied);
        await Assert.That(resource.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Draft);
        await Assert.That(await verify.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId)
            .Select(row => row.ExpiresAt).SingleAsync()).IsEqualTo(originalKeyExpiry);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrExpiredOriginalCannotAcknowledgeAnExistingNativeCause(bool expired)
    {
        await using var fixture = await Fixture.CreateAsync();
        var applied = await fixture.ExecuteAsync();
        await Assert.That(applied.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        if (expired) fixture.Clock.UtcNow = fixture.PurposeDeadline;
        else
        {
            await using var writer = fixture.Context();
            writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            writer.ModerationNoticeSources.Remove(await writer.ModerationNoticeSources.SingleAsync());
            await writer.SaveChangesAsync();
        }
        var replay = await fixture.ExecuteAsync();
        await Assert.That(replay.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Unavailable);
        await Assert.That(replay.Receipt).IsNull();
        await AssertState(fixture, 1);
    }

    [Test]
    public async Task ExactReplayDoesNotTreatItsReceiptAsAuthorityForChangedNativeMaterial()
    {
        await using var fixture = await Fixture.CreateAsync();
        var applied = await fixture.ExecuteAsync();
        await Assert.That(applied.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Applied);
        var replacement = Guid.CreateVersion7();
        await using (var writer = fixture.Context())
            await writer.EventResources.Where(row => row.Id == fixture.ResourceId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.MaterialVersionId, replacement));
        var replay = await fixture.ExecuteAsync();
        var altered = await fixture.ExecuteAsync(materialId: replacement);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Unavailable);
        await Assert.That(altered.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Unavailable);
        await Assert.That(replay.Receipt).IsNull();
        await Assert.That(altered.Receipt).IsNull();
        await AssertState(fixture, 1);
        await using var reader = fixture.Context();
        var original = await reader.EventResourceModerationQuarantines.SingleAsync(row => row.EffectId == fixture.EffectId);
        await Assert.That(original.MaterialVersionId).IsEqualTo(fixture.MaterialId);
        await Assert.That(original.Id).IsEqualTo(applied.Receipt!.Id);
        await Assert.That(original.AppliedAt).IsEqualTo(Now);
    }

    [Test]
    public async Task OriginalRetentionDeadlineRejectsBeforePrivateCaptureEvenWithNewLiveLease()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Clock.UtcNow = Now.AddYears(1).AddSeconds(-1);
        await fixture.ClaimAsync();
        fixture.Clock.UtcNow = Now.AddYears(1);
        var reads = new CaptureReads();
        var result = await fixture.ExecuteAsync(interceptors: [reads]);
        await Assert.That(result.Outcome).IsEqualTo(ModerationResourceQuarantineOutcome.Unavailable);
        await Assert.That(reads.Count).IsEqualTo(0);
        await AssertState(fixture, 0);
    }

    private static async Task AssertState(Fixture fixture, int causes)
    {
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var resource = await reader.EventResources.Include(row => row.ModerationQuarantines)
            .SingleAsync(row => row.Id == fixture.ResourceId);
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(causes);
        await Assert.That(resource.ModerationQuarantines.Count).IsEqualTo(causes);
        var effect = await reader.ModerationEffectIntents.SingleAsync(row => row.Id == fixture.EffectId);
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(effect.LeaseId).IsEqualTo(fixture.LeaseId);
        await Assert.That(effect.Revision).IsEqualTo(fixture.LeaseRevision);
    }

    private sealed class FinalFlushDeadline(Fixture fixture, bool expire) : SaveChangesInterceptor
    {
        public bool SawQuarantineFlush { get; private set; }
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<EventResourceModerationQuarantine>().Any())
            {
                SawQuarantineFlush = true;
                if (expire) fixture.Clock.UtcNow = fixture.PurposeDeadline;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class EnlistQuarantineDisclosure(Guid tenantId, QuarantineDisclosureAdvancement advancement)
        : SaveChangesInterceptor
    {
        public bool ObservedQuarantine { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!ObservedQuarantine && eventData.Context is ExploreDbContext context
                && !context.ChangeTracker.HasChanges()
                && context.ChangeTracker.Entries<EventResourceModerationQuarantine>().Any())
            {
                await Assert.That(context.IdentityFenceOwnsTransaction).IsFalse();
                ObservedQuarantine = true;
                context.DisclosureMutations.Enlist([tenantId]);
                advancement.Armed = true;
            }
            return result;
        }
    }

    private sealed class QuarantineDisclosureAdvancement(
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

    private sealed class CaptureReads : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("moderation_notice_sources", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("snapshot_json", StringComparison.OrdinalIgnoreCase)) Count++;
            return ValueTask.FromResult(result);
        }
    }
}
