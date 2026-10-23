using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services.Registration;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RegistrationFinalizationExecutionCustodyTests(PostgreSqlContainerFixture database)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpiredOrReplacedClaimCannotRouteOrderForCheckout(bool replace)
    {
        var clock = new CustodyClock();
        await using var fixture = await CreateAsync(clock);
        RegistrationFinalizationClaim original = await SeedClaimAsync(fixture, clock);
        clock.Now = clock.Now.AddMinutes(1);
        if (replace)
            await ReplaceClaimAsync(fixture, original, clock);

        await using var scope = fixture.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IRegistrationOrderLifecycleService>();
        RegistrationOrderLifecycleResponseDto result = await lifecycle.ReadyForCheckoutAsync(original, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await AssertOrderStatusAsync(fixture, original.RegistrationOrderId, RegistrationOrderStatusEnum.AwaitingRequirements);
    }

    [Test]
    public async Task SubstitutedOrderCannotUseAnotherOrdersOriginalCustody()
    {
        var clock = new CustodyClock();
        await using var fixture = await CreateAsync(clock);
        RegistrationFinalizationClaim original = await SeedClaimAsync(fixture, clock);
        RegistrationFinalizationClaim other = await SeedClaimAsync(fixture, clock);
        await using var scope = fixture.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IRegistrationOrderLifecycleService>();

        RegistrationOrderLifecycleResponseDto result = await lifecycle.ReadyForCheckoutAsync(
            original with { RegistrationOrderId = other.RegistrationOrderId }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await AssertOrderStatusAsync(fixture, original.RegistrationOrderId, RegistrationOrderStatusEnum.AwaitingRequirements);
        await AssertOrderStatusAsync(fixture, other.RegistrationOrderId, RegistrationOrderStatusEnum.AwaitingRequirements);
    }

    [Test]
    public async Task CurrentCustodyRoutesOnceAndDirectLifecycleRemainsAuthorized()
    {
        var clock = new CustodyClock();
        await using var fixture = await CreateAsync(clock);
        RegistrationFinalizationClaim original = await SeedClaimAsync(fixture, clock);
        await using var scope = fixture.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IRegistrationOrderLifecycleService>();

        await Assert.That((await lifecycle.ReadyForCheckoutAsync(original, CancellationToken.None)).IsSuccess).IsTrue();
        await Assert.That((await lifecycle.ReadyForCheckoutAsync(original, CancellationToken.None)).IsSuccess).IsTrue();
        await AssertOrderStatusAsync(fixture, original.RegistrationOrderId, RegistrationOrderStatusEnum.ReadyForCheckout);
        clock.Now = clock.Now.AddMinutes(1);
        await Assert.That((await lifecycle.FinalizeFreeAsync(
            original.RegistrationOrderId, original.TenantId, CancellationToken.None)).IsSuccess).IsTrue();
        await AssertOrderStatusAsync(fixture, original.RegistrationOrderId, RegistrationOrderStatusEnum.Confirmed);
    }

    [Test]
    public async Task ExpiryDuringLifecycleSaveRollsBackOrderAndInventoryEffects()
    {
        var clock = new CustodyClock();
        var expiry = new ExpireAfterCheckoutSave(clock);
        await using var fixture = await CreateAsync(clock, expiry);
        RegistrationFinalizationClaim claim = await SeedClaimAsync(fixture, clock);
        int outboxCount = await fixture.Context.OutboxMessages.CountAsync();
        int holdCount = await fixture.Context.RegistrationInventoryHolds.CountAsync();
        expiry.Armed = true;
        await using var scope = fixture.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IRegistrationOrderLifecycleService>();

        RegistrationOrderLifecycleResponseDto result = await lifecycle.ReadyForCheckoutAsync(claim, CancellationToken.None);

        await Assert.That(expiry.ObservedCheckoutSave).IsTrue();
        await Assert.That(result.IsSuccess).IsFalse();
        await AssertOrderStatusAsync(fixture, claim.RegistrationOrderId, RegistrationOrderStatusEnum.AwaitingRequirements);
        await Assert.That(await fixture.Context.OutboxMessages.CountAsync()).IsEqualTo(outboxCount);
        await Assert.That(await fixture.Context.RegistrationInventoryHolds.CountAsync()).IsEqualTo(holdCount);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpiredOrReclaimedCustodyCannotIssueTicketsOrCredentials(bool reclaim)
    {
        var clock = new CustodyClock();
        await using var fixture = await CreateAsync(clock);
        RegistrationFinalizationClaim claim = await SeedIssuanceClaimAsync(fixture, clock);
        clock.Now = clock.Now.AddMinutes(1);
        if (reclaim)
            await ReplaceClaimAsync(fixture, claim, clock);
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        AdmissionIssuanceResult result = await Issuance(context, clock, new CustodySecrets())
            .IssueConfirmedAsync(Request(claim), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionIssuanceOutcome.StaleFinalizationClaim);
        await Assert.That(result.OneTimeCredentials).IsEmpty();
        await AssertNoIssuanceAsync(fixture);
    }

    [Test]
    public async Task IssuanceRejectsSubstitutedOrderEvenWhenBothOrdersAreConfirmed()
    {
        var clock = new CustodyClock();
        await using var fixture = await CreateAsync(clock);
        RegistrationFinalizationClaim claim = await SeedIssuanceClaimAsync(fixture, clock);
        RegistrationFinalizationClaim other = await SeedIssuanceClaimAsync(fixture, clock);
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        AdmissionIssuanceResult result = await Issuance(context, clock, new CustodySecrets()).IssueConfirmedAsync(
            Request(other) with { FinalizationClaim = claim }, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AdmissionIssuanceOutcome.StaleFinalizationClaim);
        await AssertNoIssuanceAsync(fixture);
    }

    [Test]
    public async Task AwaitedIssuanceHoldsOriginalEffectFenceAndExpiryRollsBackBeforeReclaim()
    {
        var clock = new CustodyClock();
        await using var fixture = await CreateAsync(clock);
        RegistrationFinalizationClaim claim = await SeedIssuanceClaimAsync(fixture, clock);
        var secrets = new CustodySecrets(block: true);
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Task<AdmissionIssuanceResult> pending = Issuance(context, clock, secrets)
            .IssueConfirmedAsync(Request(claim), CancellationToken.None);
        try
        {
            await secrets.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            clock.Now = clock.Now.AddMinutes(1);
            await using var contenderScope = fixture.CreateScope();
            var contender = contenderScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            // NOWAIT establishes actual native lock ownership without a delay,
            // polling loop or relying on how quickly another worker is scheduled.
            var entity = contender.Model.FindEntityType(typeof(RegistrationFinalizationEffect))!;
            string table = new NpgsqlCommandBuilder().QuoteIdentifier(entity.GetSchema()!) + "." +
                new NpgsqlCommandBuilder().QuoteIdentifier(entity.GetTableName()!);
            PostgresException? denied = null;
            try
            {
                await contender.Database.ExecuteSqlRawAsync(
                    $"SELECT id FROM {table} WHERE tenant_id = {{0}} AND id = {{1}} FOR UPDATE NOWAIT",
                    [claim.TenantId, claim.EffectId]);
            }
            catch (PostgresException exception)
            {
                denied = exception;
            }
            await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
        }
        finally
        {
            secrets.Release.TrySetResult();
        }

        AdmissionIssuanceResult stale = await pending.WaitAsync(TimeSpan.FromSeconds(20));
        await Assert.That(stale.Outcome).IsEqualTo(AdmissionIssuanceOutcome.StaleFinalizationClaim);
        await Assert.That(stale.OneTimeCredentials).IsEmpty();
        await AssertNoIssuanceAsync(fixture);
        await ReplaceClaimAsync(fixture, claim, clock);
        await using var retryScope = fixture.CreateScope();
        var retryContext = retryScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        RegistrationFinalizationEffect replacement = await retryContext.RegistrationFinalizationEffects
            .AsNoTracking().SingleAsync(value => value.Id == claim.EffectId);
        var current = new RegistrationFinalizationClaim(replacement.Id, replacement.TenantId,
            replacement.RegistrationOrderId, replacement.ProcessingLeaseToken!.Value, replacement.ProcessingFence);
        var service = Issuance(retryContext, clock, new CustodySecrets());
        await Assert.That((await service.IssueConfirmedAsync(Request(claim), CancellationToken.None)).Outcome)
            .IsEqualTo(AdmissionIssuanceOutcome.StaleFinalizationClaim);
        AdmissionIssuanceResult issued = await service.IssueConfirmedAsync(Request(current), CancellationToken.None);
        await Assert.That(issued.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
        await Assert.That(issued.IssuedTicketIds.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExpiryAfterNativeTicketSaveRollsBackTicketCredentialAndDeliveryIntent()
    {
        var clock = new CustodyClock();
        var expiry = new ExpireAfterTicketSave(clock);
        await using var fixture = await CreateAsync(clock, expiry);
        RegistrationFinalizationClaim claim = await SeedIssuanceClaimAsync(fixture, clock);
        expiry.Armed = true;
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        AdmissionIssuanceResult result = await Issuance(context, clock, new CustodySecrets())
            .IssueConfirmedAsync(Request(claim), CancellationToken.None);

        await Assert.That(expiry.ObservedTicketSave).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(AdmissionIssuanceOutcome.StaleFinalizationClaim);
        await Assert.That(result.OneTimeCredentials).IsEmpty();
        await AssertNoIssuanceAsync(fixture);
    }

    [Test]
    public async Task CurrentIssuanceReplayAndDirectAuthenticatedReplayPreserveOneTicket()
    {
        var clock = new CustodyClock();
        await using var fixture = await CreateAsync(clock);
        RegistrationFinalizationClaim claim = await SeedIssuanceClaimAsync(fixture, clock);
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var service = Issuance(context, clock, new CustodySecrets());
        AdmissionIssuanceResult first = await service.IssueConfirmedAsync(Request(claim), CancellationToken.None);
        AdmissionIssuanceResult replay = await service.IssueConfirmedAsync(Request(claim), CancellationToken.None);
        clock.Now = clock.Now.AddMinutes(1);
        AdmissionIssuanceResult direct = await service.IssueConfirmedAsync(
            Request(claim) with { FinalizationClaim = null }, CancellationToken.None);

        await Assert.That(first.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
        await Assert.That(replay.Outcome).IsEqualTo(AdmissionIssuanceOutcome.AlreadyIssued);
        await Assert.That(direct.Outcome).IsEqualTo(AdmissionIssuanceOutcome.AlreadyIssued);
        await Assert.That(replay.ExistingTicketIds).IsEquivalentTo(first.IssuedTicketIds);
        await Assert.That(direct.ExistingTicketIds).IsEquivalentTo(first.IssuedTicketIds);
        await Assert.That(await context.AdmissionTickets.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.AdmissionTicketCredentials.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.AdmissionDeliveryIntents.CountAsync()).IsEqualTo(1);
    }

    private async Task<EventVisitorCapabilitySqliteFixture> CreateAsync(
        CustodyClock clock, IInterceptor? interceptor = null)
    {
        await database.ResetAsync();
        return await EventVisitorCapabilitySqliteFixture.CreatePostgreSqlAsync(database.ConnectionString, services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            if (interceptor is not null)
                services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(interceptor));
        });
    }

    private static AdmissionIssuanceRequest Request(RegistrationFinalizationClaim claim) =>
        new(claim.TenantId, claim.RegistrationOrderId, claim.EffectId, AdmissionIssuanceAuthority.ConfirmedFreeOrder)
        {
            FinalizationClaim = claim
        };

    private static AdmissionIssuanceService Issuance(
        ExploreDbContext context, CustodyClock clock, CustodySecrets secrets) => new(
            new AdmissionIssuanceRepository(context, new ParticipantAdmissionEligibilityRepository(context), clock),
            new AdmissionCredentialDigestService(secrets,
                Options.Create(new AdmissionCredentialOptions { ActiveKeyVersion = 7 })),
            new AdmissionDeliveryEnvelopeProtector(new EphemeralDataProtectionProvider()),
            new CompletingDelivery(context, clock),
            new EfCoreUnitOfWork(context),
            clock,
            new RegistrationFinalizationRepository(context, clock));

    private static async Task<RegistrationFinalizationClaim> SeedIssuanceClaimAsync(
        EventVisitorCapabilitySqliteFixture fixture, CustodyClock clock)
    {
        var target = await fixture.SeedEventAsync(accountRequired: true, published: true);
        var ticket = await fixture.SeedTicketAsync(target.Id);
        EventTicketCatalogVersion catalog = await fixture.Context.EventTicketCatalogVersions
            .Include(value => value.TicketTypes).SingleAsync(value => value.Id == ticket.CatalogId);
        RegistrationOrder order = RegistrationOrder.Create(
            fixture.TenantId, target.Id, fixture.UserId, fixture.ActorId, BookingPartyTypeEnum.Individual,
            catalog.Id, RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
            null, null, "USD", clock.Now.UtcDateTime, null);
        RegistrationOrderLine line = RegistrationOrderLine.Create(
            catalog, catalog.TicketTypes.Single(), order.Id, 1, null, null);
        order.AddLine(line);
        RegistrationParticipant participant = RegistrationParticipant.Create(
            order.TenantId, order.Id, fixture.UserId, ParticipantTypeEnum.Adult, null);
        RegistrationTicketAssignment assignment = RegistrationTicketAssignment.CreateAssigned(
            Guid.CreateVersion7(), line.Id, 1, participant, clock.Now.UtcDateTime);
        order.AddParticipant(participant);
        order.AddAssignment(line, assignment, participant);
        order.SetPii(RegistrationOrderPii.Create(order.Id, order.TenantId, "Custody", "custody@example.test",
            null, null, (int)RegistrationRetentionPolicyEnum.StandardOperational, clock.Now.UtcDateTime));
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("USD", 0, 0, 0, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, clock.Now.UtcDateTime);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, clock.Now.UtcDateTime);
        order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, clock.Now.UtcDateTime);
        ParticipantAdmissionEligibility eligibility = ParticipantAdmissionEligibility.Create(
            order.TenantId, order.EventId, assignment, participant, false, false, clock.Now.UtcDateTime);
        eligibility.RecordSubjectCompletion(participant, fixture.UserId, null, clock.Now.UtcDateTime, Guid.CreateVersion7());
        RegistrationFinalizationEffect effect = RegistrationFinalizationEffect.Create(order, clock.Now.UtcDateTime);
        Guid token = Guid.CreateVersion7();
        effect.Claim("issuance-custody", token, clock.Now.AddMinutes(1).UtcDateTime, clock.Now.UtcDateTime);
        fixture.Context.AddRange(order, eligibility, effect);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return new(effect.Id, order.TenantId, order.Id, token, effect.ProcessingFence);
    }

    private static async Task AssertNoIssuanceAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await context.AdmissionTickets.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AdmissionTicketCredentials.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AdmissionDeliveryIntents.CountAsync()).IsEqualTo(0);
    }

    private static async Task<RegistrationFinalizationClaim> SeedClaimAsync(
        EventVisitorCapabilitySqliteFixture fixture, CustodyClock clock)
    {
        var target = await fixture.SeedEventAsync(published: true);
        fixture.Context.EventSessions.Add(new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            EventId = target.Id,
            Event = null!,
            TenantId = fixture.TenantId,
            Tenant = null!,
            RegistrationModeId = (int)RegistrationModeEnum.Open,
            StartTime = clock.Now.AddDays(1),
            EndTime = clock.Now.AddDays(1).AddHours(1)
        });
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var ticket = await fixture.SeedTicketAsync(target.Id);
        var proof = await fixture.IssueGuestProofAsync(new(target.Id, ticket.CatalogId,
            BookingPartyTypeEnum.Individual, [new(ticket.TicketId, 1, null)]));
        var created = await fixture.ExecuteCommandAsync<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>(proof.Request);
        await Assert.That(created.IsSuccess).IsTrue();
        var lifecycle = fixture.Services.GetRequiredService<IRegistrationOrderLifecycleService>();
        await Assert.That((await lifecycle.SubmitAsync(created.Id, fixture.TenantId, CancellationToken.None)).IsSuccess).IsTrue();
        RegistrationOrder order = await fixture.Context.RegistrationOrders.SingleAsync(value => value.Id == created.Id);
        RegistrationFinalizationEffect? effect = await fixture.Context.RegistrationFinalizationEffects
            .SingleOrDefaultAsync(value => value.RegistrationOrderId == order.Id);
        if (effect is null)
        {
            effect = RegistrationFinalizationEffect.Create(order, clock.Now.UtcDateTime);
            fixture.Context.RegistrationFinalizationEffects.Add(effect);
        }
        Guid token = Guid.CreateVersion7();
        effect.Claim("custody-specification", token, clock.Now.AddMinutes(1).UtcDateTime, clock.Now.UtcDateTime);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return new(effect.Id, order.TenantId, order.Id, token, effect.ProcessingFence);
    }

    private static async Task ReplaceClaimAsync(
        EventVisitorCapabilitySqliteFixture fixture, RegistrationFinalizationClaim original, CustodyClock clock)
    {
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Explore.Persistence.ExploreDbContext>();
        RegistrationFinalizationEffect effect = await context.RegistrationFinalizationEffects.SingleAsync(value => value.Id == original.EffectId);
        effect.RecoverExpiredClaim(clock.Now.UtcDateTime);
        effect.Claim("replacement", Guid.CreateVersion7(), clock.Now.AddMinutes(1).UtcDateTime, clock.Now.UtcDateTime);
        await context.SaveChangesAsync();
    }

    private static async Task AssertOrderStatusAsync(
        EventVisitorCapabilitySqliteFixture fixture, Guid orderId, RegistrationOrderStatusEnum expected)
    {
        await using var scope = fixture.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IRegistrationInventoryRepository>();
        RegistrationOrder? order = await inventory.GetOrderByIdAsync(orderId, fixture.TenantId, CancellationToken.None);
        await Assert.That(order!.RegistrationOrderStatusId).IsEqualTo((int)expected);
    }

    private sealed class CustodyClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ExpireAfterTicketSave(CustodyClock clock) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool ObservedTicketSave { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<AdmissionTicket>().Any())
            {
                Armed = false;
                ObservedTicketSave = true;
                clock.Now = clock.Now.AddMinutes(1);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ExpireAfterCheckoutSave(CustodyClock clock) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool ObservedCheckoutSave { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<RegistrationOrder>().Any(entry =>
                    entry.Entity.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.ReadyForCheckout))
            {
                Armed = false;
                ObservedCheckoutSave = true;
                clock.Now = clock.Now.AddMinutes(1);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CompletingDelivery(ExploreDbContext context, CustodyClock clock) : IAdmissionDeliveryDispatcher
    {
        public async Task<AdmissionDeliveryDispatchResult> DispatchAsync(
            AdmissionDeliveryDispatchRequest request, CancellationToken cancellationToken)
        {
            AdmissionDeliveryIntent intent = await context.AdmissionDeliveryIntents
                .SingleAsync(value => value.Id == request.DeliveryIntentId, cancellationToken);
            intent.MarkRouted(clock.Now.UtcDateTime);
            intent.CompleteHandoff(Guid.CreateVersion7().ToString("N"), clock.Now.UtcDateTime);
            await context.SaveChangesAsync(cancellationToken);
            return new(AdmissionDeliveryOutcome.Delivered);
        }
    }

    private sealed class CustodySecrets(bool block = false) : ISecretResolver
    {
        private readonly string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SecretResolutionResult> ResolveQualifiedAsync(
            string settingKey, SecretScope scope, Guid? scopeId, string qualifier,
            CancellationToken cancellationToken = default)
        {
            if (block)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return settingKey == Explore.Domain.Secrets.SecretDefinitionRegistry.Keys.Admissions.CredentialLookupHmacKey &&
                qualifier == "v7"
                ? SecretResolutionResult.Resolved(new ResolvedSecret(
                    settingKey, key, SecretSourceType.EnvironmentVariable, scope, scopeId,
                    new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc)))
                : SecretResolutionResult.Unconfigured;
        }

        public Task<SecretResolutionResult> ResolveAsync(
            string settingKey, Guid? tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(
            Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(
            string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
