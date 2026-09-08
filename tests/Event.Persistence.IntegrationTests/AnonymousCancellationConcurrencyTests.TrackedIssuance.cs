
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed partial class AnonymousCancellationConcurrencyTests
{
    [Test]
    public async Task TrackedConfirmedOrderCannotIssueAfterCancellationWinsBeforeAnyTicket()
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var command = await ConfirmAsync(fixture, clock);
        await using var issuanceScope = fixture.CreateScope();
        var services = issuanceScope.ServiceProvider;
        var context = services.GetRequiredService<ExploreDbContext>();
        var tracked = await context.RegistrationOrders
            .Include(value => value.Pii)
            .Include(value => value.Lines).ThenInclude(value => value.Assignments)
            .Include(value => value.Participants)
            .SingleAsync(value => value.Id == command.OrderId);
        var unrelated = await context.UserPii.SingleAsync(value => value.UserId == fixture.UserId);
        unrelated.FirstName = "Retained local edit";
        var effect = await context.RegistrationFinalizationEffects.AsNoTracking().SingleAsync();
        var request = new AdmissionIssuanceRequest(fixture.TenantId, command.OrderId, effect.Id,
            AdmissionIssuanceAuthority.ConfirmedFreeOrder);
        var issuance = services.GetRequiredService<IAdmissionIssuanceService>();
        await Assert.That(tracked.RegistrationOrderStatusId).IsEqualTo((int)RegistrationOrderStatusEnum.Confirmed);
        await Assert.That(await context.AdmissionTickets.AnyAsync()).IsFalse();

        // CancelAsync uses scope B and commits before scope A enters its issuance fence.
        await Assert.That((await CancelAsync(fixture, command)).IsSuccess).IsTrue();
        var cancelled = await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync();
        var released = await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync();
        await Assert.That(cancelled.RegistrationOrderStatusId).IsEqualTo((int)RegistrationOrderStatusEnum.Cancelled);
        await Assert.That(released.IsCapacityAllocated).IsFalse();
        await Assert.That(released.ConsumedAt).IsNotNull();
        await Assert.That(tracked.RegistrationOrderStatusId).IsEqualTo((int)RegistrationOrderStatusEnum.Confirmed);

        var delayed = await issuance.IssueConfirmedAsync(request, CancellationToken.None);
        await Assert.That(delayed.Outcome).IsEqualTo(AdmissionIssuanceOutcome.NotConfirmed);
        await Assert.That(delayed.IssuedTicketIds).IsEmpty();
        await Assert.That(delayed.OneTimeCredentials).IsEmpty();
        await Assert.That(delayed.DeliveryIntentIds).IsEmpty();
        await Assert.That(await fixture.Context.AdmissionTickets.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.AdmissionTicketCredentials.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.AdmissionDeliveryIntents.CountAsync()).IsEqualTo(0);
        await Assert.That((await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync()).ConcurrencyStamp)
            .IsEqualTo(released.ConcurrencyStamp);
        await Assert.That((await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync()).ConcurrencyStamp)
            .IsEqualTo(cancelled.ConcurrencyStamp);

        var inventory = fixture.Services.GetRequiredService<IRegistrationInventoryRepository>();
        await Assert.That(await inventory.GetAllocatedQuantityAsync(released.CapacityPoolId, fixture.TenantId, CancellationToken.None))
            .IsEqualTo(0);
        var ticket = await fixture.Context.EventTicketTypes.AsNoTracking().SingleAsync();
        var proof = await fixture.IssueGuestProofAsync(new(command.EventId, ticket.CatalogId,
            BookingPartyTypeEnum.Individual, [new(ticket.Id, 10, null)]));
        await Assert.That((await fixture.ExecuteAsync<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>(proof.Request)).IsSuccess)
            .IsTrue();

        // Reusing the released pool must remain safe against another delayed producer in scope A.
        var replay = await issuance.IssueConfirmedAsync(request, CancellationToken.None);
        await Assert.That(replay.Outcome).IsEqualTo(AdmissionIssuanceOutcome.NotConfirmed);
        await Assert.That(replay.OneTimeCredentials).IsEmpty();
        await Assert.That(await fixture.Context.AdmissionTickets.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.AdmissionTicketCredentials.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.AdmissionDeliveryIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await inventory.GetAllocatedQuantityAsync(released.CapacityPoolId, fixture.TenantId, CancellationToken.None))
            .IsEqualTo(10);
        await Assert.That((await fixture.Context.RegistrationInventoryHolds.AsNoTracking()
            .SingleAsync(value => value.Id == released.Id)).ConcurrencyStamp).IsEqualTo(released.ConcurrencyStamp);
        await Assert.That(ReferenceEquals(context.RegistrationOrders.Local.Single(value => value.Id == tracked.Id), tracked)).IsTrue();
        await Assert.That(context.Entry(tracked).State).IsEqualTo(EntityState.Unchanged);
        await Assert.That(context.Entry(unrelated).State).IsEqualTo(EntityState.Modified);
        await Assert.That(unrelated.FirstName).IsEqualTo("Retained local edit");
    }

    [Test]
    public async Task TrackedConfirmedOrderIssuesAndReplaysWithoutReattachingOrderGraph()
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var command = await ConfirmAsync(fixture, clock);
        await using var scope = fixture.CreateScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<ExploreDbContext>();
        var tracked = await context.RegistrationOrders
            .Include(value => value.Pii)
            .Include(value => value.Lines).ThenInclude(value => value.Assignments)
            .Include(value => value.Participants)
            .SingleAsync(value => value.Id == command.OrderId);
        var stamp = tracked.ConcurrencyStamp;
        var unrelated = await context.UserPii.SingleAsync(value => value.UserId == fixture.UserId);
        unrelated.FirstName = "Preserved through issuance";
        var effect = await context.RegistrationFinalizationEffects.AsNoTracking().SingleAsync();
        var request = new AdmissionIssuanceRequest(fixture.TenantId, command.OrderId, effect.Id,
            AdmissionIssuanceAuthority.ConfirmedFreeOrder);
        var issuance = services.GetRequiredService<IAdmissionIssuanceService>();

        var issued = await issuance.IssueConfirmedAsync(request, CancellationToken.None);
        await Assert.That(issued.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
        await Assert.That(issued.OneTimeCredentials.Count).IsEqualTo(1);
        await Assert.That(context.Entry(tracked).State).IsEqualTo(EntityState.Unchanged);
        await Assert.That(ReferenceEquals(context.RegistrationOrders.Local.Single(), tracked)).IsTrue();
        await Assert.That(tracked.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That((await fixture.Context.UserPii.AsNoTracking().SingleAsync(value => value.UserId == fixture.UserId)).FirstName)
            .IsEqualTo("Preserved through issuance");

        var replay = await issuance.IssueConfirmedAsync(request, CancellationToken.None);
        await using var otherScope = fixture.CreateScope();
        var otherReplay = await otherScope.ServiceProvider.GetRequiredService<IAdmissionIssuanceService>()
            .IssueConfirmedAsync(request, CancellationToken.None);
        await Assert.That(replay.Outcome).IsEqualTo(AdmissionIssuanceOutcome.AlreadyIssued);
        await Assert.That(otherReplay.Outcome).IsEqualTo(AdmissionIssuanceOutcome.AlreadyIssued);
        await Assert.That(replay.ExistingTicketIds).IsEquivalentTo(issued.IssuedTicketIds);
        await Assert.That(otherReplay.ExistingTicketIds).IsEquivalentTo(issued.IssuedTicketIds);
        await Assert.That(await fixture.Context.RegistrationOrders.CountAsync()).IsEqualTo(1);
        await Assert.That((await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync()).ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(await fixture.Context.AdmissionTickets.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.AdmissionTicketCredentials.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.AdmissionDeliveryIntents.CountAsync()).IsEqualTo(1);
        await Assert.That((await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync()).IsCapacityAllocated).IsTrue();
    }
}
