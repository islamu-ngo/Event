using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed partial class EventDiscoveryEnrollmentProducerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LostConnectorAfterAcknowledgedLocalCommitPreservesEffectsAndBothOwnedCleanupFailures(bool bulk)
    {
        await using var store = await Store.CreateAsync();
        var fault = new CloseLocalWriterAfterCommit();
        await using var writer = store.Open(store.TenantId, fault);
        var tenant = await writer.Tenants.SingleAsync(row => row.Id == store.TenantId);
        string originalName = tenant.FullName;
        fault.Bind(writer);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (bulk)
                await new TenantRepository(writer).TryTransitionStatusAsync(
                    store.TenantId, (int)TenantStatusEnum.Active, (int)TenantStatusEnum.Suspended,
                    DateTime.UtcNow, Guid.CreateVersion7());
            else
            {
                tenant.FullName = "Committed before connector loss";
                tenant.TenantStatusId = (int)TenantStatusEnum.Suspended;
                await writer.SaveChangesAsync();
            }
        });

        await Assert.That(fault.Commits).IsEqualTo(1);
        await Assert.That(fault.OriginalGatesHeld).IsTrue();
        await Assert.That(failure.Message).IsEqualTo("discovery_committed_gate_cleanup_failed");
        var causes = (failure.InnerException as AggregateException)?.Flatten().InnerExceptions
            ?? throw new InvalidOperationException("Expected both owned gate cleanup failures.");
        await Assert.That(causes.Any(error => error.Message == "discovery_enrollment_cleanup_lost_custody")).IsTrue();
        await Assert.That(causes.Any(error => error.Message == "human_security_source_cleanup_lost_custody")).IsTrue();
        await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(writer)).IsFalse();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(writer)).IsFalse();

        await using var reader = store.Open(store.TenantId);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var human = await new HumanSecuritySourceGate(reader).AcquireAsync(deadline.Token);
        await using var discovery = await new EventDiscoveryEnrollmentGate(reader).AcquireAsync(deadline.Token);
        var committed = await reader.Tenants.AsNoTracking().SingleAsync();
        var revision = await reader.Set<EventDiscoveryRevision>().AsNoTracking().SingleAsync();
        await Assert.That(committed.FullName).IsEqualTo(bulk ? originalName : "Committed before connector loss");
        await Assert.That(committed.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Suspended);
        await Assert.That(revision.Id).IsEqualTo(store.RevisionId);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(7);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(12);
    }

    private sealed class CloseLocalWriterAfterCommit : DbTransactionInterceptor
    {
        private ExploreDbContext? original;
        internal int Commits { get; private set; }
        internal bool OriginalGatesHeld { get; private set; }

        internal void Bind(ExploreDbContext context) => original = context;

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(eventData.Context, original) || original is null)
                return;
            Commits++;
            original.RequireBoundTenantLease();
            OriginalGatesHeld = HumanSecuritySourceGate.TryRequireCurrent(original)
                && EventDiscoveryEnrollmentGate.TryRequireCurrent(original);
            // The real provider commit is acknowledged. Losing this connector must
            // not replay the write or let either owned cleanup hide the other failure.
            await original.Database.GetDbConnection().CloseAsync();
        }
    }
}
