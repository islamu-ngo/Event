
using System.Data.Common;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Queries;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed class GuestRegistrationStatusTests
{
    private static readonly DateTimeOffset EventEnd = new(2027, 1, 1, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Deadline = new(2027, 1, 31, 14, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ConfirmedGuestStatusSurvivesHoldWithoutRestoringCheckoutAuthority()
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var query = await ConfirmAsync(fixture);
        clock.Now = clock.Now.AddHours(1);
        var status = await ReadAsync(fixture, query);
        await Assert.That(status).IsNotNull();
        await Assert.That(status!.StatusAccessUntil).IsEqualTo(Deadline);
        await Assert.That(status.RegistrationOrderStatusId).IsEqualTo((int)RegistrationOrderStatusEnum.Confirmed);
        await Assert.That(await fixture.ExecuteAsync<GetGuestRegistrationOrderQuery, GuestRegistrationOrderDto?>(
            new(query.EventId, query.OrderId, query.CapabilityToken))).IsNull();
        var continueResult = await fixture.ExecuteAsync<ContinueGuestRegistrationOrderCommand, GuestRegistrationOrderLifecycleResponseDto>(
            new(query.EventId, query.OrderId, query.CapabilityToken));
        await Assert.That(continueResult.IsSuccess).IsFalse();
        await Assert.That(continueResult.FailureCode).IsEqualTo("registration_order_not_found");
        await Assert.That(await fixture.Context.RegistrationOrderPii.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.ExecuteAsync<GetGuestRegistrationPaymentQuery, RegistrationPaymentDto?>(
            new(query.EventId, query.OrderId, query.CapabilityToken))).IsNull();
        var orderWithPii = await fixture.Context.RegistrationOrders.SingleAsync(order => order.Id == query.OrderId);
        string retainedName = Guid.CreateVersion7().ToString("N");
        orderWithPii.SetPii(RegistrationOrderPii.Create(query.OrderId, fixture.TenantId, retainedName,
            null, null, null, (int)RegistrationRetentionPolicyEnum.SensitiveShort, clock.Now.UtcDateTime));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        clock.Now = Deadline.AddDays(-1);
        var retainedPii = await fixture.Context.RegistrationOrderPii.SingleAsync();
        await Assert.That(retainedPii.RetentionUntil < clock.Now.UtcDateTime).IsTrue();
        status = await ReadAsync(fixture, query);
        await Assert.That(status).IsNotNull();
        string json = JsonSerializer.Serialize(status);
        await Assert.That(json.Contains(retainedName, StringComparison.Ordinal)).IsFalse();
        await Assert.That(json.Contains(query.CapabilityToken!, StringComparison.Ordinal)).IsFalse();
        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.EnumerateObject().All(property => property.Name is
            "EventId" or "OrderId" or "EventStatusId" or "RegistrationOrderStatusId" or "ConfirmedAt" or
            "CancelledAt" or "LastSessionEndUtc" or "StatusAccessUntil")).IsTrue();
        await Assert.That(query.ToString().Contains(query.CapabilityToken!, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments(-1, true)]
    [Arguments(0, false)]
    [Arguments(1, false)]
    public async Task DeadlineIsExclusive(long ticks, bool allowed)
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var query = await ConfirmAsync(fixture);
        clock.Now = Deadline.AddTicks(ticks);
        await Assert.That(await ReadAsync(fixture, query) is not null).IsEqualTo(allowed);
    }

    [Test]
    public async Task EarlierAndNullScheduleCannotShortenAndLaterReadPersistsItsPromise()
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var query = await ConfirmAsync(fixture);
        await SetEndAsync(fixture, query.EventId, EventEnd.AddDays(-10));
        await Assert.That((await ReadAsync(fixture, query))!.StatusAccessUntil).IsEqualTo(Deadline);
        await SetEndAsync(fixture, query.EventId, null);
        var noEnd = await ReadAsync(fixture, query);
        await Assert.That(noEnd!.LastSessionEndUtc).IsNull();
        await Assert.That(noEnd.StatusAccessUntil).IsEqualTo(Deadline);
        await SetEndAsync(fixture, query.EventId, EventEnd.AddDays(10));
        await Assert.That((await ReadAsync(fixture, query))!.StatusAccessUntil).IsEqualTo(Deadline.AddDays(10));
        await SetEndAsync(fixture, query.EventId, EventEnd.AddDays(-10));
        clock.Now = Deadline.AddDays(1);
        await Assert.That((await ReadAsync(fixture, query))!.StatusAccessUntil).IsEqualTo(Deadline.AddDays(10));
    }

    [Test]
    public async Task ExpiredPromiseDoesNotReviveUnderLaterSchedule()
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var query = await ConfirmAsync(fixture);
        clock.Now = Deadline;
        await SetEndAsync(fixture, query.EventId, EventEnd.AddDays(100));
        await Assert.That(await ReadAsync(fixture, query)).IsNull();
    }

    [Test]
    [Arguments("missing-token")]
    [Arguments("malformed-token")]
    [Arguments("wrong-token")]
    [Arguments("foreign-event")]
    [Arguments("foreign-order")]
    [Arguments("foreign-tenant")]
    [Arguments("deleted-order")]
    [Arguments("deleted-event")]
    [Arguments("unconfirmed")]
    [Arguments("missing-promise")]
    [Arguments("expired-state")]
    public async Task UnauthorizedAndPriorStatesAreIndistinguishableAbsence(string scenario)
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var query = await ConfirmAsync(fixture, confirm: scenario != "unconfirmed");
        switch (scenario)
        {
            case "missing-token": query = query with { CapabilityToken = null }; break;
            case "malformed-token": query = query with { CapabilityToken = new string('!', 43) }; break;
            case "wrong-token": query = query with { CapabilityToken = fixture.Services.GetRequiredService<IGuestCapabilityTokenService>().Issue().RawToken }; break;
            case "foreign-event": query = query with { EventId = Guid.CreateVersion7() }; break;
            case "foreign-order": query = query with { OrderId = Guid.CreateVersion7() }; break;
            case "foreign-tenant":
                await using (var scope = fixture.CreateScope())
                {
                    var foreignTenant = new TenantScope(Guid.CreateVersion7());
                    var services = scope.ServiceProvider;
                    services.GetRequiredService<ExploreDbContext>().TenantContext = foreignTenant;
                    var handler = new GetGuestRegistrationStatusQueryHandler(
                        services.GetRequiredService<IGuestRegistrationCapabilityRepository>(),
                        services.GetRequiredService<IEventRepository>(),
                        services.GetRequiredService<IGuestCapabilityTokenService>(), foreignTenant,
                        services.GetRequiredService<IUnitOfWork>(), clock);
                    await Assert.That(await handler.Handle(query, CancellationToken.None)).IsNull();
                }
                return;
            case "expired-state":
                await fixture.Context.RegistrationOrders.Where(order => order.Id == query.OrderId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.RegistrationOrderStatusId,
                        (int)RegistrationOrderStatusEnum.Expired));
                break;
            case "deleted-order":
                await fixture.Context.RegistrationOrders.Where(order => order.Id == query.OrderId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.IsDeleted, true));
                break;
            case "deleted-event":
                await fixture.Context.Events.Where(target => target.Id == query.EventId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(target => target.IsDeleted, true));
                break;
            case "missing-promise":
                var order = await fixture.Context.RegistrationOrders.SingleAsync(order => order.Id == query.OrderId);
                fixture.Context.Entry(order).Property("GuestStatusAccessUntilUtc").CurrentValue = null;
                await fixture.Context.SaveChangesAsync();
                fixture.Context.ChangeTracker.Clear();
                break;
        }
        await Assert.That(await ReadAsync(fixture, query)).IsNull();
    }

    [Test]
    public async Task EventCancellationReportsFactsWithoutPretendingOrderWasRevoked()
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var query = await ConfirmAsync(fixture);
        var target = await fixture.Context.Events.SingleAsync(target => target.Id == query.EventId);
        target.Cancel(clock.Now.UtcDateTime);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var status = await ReadAsync(fixture, query);
        await Assert.That(status!.EventStatusId).IsEqualTo((int)EventStatusEnum.Cancelled);
        await Assert.That(status.RegistrationOrderStatusId).IsEqualTo((int)RegistrationOrderStatusEnum.Confirmed);
        await Assert.That(status.CancelledAt).IsNull();
        await Assert.That(status.StatusAccessUntil).IsEqualTo(Deadline);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingEndRejectsRetentionValidationBeforeCreatingOrderOrHold(bool paid)
    {
        await using var fixture = await CreateAsync(new Clock());
        var result = await StartAllocationAsync(fixture, null, paid);
        await AssertNoAllocationAsync(fixture, result, "registration_order_validation_failed");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnrepresentableStatusPromiseWithValidRetentionRejectsBeforeCreatingOrderOrHold(bool paid)
    {
        await using var fixture = await CreateAsync(new Clock());
        // Seven-day retention fits in DateTime; the separate 30-day status promise does not.
        var end = new DateTimeOffset(9999, 12, 15, 14, 0, 0, TimeSpan.Zero);
        var result = await StartAllocationAsync(fixture, end, paid);
        await AssertNoAllocationAsync(fixture, result, "registration_order_finite_status_window_required");
    }

    [Test]
    [Arguments(7, -1, true, false)]
    [Arguments(7, -1, true, true)]
    [Arguments(7, 0, false, false)]
    [Arguments(7, 0, false, true)]
    [Arguments(7, 1, false, false)]
    [Arguments(7, 1, false, true)]
    [Arguments(30, 0, false, false)]
    [Arguments(30, 0, false, true)]
    public async Task RetentionBoundaryPrecedesStatusPromiseAndDeniesWithoutAllocation(
        int daysAfterEnd, long ticks, bool allowed, bool paid)
    {
        var clock = new Clock { Now = EventEnd.AddDays(daysAfterEnd).AddTicks(ticks) };
        await using var fixture = await CreateAsync(clock);
        var result = await StartAllocationAsync(fixture, EventEnd, paid);
        if (!allowed)
        {
            await AssertNoAllocationAsync(fixture, result, "anonymous_registration_retention_expired");
            return;
        }

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.GuestCapabilityToken).IsNotNull();
        var order = await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync();
        await Assert.That(order.Id).IsEqualTo(result.Id);
        await Assert.That(order.AnonymousPiiRetentionUntilUtc)
            .IsEqualTo(new DateTime(2027, 1, 8, 14, 0, 0, DateTimeKind.Utc));
        await Assert.That(order.GuestStatusAccessUntilUtc).IsEqualTo(Deadline.UtcDateTime);
        var hold = await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync();
        await Assert.That(hold.RegistrationOrderId).IsEqualTo(result.Id);
        await Assert.That(await fixture.Context.RegistrationOrderLines.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.RegistrationOrderPii.CountAsync()).IsEqualTo(0);
    }

    private static async Task<GuestRegistrationOrderStartDto> StartAllocationAsync(
        EventVisitorCapabilitySqliteFixture fixture, DateTimeOffset? end, bool paid)
    {
        var target = await fixture.SeedEventAsync(published: true);
        await SetEndAsync(fixture, target.Id, end);
        var ticket = paid ? await SeedPaidTicketAsync(fixture, target.Id) : await fixture.SeedTicketAsync(target.Id);
        var proof = await fixture.IssueGuestProofAsync(new(target.Id, ticket.CatalogId,
            BookingPartyTypeEnum.Individual, [new(ticket.TicketId, 1, null)]));
        return await fixture.ExecuteAsync<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>(proof.Request);
    }

    private static async Task AssertNoAllocationAsync(EventVisitorCapabilitySqliteFixture fixture,
        GuestRegistrationOrderStartDto result, string failureCode)
    {
        await Assert.That(await fixture.Context.RegistrationOrders.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.RegistrationInventoryHolds.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.RegistrationOrderLines.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.RegistrationOrderPii.CountAsync()).IsEqualTo(0);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.GuestCapabilityToken).IsNull();
        await Assert.That(result.FailureCode).IsEqualTo(failureCode);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task ReadCommitOrPromiseCasWaitCrossingDeadlineCannotDisclose(int point)
    {
        var clock = new Clock();
        var barrier = new DeadlineBarrier(point);
        await using var fixture = await CreateAsync(clock, barrier);
        var query = await ConfirmAsync(fixture);
        if (point == 2) await SetEndAsync(fixture, query.EventId, EventEnd.AddDays(10));
        clock.Now = Deadline.AddTicks(-1);
        barrier.Arm();
        var pending = ReadAsync(fixture, query);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(pending.IsCompleted).IsFalse();
            clock.Now = Deadline;
        }
        finally { barrier.Release.TrySetResult(); }
        await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(20))).IsNull();
        var stored = await fixture.Services.GetRequiredService<IRegistrationInventoryRepository>()
            .GetOrderByIdAsync(query.OrderId, fixture.TenantId, CancellationToken.None);
        await Assert.That(stored!.GuestStatusAccessUntilUtc).IsEqualTo(Deadline.UtcDateTime);
    }

    [Test]
    public async Task TrackedOldEntitiesCannotOverrideCurrentFencedSnapshot()
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var query = await ConfirmAsync(fixture);
        await fixture.Context.RegistrationOrders.SingleAsync(order => order.Id == query.OrderId);
        await fixture.Context.Events.SingleAsync(target => target.Id == query.EventId);
        await using (var writer = fixture.CreateScope())
        {
            var context = writer.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await context.Events.Where(target => target.Id == query.EventId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(target => target.LastSessionEndUtc, EventEnd.AddDays(10)));
        }
        var current = await fixture.ExecuteAsync<GetGuestRegistrationStatusQuery, GuestRegistrationStatusDto?>(query);
        await Assert.That(current!.StatusAccessUntil).IsEqualTo(Deadline.AddDays(10));
        await using (var writer = fixture.CreateScope())
        {
            var context = writer.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await context.RegistrationOrders.Where(order => order.Id == query.OrderId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.IsDeleted, true));
        }
        await Assert.That(await fixture.ExecuteAsync<GetGuestRegistrationStatusQuery, GuestRegistrationStatusDto?>(query)).IsNull();
    }

    [Test]
    public async Task PersistedPostConfirmationCancellationReportsOnlyActualOrderFacts()
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var query = await ConfirmAsync(fixture);
        // Seed the persisted outcome of an independent lifecycle owner. P09 provides no cancellation command.
        await fixture.Context.RegistrationOrders.Where(order => order.Id == query.OrderId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(order => order.RegistrationOrderStatusId, (int)RegistrationOrderStatusEnum.Cancelled)
                .SetProperty(order => order.CancelledAt, clock.Now.UtcDateTime));
        var status = await ReadAsync(fixture, query);
        await Assert.That(status!.RegistrationOrderStatusId).IsEqualTo((int)RegistrationOrderStatusEnum.Cancelled);
        await Assert.That(status.CancelledAt).IsEqualTo(clock.Now.UtcDateTime);
        await Assert.That(status.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(status.StatusAccessUntil).IsEqualTo(Deadline);
    }

    private static Task<EventVisitorCapabilitySqliteFixture> CreateAsync(Clock clock, DeadlineBarrier? barrier = null) =>
        EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            if (barrier is not null)
                services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(barrier, barrier.Commits));
        });

    private static async Task<GetGuestRegistrationStatusQuery> ConfirmAsync(EventVisitorCapabilitySqliteFixture fixture, bool confirm = true)
    {
        var target = await fixture.SeedEventAsync(published: true);
        await SetEndAsync(fixture, target.Id, EventEnd);
        fixture.Context.EventSessions.Add(new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(), EventId = target.Id, Event = null!, TenantId = fixture.TenantId,
            Tenant = null!, RegistrationModeId = (int)RegistrationModeEnum.Open,
            StartTime = EventEnd.AddHours(-2), EndTime = EventEnd
        });
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var ticket = await fixture.SeedTicketAsync(target.Id);
        var proof = await fixture.IssueGuestProofAsync(new(target.Id, ticket.CatalogId,
            BookingPartyTypeEnum.Individual, [new(ticket.TicketId, 1, null)]));
        var created = await fixture.ExecuteAsync<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>(proof.Request);
        await Assert.That(created.IsSuccess).IsTrue();
        if (confirm)
        {
            var lifecycle = fixture.Services.GetRequiredService<IRegistrationOrderLifecycleService>();
            await Assert.That((await lifecycle.SubmitAsync(created.Id, fixture.TenantId, CancellationToken.None)).IsSuccess).IsTrue();
            await Assert.That((await lifecycle.ReadyForCheckoutAsync(created.Id, fixture.TenantId, CancellationToken.None)).IsSuccess).IsTrue();
            var confirmed = await lifecycle.FinalizeFreeAsync(created.Id, fixture.TenantId, CancellationToken.None);
            await Assert.That(confirmed.IsSuccess).IsTrue();
            var order = await fixture.Services.GetRequiredService<IRegistrationInventoryRepository>()
                .GetOrderByIdAsync(created.Id, fixture.TenantId, CancellationToken.None);
            await Assert.That(order!.RegistrationOrderStatusId).IsEqualTo((int)RegistrationOrderStatusEnum.Confirmed);
        }
        fixture.Context.ChangeTracker.Clear();
        return new(target.Id, created.Id, created.GuestCapabilityToken);
    }

    private static async Task<GuestRegistrationStatusDto?> ReadAsync(EventVisitorCapabilitySqliteFixture fixture,
        GetGuestRegistrationStatusQuery query)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRequestHandler<GetGuestRegistrationStatusQuery, GuestRegistrationStatusDto?>>()
            .Handle(query, CancellationToken.None);
    }

    private static async Task<(Guid CatalogId, Guid TicketId)> SeedPaidTicketAsync(
        EventVisitorCapabilitySqliteFixture fixture, Guid eventId)
    {
        var catalog = EventTicketCatalogVersion.Create(fixture.TenantId, eventId, "USD", 1);
        var pool = EventCapacityPool.Create(fixture.TenantId, eventId, "Paid allocation", 10, 900,
            CapacityHoldPolicyEnum.TimedHoldOnSelection, CapacityOversellPolicyEnum.Disallow, true);
        var ticket = EventTicketType.Create(Guid.CreateVersion7(), fixture.TenantId, catalog.Id,
            "Paid admission", "USD", TicketPricingModeEnum.Fixed, Money.Create(100, "USD"), null, null,
            ParticipantDataCollectionModeEnum.None, pool.Id, null, null, false, false, null, null, null, null);
        catalog.AddTicketType(ticket, pool);
        catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, fixture.TenantId, eventId, 1));
        catalog.UpdateCommercialDisclosures("Merchant", "Refund", "Support");
        catalog.Publish();
        fixture.Context.AddRange(catalog, pool);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return (catalog.Id, ticket.Id);
    }

    private static Task<int> SetEndAsync(EventVisitorCapabilitySqliteFixture fixture, Guid eventId, DateTimeOffset? end) =>
        fixture.Context.Events.Where(target => target.Id == eventId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(target => target.LastSessionEndUtc, end));

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record TenantScope(Guid TenantId) : ITenantContext;

    private sealed class DeadlineBarrier(int point) : DbCommandInterceptor
    {
        private int _armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool OnCommit => point == 1;
        public DbTransactionInterceptor Commits => new CommitBarrier(this);
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        private async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _armed, 0, 1) != 1) return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            string table = eventData.Context!.Model.FindEntityType(typeof(RegistrationOrder))!.GetTableName()!;
            if (point == 0 && command.CommandText.StartsWith("SELECT ", StringComparison.Ordinal)
                && command.CommandText.Contains('"' + table + '"', StringComparison.Ordinal)) await WaitAsync(cancellationToken);
            return result;
        }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            string column = eventData.Context!.Model.FindEntityType(typeof(RegistrationOrder))!
                .FindProperty(nameof(RegistrationOrder.GuestStatusAccessUntilUtc))!.GetColumnName();
            if (point == 2 && command.CommandText.StartsWith("UPDATE ", StringComparison.Ordinal)
                && command.CommandText.Contains('"' + column + '"', StringComparison.Ordinal)) await WaitAsync(cancellationToken);
            return result;
        }
        private sealed class CommitBarrier(DeadlineBarrier owner) : DbTransactionInterceptor
        {
            public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
                TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            {
                if (owner.OnCommit) await owner.WaitAsync(cancellationToken);
                return result;
            }
        }
    }
}
