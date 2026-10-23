using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed partial class EventDiscoveryEnrollmentProducerTests
{
    [Test]
    public async Task ExplicitGlobalSettingFanoutWithoutPreBeginCustodyRejectsBeforeSourceWrites()
    {
        await using var store = await Store.CreateAsync();
        var commands = new TenantDmlObserver();
        await using var writer = store.Open(store.TenantId, commands);
        var setting = await writer.SystemSettings.SingleAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventValidationProfile);
        string before = setting.Value;
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            setting.Value = "\"community_lexicon\"";
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveChangesAsync());
            await Assert.That(refusal.Message).IsEqualTo("discovery_enrollment_gate_missing");
            await Assert.That(commands.TenantCommands).IsEqualTo(0);
            await transaction.RollbackAsync();
        }
        await using var reader = store.Open(store.TenantId);
        await Assert.That((await reader.SystemSettings.SingleAsync(row => row.SettingKey == setting.SettingKey)).Value)
            .IsEqualTo(before);
        await Assert.That((await reader.Set<EventDiscoveryRevision>().SingleAsync()).DisclosureEpoch).IsEqualTo(11);
    }

    [Test]
    public async Task ImplicitGlobalSettingFanoutAcquiresItsOriginalGateBeforeBegin()
    {
        await using var store = await Store.CreateAsync();
        var probe = new FanoutBeginProbe();
        await using var writer = store.Open(null, probe);
        var setting = await writer.SystemSettings.SingleAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventValidationProfile);
        setting.Value = "\"community_lexicon\"";

        await writer.SaveChangesAsync();

        await Assert.That(probe.ObservedPreBeginCustody).IsTrue();
        await Assert.That(writer.Database.CurrentTransaction).IsNull();
        await using var reader = store.Open(store.TenantId);
        await Assert.That((await reader.SystemSettings.SingleAsync(row => row.SettingKey == setting.SettingKey)).Value)
            .IsEqualTo("\"community_lexicon\"");
        var revision = await reader.Set<EventDiscoveryRevision>().SingleAsync();
        await Assert.That(revision.Id).IsEqualTo(store.RevisionId);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(7);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(12);
    }

    private sealed class FanoutBeginProbe : DbTransactionInterceptor
    {
        internal bool ObservedPreBeginCustody { get; private set; }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext context)
            {
                EventDiscoveryEnrollmentGate.RequireCurrent(context);
                ObservedPreBeginCustody = true;
            }
            return ValueTask.FromResult(result);
        }
    }
}
