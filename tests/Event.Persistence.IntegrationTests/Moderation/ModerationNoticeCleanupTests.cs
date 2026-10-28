using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Native cleanup regressions authored for the deferred programme assurance run.</summary>
public sealed partial class ModerationNoticeCleanupTests
{
    private static readonly DateTimeOffset OriginalAt = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ceiling = new(2027, 5, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Fixed_ceiling_removes_all_original_copies_but_keeps_pending_effect_and_case_authority(bool acknowledged)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync(acknowledged: acknowledged);
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(result.Purged).IsEqualTo(1);
        await Assert.That(result.OriginalWritesProven).IsEqualTo(1);
        await Assert.That(writer.ModerationTrustDomainId).IsNull();
        await Assert.That(writer.ChangeTracker.Entries().Any()).IsFalse();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Instance.TrustDomainId;
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNotices.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationFindings.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationDecisions.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationOperationReceipts.CountAsync()).IsEqualTo(1);
        var effect = await reader.ModerationEffectIntents.SingleAsync(row => row.Id == effectId);
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(effect.LeaseId).IsNull();
        await Assert.That(effect.HasOriginalNoticeCleanup(fixture.Database.HostId, fixture.Database.TenantId, Ceiling)).IsTrue();
        await Assert.That(effect.OriginalNoticeRetainUntil).IsEqualTo(Ceiling);
        await Assert.That(effect.OriginalNoticePurgedAt).IsEqualTo(Ceiling);
    }

    [Test]
    public async Task Cursor_passes_a_live_head_without_extending_or_deleting_its_payload()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var firstApproval = Guid.Parse("019a0000-0000-7000-8000-000000000001");
        var secondApproval = Guid.Parse("019a0000-0000-7000-8000-000000000002");
        var live = await fixture.SeedAsync(approvalId: firstApproval, retainUntil: Ceiling.AddTicks(1));
        var expired = await fixture.SeedAsync(approvalId: secondApproval);
        await using var writer = fixture.Context();
        var repository = fixture.Repository(writer, await fixture.AuthenticateAsync(writer));
        var first = await repository.PruneAsync(null, 1);
        await Assert.That(first.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(first.LastScannedApprovalId).IsEqualTo(firstApproval);
        await Assert.That(first.Purged).IsEqualTo(0);
        var second = await repository.PruneAsync(first.LastScannedApprovalId, 1);
        await Assert.That(second.Purged).IsEqualTo(1);
        await Assert.That(second.LastScannedApprovalId).IsEqualTo(secondApproval);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Instance.TrustDomainId;
        await Assert.That((await reader.ModerationNoticeSources.SingleAsync()).EffectId).IsEqualTo(live);
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync(row => row.Id == live)).OriginalNoticeCleanupId).IsNull();
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync(row => row.Id == expired)).CompletedAt).IsNull();
    }

    [Test]
    [Arguments("missing-source")]
    [Arguments("missing-reviewer")]
    [Arguments("wrong-hash")]
    [Arguments("wrong-writer")]
    [Arguments("wrong-operation-receipt")]
    public async Task Corrupt_or_incomplete_original_write_is_purged_without_laundering_it_into_replay(string defect)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync(defect: defect);
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(result.Purged).IsEqualTo(1);
        await Assert.That(result.OriginalWritesProven).IsEqualTo(0);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Instance.TrustDomainId;
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(0);
        var effect = await reader.ModerationEffectIntents.SingleAsync(row => row.Id == effectId);
        await Assert.That(effect.HasOriginalNoticeCleanup(fixture.Database.HostId, fixture.Database.TenantId, Ceiling)).IsFalse();
        await Assert.That(effect.CompletedAt).IsNull();
    }

    [Test]
    public async Task Divergent_original_retention_clocks_are_unavailable_not_a_new_retention_policy()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        await fixture.SeedAsync(defect: "retention");
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Instance.TrustDomainId;
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync()).OriginalNoticeCleanupId).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Missing_native_notification_cleanup_owner_rolls_back_the_whole_payload_deletion(bool orphaned)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync(nativePointer: true, acknowledged: !orphaned,
            defect: orphaned ? "missing-subject" : null);
        if (orphaned)
        {
            await using var mutation = fixture.Context();
            await mutation.NotificationIntents.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        }
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
        await Assert.That(result.LastScannedApprovalId).IsNull();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Instance.TrustDomainId;
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.ModerationNotices.CountAsync()).IsEqualTo(orphaned ? 1 : 2);
        await Assert.That(await reader.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(orphaned ? 0 : 1);
        await Assert.That(await reader.NotificationIntents.IgnoreQueryFilters(["SoftDelete"]).CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync(row => row.Id == effectId)).OriginalNoticeCleanupId).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Actual_native_graph_owner_removes_expired_pointer_and_soft_deleted_orphan_without_claiming_notification(bool orphaned)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync(nativePointer: true, acknowledged: !orphaned,
            defect: orphaned ? "missing-subject" : null);
        if (orphaned)
        {
            await using var mutation = fixture.Context();
            await mutation.NotificationIntents.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        }
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer),
            nativeNotificationOwner: true).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(result.Purged).IsEqualTo(1);
        await Assert.That(result.OriginalWritesProven).IsEqualTo(orphaned ? 0 : 1);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Instance.TrustDomainId;
        await Assert.That(await reader.NotificationIntents.IgnoreQueryFilters(["SoftDelete"]).CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNotices.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNoticeNotificationReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationCases.CountAsync()).IsEqualTo(1);
        var effect = await reader.ModerationEffectIntents.SingleAsync(row => row.Id == effectId);
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(effect.HasOriginalNoticeCleanup(fixture.Database.HostId, fixture.Database.TenantId, Ceiling))
            .IsEqualTo(!orphaned);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Native_materialization_and_repair_require_the_original_source_even_with_a_stale_pointer(bool repair)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        await fixture.SeedAsync(nativePointer: true);
        NotificationIntent pointer;
        await using (var discovery = fixture.Context())
            pointer = await discovery.NotificationIntents.AsNoTracking().SingleAsync();
        await using (var cleanup = fixture.Context())
        {
            var result = await fixture.Repository(cleanup, await fixture.AuthenticateAsync(cleanup),
                nativeNotificationOwner: true).PruneAsync(null, 10);
            await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        }
        if (repair)
        {
            // Reproduce a stale graph independently of the guarded materializer. Repair must
            // reject missing original proof even when its normal intent lookup would succeed.
            await using var staleWriter = fixture.Context();
            staleWriter.NotificationIntents.Add(pointer);
            await staleWriter.SaveChangesAsync();
        }
        await using (var writer = fixture.Context())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            writer.ModerationTrustDomainId = fixture.Database.Instance.TrustDomainId;
            var repository = new NotificationIntentRepository(writer,
                new RelationalSettingMutationLock(writer, new EfCoreUnitOfWork(writer)), fixture.Clock);
            InvalidOperationException? rejection = null;
            try
            {
                if (repair)
                    await repository.RepairMissingRecipientDeliveryRowsAsync(pointer, [], null, null);
                else
                    await repository.CreateGraphAsync(pointer);
            }
            catch (InvalidOperationException exception)
            {
                rejection = exception;
            }
            await Assert.That(rejection).IsNotNull();
        }
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Instance.TrustDomainId;
        await Assert.That(await reader.NotificationIntents.IgnoreQueryFilters(["SoftDelete"]).CountAsync()).IsEqualTo(repair ? 1 : 0);
        await Assert.That(await reader.NotificationDeliveries.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.Notifications.IgnoreQueryFilters(["SoftDelete"]).CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
    }

    [Test]
    [Arguments("ordinary")]
    [Arguments("configured")]
    [Arguments("other-service")]
    [Arguments("foreign-credential-tenant")]
    public async Task Cleanup_requires_the_installed_native_maintenance_credential(string substitution)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        await fixture.SeedAsync();
        await using var writer = fixture.Context();
        var machine = await fixture.AuthenticateAsync(writer);
        if (substitution == "ordinary") machine.Clear();
        else if (substitution == "configured")
            machine.SetPrincipal(machine.Current! with { CredentialSource = ApiKeyCredentialSource.Configured });
        else if (substitution == "foreign-credential-tenant")
            machine.SetPrincipal(machine.Current! with { TenantId = Guid.CreateVersion7() });
        else
        {
            await using var mutation = fixture.Context();
            var service = new ServicePrincipal
            {
                Id = Guid.CreateVersion7(),
                Code = $"other-{Guid.CreateVersion7():N}",
                DisplayName = "Unrelated service"
            };
            mutation.ServicePrincipals.Add(service);
            await mutation.SaveChangesAsync();
            await mutation.ModerationMachineSubjectBindings.ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.ServicePrincipalId, service.Id));
        }
        var result = await fixture.Repository(writer, machine).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Denied);
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Instance.TrustDomainId;
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("clock")]
    [Arguments("principal")]
    [Arguments("key")]
    [Arguments("trust-domain")]
    public async Task Final_flush_loss_of_time_or_credential_authority_rolls_back_privacy_cleanup(string failure)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        await fixture.SeedAsync();
        MachinePrincipalAccessor? machine = null;
        var observer = new CleanupSaved(async context =>
        {
            if (failure == "clock") fixture.Clock.UtcNow = Ceiling.AddTicks(-1);
            else if (failure == "principal") machine!.Clear();
            else if (failure == "trust-domain") context.ModerationTrustDomainId = null;
            else
                await context.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.ExternalApiKeyStatusId, (int)ExternalApiKeyStatusEnum.Revoked));
        });
        await using var writer = fixture.Context(observer);
        machine = await fixture.AuthenticateAsync(writer);
        var result = await fixture.Repository(writer, machine).PruneAsync(null, 10);
        await Assert.That(result.Outcome == ModerationNoticeCleanupOutcome.Applied).IsFalse();
        await Assert.That(observer.Observed).IsTrue();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Instance.TrustDomainId;
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync()).OriginalNoticeCleanupId).IsNull();
    }

    [Test]
    public async Task Lost_commit_acknowledgement_never_claims_success_or_reconstructs_deleted_content()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effectId = await fixture.SeedAsync();
        var fault = new LostCleanupAcknowledgement();
        await using (var writer = fixture.Context(fault))
        {
            var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 10);
            await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
            await Assert.That(result.LastScannedApprovalId).IsNull();
        }
        await Assert.That(fault.Observed).IsTrue();
        await using var retry = fixture.Context();
        var replay = await fixture.Repository(retry, await fixture.AuthenticateAsync(retry)).PruneAsync(null, 10);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(replay.Purged).IsEqualTo(0);
        retry.ModerationTrustDomainId = fixture.Database.Instance.TrustDomainId;
        await Assert.That(await retry.ModerationNoticeSources.CountAsync()).IsEqualTo(0);
        var effect = await retry.ModerationEffectIntents.SingleAsync(row => row.Id == effectId);
        await Assert.That(effect.OriginalNoticeCleanupId).IsEqualTo(fault.CleanupId);
        await Assert.That(effect.CompletedAt).IsNull();
    }

    private sealed class CleanupSaved(Func<Explore.Persistence.ExploreDbContext, Task> action) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ModerationEffectIntent>()
                .Any(entry => entry.Entity.OriginalNoticeCleanupId is not null))
            {
                Observed = true;
                await action((Explore.Persistence.ExploreDbContext)eventData.Context);
            }
            return result;
        }
    }

    private sealed class LostCleanupAcknowledgement : DbTransactionInterceptor
    {
        public bool Observed { get; private set; }
        public Guid? CleanupId { get; private set; }
        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Observed = true;
            CleanupId = eventData.Context!.ChangeTracker.Entries<ModerationEffectIntent>()
                .Single().Entity.OriginalNoticeCleanupId;
            throw new TimeoutException("Native cleanup commit acknowledgement unavailable.");
        }
    }
}
