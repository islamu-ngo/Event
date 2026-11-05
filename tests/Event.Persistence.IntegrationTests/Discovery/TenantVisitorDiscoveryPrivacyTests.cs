using System.Data;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Discovery;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services.Discovery;
using Explore.Domain.Services.Scheduling;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Discovery;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class TenantVisitorDiscoveryPrivacyTests(PostgreSqlContainerFixture database)
{
    private static readonly DateTimeOffset Now = new(2028, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static GetEventListRequest Criteria => new()
    {
        View = TemporalView.All,
        OperationNow = Now,
        SortBy = "title",
        SortDescending = false
    };

    [Test]
    public async Task AnonymousPrivateTenantQueryDeniesCountAndPageWhileExistingAccountCanRead()
    {
        await using var fixture = await CreateAsync();
        try
        {
            Guid eventId = await SeedAsync(fixture);
            await using (var authenticated = fixture.CreateScope())
            {
                var policy = authenticated.ServiceProvider.GetRequiredService<IVisitorTenantReadAccessPolicy>();
                await Assert.That(policy is VisitorTenantReadAccessPolicy).IsTrue();
                await Assert.That(await policy.CheckAsync(fixture.TenantId))
                    .IsEqualTo(VisitorTenantReadAccessDecision.Allowed);
                var source = ActivatorUtilities.CreateInstance<EventDiscoveryLocalSource>(authenticated.ServiceProvider);
                var result = await source.QueryAsync(Criteria, CancellationToken.None);
                await Assert.That(result.TotalCount).IsEqualTo(1);
                await Assert.That(result.Items.Single().Id).IsEqualTo(eventId);
            }

            SetAnonymous(fixture);
            await using var anonymous = fixture.CreateScope();
            await Assert.That(anonymous.ServiceProvider.GetRequiredService<IUserContext>().IsAuthenticated).IsFalse();
            var deniedSource = ActivatorUtilities.CreateInstance<EventDiscoveryLocalSource>(anonymous.ServiceProvider);
            var denied = await Assert.ThrowsAsync<TenantVisitorReadAccessException>(() =>
                deniedSource.QueryAsync(Criteria, CancellationToken.None));
            await Assert.That(denied!.Decision).IsEqualTo(VisitorTenantReadAccessDecision.AuthenticationRequired);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    [Test]
    public async Task AnonymousPrivateTenantCannotReprojectStoredAuthenticatedSnapshotMembership()
    {
        await using var fixture = await CreateAsync();
        try
        {
            Guid eventId = await SeedAsync(fixture);
            Guid snapshotId = Guid.CreateVersion7();
            await using (var authenticated = fixture.CreateScope())
            {
                var authenticatedServices = authenticated.ServiceProvider;
                var context = authenticatedServices.GetRequiredService<ExploreDbContext>();
                var reader = Reader(authenticatedServices);
                var snapshots = new EventDiscoverySnapshotRepository(context);
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                await snapshots.AcquireFenceAsync(fixture.TenantId, default);
                var revision = await new EventDiscoveryDisclosureRepository(context)
                    .AcquireCurrentAsync(fixture.TenantId, default);
                var capture = await reader.CaptureAsync(Criteria, new());
                await Assert.That(capture.Membership.Single().SourceId).IsEqualTo(eventId);
                var now = DateTime.UtcNow;
                var snapshot = EventDiscoverySnapshot.Create(snapshotId, fixture.TenantId,
                    snapshotId.ToString("N") + snapshotId.ToString("N"),
                    revision.IdentityEpoch, revision.DisclosureEpoch, now, now.AddMinutes(5),
                    capture.Truncated, capture.LocalSourceComplete, capture.RemoteSourceComplete,
                    capture.Membership, new());
                await Assert.That(await snapshots.CaptureAsync(snapshot, new(), now, default)).IsNotNull();
                await transaction.CommitAsync();
            }

            SetAnonymous(fixture);
            await using var anonymous = fixture.CreateScope();
            var services = anonymous.ServiceProvider;
            var repository = new EventDiscoverySnapshotRepository(services.GetRequiredService<ExploreDbContext>());
            var membership = await repository.GetItemsAsync(fixture.TenantId, snapshotId, -1, 25, default);
            await Assert.That(membership.Single().SourceId).IsEqualTo(eventId);
            var denied = await Assert.ThrowsAsync<TenantVisitorReadAccessException>(() =>
                Reader(services).ReprojectAsync(Criteria, membership));
            await Assert.That(denied!.Decision).IsEqualTo(VisitorTenantReadAccessDecision.AuthenticationRequired);
        }
        finally
        {
            await CleanupAsync(fixture);
        }
    }

    private Task<EventVisitorCapabilitySqliteFixture> CreateAsync() =>
        EventVisitorCapabilitySqliteFixture.CreatePostgreSqlAsync(
            database.ConnectionString,
            configureDatabase: configuration =>
            {
                configuration["Database:Migrator:Username"] = database.NativeQueueEnrollment.MigratorLogin;
                foreach (var purpose in new[]
                {
                    PrimaryDatabaseRole.Runtime, PrimaryDatabaseRole.Global,
                    PrimaryDatabaseRole.EmailPolicyInstanceMutation, PrimaryDatabaseRole.EmailPolicyTenantMutation,
                    PrimaryDatabaseRole.InstanceKeyRead, PrimaryDatabaseRole.InstanceKeyWrite,
                    PrimaryDatabaseRole.InstanceKeyIssuance, PrimaryDatabaseRole.PlatformKeyReport
                })
                {
                    var options = database.GetNativeQueueOptions(purpose);
                    configuration[$"Database:{purpose}:Username"] = options.Username;
                    configuration[$"Database:{purpose}:Password"] = options.Password;
                }
            },
            createSeedContext: () => database.CreateDbContext());

    private static EventDiscoveryCandidateReader Reader(IServiceProvider services) =>
        ActivatorUtilities.CreateInstance<EventDiscoveryCandidateReader>(services,
            ActivatorUtilities.CreateInstance<EventDiscoveryLocalSource>(services));

    private static void SetAnonymous(EventVisitorCapabilitySqliteFixture fixture) =>
        fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

    private static async Task<Guid> SeedAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        var context = fixture.Context;
        context.Set<TenantSetting>().Add(new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = await context.Tenants.SingleAsync(row => row.Id == fixture.TenantId),
            SettingKey = GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
            Value = "\"AuthenticatedOnly\""
        });
        var entity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            Title = "Private tenant native discovery",
            PublicCode = Guid.CreateVersion7().ToString("N"),
            ActorId = fixture.ActorId,
            Actor = null!,
            OrganizerActorId = fixture.ActorId,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            Timezone = "UTC",
            CreatedAt = Now.UtcDateTime
        };
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            Event = entity,
            EventId = entity.Id,
            StartTime = Now,
            EndTime = Now.AddHours(1),
            EndTimeType = SessionEndTimeType.Fixed
        };
        session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
        context.AddRange(entity, session);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return entity.Id;
    }

    private static async Task CleanupAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        var context = fixture.Context;
        context.ChangeTracker.Clear();
        Guid tenant = fixture.TenantId;
        await context.Set<EventDiscoverySnapshotItem>().Where(row => row.TenantId == tenant).ExecuteDeleteAsync();
        await context.Set<EventDiscoverySnapshot>().Where(row => row.TenantId == tenant).ExecuteDeleteAsync();
        await context.Set<EventDiscoverySnapshotReservation>().Where(row => row.TenantId == tenant).ExecuteDeleteAsync();
        await context.Set<EventDiscoveryIdentity>().Where(row => row.TenantId == tenant).ExecuteDeleteAsync();
        await context.EventSessions.Where(row => row.TenantId == tenant).ExecuteDeleteAsync();
        await context.Events.Where(row => row.TenantId == tenant).ExecuteDeleteAsync();
        await context.TenantUsers.Where(row => row.TenantId == tenant).ExecuteDeleteAsync();
        await context.Set<TenantSetting>().Where(row => row.TenantId == tenant).ExecuteDeleteAsync();
        await context.Tenants.Where(row => row.Id == tenant).ExecuteDeleteAsync();
        await context.Set<EventDiscoveryRevision>().Where(row => row.TenantId == tenant).ExecuteDeleteAsync();
        await context.Set<ActorPii>().Where(row => row.ActorId == fixture.ActorId).ExecuteDeleteAsync();
        await context.Actors.Where(row => row.Id == fixture.ActorId).ExecuteDeleteAsync();
        await context.Set<UserPii>().Where(row => row.UserId == fixture.UserId).ExecuteDeleteAsync();
        await context.Users.Where(row => row.Id == fixture.UserId).ExecuteDeleteAsync();
    }
}
