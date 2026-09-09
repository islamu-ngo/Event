using System.Data.Common;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services.Registration;
using Explore.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AnonymousCancellationPostgreSqlSnapshotRaceTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    public async Task CheckInCommittedAtFirstAssignmentFenceDeniesCancellationWithoutReleasingCapacity()
    {
        var clock = new Clock();
        var barrier = new FirstAssignmentFenceBarrier();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreatePostgreSqlAsync(
            postgres.ConnectionString,
            services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(barrier));
                var secrets = Substitute.For<ISecretResolver>();
                secrets.ResolveQualifiedAsync(
                        Arg.Any<string>(), Arg.Any<SecretScope>(), Arg.Any<Guid?>(), Arg.Any<string>(),
                        Arg.Any<CancellationToken>())
                    .Returns(SecretResolutionResult.Resolved(new(
                        "test", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), SecretSourceType.EnvironmentVariable,
                        SecretScope.Instance, null, clock.Now)));
                services.AddSingleton<IAdmissionCredentialDigestService>(new AdmissionCredentialDigestService(
                    secrets, Options.Create(new AdmissionCredentialOptions())));
            });
        CancelConfirmedGuestRegistrationCommand command = await ConfirmAsync(fixture, clock);
        await IssueAsync(fixture, command);

        await using AsyncServiceScope cancellationScope = fixture.CreateScope();
        IServiceProvider cancellationServices = cancellationScope.ServiceProvider;
        barrier.Arm(cancellationServices.GetRequiredService<ExploreDbContext>().ContextId.InstanceId);
        Task<BaseCommandResponse<Guid>> cancellation = cancellationServices
            .GetRequiredService<IRequestHandler<CancelConfirmedGuestRegistrationCommand, BaseCommandResponse<Guid>>>()
            .Handle(command, CancellationToken.None);

        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            AdmissionCheckInDecision? checkIn = await CheckInAsync(fixture, command, clock)
                .WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(checkIn?.Event).IsNotNull();
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        BaseCommandResponse<Guid> denied = await cancellation.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(denied.FailureCode).IsEqualTo("guest_registration_cancellation_ineligible");
        await Assert.That(await fixture.Context.AdmissionCheckInEvents.CountAsync()).IsEqualTo(1);
        await Assert.That((await fixture.Context.RegistrationInventoryHolds.AsNoTracking().SingleAsync()).IsCapacityAllocated)
            .IsTrue();
    }

    private static async Task<CancelConfirmedGuestRegistrationCommand> ConfirmAsync(
        EventVisitorCapabilitySqliteFixture fixture,
        Clock clock)
    {
        Explore.Domain.Event target = await fixture.SeedEventAsync(published: true);
        fixture.Context.EventSessions.Add(new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            EventId = target.Id,
            Event = null!,
            TenantId = fixture.TenantId,
            Tenant = null!,
            RegistrationModeId = (int)RegistrationModeEnum.Open,
            StartTime = target.FirstSessionStartUtc,
            EndTime = target.LastSessionEndUtc
        });
        await fixture.Context.SaveChangesAsync();

        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(fixture.TenantId, target.Id, "USD", 1);
        EventCapacityPool pool = EventCapacityPool.Create(fixture.TenantId, target.Id, "Cancellation capacity", 10, 900,
            CapacityHoldPolicyEnum.TimedHoldOnSelection, CapacityOversellPolicyEnum.Disallow, true);
        EventTicketType ticket = EventTicketType.Create(Guid.CreateVersion7(), fixture.TenantId, catalog.Id,
            "Free admission", "USD", TicketPricingModeEnum.Free, null, null, null,
            ParticipantDataCollectionModeEnum.PerTicketOptional, pool.Id, null, null, false, false,
            null, null, null, null);
        catalog.AddTicketType(ticket, pool);
        catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, fixture.TenantId, target.Id, 1));
        catalog.Publish();
        fixture.Context.AddRange(catalog, pool);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        EventVisitorCapabilitySqliteFixture.GuestAllocationProof proof = await fixture.IssueGuestProofAsync(
            new(target.Id, catalog.Id, BookingPartyTypeEnum.Individual, [new(ticket.Id, 1, null)]));
        GuestRegistrationOrderStartDto created = await fixture
            .ExecuteAsync<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>(proof.Request);
        await Assert.That(created.IsSuccess).IsTrue();
        RegistrationOrder order = await fixture.Context.RegistrationOrders.Include(value => value.Lines)
            .SingleAsync(value => value.Id == created.Id);
        order.SetPii(RegistrationOrderPii.Create(order.Id, fixture.TenantId, null, "guest@example.test", null, null));
        RegistrationParticipant participant = RegistrationParticipant.Create(
            Guid.CreateVersion7(), fixture.TenantId, order.Id, fixture.UserId, ParticipantTypeEnum.Adult, null);
        order.AddParticipant(participant);
        RegistrationTicketAssignment assignment = RegistrationTicketAssignment.CreateAssigned(
            Guid.CreateVersion7(), order.Lines.Single().Id, 1, participant, clock.Now.UtcDateTime);
        order.AddAssignment(order.Lines.Single(), assignment, participant);
        fixture.Context.RegistrationFinalizationEffects.Add(
            RegistrationFinalizationEffect.Create(order, clock.Now.UtcDateTime));
        await fixture.Context.SaveChangesAsync();

        IParticipantAdmissionEligibilityRepository readiness =
            fixture.Services.GetRequiredService<IParticipantAdmissionEligibilityRepository>();
        await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            await readiness.EnsureForAssignmentsAsync(
                fixture.TenantId, target.Id, order.Id, [assignment.Id], clock.Now.UtcDateTime, token);
            ParticipantAdmissionCompletionContext completion = (await readiness.LoadCompletionForUpdateAsync(
                fixture.TenantId, target.Id, order.Id, assignment.Id, participant.Id, fixture.UserId, token))!;
            completion.Eligibility.RecordSubjectCompletion(completion.Participant, fixture.UserId,
                completion.SubjectConsentRecordId, clock.Now.UtcDateTime, Guid.CreateVersion7());
            await readiness.ApplyDecisionAsync(completion.Eligibility, token);
        });
        fixture.Context.ChangeTracker.Clear();

        IRegistrationInventoryRepository inventory = fixture.Services.GetRequiredService<IRegistrationInventoryRepository>();
        await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            RegistrationOrder current = (await inventory.GetOrderForUpdateWithLinesAsync(
                order.Id, fixture.TenantId, token))!;
            current.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, clock.Now.UtcDateTime);
            current.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, clock.Now.UtcDateTime);
            await Assert.That(await inventory.TryConsumeActiveHoldsForOrderAsync(
                order.Id, fixture.TenantId, clock.Now.UtcDateTime, token)).IsEqualTo(1);
            current.TransitionTo(RegistrationOrderStatusEnum.Confirmed, clock.Now.UtcDateTime);
            await inventory.SaveChangesAsync(token);
        });
        fixture.Context.ChangeTracker.Clear();

        AdmissionTarget admissionTarget = AdmissionTarget.Create(
            Guid.CreateVersion7(), fixture.TenantId, target.Id, AdmissionTargetTypeEnum.Event, null, null);
        fixture.Context.AddRange(admissionTarget, AdmissionCheckInPolicy.Create(
            Guid.CreateVersion7(), admissionTarget, clock.Now.AddDays(-1).UtcDateTime,
            clock.Now.AddDays(180).UtcDateTime, 2));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return new(target.Id, created.Id, created.GuestCapabilityToken);
    }

    private static async Task IssueAsync(
        EventVisitorCapabilitySqliteFixture fixture,
        CancelConfirmedGuestRegistrationCommand command)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        ExploreDbContext context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        RegistrationFinalizationEffect effect = await context.RegistrationFinalizationEffects.AsNoTracking()
            .SingleAsync(value => value.RegistrationOrderId == command.OrderId);
        AdmissionIssuanceResult result = await scope.ServiceProvider.GetRequiredService<IAdmissionIssuanceService>()
            .IssueConfirmedAsync(new(fixture.TenantId, command.OrderId, effect.Id,
                AdmissionIssuanceAuthority.ConfirmedFreeOrder), CancellationToken.None);
        await Assert.That(result.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
    }

    private static async Task<AdmissionCheckInDecision?> CheckInAsync(
        EventVisitorCapabilitySqliteFixture fixture,
        CancelConfirmedGuestRegistrationCommand command,
        Clock clock)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        ExploreDbContext context = services.GetRequiredService<ExploreDbContext>();
        AdmissionTicketCredential credential = await context.AdmissionTicketCredentials.AsNoTracking().SingleAsync();
        AdmissionTarget target = await context.AdmissionTargets.AsNoTracking()
            .SingleAsync(value => value.EventId == command.EventId);
        return await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(token =>
            services.GetRequiredService<IAdmissionCheckInTransaction>().ExecuteAsync(new(
                fixture.TenantId, command.EventId, target.Id,
                [new(credential.LookupDigest, credential.LookupKeyVersion)], AdmissionCheckInAction.CheckIn,
                null, fixture.ActorId, null, clock.Now), token));
    }

    private sealed class FirstAssignmentFenceBarrier : DbCommandInterceptor
    {
        private Guid _contextId;
        private int _armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Arm(Guid contextId)
        {
            _contextId = contextId;
            Interlocked.Exchange(ref _armed, 1);
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ContextId.InstanceId == _contextId
                && command.CommandText.Contains("registration_ticket_assignments", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref _armed, 0, 1) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
