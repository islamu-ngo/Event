// ABOUTME: Real-PostgreSQL acceptance for bounded EventLocation disclosure and exact-read auditing.
// ABOUTME: Counts SQL and authorization batches while proving tenant-scoped PII joins and no per-row I/O.

using System.Data.Common;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Tests.Shared.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Privacy;

[ClassDataSource<RegistrationCoveragePostgreSqlFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
[Category("EventLocationPrivacy")]
public sealed class EventLocationDisclosureBatchTests(RegistrationCoveragePostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task PublicBatch_UsesOneLocationPiiQueryAndOneRoomQueryWithoutAuthorization()
    {
        DisclosureGraph graph = await SeedGraphAsync("public");
        var commands = new SelectCommandInterceptor();
        var authorization = new CountingAuthorizationProvider();
        await using var context = CreateTenantContext(graph.TenantId, commands);
        var service = CreateService(context, authorization, graph.RequesterUserId);

        IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results = await service.ResolveManyAsync(
        [
            new(graph.TenantId, graph.EventId, graph.EventLocationIds[0], graph.RoomId,
                Guid.CreateVersion7(), EventLocationDisclosurePurpose.Public),
            new(graph.TenantId, graph.EventId, graph.EventLocationIds[1], null,
                Guid.CreateVersion7(), EventLocationDisclosurePurpose.Public)
        ], CancellationToken.None);

        await Assert.That(commands.SelectCommands.Count).IsEqualTo(2);
        await Assert.That(commands.SelectCommands.Count(command => ContainsTable(command, "event_locations")))
            .IsEqualTo(1);
        await Assert.That(commands.SelectCommands.Count(command => ContainsTable(command, "location_rooms")))
            .IsEqualTo(1);
        await Assert.That(commands.SelectCommands.Single(command => ContainsTable(command, "event_locations")))
            .Contains("location_pii");
        await Assert.That(authorization.BatchCalls).IsEqualTo(0);
        await Assert.That(results.Count).IsEqualTo(2);
        await Assert.That(results.Values.All(result =>
            result.Purpose == EventLocationDisclosurePurpose.Public
            && result.LocationId is null)).IsTrue();
        await Assert.That(context.ChangeTracker.Entries<EventLocation>()).IsEmpty();
        await Assert.That(context.ChangeTracker.Entries<Location>()).IsEmpty();
        await Assert.That(context.ChangeTracker.Entries<LocationRoom>()).IsEmpty();
    }

    [Test]
    public async Task ManagementBatch_UsesFourSelectsOneAuthorizationBatchAndPersistsEveryDecision()
    {
        DisclosureGraph graph = await SeedGraphAsync("management");
        var commands = new SelectCommandInterceptor();
        var authorization = new CountingAuthorizationProvider();
        await using var context = CreateTenantContext(graph.TenantId, commands);
        var service = CreateService(context, authorization, graph.RequesterUserId);

        IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results = await service.ResolveManyAsync(
        [
            new(graph.TenantId, graph.EventId, graph.EventLocationIds[0], graph.RoomId,
                graph.RequesterUserId, EventLocationDisclosurePurpose.Management),
            new(graph.TenantId, graph.EventId, graph.EventLocationIds[1], null,
                null, EventLocationDisclosurePurpose.Management)
        ], CancellationToken.None);
        int selectCountAtReturn = commands.SelectCommands.Count;

        await Assert.That(selectCountAtReturn).IsEqualTo(4);
        await Assert.That(commands.SelectCommands.Count(command => ContainsTable(command, "event_locations")))
            .IsEqualTo(2);
        await Assert.That(commands.SelectCommands.Count(command => ContainsTable(command, "location_rooms")))
            .IsEqualTo(1);
        await Assert.That(commands.SelectCommands.Count(command => ContainsTable(command, "events")))
            .IsEqualTo(1);
        await Assert.That(commands.SelectCommands.Any(command => command.Contains("location_pii", StringComparison.OrdinalIgnoreCase)))
            .IsTrue();
        await Assert.That(authorization.BatchCalls).IsEqualTo(1);
        await Assert.That(authorization.LastChecks.Count).IsEqualTo(1);
        await Assert.That(authorization.LastChecks[0].ResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(authorization.LastChecks[0].Action)
            .IsEqualTo(AuthorizationActions.Events.ViewManagement);
        await Assert.That(results.Count).IsEqualTo(2);
        await Assert.That(results.Values.All(result => result.LocationId is null)).IsTrue();
        await Assert.That(context.ChangeTracker.Entries<EventLocation>()).IsEmpty();
        await Assert.That(context.ChangeTracker.Entries<Location>()).IsEmpty();
        await Assert.That(context.ChangeTracker.Entries<LocationRoom>()).IsEmpty();

        await using var verification = CreateTenantContext(graph.TenantId);
        EventLocationExactReadAudit[] audits = await verification.EventLocationExactReadAudits
            .AsNoTracking()
            .Where(audit => graph.EventLocationIds.Contains(audit.EventLocationId))
            .OrderBy(audit => audit.EventLocationId)
            .ToArrayAsync();
        await Assert.That(audits.Length).IsEqualTo(2);
        await Assert.That(audits.All(audit =>
            audit.TenantId == graph.TenantId
            && audit.RequesterUserId == graph.RequesterUserId
            && audit.Purpose == EventLocationExactReadPurposeEnum.EventManagement
            && audit.WasAuthorized
            && audit.OccurredAtUtc == Now.UtcDateTime
            && (audit.CorrelationId.HasValue || audit.TraceId.HasValue))).IsTrue();
        await Assert.That(commands.SelectCommands.Count).IsEqualTo(selectCountAtReturn);
    }

    [Test]
    public async Task MaximumBatch_UsesFixedReadsAndOneAuthorizationWhileOverMaximumDoesNoWork()
    {
        DisclosureGraph graph = await SeedGraphAsync("maximum", IEventLocationDisclosureService.MaximumBatchSize);
        var commands = new SelectCommandInterceptor();
        var authorization = new CountingAuthorizationProvider();
        await using var context = CreateTenantContext(graph.TenantId, commands);
        var service = CreateService(context, authorization, graph.RequesterUserId);
        EventLocationDisclosureRequest[] requests = graph.EventLocationIds
            .Select((id, index) => new EventLocationDisclosureRequest(
                graph.TenantId,
                graph.EventId,
                id,
                index == 0 ? graph.RoomId : null,
                graph.RequesterUserId,
                EventLocationDisclosurePurpose.Management))
            .ToArray();

        IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results =
            await service.ResolveManyAsync(requests, CancellationToken.None);

        await Assert.That(results.Count).IsEqualTo(IEventLocationDisclosureService.MaximumBatchSize);
        await Assert.That(commands.SelectCommands.Count).IsEqualTo(4);
        await Assert.That(authorization.BatchCalls).IsEqualTo(1);
        await using (var verification = CreateTenantContext(graph.TenantId))
        {
            await Assert.That(await verification.EventLocationExactReadAudits.CountAsync(audit =>
                graph.EventLocationIds.Contains(audit.EventLocationId)))
                .IsEqualTo(IEventLocationDisclosureService.MaximumBatchSize);
        }

        var rejectedCommands = new SelectCommandInterceptor();
        var rejectedAuthorization = new CountingAuthorizationProvider();
        await using var rejectedContext = CreateTenantContext(graph.TenantId, rejectedCommands);
        var rejectedService = CreateService(rejectedContext, rejectedAuthorization, graph.RequesterUserId);
        EventLocationDisclosureRequest[] overMaximum =
        [
            .. requests,
            new(
                graph.TenantId,
                graph.EventId,
                Guid.CreateVersion7(),
                null,
                graph.RequesterUserId,
                EventLocationDisclosurePurpose.Management)
        ];

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            rejectedService.ResolveManyAsync(overMaximum, CancellationToken.None));
        await Assert.That(rejectedCommands.SelectCommands).IsEmpty();
        await Assert.That(rejectedAuthorization.BatchCalls).IsEqualTo(0);
    }

    [Test]
    public async Task DeniedManagementRead_CommitsPiiFreeAuditBeforeReturningHiddenResult()
    {
        DisclosureGraph graph = await SeedGraphAsync("denied");
        var authorization = new CountingAuthorizationProvider(allow: false);
        await using var context = CreateTenantContext(graph.TenantId);
        var service = CreateService(context, authorization, graph.RequesterUserId);

        IReadOnlyDictionary<Guid, EventLocationDisclosureResult> results = await service.ResolveManyAsync(
            graph.EventLocationIds.Select(id => new EventLocationDisclosureRequest(
                graph.TenantId,
                graph.EventId,
                id,
                null,
                graph.RequesterUserId,
                EventLocationDisclosurePurpose.Management)).ToArray(),
            CancellationToken.None);

        await Assert.That(results.Values.All(result =>
            result.State == EventLocationDisclosureState.Hidden
            && result.Values is null
            && result.LocationId is null)).IsTrue();
        await Assert.That(authorization.BatchCalls).IsEqualTo(1);
        await using var verification = CreateTenantContext(graph.TenantId);
        EventLocationExactReadAudit[] audits = await verification.EventLocationExactReadAudits
            .AsNoTracking()
            .Where(audit => graph.EventLocationIds.Contains(audit.EventLocationId))
            .ToArrayAsync();
        await Assert.That(audits.Length).IsEqualTo(graph.EventLocationIds.Length);
        await Assert.That(audits.All(audit =>
            !audit.WasAuthorized
            && audit.RequesterUserId == graph.RequesterUserId
            && audit.Purpose == EventLocationExactReadPurposeEnum.EventManagement))
            .IsTrue();
    }

    [Test]
    public async Task AuditFailureOrCancellation_ReturnsNoDisclosureAndCleanRetrySucceeds()
    {
        DisclosureGraph graph = await SeedGraphAsync("audit-failure");
        EventLocationDisclosureRequest[] requests = graph.EventLocationIds
            .Select(id => new EventLocationDisclosureRequest(
                graph.TenantId,
                graph.EventId,
                id,
                null,
                graph.RequesterUserId,
                EventLocationDisclosurePurpose.Management))
            .ToArray();
        await using (var failingContext = CreateTenantContext(graph.TenantId))
        {
            var failingService = CreateService(
                failingContext,
                new CountingAuthorizationProvider(),
                graph.RequesterUserId,
                new ThrowingExactReadAuditRepository());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                failingService.ResolveManyAsync(requests, CancellationToken.None));
        }

        await using (var verification = CreateTenantContext(graph.TenantId))
        {
            await Assert.That(await verification.EventLocationExactReadAudits.CountAsync(audit =>
                graph.EventLocationIds.Contains(audit.EventLocationId))).IsEqualTo(0);
        }

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await using (var canceledContext = CreateTenantContext(graph.TenantId))
        {
            var canceledService = CreateService(
                canceledContext,
                new CountingAuthorizationProvider(),
                graph.RequesterUserId);
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                canceledService.ResolveManyAsync(requests, cancellation.Token));
        }

        await using (var retryContext = CreateTenantContext(graph.TenantId))
        {
            var retryService = CreateService(
                retryContext,
                new CountingAuthorizationProvider(),
                graph.RequesterUserId);
            IReadOnlyDictionary<Guid, EventLocationDisclosureResult> retry =
                await retryService.ResolveManyAsync(requests, CancellationToken.None);
            await Assert.That(retry.Count).IsEqualTo(graph.EventLocationIds.Length);
        }
    }

    private EventLocationDisclosureService CreateService(
        ExploreDbContext context,
        CountingAuthorizationProvider authorization,
        Guid requesterUserId,
        IEventLocationExactReadAuditRepository? auditRepository = null)
    {
        var exactAudit = new EventLocationExactReadAuditService(
            auditRepository ?? new EventLocationExactReadAuditRepository(context),
            new FixedTimeProvider(Now));
        var management = new EventLocationManagementAuthorizationService(
            new EventRepository(context),
            authorization,
            new TestCurrentUserService(requesterUserId),
            exactAudit,
            NullLogger<EventLocationManagementAuthorizationService>.Instance);
        return new EventLocationDisclosureService(
            new EventLocationRepository(context),
            new LocationRoomRepository(context),
            new EventRegistrationRepository(context),
            new EventLocationRegistrationAccessService(),
            new ResolvedGovernanceService(),
            management,
            new TestCurrentUserService(requesterUserId),
            new EventLocationDisclosureEvaluator(),
            EventLocationPrivacyMetricsFactory.Create(),
            new FixedTimeProvider(Now));
    }

    private ExploreDbContext CreateTenantContext(
        Guid tenantId,
        DbCommandInterceptor? interceptor = null)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new ExploreDbContext(options.Options)
        {
            TenantContext = new TestTenantContext(tenantId)
        };
    }

    private async Task<DisclosureGraph> SeedGraphAsync(string suffix, int locationCount = 2)
    {
        await using var context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Disclosure {suffix} tenant",
            Slug = $"disclosure-{suffix}-{Guid.NewGuid():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"disclosure-{suffix}-{Guid.NewGuid():N}@example.test",
                FirstName = "Disclosure",
                LastName = "Manager"
            }
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Disclosure manager" }
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = null!,
            Title = $"Disclosure {suffix} event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Private,
            VisibilityType = null!
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        Location[] locations = Enumerable.Range(0, locationCount)
            .Select(index => CreateLocation(tenant.Id, $"Disclosure {suffix} {index}"))
            .ToArray();
        context.Locations.AddRange(locations);
        await context.SaveChangesAsync();
        var room = new LocationRoom
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = null!,
            LocationId = locations[0].Id,
            Location = null!,
            Name = "Room A",
            Description = "Management-only room description",
            SortOrder = 1,
            CreatedAt = Now.UtcDateTime,
            CreatedBy = user.Id,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.LocationRooms.Add(room);
        await context.SaveChangesAsync();

        EventLocation[] placements = locations
            .Select(location => EventLocation.CreatePhysical(
                tenant.Id,
                @event.Id,
                location.Id,
                user.Id,
                Now.UtcDateTime))
            .ToArray();
        context.EventLocations.AddRange(placements);
        await context.SaveChangesAsync();
        return new(
            tenant.Id,
            @event.Id,
            user.Id,
            placements.Select(placement => placement.Id).ToArray(),
            room.Id);
    }

    private static Location CreateLocation(Guid tenantId, string name)
    {
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            FullName = name,
            Country = "BE",
            City = "Brussels",
            Timezone = "Europe/Brussels",
            CreatedAt = Now.UtcDateTime,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        location.ClassifyAs(LocationKindEnum.CommercialVenue);
        location.SetProviderAddress(
            "Test address",
            "1000",
            Explore.Domain.ValueObjects.GeoCoordinate.Create(50.85, 4.35));
        return location;
    }

    private static bool ContainsTable(string command, string table) =>
        command.Contains(table, StringComparison.OrdinalIgnoreCase);

    private sealed record DisclosureGraph(
        Guid TenantId,
        Guid EventId,
        Guid RequesterUserId,
        Guid[] EventLocationIds,
        Guid RoomId);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record TestCurrentUserService(Guid RequiredUserId) : ICurrentUserService
    {
        public Guid? UserId => RequiredUserId;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ResolvedGovernanceService : ILocationPrivacyGovernanceService
    {
        public Task<EffectiveLocationPrivacyGovernance> ResolveAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new EffectiveLocationPrivacyGovernance(
                IsResolved: tenantId != Guid.Empty,
                LocationPrivacyGovernanceReasonCode.Resolved,
                AllowHomeLocations: true,
                AllowPublicExactAddress: true,
                AllowPublicCoordinates: true,
                MinimumHomeAudience: LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
                DefaultRevealOffset: TimeSpan.Zero));
        }
    }

    private sealed class CountingAuthorizationProvider(bool allow = true) : IAuthorizationProvider
    {
        public int BatchCalls { get; private set; }
        public IReadOnlyList<AuthorizationRequest> LastChecks { get; private set; } = [];

        public Task<AuthorizationDecision> AuthorizeAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Disclosure must use one authorization batch.");

        public Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCalls++;
            LastChecks = requests;
            var decision = allow
                ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local);
            return Task.FromResult<IReadOnlyList<AuthorizationDecision>>(
                Enumerable.Repeat(decision, requests.Count).ToArray());
        }
    }

    private sealed class SelectCommandInterceptor : DbCommandInterceptor
    {
        public List<string> SelectCommands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                SelectCommands.Add(command.CommandText);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingExactReadAuditRepository : IEventLocationExactReadAuditRepository
    {
        public Task<EventLocationExactReadAudit> AppendAsync(
            EventLocationExactReadAudit audit,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("forced audit failure");

        public Task AppendManyAsync(
            IReadOnlyCollection<EventLocationExactReadAudit> audits,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("forced audit failure");

        public Task<IReadOnlyList<EventLocationExactReadAudit>> GetByEventLocationsAsync(
            IReadOnlyCollection<Guid> eventLocationIds,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("forced audit failure");
    }
}
