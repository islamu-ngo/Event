using System.Data.Common;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed partial class EventDiscoveryEnrollmentProducerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RepositoryDeletionKeepsOriginalEnrollmentCustodyThroughSettlement(bool gateAlreadyHeld)
    {
        await using var store = await Store.CreateAsync();
        var probe = new DeletionEnrollmentProbe();
        await using var writer = store.Open(store.TenantId, probe);
        var tenant = await writer.Tenants.SingleAsync();
        var connector = writer.Database.GetDbConnection();
        await using var retained = gateAlreadyHeld
            ? await new EventDiscoveryEnrollmentGate(writer).AcquireAsync() : null;

        await new TenantRepository(writer).Delete(tenant);

        await Assert.That(probe.HeldBeforeBegin).IsTrue();
        await Assert.That(probe.HeldThroughCommit).IsTrue();
        await Assert.That(writer.Database.GetDbConnection()).IsSameReferenceAs(connector);
        await Assert.That(writer.Database.CurrentTransaction).IsNull();
        await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(writer)).IsEqualTo(gateAlreadyHeld);
        await using var reader = store.Open(store.TenantId);
        await Assert.That(await reader.Tenants.AnyAsync()).IsFalse();
        await Assert.That(await reader.Set<EventDiscoveryRevision>().AnyAsync()).IsFalse();
        await Assert.That(await reader.Set<EventDiscoverySnapshotReservation>().AnyAsync()).IsFalse();
    }

    private sealed class DeletionEnrollmentProbe : DbTransactionInterceptor
    {
        internal bool HeldBeforeBegin { get; private set; }
        internal bool HeldThroughCommit { get; private set; }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext context)
            {
                EventDiscoveryEnrollmentGate.RequireCurrent(context);
                HeldBeforeBegin = true;
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext context)
            {
                EventDiscoveryEnrollmentGate.RequireCurrent(context);
                HeldThroughCommit = true;
            }
            return ValueTask.FromResult(result);
        }
    }
}
