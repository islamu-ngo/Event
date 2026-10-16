using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using NSubstitute;

namespace Event.Application.UnitTests.Services;

public sealed class EventResourceAuditRetentionServiceTests
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ExpiryDeletesTheWholeRowAtTheInclusiveDeadline()
    {
        var tenant = Guid.CreateVersion7();
        var expired = Audit(tenant, Now.AddDays(-30));
        var retained = Audit(tenant, Now.AddDays(-30).AddTicks(1));
        var fixture = new Fixture([expired, retained], new() { [tenant] = 30 });
        await Assert.That(await fixture.Service.CleanupAsync(default)).IsEqualTo(1L);
        await Assert.That(fixture.Rows.Select(row => row.Id)).IsEquivalentTo([retained.Id]);
        await Assert.That(retained.ResponsibleManagerUserId).IsNotNull();
    }

    [Test]
    public async Task ZeroRetentionPurgesEveryExistingRowAcrossDeleteBatches()
    {
        var tenant = Guid.CreateVersion7();
        var rows = Enumerable.Range(0, EventResourceAuditRetentionService.DeleteBatchSize + 1)
            .Select(_ => Audit(tenant, Now.AddDays(1))).ToList();
        var fixture = new Fixture(rows, new() { [tenant] = 0 });
        await Assert.That(await fixture.Service.CleanupAsync(default))
            .IsEqualTo((long)EventResourceAuditRetentionService.DeleteBatchSize + 1);
        await Assert.That(fixture.Rows).IsEmpty();
    }

    [Test]
    public async Task UnexpiredTenantsCannotStrandAnotherTenantBeyondTheFirstPage()
    {
        var tenants = Enumerable.Range(0, EventResourceAuditRetentionService.TenantPageSize + 1)
            .Select(_ => Guid.CreateVersion7()).OrderBy(id => id).ToArray();
        var rows = tenants.Select(tenant => Audit(tenant, Now)).ToList();
        var retention = tenants.ToDictionary(tenant => tenant, _ => 90);
        retention[tenants[^1]] = 0;
        var fixture = new Fixture(rows, retention);
        await Assert.That(await fixture.Service.CleanupAsync(default)).IsEqualTo(1L);
        await Assert.That(fixture.Rows.Count).IsEqualTo(EventResourceAuditRetentionService.TenantPageSize);
        await Assert.That(fixture.Rows.Any(row => row.TenantId == tenants[^1])).IsFalse();
    }

    [Test]
    public async Task InvalidCurrentGovernanceFailsWithoutDeletingAuditEvidence()
    {
        var tenant = Guid.CreateVersion7();
        var expired = Audit(tenant, Now.AddDays(-90));
        var fixture = new Fixture([expired], new() { [tenant] = 30 }, invalidPolicy: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CleanupAsync(default));
        await Assert.That(fixture.Rows.Select(row => row.Id)).IsEquivalentTo([expired.Id]);
    }

    private static EventResourceAuditEntry Audit(Guid tenant, DateTime occurredAt) =>
        EventResourceAuditEntry.Create(tenant, Guid.CreateVersion7(), Guid.CreateVersion7(),
            EventResourceAuditAction.UpdateMetadata, EventResourceAuditOutcome.Succeeded,
            EventResourceAuditReason.OrganizerMutation, occurredAt);

    private sealed class Fixture
    {
        public List<EventResourceAuditEntry> Rows { get; }
        public EventResourceAuditRetentionService Service { get; }

        public Fixture(IEnumerable<EventResourceAuditEntry> rows, Dictionary<Guid, int> retention,
            bool invalidPolicy = false)
        {
            Rows = rows.ToList();
            var owners = Substitute.For<IEventResourceAuditOwnerDiscoveryRepository>();
            var repository = Substitute.For<IEventResourceAuditRetentionRepository>();
            owners.GetTenantIdsWithAuditAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => Rows.Select(row => row.TenantId).Distinct().OrderBy(id => id)
                    .Where(id => call.Arg<Guid?>() is not { } cursor || id.CompareTo(cursor) > 0)
                    .Take(call.Arg<int>()).ToArray());
            repository.DeleteExpiredBatchAsync(Arg.Any<Guid>(), Arg.Any<DateTime?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var ids = Rows.Where(row => row.TenantId == call.Arg<Guid>()
                            && (call.Arg<DateTime?>() is not { } cutoff || row.Timestamp <= cutoff))
                        .Take(call.Arg<int>()).Select(row => row.Id).ToHashSet();
                    return Rows.RemoveAll(row => ids.Contains(row.Id));
                });
            var governance = Substitute.For<IEventResourceGovernancePolicyReader>();
            governance.ReadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (invalidPolicy) return null;
                var defaults = EventResourceGovernancePolicy.Default(long.MaxValue);
                return EventResourceGovernancePolicy.Create(defaults.EnabledDeliveryTypes, defaults.EnabledAudiences,
                    defaults.PermittedFileTypes, defaults.MaxUploadBytes, false, [], retention[call.Arg<Guid>()],
                    defaults.MaxActiveResources, long.MaxValue);
            });
            var unit = Substitute.For<IUnitOfWork>();
            unit.ExecuteSerializableAsync(Arg.Any<Func<CancellationToken, Task<int>>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var operation = call.Arg<Func<CancellationToken, Task<int>>>();
                    ArgumentNullException.ThrowIfNull(operation);
                    return operation(call.Arg<CancellationToken>());
                });
            var factory = Substitute.For<IEventResourceAuditRetentionExecutionScopeFactory>();
            factory.CreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(_ => new ExecutionScope(repository, governance, unit));
            Service = new(owners, factory, new Clock());
        }
    }

    private sealed class ExecutionScope(
        IEventResourceAuditRetentionRepository repository,
        IEventResourceGovernancePolicyReader governance,
        IUnitOfWork unitOfWork) : IEventResourceAuditRetentionExecutionScope
    {
        public IEventResourceAuditRetentionRepository Repository { get; } = repository;
        public IEventResourceGovernancePolicyReader Governance { get; } = governance;
        public IUnitOfWork UnitOfWork { get; } = unitOfWork;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
