using System.Collections.Concurrent;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed class PdsDrainExecutionAuthorityTests
{
    [Test]
    [Arguments(90, false)]
    [Arguments(91, false)]
    [Arguments(30, true)]
    public async Task FinalGateCompletionHonorsPersistedRenewedExpiry(int gateSeconds, bool delivered)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        using var clock = new HandoffClock();
        var repository = fixture.Services.GetRequiredService<IPdsSyncOutboxRepository>();
        var outbox = Pending(fixture.TenantId, fixture.UserId);
        await repository.AddAsync(outbox);
        PdsSyncClaim claim = (await repository.ClaimDueAsync(
            1, "handoff-worker", clock.GetUtcNow().UtcDateTime, TimeSpan.FromSeconds(90))).Single();
        clock.Advance(TimeSpan.FromSeconds(1));
        DateTime renewedExpiry = clock.GetUtcNow().UtcDateTime.AddSeconds(90);
        var gate = new FinalHandoffGate(async () =>
        {
            await using var scope = fixture.CreateScope();
            var persisted = await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
                .PdsSyncOutbox.AsNoTracking().SingleAsync(row => row.Id == outbox.Id);
            await Assert.That(persisted.LeaseExpiresAt).IsEqualTo((DateTime?)renewedExpiry);
            await Assert.That(persisted.LeaseToken).IsEqualTo((Guid?)claim.LeaseToken);
            await Assert.That(persisted.LeaseFence).IsEqualTo(claim.LeaseFence);
            clock.Advance(TimeSpan.FromSeconds(gateSeconds));
        });
        var gateway = new HandoffGateway();
        var processor = new AtprotoPdsDeliveryProcessor(repository, gate, gateway, clock);

        AtprotoPdsClaimResult result = await processor.ProcessAsync(
            claim, TimeSpan.FromSeconds(90), default);

        await Assert.That(result.Outcome).IsEqualTo(delivered
            ? AtprotoPdsClaimOutcome.Delivered : AtprotoPdsClaimOutcome.ClaimLost);
        await Assert.That(gateway.Calls).IsEqualTo(delivered ? 1 : 0);
        fixture.Context.ChangeTracker.Clear();
        var stored = await fixture.Context.PdsSyncOutbox.AsNoTracking().SingleAsync(row => row.Id == outbox.Id);
        await Assert.That(stored.Status).IsEqualTo(delivered ? PdsSyncStatus.Completed : PdsSyncStatus.Processing);
        await Assert.That(stored.RetryCount).IsEqualTo(0);
        if (!delivered)
        {
            await Assert.That(stored.LeaseExpiresAt).IsEqualTo((DateTime?)renewedExpiry);
            await Assert.That(await repository.GetActiveClaimAsync(claim, clock.GetUtcNow().UtcDateTime)).IsNull();
            await Assert.That(await fixture.Context.AtprotoRecords.AnyAsync()).IsFalse();
        }
        else
        {
            var request = gateway.Request ?? throw new InvalidOperationException("No delivery request.");
            await Assert.That(request.TenantId).IsEqualTo(outbox.TenantId);
            await Assert.That(request.UserId).IsEqualTo(outbox.UserId);
            await Assert.That(request.Did).IsEqualTo(outbox.Did);
            await Assert.That(request.PdsHost.AbsoluteUri).IsEqualTo(outbox.PdsHost);
            await Assert.That(request.RecordKey).IsEqualTo(outbox.RecordKey);
            await Assert.That(request.Payload).IsEqualTo(outbox.Payload);
            await Assert.That(request.ExpectedCid).IsEqualTo(outbox.ExpectedCid);
            var record = await fixture.Context.AtprotoRecords.AsNoTracking().SingleAsync();
            await Assert.That(record.Cid).IsEqualTo("bafy-handoff");
            await Assert.That(record.RecordJson).IsEqualTo(outbox.Payload);
        }
    }

    [Test]
    public async Task FinalGateCompletionCannotBorrowReplacementTokenCustody()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        using var clock = new HandoffClock();
        var repository = fixture.Services.GetRequiredService<IPdsSyncOutboxRepository>();
        var outbox = Pending(fixture.TenantId, fixture.UserId);
        await repository.AddAsync(outbox);
        PdsSyncClaim claim = (await repository.ClaimDueAsync(
            1, "original-worker", clock.GetUtcNow().UtcDateTime, TimeSpan.FromSeconds(90))).Single();
        PdsSyncClaim? replacement = null;
        var gate = new FinalHandoffGate(async () =>
        {
            clock.Advance(TimeSpan.FromSeconds(30));
            await using var scope = fixture.CreateScope();
            var contender = scope.ServiceProvider.GetRequiredService<IPdsSyncOutboxRepository>();
            await Assert.That(await contender.TryFailAsync(
                claim, "provider_timeout", true, clock.GetUtcNow().UtcDateTime, TimeSpan.Zero)).IsTrue();
            replacement = (await contender.ClaimDueAsync(
                1, "replacement-worker", clock.GetUtcNow().UtcDateTime, TimeSpan.FromSeconds(90))).Single();
        });
        var gateway = new HandoffGateway();
        var processor = new AtprotoPdsDeliveryProcessor(repository, gate, gateway, clock);

        AtprotoPdsClaimResult result = await processor.ProcessAsync(
            claim, TimeSpan.FromSeconds(90), default);

        await Assert.That(result.Outcome).IsEqualTo(AtprotoPdsClaimOutcome.ClaimLost);
        await Assert.That(gateway.Calls).IsEqualTo(0);
        var currentClaim = replacement ?? throw new InvalidOperationException("No replacement claim.");
        await Assert.That(currentClaim.LeaseToken).IsNotEqualTo(claim.LeaseToken);
        await Assert.That(currentClaim.LeaseFence).IsEqualTo(claim.LeaseFence + 1);
        await Assert.That(await repository.GetActiveClaimAsync(claim, clock.GetUtcNow().UtcDateTime)).IsNull();
        await Assert.That(await repository.GetActiveClaimAsync(currentClaim, clock.GetUtcNow().UtcDateTime)).IsNotNull();
        fixture.Context.ChangeTracker.Clear();
        var stored = await fixture.Context.PdsSyncOutbox.AsNoTracking().SingleAsync(row => row.Id == outbox.Id);
        await Assert.That(stored.LeaseToken).IsEqualTo((Guid?)currentClaim.LeaseToken);
        await Assert.That(stored.RetryCount).IsEqualTo(1);
        await Assert.That(await fixture.Context.AtprotoRecords.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task TransportCancellationUsesOnlyRemainingRenewedDuration()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        using var clock = new HandoffClock();
        var repository = fixture.Services.GetRequiredService<IPdsSyncOutboxRepository>();
        var outbox = Pending(fixture.TenantId, fixture.UserId);
        await repository.AddAsync(outbox);
        PdsSyncClaim claim = (await repository.ClaimDueAsync(
            1, "timeout-worker", clock.GetUtcNow().UtcDateTime, TimeSpan.FromSeconds(90))).Single();
        var gate = new FinalHandoffGate(() =>
        {
            clock.Advance(TimeSpan.FromSeconds(80));
            return Task.CompletedTask;
        });
        var gateway = new HandoffGateway(clock);
        var processor = new AtprotoPdsDeliveryProcessor(repository, gate, gateway, clock);

        AtprotoPdsClaimResult result = await processor.ProcessAsync(
            claim, TimeSpan.FromSeconds(90), default);

        await Assert.That(gateway.Calls).IsEqualTo(1);
        await Assert.That(gateway.CancellationObserved).IsTrue();
        await Assert.That(clock.TimerDuration).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(result.FailureCode).IsEqualTo("provider_timeout");
        await Assert.That(result.FailureDisposition).IsEqualTo(AtprotoPdsFailureDisposition.RetryScheduled);
        fixture.Context.ChangeTracker.Clear();
        var stored = await fixture.Context.PdsSyncOutbox.AsNoTracking().SingleAsync(row => row.Id == outbox.Id);
        await Assert.That(stored.Status).IsEqualTo(PdsSyncStatus.Pending);
        await Assert.That(stored.RetryCount).IsEqualTo(1);
        await Assert.That(await fixture.Context.AtprotoRecords.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task MixedTenantClaimsBindBeforeConstructingRealDeliveryGraphAndPreserveCaller()
    {
        var constructed = new ConcurrentBag<(Guid Tenant, ExploreDbContext Context)>();
        var gateway = new ObservedGateway();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            var ordinaryTenant = (ITenantContext)services.Last(
                descriptor => descriptor.ServiceType == typeof(ITenantContext)).ImplementationInstance!;
            services.AddScoped<ITenantContext>(provider => new ExecutionTenant(
                provider.GetRequiredService<ITenantContextAccessor>(), ordinaryTenant));
            services.AddSingleton<IAtprotoPdsDeliveryGateway>(gateway);
            services.AddScoped(provider =>
            {
                Guid tenant = provider.GetRequiredService<ITenantContextAccessor>().TenantId
                    ?? throw new InvalidOperationException("Delivery graph constructed before tenant binding.");
                var context = provider.GetRequiredService<ExploreDbContext>();
                if (context.TenantFilterTenantId != tenant)
                    throw new InvalidOperationException("Delivery context does not own the admitted tenant.");
                constructed.Add((tenant, context));
                return ActivatorUtilities.CreateInstance<AtprotoPdsDeliveryProcessor>(provider);
            });
        });
        Guid foreignTenant = Guid.CreateVersion7();
        var caller = fixture.Context;
        var disabled = await caller.SystemSettings.SingleAsync(
            row => row.SettingKey == "federation.atproto_events_enabled");
        disabled.Value = "false";
        disabled.IsLocked = false;
        PdsSyncOutbox own = Pending(fixture.TenantId, fixture.UserId);
        caller.PdsSyncOutbox.Add(own);
        await caller.SaveChangesAsync();
        caller.ChangeTracker.Clear();

        await using (var seedScope = fixture.CreateScope())
        {
            seedScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                .BindExecutionTenant(foreignTenant);
            var seed = seedScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            seed.Tenants.Add(new Tenant
            {
                Id = foreignTenant,
                FullName = "Foreign delivery tenant",
                Slug = $"delivery-{foreignTenant:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            seed.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = foreignTenant,
                Tenant = null!,
                UserId = fixture.UserId,
                User = null!,
                ActorId = fixture.ActorId,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            seed.PdsSyncOutbox.Add(Pending(foreignTenant, fixture.UserId));
            await seed.SaveChangesAsync();
        }

        PdsSyncDrainResult result = await fixture.Services.GetRequiredService<IPdsSyncDrainService>()
            .ProcessBatchAsync(default);

        await Assert.That(result.ClaimedCount).IsEqualTo(2);
        await Assert.That(result.FailedCount).IsEqualTo(2);
        await Assert.That(result.ClaimLostCount).IsEqualTo(0);
        await Assert.That(constructed.Select(item => item.Tenant))
            .IsEquivalentTo([fixture.TenantId, foreignTenant]);
        await Assert.That(constructed.Select(item => item.Context).Distinct().Count()).IsEqualTo(2);
        await Assert.That(gateway.Calls).IsEqualTo(0);
        await Assert.That(caller.TenantFilterTenantId).IsEqualTo((Guid?)fixture.TenantId);
        await Assert.That(caller.ChangeTracker.Entries()).IsEmpty();
        await Assert.That((await caller.PdsSyncOutbox.AsNoTracking().SingleAsync(row => row.Id == own.Id)).Status)
            .IsEqualTo(PdsSyncStatus.DeadLettered);
        await Assert.That(await caller.PdsSyncOutbox.AnyAsync(row => row.TenantId == foreignTenant)).IsFalse();
    }

    private static PdsSyncOutbox Pending(Guid tenant, Guid user) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant,
        UserId = user,
        Did = "did:plc:delivery-authority",
        Collection = "community.lexicon.calendar.event",
        RecordKey = Guid.CreateVersion7().ToString("N"),
        SourceEntityType = "Event",
        SourceEntityId = Guid.CreateVersion7(),
        SourceVersion = Guid.CreateVersion7(),
        Operation = PdsSyncOperation.Create,
        Payload = "{}",
        PayloadHash = Convert.ToHexStringLower(SHA256.HashData("{}"u8)),
        IdempotencyKey = Guid.CreateVersion7().ToString("N"),
        PdsHost = "https://pds.example.test/",
        Status = PdsSyncStatus.Pending,
        CreatedAt = DateTime.UtcNow.AddMinutes(-1),
        MaxRetries = 10
    };

    private sealed class ExecutionTenant(ITenantContextAccessor accessor, ITenantContext ordinary) : ITenantContext
    {
        public Guid TenantId => accessor.TenantId ?? ordinary.TenantId;
    }

    private sealed class FinalHandoffGate(Func<Task> completeFinalGate) : IAtprotoDeliveryGate
    {
        private int _calls;

        public async Task<AtprotoDeliveryGateResult> CheckDeliveryAsync(
            PdsSyncOutbox outbox, DateTimeOffset observedAt, CancellationToken cancellationToken)
        {
            if (++_calls == 2)
                await completeFinalGate();
            return AtprotoDeliveryGateResult.Permit();
        }
    }

    private sealed class HandoffGateway(HandoffClock? clock = null) : IAtprotoPdsDeliveryGateway
    {
        internal int Calls { get; private set; }
        internal AtprotoPdsDeliveryRequest? Request { get; private set; }
        internal bool CancellationObserved { get; private set; }

        public Task<AtprotoPdsDeliveryResult> DeliverAsync(
            AtprotoPdsDeliveryRequest command, CancellationToken cancellationToken)
        {
            Calls++;
            Request = command;
            if (clock is not null)
            {
                clock.Advance(TimeSpan.FromSeconds(5));
                CancellationObserved = cancellationToken.IsCancellationRequested;
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(AtprotoPdsDeliveryResult.Failed("deadline_not_enforced", false));
            }
            return Task.FromResult(AtprotoPdsDeliveryResult.Success(
                $"at://{command.Did}/{command.Collection}/{command.RecordKey}", "bafy-handoff"));
        }
    }

    private sealed class HandoffClock : TimeProvider, IDisposable
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        private HandoffTimer? _timer;
        internal TimeSpan TimerDuration { get; private set; }
        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan duration)
        {
            _now += duration;
            _timer?.FireIfDue(_now);
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            TimerDuration = dueTime;
            _timer = new HandoffTimer(this, callback, state);
            _timer.Change(dueTime, period);
            return _timer;
        }

        public void Dispose() => _timer?.Dispose();

        private sealed class HandoffTimer(HandoffClock clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? _due;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed)
                    return false;
                if (period != Timeout.InfiniteTimeSpan)
                    throw new InvalidOperationException("Only single-shot handoff deadlines are expected.");
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetUtcNow() + dueTime;
                return true;
            }

            internal void FireIfDue(DateTimeOffset now)
            {
                if (_due is not { } due || now < due || _disposed)
                    return;
                _due = null;
                callback(state);
            }

            public void Dispose()
            {
                _disposed = true;
                _due = null;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ObservedGateway : IAtprotoPdsDeliveryGateway
    {
        private int _calls;
        internal int Calls => _calls;

        public Task<AtprotoPdsDeliveryResult> DeliverAsync(
            AtprotoPdsDeliveryRequest command, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(AtprotoPdsDeliveryResult.Failed("unexpected_transport", false));
        }
    }
}
