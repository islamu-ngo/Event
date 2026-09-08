// ABOUTME: Forces both native cancellation race winners and rollback at every persisted mutation boundary.
// ABOUTME: Barriers subscribe before triggering real database work and never depend on sleeps or mock readiness.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.RegistrationOrders.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed partial class AnonymousCancellationConcurrencyTests
{
    [Test]
    public async Task CapabilityTokenAllowedPinnedProfileRemainsAnonymous()
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var command = await ConfirmAsync(fixture, clock, capabilityProfile: true);
        await Assert.That(await EligibleAsync(fixture, command)).IsEqualTo(true);
        await Assert.That((await CancelAsync(fixture, command)).IsSuccess).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TerminalTicketsArePreservedButRetainedEntryCountStillDenies(bool attended)
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var command = await ConfirmAsync(fixture, clock);
        await IssueAsync(fixture, command);
        if (attended)
        {
            var target = await fixture.Context.AdmissionTargets.AsNoTracking().SingleAsync();
            var ticket = await fixture.Context.AdmissionTickets.AsNoTracking().SingleAsync();
            fixture.Context.AdmissionCheckInStates.Add(AdmissionCheckInState.Rehydrate(Guid.CreateVersion7(), fixture.TenantId,
                ticket.Id, target.Id, null, 1, 2, Guid.CreateVersion7()));
            await fixture.Context.SaveChangesAsync();
        }
        var terminal = await fixture.Context.AdmissionTickets.Include(value => value.Credentials).SingleAsync();
        terminal.TransitionTo(AdmissionTicketStatusEnum.Expired, clock.Now.UtcDateTime);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var result = await CancelAsync(fixture, command);
        await Assert.That(result.IsSuccess).IsEqualTo(!attended);
        var after = await fixture.Context.AdmissionTickets.AsNoTracking().SingleAsync();
        await Assert.That(after.AdmissionTicketStatusId).IsEqualTo((int)AdmissionTicketStatusEnum.Expired);
        await Assert.That(after.ConcurrencyStamp).IsEqualTo(terminal.ConcurrencyStamp);
    }

    [Test]
    [Arguments("checkin", true)]
    [Arguments("checkin", false)]
    [Arguments("issuance", true)]
    [Arguments("issuance", false)]
    [Arguments("cancel", true)]
    public async Task RealContendersSerializeAtNativeFences(string contender, bool cancellationWins)
    {
        var clock = new Clock();
        var barrier = new RaceBarrier();
        await using var fixture = await CreateAsync(clock, barrier, barrier.Transactions);
        var command = await ConfirmAsync(fixture, clock);
        if (contender == "checkin") await IssueAsync(fixture, command);
        await using var firstScope = fixture.CreateScope();
        await using var secondScope = fixture.CreateScope();
        var first = firstScope.ServiceProvider;
        var second = secondScope.ServiceProvider;
        string firstKind = cancellationWins ? "cancel" : contender;
        string secondKind = cancellationWins ? contender : "cancel";
        // Resolve exact inputs before either transaction takes SQLite's writer fence.
        var effect = await fixture.Context.RegistrationFinalizationEffects.AsNoTracking().SingleAsync();
        var target = await fixture.Context.AdmissionTargets.AsNoTracking().SingleAsync();
        var credential = await fixture.Context.AdmissionTicketCredentials.AsNoTracking().SingleOrDefaultAsync();
        barrier.Arm(first.GetRequiredService<ExploreDbContext>().ContextId.InstanceId,
            second.GetRequiredService<ExploreDbContext>().ContextId.InstanceId,
            firstKind == "checkin" ? typeof(RegistrationTicketAssignment) : typeof(RegistrationOrder));
        Task<object?> ExecuteAsync(IServiceProvider services, string kind) => ExecuteContenderAsync(services, fixture,
            command, clock, kind, effect.Id, target.Id, credential);
        var winner = Task.Run(() => ExecuteAsync(first, firstKind));
        Task<object?>? loser = null;
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            loser = Task.Run(() => ExecuteAsync(second, secondKind));
            await barrier.ContenderStarting.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(winner.IsCompleted).IsFalse();
            await Assert.That(loser.IsCompleted).IsFalse();
        }
        finally { barrier.Release.TrySetResult(); }
        object? firstResult = await winner.WaitAsync(TimeSpan.FromSeconds(30));
        object? secondResult = await loser!.WaitAsync(TimeSpan.FromSeconds(30));
        var cancellation = (BaseCommandResponse<Guid>)(cancellationWins ? firstResult! : secondResult!);
        await Assert.That(cancellation.IsSuccess).IsEqualTo(contender != "checkin" || cancellationWins);
        if (contender == "checkin")
        {
            await Assert.That(await fixture.Context.AdmissionCheckInEvents.CountAsync()).IsEqualTo(cancellationWins ? 0 : 1);
            if (!cancellationWins)
                await Assert.That(cancellation.FailureCode).IsEqualTo("guest_registration_cancellation_ineligible");
        }
        if (contender == "issuance")
        {
            var issuance = (AdmissionIssuanceResult)(cancellationWins ? secondResult! : firstResult!);
            await Assert.That(issuance.Outcome).IsEqualTo(cancellationWins ? AdmissionIssuanceOutcome.NotConfirmed : AdmissionIssuanceOutcome.Issued);
            await Assert.That(await fixture.Context.AdmissionTickets.CountAsync()).IsEqualTo(cancellationWins ? 0 : 1);
        }
        if (contender == "cancel") await Assert.That(((BaseCommandResponse<Guid>)secondResult!).IsSuccess).IsTrue();
        await Assert.That((await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync()).IsCapacityAllocated)
            .IsEqualTo(contender == "checkin" && !cancellationWins);
        if (cancellation.IsSuccess)
            await Assert.That(await fixture.Context.AdmissionTicketCredentials.AnyAsync(value =>
                value.AdmissionTicketCredentialStatusId == (int)AdmissionTicketCredentialStatusEnum.Active)).IsFalse();
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    public async Task FailureOrDeadlineAtEveryMutationBoundaryRollsBackEverything(int point, bool deadline)
    {
        var clock = new Clock();
        var barrier = new MutationBarrier(point, !deadline);
        await using var fixture = await CreateAsync(clock, barrier, barrier.Saves);
        var command = await ConfirmAsync(fixture, clock);
        await IssueAsync(fixture, command);
        var order = await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync();
        var hold = await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync();
        var ticket = await fixture.Context.AdmissionTickets.AsNoTracking().SingleAsync();
        barrier.Arm();
        var pending = CancelAsync(fixture, command);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (deadline) clock.Now = new DateTimeOffset(order.GuestStatusAccessUntilUtc!.Value, TimeSpan.Zero);
        }
        finally { barrier.Release.TrySetResult(); }
        if (deadline)
            await Assert.That((await pending.WaitAsync(TimeSpan.FromSeconds(30))).FailureCode).IsEqualTo("registration_order_not_found");
        else
            await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(30))).Throws<InjectedMutationException>();
        await Assert.That((await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync()).ConcurrencyStamp).IsEqualTo(order.ConcurrencyStamp);
        await Assert.That((await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync()).ConcurrencyStamp).IsEqualTo(hold.ConcurrencyStamp);
        await Assert.That((await fixture.Context.AdmissionTickets.AsNoTracking().SingleAsync()).ConcurrencyStamp).IsEqualTo(ticket.ConcurrencyStamp);
        await Assert.That((await fixture.Context.AdmissionTicketCredentials.AsNoTracking().SingleAsync()).AdmissionTicketCredentialStatusId)
            .IsEqualTo((int)AdmissionTicketCredentialStatusEnum.Active);
    }

    [Test]
    public async Task EligibilityCommitCrossingDeadlineCannotGrantExpiredCapability()
    {
        var clock = new Clock();
        var commits = new DeadlineCommit(clock);
        await using var fixture = await CreateAsync(clock, commits);
        var command = await ConfirmAsync(fixture, clock);
        var order = await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync();
        commits.Deadline = new DateTimeOffset(order.GuestStatusAccessUntilUtc!.Value, TimeSpan.Zero);
        await Assert.That(await EligibleAsync(fixture, command)).IsNull();
        await Assert.That((await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync()).ConcurrencyStamp).IsEqualTo(order.ConcurrencyStamp);
        await Assert.That((await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync()).IsCapacityAllocated).IsTrue();
    }

    private sealed class DeadlineCommit(Clock clock) : DbTransactionInterceptor
    {
        public DateTimeOffset? Deadline { get; set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Deadline is { } deadline)
            {
                clock.Now = deadline;
                Deadline = null;
            }
            return Task.CompletedTask;
        }
    }

    private static async Task<object?> ExecuteContenderAsync(IServiceProvider services, EventVisitorCapabilitySqliteFixture fixture,
        CancelConfirmedGuestRegistrationCommand command, Clock clock, string kind, Guid effectId, Guid targetId,
        AdmissionTicketCredential? credential)
    {
        if (kind == "cancel")
            return await services.GetRequiredService<IRequestHandler<CancelConfirmedGuestRegistrationCommand, BaseCommandResponse<Guid>>>()
                .Handle(command, CancellationToken.None);
        if (kind == "issuance")
            return await services.GetRequiredService<IAdmissionIssuanceService>().IssueConfirmedAsync(
                new(fixture.TenantId, command.OrderId, effectId, AdmissionIssuanceAuthority.ConfirmedFreeOrder), CancellationToken.None);
        return await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(token =>
            services.GetRequiredService<IAdmissionCheckInTransaction>().ExecuteAsync(new(fixture.TenantId, command.EventId, targetId,
                [new(credential!.LookupDigest, credential.LookupKeyVersion)], AdmissionCheckInAction.CheckIn,
                null, fixture.ActorId, null, clock.Now), token));
    }

    private sealed class RaceBarrier : DbCommandInterceptor
    {
        private Guid _winner;
        private Guid _contender;
        private Type _fence = typeof(RegistrationOrder);
        private int _armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContenderStarting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DbTransactionInterceptor Transactions => new StartObserver(this);
        public void Arm(Guid winner, Guid contender, Type fence)
        {
            _winner = winner;
            _contender = contender;
            _fence = fence;
            Interlocked.Exchange(ref _armed, 1);
        }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ContextId.InstanceId == _winner &&
                command.CommandText.StartsWith("UPDATE ", StringComparison.Ordinal) &&
                command.CommandText.Contains('"' + eventData.Context.Model.FindEntityType(_fence)!.GetTableName()! + '"', StringComparison.Ordinal) &&
                Interlocked.CompareExchange(ref _armed, 0, 1) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
        private sealed class StartObserver(RaceBarrier owner) : DbTransactionInterceptor
        {
            public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
                TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
            {
                if (eventData.Context?.ContextId.InstanceId == owner._contender) owner.ContenderStarting.TrySetResult();
                return ValueTask.FromResult(result);
            }
        }
    }

    private sealed class InjectedMutationException : Exception;

    private sealed class MutationBarrier(int point, bool fail) : DbCommandInterceptor
    {
        private int _armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int Point => point;
        public SaveChangesInterceptor Saves => new SaveObserver(this);
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        private async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _armed, 0, 1) != 1) return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            if (fail) throw new InjectedMutationException();
        }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            string table = eventData.Context!.Model.FindEntityType(typeof(RegistrationInventoryHold))!.GetTableName()!;
            if (point == 2 && command.CommandText.StartsWith("UPDATE ", StringComparison.Ordinal) &&
                command.CommandText.Contains('"' + table + '"', StringComparison.Ordinal)) await WaitAsync(cancellationToken);
            return result;
        }
        private sealed class SaveObserver(MutationBarrier owner) : SaveChangesInterceptor
        {
            public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
                CancellationToken cancellationToken = default)
            {
                bool cancelled = eventData.Context!.ChangeTracker.Entries<RegistrationOrder>().Any(entry =>
                    entry.Entity.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.Cancelled);
                bool ticketsCancelled = eventData.Context.ChangeTracker.Entries<AdmissionTicket>().Any(entry =>
                    entry.Entity.AdmissionTicketStatusId == (int)AdmissionTicketStatusEnum.Cancelled);
                if (cancelled && (owner.Point == 0 || owner.Point == 1 && ticketsCancelled)) await owner.WaitAsync(cancellationToken);
                return result;
            }
        }
    }
}
