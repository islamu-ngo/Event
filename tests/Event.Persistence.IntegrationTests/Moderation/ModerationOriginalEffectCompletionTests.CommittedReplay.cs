using Explore.Application.Contracts.Persistence;
using Explore.Application.Models.InternalEvents;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Messaging;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineExecutionTests
{
    [Test]
    public async Task Committed_completion_pointer_replay_reads_only_the_owned_transition_metadata()
    {
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await SeedCommittedCompletionBoundaryAsync(fixture);
        var reads = new CaptureReads();
        await using (var context = fixture.Context(reads))
        {
            var identity = await fixture.AuthenticateAsync(context);
            var result = await new ModerationEffectLeaseRepository(context, identity.Machine, identity.Bindings,
                new ExternalApiKeyRepository(context), fixture.Clock).ReadOriginalCompletionAsync(fixture.EffectId);
            await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Completed);
            await Assert.That(result.Effect!.CompletedAt).IsEqualTo(receipt.CompletedAt);
            await Assert.That(result.Effect.Revision).IsEqualTo(receipt.LeaseRevision + 1);
            await Assert.That(reads.Count).IsEqualTo(0);
        }
        var tenants = new List<QuarantineDrainTenant>();
        var machines = new List<MachinePrincipalAccessor>();
        await using var services = DrainServices(fixture, tenants, machines);
        await services.GetRequiredService<ModerationEffectOutboxDispatcher>().DispatchAsync(new OutboxMessage
        {
            Id = fixture.EffectId, AggregateId = fixture.EffectId,
            AggregateType = ModerationEffectOutboxMessageFactory.AggregateType,
            EventType = ModerationEffectOutboxMessageFactory.EventType
        });
        await using var verify = fixture.Context();
        verify.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That((await verify.ModerationEffectIntents.SingleAsync(row => row.Id == fixture.EffectId)).Revision)
            .IsEqualTo(receipt.LeaseRevision + 1);
        await Assert.That(await verify.EventResourceModerationQuarantines.CountAsync()).IsEqualTo(0);
        foreach (var tenant in tenants) await Assert.That(tenant.TenantId).IsNull();
        foreach (var machine in machines) await Assert.That(machine.Current).IsNull();
    }

    [Test]
    [Arguments("marker")]
    [Arguments("host")]
    [Arguments("tenant")]
    [Arguments("credential")]
    public async Task Completed_effect_without_exact_transition_ownership_cannot_be_acknowledged(string denial)
    {
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await SeedCommittedCompletionBoundaryAsync(fixture);
        await using (var writer = fixture.Context())
        {
            writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            if (denial == "marker")
                await writer.ModerationEffectIntents.Where(row => row.Id == fixture.EffectId)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(row => row.OriginalCompletionId, (Guid?)null)
                        .SetProperty(row => row.OriginalCompletionHostBindingId, (Guid?)null)
                        .SetProperty(row => row.OriginalCompletionWriterTenantId, (Guid?)null)
                        .SetProperty(row => row.OriginalCompletionRevision, (long?)null));
            if (denial == "host")
                await writer.ModerationEffectIntents.Where(row => row.Id == fixture.EffectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.OriginalCompletionHostBindingId,
                        (Guid?)Guid.CreateVersion7()));
            if (denial == "tenant")
                await writer.ModerationEffectIntents.Where(row => row.Id == fixture.EffectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.OriginalCompletionWriterTenantId,
                        (Guid?)Guid.CreateVersion7()));
        }
        var reads = new CaptureReads();
        await using var context = fixture.Context(reads);
        var identity = await fixture.AuthenticateAsync(context);
        if (denial == "credential")
        {
            await using var writer = fixture.Context();
            await writer.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, Now.UtcDateTime));
        }
        var result = await new ModerationEffectLeaseRepository(context, identity.Machine, identity.Bindings,
            new ExternalApiKeyRepository(context), fixture.Clock).ReadOriginalCompletionAsync(fixture.EffectId);
        await Assert.That(result.Outcome == ModerationEffectLeaseOutcome.Completed).IsFalse();
        await Assert.That(reads.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Committed_original_acknowledgement_outlives_private_custody_without_reconstructing_it(bool cleanup)
    {
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await SeedCommittedCompletionBoundaryAsync(fixture);
        fixture.Clock.UtcNow = receipt.RetainUntil;
        if (cleanup)
        {
            // Seed the cleanup boundary, not native cleanup or complete-consumer assurance.
            await using var writer = fixture.Context();
            writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var effect = await writer.ModerationEffectIntents.SingleAsync(row => row.Id == fixture.EffectId);
            var source = await writer.ModerationNoticeSources.SingleAsync(row => row.EffectId == fixture.EffectId);
            effect.RecordOriginalNoticeCleanup(source, receipt.HostBindingId, receipt.TenantId,
                Guid.CreateVersion7(), effect.Revision, fixture.Clock.UtcNow);
            await writer.SaveChangesAsync();
            await writer.ModerationNoticeSources.Where(row => row.EffectId == fixture.EffectId).ExecuteDeleteAsync();
            await Assert.That(await writer.ModerationOriginalEffectCompletionReceipts.AnyAsync(
                row => row.EffectId == fixture.EffectId)).IsFalse();
        }
        var reads = new CaptureReads();
        await using var context = fixture.Context(reads);
        var identity = await fixture.AuthenticateAsync(context);
        var result = await new ModerationEffectLeaseRepository(context, identity.Machine, identity.Bindings,
            new ExternalApiKeyRepository(context), fixture.Clock).ReadOriginalCompletionAsync(fixture.EffectId);
        await Assert.That(result.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Completed);
        await Assert.That(result.Effect!.OriginalCompletionId).IsEqualTo(receipt.Id);
        await Assert.That(result.Effect.OriginalCompletionRevision).IsEqualTo(receipt.LeaseRevision + 1);
        await Assert.That(result.Effect.Revision).IsEqualTo(receipt.LeaseRevision + (cleanup ? 2 : 1));
        await Assert.That(result.Effect.CompletedAt).IsEqualTo(receipt.CompletedAt);
        await Assert.That(result.Effect.LeaseExpiresAt).IsEqualTo(receipt.LeaseExpiresAt);
        await Assert.That(reads.Count).IsEqualTo(0);
    }

    /// <summary>
    /// Seeds the committing completion owner's boundary, not a successful completion-proof fixture.
    /// This consumer witness tests exact receipt replay only; it supplies no consumer/activation grant.
    /// </summary>
    private static async Task<ModerationOriginalEffectCompletionReceipt> SeedCommittedCompletionBoundaryAsync(Fixture fixture)
    {
        await using var context = fixture.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var effect = await context.ModerationEffectIntents.SingleAsync(row => row.Id == fixture.EffectId);
        var source = await context.ModerationNoticeSources.SingleAsync(row => row.EffectId == fixture.EffectId);
        var selection = await context.ModerationContributionSelections.SingleAsync(row => row.EffectId == fixture.EffectId);
        var receipt = ModerationOriginalEffectCompletionReceipt.Record(Guid.CreateVersion7(), effect, source,
            fixture.Database.HostId, fixture.LeaseId, fixture.LeaseRevision, selection.PublicationId, selection.ProfileId,
            selection.SelectionDigest, new string('c', 64), 1, fixture.Clock.UtcNow,
            fixture.Clock.UtcNow.AddSeconds(30));
        effect.Complete(fixture.LeaseId, fixture.LeaseRevision, fixture.Clock.UtcNow);
        effect.RecordOriginalCompletionAcknowledgement(receipt);
        context.ModerationOriginalEffectCompletionReceipts.Add(receipt);
        await context.SaveChangesAsync();
        return receipt;
    }
}
