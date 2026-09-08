// ABOUTME: Exercises native anonymous cancellation, issuance and check-in against one real SQLite authority graph.
// ABOUTME: Uses production repositories, readiness and UoW with deterministic database barriers, never timing sleeps.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Commands;
using Explore.Application.Features.RegistrationOrders.Queries;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services.Registration;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed partial class AnonymousCancellationConcurrencyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationAndDuplicateRevokeAndReleaseExactlyOnce(bool issue)
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var command = await ConfirmAsync(fixture, clock);
        if (issue) await IssueAsync(fixture, command);
        var before = await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync();
        await Assert.That(await EligibleAsync(fixture, command)).IsEqualTo(true);
        await Assert.That((await CancelAsync(fixture, command)).IsSuccess).IsTrue();
        var released = await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync();
        var cancelled = await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync();
        await Assert.That(released.Quantity).IsEqualTo(before.Quantity);
        await Assert.That(released.ConsumedAt).IsEqualTo(before.ConsumedAt);
        await Assert.That(released.ReleasedAt).IsEqualTo(cancelled.CancelledAt);
        await Assert.That(released.UpdatedAt).IsEqualTo(cancelled.CancelledAt);
        await Assert.That(released.ConcurrencyStamp).IsNotEqualTo(before.ConcurrencyStamp);
        await Assert.That(released.IsCapacityAllocated).IsFalse();
        await Assert.That(await EligibleAsync(fixture, command)).IsEqualTo(false);
        clock.Now = clock.Now.AddMinutes(1);
        await Assert.That((await CancelAsync(fixture, command)).IsSuccess).IsTrue();
        var duplicate = await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync();
        await Assert.That(duplicate.ConcurrencyStamp).IsEqualTo(released.ConcurrencyStamp);
        await Assert.That((await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync()).ConcurrencyStamp)
            .IsEqualTo(cancelled.ConcurrencyStamp);
        await Assert.That(await fixture.Context.AdmissionTicketCredentials.AnyAsync(value =>
            value.AdmissionTicketCredentialStatusId == (int)AdmissionTicketCredentialStatusEnum.Active)).IsFalse();
        await Assert.That(await fixture.Services.GetRequiredService<IRegistrationInventoryRepository>()
            .GetAllocatedQuantityAsync(before.CapacityPoolId, fixture.TenantId, CancellationToken.None)).IsEqualTo(0);
        var ticket = await fixture.Context.EventTicketTypes.AsNoTracking().SingleAsync();
        var proof = await fixture.IssueGuestProofAsync(new(command.EventId, ticket.CatalogId,
            BookingPartyTypeEnum.Individual, [new(ticket.Id, 10, null)]));
        await Assert.That((await fixture.ExecuteAsync<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>(proof.Request)).IsSuccess).IsTrue();
        await Assert.That(await fixture.Services.GetRequiredService<IRegistrationInventoryRepository>()
            .GetAllocatedQuantityAsync(before.CapacityPoolId, fixture.TenantId, CancellationToken.None)).IsEqualTo(10);
        await Assert.That((await CancelAsync(fixture, command)).IsSuccess).IsTrue();
        await Assert.That(await fixture.Services.GetRequiredService<IRegistrationInventoryRepository>()
            .GetAllocatedQuantityAsync(before.CapacityPoolId, fixture.TenantId, CancellationToken.None)).IsEqualTo(10);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task AnyAttendanceIncludingUndoDeniesWithoutWrites(bool undo, bool deletedAssignment)
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var command = await ConfirmAsync(fixture, clock);
        await IssueAsync(fixture, command);
        var decision = await CheckInAsync(fixture, command, clock);
        await Assert.That(decision?.Event).IsNotNull();
        if (undo)
        {
            var reversed = await CheckInAsync(fixture, command, clock, decision!.Event!.Id);
            await Assert.That(reversed?.Event).IsNotNull();
            await Assert.That((await fixture.Context.AdmissionCheckInStates.AsNoTracking().SingleAsync()).ActiveCheckInEventId).IsNull();
        }
        if (deletedAssignment)
        {
            await fixture.Context.RegistrationTicketAssignments.ExecuteUpdateAsync(setters =>
                setters.SetProperty(value => EF.Property<bool>(value, "IsDeleted"), true));
            await Assert.That(await fixture.Context.RegistrationTicketAssignments.AnyAsync()).IsFalse();
            await Assert.That(await fixture.Context.RegistrationTicketAssignments.IncludeDeleted().CountAsync()).IsEqualTo(1);
        }
        var before = await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync();
        await Assert.That(await EligibleAsync(fixture, command)).IsEqualTo(false);
        var denied = await CancelAsync(fixture, command);
        await Assert.That(denied.FailureCode).IsEqualTo("guest_registration_cancellation_ineligible");
        await Assert.That((await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync()).ConcurrencyStamp).IsEqualTo(before.ConcurrencyStamp);
        await Assert.That((await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync()).IsCapacityAllocated).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeletedHoldLineageCannotAuthorizeCancellationOrCompletedReplay(bool cancelled)
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var command = await ConfirmAsync(fixture, clock);
        if (cancelled)
            await Assert.That((await CancelAsync(fixture, command)).IsSuccess).IsTrue();
        await fixture.Context.RegistrationInventoryHolds.ExecuteUpdateAsync(setters =>
            setters.SetProperty(value => value.IsDeleted, true));
        var order = await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync();
        var hold = await fixture.Context.RegistrationInventoryHolds.IncludeDeleted().AsNoTracking().SingleAsync();
        await Assert.That(await fixture.Context.RegistrationInventoryHolds.AnyAsync()).IsFalse();

        await Assert.That(await EligibleAsync(fixture, command)).IsEqualTo(false);
        await Assert.That((await CancelAsync(fixture, command)).FailureCode)
            .IsEqualTo("guest_registration_cancellation_ineligible");
        await Assert.That((await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync()).ConcurrencyStamp)
            .IsEqualTo(order.ConcurrencyStamp);
        var retained = await fixture.Context.RegistrationInventoryHolds.IncludeDeleted().AsNoTracking().SingleAsync();
        await Assert.That(retained.ConcurrencyStamp).IsEqualTo(hold.ConcurrencyStamp);
        await Assert.That(retained.ConsumedAt).IsEqualTo(hold.ConsumedAt);
        await Assert.That(retained.ReleasedAt).IsEqualTo(hold.ReleasedAt);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrWrongAmbientTenantCannotUseExactGuestAuthority(bool missing)
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var command = await ConfirmAsync(fixture, clock);
        await IssueAsync(fixture, command);
        var order = await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync();
        var hold = await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync();
        await using var scope = fixture.CreateScope();
        var services = scope.ServiceProvider;
        var foreignTenant = Substitute.For<ITenantContext>();
        foreignTenant.TenantId.Returns(Guid.CreateVersion7());
        services.GetRequiredService<ExploreDbContext>().TenantContext = missing ? null : foreignTenant;

        // Even a valid proof and exact explicit tenant predicate cannot replace ambient isolation.
        await Assert.That(await services.GetRequiredService<IRequestHandler<GetGuestRegistrationCancellationEligibilityQuery, bool?>>()
            .Handle(new(command.EventId, command.OrderId, command.CapabilityToken), CancellationToken.None)).IsNull();
        var denied = await services.GetRequiredService<IRequestHandler<CancelConfirmedGuestRegistrationCommand, BaseCommandResponse<Guid>>>()
            .Handle(command, CancellationToken.None);
        await Assert.That(denied.FailureCode).IsEqualTo("registration_order_not_found");
        await Assert.That((await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync()).ConcurrencyStamp)
            .IsEqualTo(order.ConcurrencyStamp);
        await Assert.That((await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync()).ConcurrencyStamp)
            .IsEqualTo(hold.ConcurrencyStamp);
        await Assert.That(await fixture.Context.AdmissionTicketCredentials.AnyAsync(value =>
            value.AdmissionTicketCredentialStatusId == (int)AdmissionTicketCredentialStatusEnum.Active)).IsTrue();
    }

    [Test]
    public async Task PurposeAuthoritySurvivesCheckoutExpiryAndPromiseCasReload()
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var command = await ConfirmAsync(fixture, clock);
        clock.Now = clock.Now.AddHours(1);
        await fixture.Context.Events.Where(value => value.Id == command.EventId).ExecuteUpdateAsync(setters =>
            setters.SetProperty(value => value.LastSessionEndUtc, new DateTimeOffset(2027, 2, 1, 14, 0, 0, TimeSpan.Zero)));
        await Assert.That((await CancelAsync(fixture, command)).IsSuccess).IsTrue();
        await Assert.That((await fixture.Context.RegistrationOrders.AsNoTracking().SingleAsync()).GuestStatusAccessUntilUtc)
            .IsEqualTo(new DateTime(2027, 3, 3, 14, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    [Arguments("wrong-token")]
    [Arguments("foreign-event")]
    [Arguments("foreign-order")]
    [Arguments("expired")]
    [Arguments("account")]
    [Arguments("unconfirmed")]
    [Arguments("staff")]
    public async Task InvalidAuthorityIsDistinctFromAuthorizedIneligibility(string scenario)
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var command = await ConfirmAsync(fixture, clock);
        switch (scenario)
        {
            case "wrong-token": command = command with { CapabilityToken = new string('Z', 43) }; break;
            case "foreign-event": command = command with { EventId = Guid.CreateVersion7() }; break;
            case "foreign-order": command = command with { OrderId = Guid.CreateVersion7() }; break;
            case "expired": clock.Now = new(2027, 1, 31, 14, 0, 0, TimeSpan.Zero); break;
            case "unconfirmed": await fixture.Context.RegistrationOrders.ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.ConfirmedAt, (DateTime?)null)
                .SetProperty(value => value.RegistrationOrderStatusId, (int)RegistrationOrderStatusEnum.ReadyForCheckout)); break;
            case "account": await fixture.Context.RegistrationOrders.ExecuteUpdateAsync(setters => setters.SetProperty(value => value.AccountUserId, fixture.UserId)); break;
            case "staff": await fixture.Context.RegistrationOrders.ExecuteUpdateAsync(setters => setters.SetProperty(value => value.PurchaserActorId, fixture.ActorId)); break;
        }
        var result = await CancelAsync(fixture, command);
        await Assert.That(result.FailureCode).IsEqualTo(scenario is "account" or "staff"
            ? "guest_registration_cancellation_ineligible" : "registration_order_not_found");
        await Assert.That((await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync()).IsCapacityAllocated).IsTrue();
    }

    [Test]
    [Arguments("attempt")]
    [Arguments("acceptance")]
    [Arguments("success")]
    public async Task ExactPaidEvidenceDeniesOnlyItsOrderEvenAtZeroBalance(string evidence)
    {
        var clock = new Clock();
        await using var fixture = await CreateAsync(clock);
        var command = await ConfirmAsync(fixture, clock);
        var recipient = OrganizerPaymentRecipientSnapshot.Create(fixture.TenantId, fixture.ActorId,
            Guid.CreateVersion7(), "stripe", "platform", "account", "BE", "USD", Guid.CreateVersion7(), null, clock.Now.UtcDateTime);
        var attempt = PaymentAttempt.Create(Guid.CreateVersion7(), fixture.TenantId, command.OrderId,
            recipient, "OrganizerDirect", "v1", "v1", Money.Create(100, "USD"), Money.Create(0, "USD"), Money.Create(0, "USD"),
            "exact-order", clock.Now.UtcDateTime, null);
        if (evidence == "acceptance")
            fixture.Context.PaidOrderAcceptanceSnapshots.Add(PaidAcceptanceTestFacts.Create(fixture.TenantId,
                command.OrderId, command.EventId, "v1", recipient.InstancePolicyVersionId, 100, 0, 0,
                clock.Now.UtcDateTime, "USD", recipient));
        else
        {
            fixture.Context.PaymentAttempts.Add(attempt);
            if (evidence == "success")
                fixture.Context.PaymentSucceededObservations.Add(PaymentSucceededObservation.Create(attempt, null,
                    "checkout", "payment", null, clock.Now.UtcDateTime));
        }
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await Assert.That(await EligibleAsync(fixture, command)).IsEqualTo(false);
        await Assert.That((await CancelAsync(fixture, command)).FailureCode).IsEqualTo("guest_registration_cancellation_ineligible");
        // Same event: an event-wide paid flag would incorrectly deny this second free order.
        var other = await ConfirmAsync(fixture, clock, command.EventId);
        await Assert.That((await CancelAsync(fixture, other)).IsSuccess).IsTrue();
        await Assert.That(await fixture.Context.PaymentAttempts.CountAsync()).IsEqualTo(evidence == "acceptance" ? 0 : 1);
        await Assert.That(await fixture.Context.PaidOrderAcceptanceSnapshots.CountAsync()).IsEqualTo(evidence == "acceptance" ? 1 : 0);
        await Assert.That(await fixture.Context.PaymentSucceededObservations.CountAsync()).IsEqualTo(evidence == "success" ? 1 : 0);
    }

    private static Task<EventVisitorCapabilitySqliteFixture> CreateAsync(Clock clock, params IInterceptor[] interceptors) =>
        EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(interceptors));
            // Only the external key source is substituted; production HMAC, issuance, readiness and DB remain real.
            var secrets = Substitute.For<ISecretResolver>();
            secrets.ResolveQualifiedAsync(Arg.Any<string>(), Arg.Any<SecretScope>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Resolved(new("test", Convert.ToBase64String(new byte[32]),
                    SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, clock.Now)));
            services.AddSingleton<IAdmissionCredentialDigestService>(new AdmissionCredentialDigestService(secrets, Options.Create(new AdmissionCredentialOptions())));
        });

    private static async Task<CancelConfirmedGuestRegistrationCommand> ConfirmAsync(EventVisitorCapabilitySqliteFixture fixture, Clock clock,
        Guid? existingEventId = null, bool capabilityProfile = false)
    {
        var target = existingEventId.HasValue
            ? await fixture.Context.Events.AsNoTracking().SingleAsync(value => value.Id == existingEventId)
            : await fixture.SeedEventAsync(published: true);
        if (capabilityProfile)
        {
            var participation = await fixture.Context.EventParticipationConfigurations.SingleAsync(value => value.Id == target.Id);
            fixture.Context.Entry(participation).Property(value => value.IdentityAccessModeId).CurrentValue = (int)IdentityAccessModeEnum.CapabilityTokenAllowed;
            await fixture.Context.SaveChangesAsync();
            fixture.Context.ChangeTracker.Clear();
        }
        fixture.Context.EventSessions.Add(new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(), EventId = target.Id, Event = null!, TenantId = fixture.TenantId, Tenant = null!,
            RegistrationModeId = (int)RegistrationModeEnum.Open, StartTime = target.FirstSessionStartUtc, EndTime = target.LastSessionEndUtc
        });
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        EventTicketCatalogVersion catalog;
        EventTicketType ticket;
        if (existingEventId.HasValue)
        {
            catalog = await fixture.Context.EventTicketCatalogVersions.AsNoTracking().Include(value => value.TicketTypes)
                .SingleAsync(value => value.EventId == target.Id);
            ticket = catalog.TicketTypes.Single();
        }
        else
        {
            catalog = EventTicketCatalogVersion.Create(fixture.TenantId, target.Id, "USD", 1);
            var pool = EventCapacityPool.Create(fixture.TenantId, target.Id, "Cancellation capacity", 10, 900,
                CapacityHoldPolicyEnum.TimedHoldOnSelection, CapacityOversellPolicyEnum.Disallow, true);
            ticket = EventTicketType.Create(Guid.CreateVersion7(), fixture.TenantId, catalog.Id, "Free admission", "USD",
                TicketPricingModeEnum.Free, null, null, null, ParticipantDataCollectionModeEnum.PerTicketOptional,
                pool.Id, null, null, false, false, null, null, null, null);
            catalog.AddTicketType(ticket, pool);
            catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, fixture.TenantId, target.Id, 1));
            catalog.Publish();
            fixture.Context.AddRange(catalog, pool);
            await fixture.Context.SaveChangesAsync();
            fixture.Context.ChangeTracker.Clear();
        }
        var proof = await fixture.IssueGuestProofAsync(new(target.Id, catalog.Id, BookingPartyTypeEnum.Individual, [new(ticket.Id, 1, null)]));
        var created = await fixture.ExecuteAsync<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>(proof.Request);
        await Assert.That(created.IsSuccess).IsTrue();
        var order = await fixture.Context.RegistrationOrders.Include(value => value.Lines).SingleAsync(value => value.Id == created.Id);
        // Native issuance currently requires a delivery address. Its presence is not anonymous authority.
        order.SetPii(RegistrationOrderPii.Create(order.Id, fixture.TenantId, null, "guest@example.test", null, null));
        var participant = RegistrationParticipant.Create(Guid.CreateVersion7(), fixture.TenantId, order.Id, fixture.UserId, ParticipantTypeEnum.Adult, null);
        order.AddParticipant(participant);
        var assignment = RegistrationTicketAssignment.CreateAssigned(Guid.CreateVersion7(), order.Lines.Single().Id, 1, participant, clock.Now.UtcDateTime);
        order.AddAssignment(order.Lines.Single(), assignment, participant);
        fixture.Context.RegistrationFinalizationEffects.Add(RegistrationFinalizationEffect.Create(order, clock.Now.UtcDateTime));
        await fixture.Context.SaveChangesAsync();
        var readiness = fixture.Services.GetRequiredService<IParticipantAdmissionEligibilityRepository>();
        await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            await readiness.EnsureForAssignmentsAsync(fixture.TenantId, target.Id, order.Id, [assignment.Id], clock.Now.UtcDateTime, token);
            var completion = await readiness.LoadCompletionForUpdateAsync(fixture.TenantId, target.Id, order.Id, assignment.Id,
                participant.Id, fixture.UserId, token);
            await Assert.That(completion!.RequirementsComplete).IsTrue();
            completion.Eligibility.RecordSubjectCompletion(completion.Participant, fixture.UserId, completion.SubjectConsentRecordId,
                clock.Now.UtcDateTime, Guid.CreateVersion7());
            await readiness.ApplyDecisionAsync(completion.Eligibility, token);
        });
        fixture.Context.ChangeTracker.Clear();
        // Seed the confirmed boundary through Domain transitions and native inventory, not a readiness stub.
        var inventory = fixture.Services.GetRequiredService<IRegistrationInventoryRepository>();
        await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            var current = (await inventory.GetOrderForUpdateWithLinesAsync(order.Id, fixture.TenantId, token))!;
            current.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, clock.Now.UtcDateTime);
            current.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, clock.Now.UtcDateTime);
            await Assert.That(await inventory.TryConsumeActiveHoldsForOrderAsync(order.Id, fixture.TenantId, clock.Now.UtcDateTime, token)).IsEqualTo(1);
            current.TransitionTo(RegistrationOrderStatusEnum.Confirmed, clock.Now.UtcDateTime);
            await inventory.SaveChangesAsync(token);
        });
        fixture.Context.ChangeTracker.Clear();
        if (!existingEventId.HasValue)
        {
            var admissionTarget = AdmissionTarget.Create(Guid.CreateVersion7(), fixture.TenantId, target.Id, AdmissionTargetTypeEnum.Event, null, null);
            fixture.Context.AddRange(admissionTarget, AdmissionCheckInPolicy.Create(Guid.CreateVersion7(), admissionTarget,
                clock.Now.AddDays(-1).UtcDateTime, clock.Now.AddDays(180).UtcDateTime, 2));
        }
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return new(target.Id, created.Id, created.GuestCapabilityToken);
    }

    private static async Task<AdmissionIssuanceResult> IssueAsync(EventVisitorCapabilitySqliteFixture fixture, CancelConfirmedGuestRegistrationCommand command)
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var effect = await context.RegistrationFinalizationEffects.AsNoTracking().SingleAsync(value => value.RegistrationOrderId == command.OrderId);
        var result = await scope.ServiceProvider.GetRequiredService<IAdmissionIssuanceService>().IssueConfirmedAsync(
            new(fixture.TenantId, command.OrderId, effect.Id, AdmissionIssuanceAuthority.ConfirmedFreeOrder), CancellationToken.None);
        await Assert.That(result.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
        return result;
    }

    private static async Task<AdmissionCheckInDecision?> CheckInAsync(EventVisitorCapabilitySqliteFixture fixture,
        CancelConfirmedGuestRegistrationCommand command, Clock clock, Guid? undo = null)
    {
        await using var scope = fixture.CreateScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<ExploreDbContext>();
        var credential = await context.AdmissionTicketCredentials.AsNoTracking().SingleAsync();
        var target = await context.AdmissionTargets.AsNoTracking().SingleAsync(value => value.EventId == command.EventId);
        return await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(token =>
            services.GetRequiredService<IAdmissionCheckInTransaction>().ExecuteAsync(new(fixture.TenantId, command.EventId, target.Id,
                [new(credential.LookupDigest, credential.LookupKeyVersion)], undo is null ? AdmissionCheckInAction.CheckIn : AdmissionCheckInAction.Undo,
                undo is null ? null : AdmissionCheckInUndoReasonCodeEnum.OperatorCorrection, fixture.ActorId, null, clock.Now, undo), token));
    }

    private static async Task<BaseCommandResponse<Guid>> CancelAsync(EventVisitorCapabilitySqliteFixture fixture, CancelConfirmedGuestRegistrationCommand command)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRequestHandler<CancelConfirmedGuestRegistrationCommand, BaseCommandResponse<Guid>>>()
            .Handle(command, CancellationToken.None);
    }

    private static async Task<bool?> EligibleAsync(EventVisitorCapabilitySqliteFixture fixture, CancelConfirmedGuestRegistrationCommand command)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRequestHandler<GetGuestRegistrationCancellationEligibilityQuery, bool?>>()
            .Handle(new(command.EventId, command.OrderId, command.CapabilityToken), CancellationToken.None);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
