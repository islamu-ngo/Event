
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed class AnonymousRegistrationRecoveryDeadlineTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactReadCrossingRecoveryDeadlineCannotReturnCommittedOrder(bool throughStarter)
    {
        var clock = new Clock();
        var barrier = new ExactReadBarrier();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(barrier));
        });
        var target = await fixture.SeedEventAsync(published: true);
        var ticket = await fixture.SeedTicketAsync(target.Id);
        var proof = await fixture.IssueGuestProofAsync(new(target.Id, ticket.CatalogId,
            BookingPartyTypeEnum.Individual, [new(ticket.TicketId, 1, null)]));
        var authority = proof.Request.ChallengeAuthority!;
        var created = await fixture.Services
            .GetRequiredService<IRequestHandler<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>>()
            .Handle(proof.Request, CancellationToken.None);
        await Assert.That(created.IsSuccess).IsTrue();
        RegistrationOrder original = await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync();
        RegistrationInventoryHold hold = await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync();
        await Assert.That(authority.RecoverUntil).IsEqualTo(proof.Challenge.ExpiresAt.AddHours(24));
        clock.Now = authority.RecoverUntil.AddTicks(-1);
        await using var scope = fixture.CreateScope();
        var starter = scope.ServiceProvider.GetRequiredService<IRegistrationOrderStarter>();
        barrier.Arm();
        async Task<BaseCommandResponse<Guid>?> RecoverAsync() => throughStarter
            ? await scope.ServiceProvider
                .GetRequiredService<IRequestHandler<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>>()
                .Handle(proof.Request, CancellationToken.None)
            : await starter.TryRecoverCommittedGuestAsync(proof.Request, CancellationToken.None);
        var pending = RecoverAsync();
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Assert.That(pending.IsCompleted).IsFalse();
            clock.Now = authority.RecoverUntil;
        }
        finally { barrier.Release.TrySetResult(); }
        var denied = await pending.WaitAsync(TimeSpan.FromSeconds(20));
        await Assert.That(denied).IsNotNull();
        await Assert.That(denied!.IsSuccess).IsFalse();
        await Assert.That(denied.FailureCode).IsEqualTo("registration_order_challenge_invalid");
        await Assert.That(denied.Id == original.Id).IsFalse();
        if (throughStarter)
            await Assert.That(((GuestRegistrationOrderStartDto)denied).GuestCapabilityToken).IsNull();
        RegistrationOrder after = await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync();
        RegistrationInventoryHold afterHold = await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync();
        await Assert.That(after.Id).IsEqualTo(original.Id);
        await Assert.That(after.CreatedAt).IsEqualTo(original.CreatedAt);
        await Assert.That(after.ExpiresAt).IsEqualTo(original.ExpiresAt);
        await Assert.That(after.ConcurrencyStamp).IsEqualTo(original.ConcurrencyStamp);
        await Assert.That(afterHold.Id).IsEqualTo(hold.Id);
        await Assert.That(afterHold.ExpiresAt).IsEqualTo(hold.ExpiresAt);
        await Assert.That(afterHold.ConcurrencyStamp).IsEqualTo(hold.ConcurrencyStamp);
        await Assert.That(await fixture.Context.RegistrationOrderLines.CountAsync()).IsEqualTo(1);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ExactReadBarrier : DbCommandInterceptor
    {
        private int _armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            string table = eventData.Context!.Model.FindEntityType(typeof(RegistrationOrder))!.GetTableName()!;
            if (command.CommandText.StartsWith("SELECT ", StringComparison.Ordinal)
                && command.CommandText.Contains('"' + table + '"', StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref _armed, 0, 1) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
