// ABOUTME: Proves PostgreSQL ticketing contention and provider-neutral assignment/deletion races.
// ABOUTME: Runs real handlers against tracked EF concurrency anchors with deterministic task gates.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Features.EventTicketing.Handlers.Commands;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TUnit.Assertions;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EventTicketingRowLockConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task DeleteWinningPoolRace_RetryRejectsAssignmentAndLeavesNoLiveReference()
    {
        DeletionWinningScenarioResult result = await new EventTicketingRowLockScenarioRunner(fixture)
            .RunDeletionWinningAsync();

        await Assert.That(result.LockSqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
        await Assert.That(result.ConcurrentAssignmentFailureCode).IsEqualTo("event_ticketing_concurrency_conflict");
        await Assert.That(result.RetryFailureCode).IsEqualTo("event_ticketing_not_found");
        await Assert.That(result.FinalState).IsEqualTo("pool_deleted_no_live_ticket");
        await Assert.That(result.TenantIsolated).IsTrue();
        Console.WriteLine($"deletion_lock={result.LockSqlState}");
        Console.WriteLine($"deletion_response={result.ConcurrentAssignmentFailureCode}");
        Console.WriteLine($"deletion_final={result.FinalState}");
        Console.WriteLine($"tenant_isolated={result.TenantIsolated.ToString().ToLowerInvariant()}");
    }

    [Test]
    public async Task AssignmentWinningPoolRace_RetryRejectsDeletionAndKeepsTicketAndPool()
    {
        AssignmentWinningScenarioResult result = await new EventTicketingRowLockScenarioRunner(fixture)
            .RunAssignmentWinningAsync();

        await Assert.That(result.LockSqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
        await Assert.That(result.AssignmentSucceeded).IsTrue();
        await Assert.That(result.ConcurrentDeletionFailureCode).IsEqualTo("event_ticketing_validation_failed");
        await Assert.That(result.RetryFailureCode).IsEqualTo("event_ticketing_validation_failed");
        await Assert.That(result.FinalState).IsEqualTo("ticket_and_pool_active");
        await Assert.That(result.TenantIsolated).IsTrue();
        Console.WriteLine($"assignment_lock={result.LockSqlState}");
        Console.WriteLine($"assignment_final={result.FinalState}");
        Console.WriteLine($"tenant_isolated={result.TenantIsolated.ToString().ToLowerInvariant()}");
    }
}

public sealed record DeletionWinningScenarioResult(
    string LockSqlState,
    string? ConcurrentAssignmentFailureCode,
    string? RetryFailureCode,
    string FinalState,
    bool TenantIsolated);

public sealed record AssignmentWinningScenarioResult(
    string LockSqlState,
    bool AssignmentSucceeded,
    string? ConcurrentDeletionFailureCode,
    string? RetryFailureCode,
    string FinalState,
    bool TenantIsolated);

public sealed class EventTicketingRowLockScenarioRunner(PostgreSqlContainerFixture fixture)
{
    public async Task<DeletionWinningScenarioResult> RunDeletionWinningAsync()
    {
        (Guid tenantId, Guid eventId, Guid poolId) = await SeedAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        string lockSqlState = await ObservePostgreSqlLockContentionAsync(tenantId, eventId, poolId, timeout.Token);

        var deletionLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var assignmentLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDeletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAssignment = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ExploreDbContext deletionContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        await using ExploreDbContext assignmentContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        await using ServiceProvider services = CreateServices();
        var deletionRepository = new GatedTicketCatalogRepository(
            new EventTicketCatalogRepository(deletionContext), deletionLoaded, releaseDeletion);
        var assignmentRepository = new GatedTicketCatalogRepository(
            new EventTicketCatalogRepository(assignmentContext), assignmentLoaded, releaseAssignment);

        Task<BaseCommandResponse<Guid>> deletion = CreateDeletionHandler(
            deletionContext, deletionRepository, tenantId, services)
            .Handle(new DeleteEventCapacityPoolCommand { EventId = eventId, CapacityPoolId = poolId }, timeout.Token);
        Task<BaseCommandResponse<Guid>> assignment = CreateAssignmentHandler(
            assignmentContext, assignmentRepository, tenantId, services)
            .Handle(CreateAssignment(eventId, poolId), timeout.Token);

        await Task.WhenAll(deletionLoaded.Task, assignmentLoaded.Task).WaitAsync(timeout.Token);
        releaseDeletion.TrySetResult();
        BaseCommandResponse<Guid> deletionResult = await deletion.WaitAsync(timeout.Token);
        releaseAssignment.TrySetResult();
        BaseCommandResponse<Guid> losingAssignment = await assignment.WaitAsync(timeout.Token);

        await using ExploreDbContext retryContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        BaseCommandResponse<Guid> retry = await CreateAssignmentHandler(
            retryContext, new EventTicketCatalogRepository(retryContext), tenantId, services)
            .Handle(CreateAssignment(eventId, poolId), timeout.Token);
        await using ExploreDbContext verifyContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        var verifyRepository = new EventTicketCatalogRepository(verifyContext);
        bool poolDeleted = await verifyRepository.GetCapacityPoolByIdEventAndTenantAsync(
            poolId, eventId, tenantId, timeout.Token) is null;
        bool hasLiveReference = await verifyRepository.HasLiveTicketTypeReferencesAsync(
            poolId, eventId, tenantId, timeout.Token);
        bool tenantIsolated = await CheckScopedMalformedInputsAsync(
            eventId, poolId, tenantId, services, timeout.Token);

        return new DeletionWinningScenarioResult(
            lockSqlState,
            losingAssignment.FailureCode,
            retry.FailureCode,
            deletionResult.IsSuccess && poolDeleted && !hasLiveReference
                ? "pool_deleted_no_live_ticket"
                : "invalid",
            tenantIsolated);
    }

    public async Task<AssignmentWinningScenarioResult> RunAssignmentWinningAsync()
    {
        (Guid tenantId, Guid eventId, Guid poolId) = await SeedAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        string lockSqlState = await ObservePostgreSqlLockContentionAsync(tenantId, eventId, poolId, timeout.Token);

        var deletionLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var assignmentLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDeletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAssignment = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ExploreDbContext deletionContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        await using ExploreDbContext assignmentContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        await using ServiceProvider services = CreateServices();
        var deletionRepository = new GatedTicketCatalogRepository(
            new EventTicketCatalogRepository(deletionContext), deletionLoaded, releaseDeletion);
        var assignmentRepository = new GatedTicketCatalogRepository(
            new EventTicketCatalogRepository(assignmentContext), assignmentLoaded, releaseAssignment);

        Task<BaseCommandResponse<Guid>> deletion = CreateDeletionHandler(
            deletionContext, deletionRepository, tenantId, services)
            .Handle(new DeleteEventCapacityPoolCommand { EventId = eventId, CapacityPoolId = poolId }, timeout.Token);
        Task<BaseCommandResponse<Guid>> assignment = CreateAssignmentHandler(
            assignmentContext, assignmentRepository, tenantId, services)
            .Handle(CreateAssignment(eventId, poolId), timeout.Token);

        await Task.WhenAll(deletionLoaded.Task, assignmentLoaded.Task).WaitAsync(timeout.Token);
        releaseAssignment.TrySetResult();
        BaseCommandResponse<Guid> assignmentResult = await assignment.WaitAsync(timeout.Token);
        releaseDeletion.TrySetResult();
        BaseCommandResponse<Guid> losingDeletion = await deletion.WaitAsync(timeout.Token);
        await using ExploreDbContext retryContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        BaseCommandResponse<Guid> retry = await CreateDeletionHandler(
            retryContext, new EventTicketCatalogRepository(retryContext), tenantId, services)
            .Handle(new DeleteEventCapacityPoolCommand { EventId = eventId, CapacityPoolId = poolId }, timeout.Token);
        await using ExploreDbContext verifyContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        var verifyRepository = new EventTicketCatalogRepository(verifyContext);
        EventTicketType? ticket = await verifyRepository.GetTicketTypeByIdEventAndTenantAsync(
            assignmentResult.Id, eventId, tenantId, timeout.Token);
        bool poolActive = await verifyRepository.GetCapacityPoolByIdEventAndTenantAsync(
            poolId, eventId, tenantId, timeout.Token) is not null;
        bool hasLiveReference = await verifyRepository.HasLiveTicketTypeReferencesAsync(
            poolId, eventId, tenantId, timeout.Token);
        bool tenantIsolated = await CheckScopedMalformedInputsAsync(
            eventId, poolId, tenantId, services, timeout.Token);

        return new AssignmentWinningScenarioResult(
            lockSqlState,
            assignmentResult.IsSuccess,
            losingDeletion.FailureCode,
            retry.FailureCode,
            ticket?.CapacityPoolId == poolId && poolActive && hasLiveReference
                ? "ticket_and_pool_active"
                : "invalid",
            tenantIsolated);
    }

    private async Task<string> ObservePostgreSqlLockContentionAsync(
        Guid tenantId,
        Guid eventId,
        Guid poolId,
        CancellationToken cancellationToken)
    {
        await using ExploreDbContext holder = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        await using ExploreDbContext contender = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        await using var holderTransaction = await holder.Database.BeginTransactionAsync(cancellationToken);
        await holder.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT id FROM event_capacity_pools
            WHERE id = {poolId} AND event_id = {eventId} AND tenant_id = {tenantId}
            FOR UPDATE
            """, cancellationToken);
        await using var contenderTransaction = await contender.Database.BeginTransactionAsync(cancellationToken);
        await contender.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '250ms'", cancellationToken);

        PostgresException? observed = null;
        try
        {
            await contender.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT id FROM event_capacity_pools
                WHERE id = {poolId} AND event_id = {eventId} AND tenant_id = {tenantId}
                FOR UPDATE
                """, cancellationToken);
        }
        catch (PostgresException exception)
        {
            observed = exception;
        }

        return observed?.SqlState ?? "none";
    }

    private async Task<bool> CheckScopedMalformedInputsAsync(
        Guid eventId,
        Guid poolId,
        Guid tenantId,
        ServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using ExploreDbContext wrongEventContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));
        BaseCommandResponse<Guid> wrongEvent = await CreateAssignmentHandler(
            wrongEventContext, new EventTicketCatalogRepository(wrongEventContext), tenantId, services)
            .Handle(CreateAssignment(Guid.CreateVersion7(), poolId), cancellationToken);
        Guid wrongTenantId = Guid.CreateVersion7();
        await using ExploreDbContext wrongTenantContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(wrongTenantId));
        BaseCommandResponse<Guid> wrongTenant = await CreateAssignmentHandler(
            wrongTenantContext, new EventTicketCatalogRepository(wrongTenantContext), wrongTenantId, services)
            .Handle(CreateAssignment(eventId, poolId), cancellationToken);
        return wrongEvent.FailureCode == "event_ticketing_not_found"
            && wrongTenant.FailureCode == "event_ticketing_not_found";
    }

    private static CreateEventTicketTypeCommand CreateAssignment(Guid eventId, Guid poolId) => new()
    {
        EventId = eventId,
        TicketType = new ManageEventTicketTypeDto
        {
            Name = "Assigned ticket",
            TicketPricingModeId = (int)TicketPricingModeEnum.Free,
            ParticipantDataCollectionModeId = (int)ParticipantDataCollectionModeEnum.None,
            CapacityPoolId = poolId,
            Entitlements =
            [
                new ManageTicketTypeEntitlementDto
                {
                    EntitlementScopeTypeId = (int)EntitlementScopeTypeEnum.Event,
                    IncludedQuantity = 1,
                    EntitlementSelectionRuleId = (int)EntitlementSelectionRuleEnum.AllIncluded
                }
            ]
        }
    };

    private static CreateEventTicketTypeCommandHandler CreateAssignmentHandler(
        ExploreDbContext context,
        IEventTicketCatalogRepository catalogs,
        Guid tenantId,
        ServiceProvider services)
    {
        var tenant = new TestTenantContext(tenantId);
        return new CreateEventTicketTypeCommandHandler(
            new EventRepository(context),
            catalogs,
            new TicketTypeEntitlementResolver(new EventDayRepository(context), new EventSessionRepository(context), tenant),
            tenant,
            new EfCoreUnitOfWork(context),
            services.GetRequiredService<HybridCache>());
    }

    private static DeleteEventCapacityPoolCommandHandler CreateDeletionHandler(
        ExploreDbContext context,
        IEventTicketCatalogRepository catalogs,
        Guid tenantId,
        ServiceProvider services) => new(
        new EventRepository(context),
        catalogs,
        new TestTenantContext(tenantId),
        new TestCurrentUser(Guid.CreateVersion7()),
        TimeProvider.System,
        new EfCoreUnitOfWork(context),
        services.GetRequiredService<HybridCache>());

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildIsolatedServiceProvider();
    }

    private async Task<(Guid TenantId, Guid EventId, Guid PoolId)> SeedAsync()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        TenantStatus activeStatus = await context.TenantStatuses.SingleAsync(status => status.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant { FullName = "Ticketing lock tenant", Slug = $"ticket-lock-{Guid.NewGuid():N}", TenantStatusId = activeStatus.Id, TenantStatus = activeStatus };
        context.Tenants.Add(tenant);
        var user = new User { Pii = new UserPii { Email = "ticket-lock@example.test", FirstName = "Ticket", LastName = "Lock" } };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var actor = new Actor { Pii = new ActorPii { DisplayName = "Ticket Lock Actor" }, ActorTypeId = 1, ActorType = null!, UserId = user.Id };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        Guid eventId = Guid.CreateVersion7();
        DateTime now = DateTime.UtcNow;
        var eventTarget = new DomainEvent(EventStatusEnum.Draft)
        {
            Id = eventId,
            Title = "Ticket lock event",
            Subtitle = "",
            Description = "",
            FirstSessionDate = DateOnly.FromDateTime(now.AddDays(1)),
            LastSessionDate = DateOnly.FromDateTime(now.AddDays(1)),
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            ActorId = actor.Id,
            Actor = null!,
            OrganizerActorId = actor.Id,
            TenantId = tenant.Id,
            Tenant = tenant,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            TotalViews = 0
        };
        eventTarget.ParticipationConfiguration = EventParticipationConfiguration.Create(
            eventId, tenant.Id, (int)ParticipationHandlingModeEnum.PlatformManaged,
            (int)AdvanceRegistrationObligationEnum.Required, (int)IdentityAccessModeEnum.AccountRequired,
            guestRecoveryPolicy: null, now);
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenant.Id, eventId, "USD", 1);
        EventCapacityPool pool = EventCapacityPool.Create(tenant.Id, eventId, "Pool", 10, 900, CapacityHoldPolicyEnum.TimedHoldOnSelection, CapacityOversellPolicyEnum.Disallow, true);
        context.AddRange(eventTarget, catalog, pool);
        await context.SaveChangesAsync();
        return (tenant.Id, eventId, pool.Id);
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record TestCurrentUser(Guid UserIdValue) : ICurrentUserService
    {
        public Guid? UserId => UserIdValue;
        public bool IsAuthenticated => true;
    }

    private sealed class GatedTicketCatalogRepository(
        IEventTicketCatalogRepository inner,
        TaskCompletionSource loaded,
        TaskCompletionSource release) : IEventTicketCatalogRepository
    {
        public Task<EventTicketCatalogVersion?> GetManagementCatalogAsync(Guid eventId, Guid tenantId, CancellationToken cancellationToken) => inner.GetManagementCatalogAsync(eventId, tenantId, cancellationToken);
        public Task<EventTicketCatalogVersion?> GetPublishedCatalogAsync(Guid eventId, Guid tenantId, CancellationToken cancellationToken) => inner.GetPublishedCatalogAsync(eventId, tenantId, cancellationToken);
        public Task<EventTicketCatalogVersion?> GetOrderCatalogAsync(Guid catalogId, Guid eventId, Guid tenantId, CancellationToken cancellationToken) => inner.GetOrderCatalogAsync(catalogId, eventId, tenantId, cancellationToken);
        public Task<EventTicketCatalogVersion?> GetDraftCatalogForUpdateAsync(Guid eventId, Guid tenantId, CancellationToken cancellationToken) => inner.GetDraftCatalogForUpdateAsync(eventId, tenantId, cancellationToken);
        public Task<EventTicketCatalogVersion?> GetPublishedForUpdateAsync(Guid eventId, Guid tenantId, CancellationToken cancellationToken) => inner.GetPublishedForUpdateAsync(eventId, tenantId, cancellationToken);
        public Task<EventTicketCatalogVersion?> GetByEventVersionAndTenantAsync(Guid eventId, int versionNumber, Guid tenantId, CancellationToken cancellationToken) => inner.GetByEventVersionAndTenantAsync(eventId, versionNumber, tenantId, cancellationToken);
        public Task<EventTicketType?> GetTicketTypeByIdEventAndTenantAsync(Guid ticketTypeId, Guid eventId, Guid tenantId, CancellationToken cancellationToken) => inner.GetTicketTypeByIdEventAndTenantAsync(ticketTypeId, eventId, tenantId, cancellationToken);
        public Task<EventCapacityPool?> GetCapacityPoolByIdEventAndTenantAsync(Guid capacityPoolId, Guid eventId, Guid tenantId, CancellationToken cancellationToken) => inner.GetCapacityPoolByIdEventAndTenantAsync(capacityPoolId, eventId, tenantId, cancellationToken);
        public async Task<EventCapacityPool?> GetActiveCapacityPoolForUpdateAsync(Guid capacityPoolId, Guid eventId, Guid tenantId, CancellationToken cancellationToken)
        {
            EventCapacityPool? pool = await inner.GetActiveCapacityPoolForUpdateAsync(capacityPoolId, eventId, tenantId, cancellationToken);
            loaded.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return pool;
        }
        public Task<bool> HasLiveTicketTypeReferencesAsync(Guid capacityPoolId, Guid eventId, Guid tenantId, CancellationToken cancellationToken) => inner.HasLiveTicketTypeReferencesAsync(capacityPoolId, eventId, tenantId, cancellationToken);
        public Task AddAsync(EventTicketCatalogVersion catalog, CancellationToken cancellationToken) => inner.AddAsync(catalog, cancellationToken);
        public Task UpdateAsync(EventTicketCatalogVersion catalog, CancellationToken cancellationToken) => inner.UpdateAsync(catalog, cancellationToken);
        public Task RemoveEntitlementsAsync(IEnumerable<TicketTypeEntitlement> entitlements, CancellationToken cancellationToken) => inner.RemoveEntitlementsAsync(entitlements, cancellationToken);
        public Task AddCapacityPoolAsync(EventCapacityPool pool, CancellationToken cancellationToken) => inner.AddCapacityPoolAsync(pool, cancellationToken);
        public Task UpdateCapacityPoolAsync(EventCapacityPool pool, CancellationToken cancellationToken) => inner.UpdateCapacityPoolAsync(pool, cancellationToken);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => inner.SaveChangesAsync(cancellationToken);
    }
}
