using System.Security.Claims;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Explore.Secrets.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

// The existing structured runtime lane supplies the actual database and approved secrets.
// The same native command corpus runs against each of its five providers; no catalog
// assembly or in-memory EF provider substitutes for runtime dispatch.
[RequiresStructuredPrimaryDatabase]
[NotInParallel("PrimaryDatabaseProviderBehaviorContract")]
public sealed class CreateEventDraftGraphTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task NativeDraftCreation_PreservesNullSchedulesMetadataAndExplicitReferences(
        bool mixed, bool explicitReferences)
    {
        var providerFixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        await providerFixture.PrepareAsync();
        var tenant = new GraphTenant(Guid.CreateVersion7());
        var userId = Guid.CreateVersion7();
        await using var services = BuildServices(providerFixture, tenant, userId);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var publisher = await SeedOwnerAsync(db, tenant.TenantId, userId);
        await AssertProviderAsync(providerFixture, db);
        var request = DraftRequest(publisher.LocationId, mixed, explicitReferences)
            with
        {
            OrganizationId = publisher.OrganizationId
        };
        var response = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { EventDto = request }, default);
        await Assert.That(response.IsSuccess).IsTrue()
            .Because($"{response.FailureCode}: {string.Join(", ", response.Errors ?? [])}");

        // A fresh provider context proves committed storage, not the handler's tracker.
        await using var fresh = providerFixture.CreateTenantContext(tenant.TenantId);
        var sessions = await new EventSessionRepository(fresh).GetSessionsByEvent(response.Id);
        await Assert.That(sessions.Count).IsEqualTo(2);
        var prepared = sessions.Single(session => session.Title == "Prepared native session");
        await Assert.That(prepared.StartTime).IsNull();
        await Assert.That(prepared.EndTime).IsNull();
        await Assert.That(prepared.LocalStartDate).IsNull();
        await Assert.That(prepared.LocalEndDate).IsNull();
        await Assert.That(prepared.LocalStartTime).IsNull();
        await Assert.That(prepared.LocalEndTime).IsNull();
        await Assert.That(prepared.LocalStartMinuteOfDay).IsNull();
        await Assert.That(prepared.LocalEndMinuteOfDay).IsNull();
        await Assert.That(prepared.Description).IsEqualTo("Prepared description");
        await Assert.That(prepared.Slug).IsEqualTo("prepared-native-session");
        await Assert.That(prepared.MaxAudienceAttendees).IsEqualTo(37);
        await Assert.That(prepared.SortOrder).IsEqualTo(7);
        await Assert.That(prepared.LocationId).IsEqualTo(publisher.LocationId);
        await Assert.That(sessions.All(session => session.EventSessionStatusId == (int)EventSessionStatusEnum.Draft)).IsTrue();
        if (mixed)
        {
            var scheduled = sessions.Single(session => session.Title == "Scheduled native session");
            await Assert.That(scheduled.StartTime).IsEqualTo(new DateTimeOffset(2040, 7, 21, 7, 0, 0, TimeSpan.Zero));
            await Assert.That(scheduled.EndTime).IsEqualTo(new DateTimeOffset(2040, 7, 21, 8, 0, 0, TimeSpan.Zero));
            await Assert.That(scheduled.LocalStartDate).IsEqualTo(new DateOnly(2040, 7, 21));
            await Assert.That(scheduled.LocalStartTime).IsEqualTo(new TimeOnly(9, 0));
        }
        var days = await new EventDayRepository(fresh).GetByEventAsync(response.Id, default);
        await Assert.That(days.Count).IsEqualTo(explicitReferences ? 1 : 0);
        if (explicitReferences)
        {
            var day = days.Single();
            await Assert.That(prepared.EventDayId).IsEqualTo(day.Id);
            await Assert.That(day.Label).IsEqualTo("Prepared day");
            await Assert.That(day.IsPublished).IsFalse();
            var room = await new LocationRoomRepository(fresh).GetById(prepared.RoomId!.Value);
            await Assert.That(room!.Name).IsEqualTo("Prepared room");
            await Assert.That(room.LocationId).IsEqualTo(publisher.LocationId);
        }
        else
        {
            await Assert.That(prepared.EventDayId).IsNull();
            await Assert.That(prepared.RoomId).IsNull();
        }
        var placement = await new EventLocationRepository(fresh).GetByIdsAsync([prepared.EventLocationId!.Value], default);
        await Assert.That(placement.Single().LocationId).IsEqualTo(publisher.LocationId);
        var parent = await new EventRepository(fresh).GetById(response.Id);
        await Assert.That(parent!.EventStatusId).IsEqualTo((int)EventStatusEnum.Draft);
        await Assert.That(parent.FirstSessionStartUtc).IsNull();
        await Assert.That(parent.LastSessionEndUtc).IsNull();
        await Assert.That(await fresh.EventRoleAssignments.CountAsync(grant => grant.EventId == response.Id
            && grant.UserId == userId && grant.RoleId == (int)RoleEnum.EventOwner)).IsEqualTo(1);
        await Assert.That(await fresh.OutboxMessages.CountAsync(message => message.AggregateId == response.Id)).IsEqualTo(0);
        await Assert.That(await new EventSessionRepository(fresh).GetPublicSessionsByEventAsync(response.Id, default)).IsEmpty();
        await Assert.That(await new EventRepository(fresh).IsPubliclyEligibleAsync(tenant.TenantId, response.Id, default)).IsFalse();
        await using var foreign = providerFixture.CreateTenantContext(Guid.CreateVersion7());
        await Assert.That(await new EventRepository(foreign).GetById(response.Id)).IsNull();
        await Assert.That(await new EventSessionRepository(foreign).GetSessionsByEvent(response.Id)).IsEmpty();
    }

    [Test]
    public async Task NativeDraftCreation_PublicationWithUnscheduledChildRejectsWithoutGraphResidue()
    {
        var providerFixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        await providerFixture.PrepareAsync();
        var tenant = new GraphTenant(Guid.CreateVersion7());
        var userId = Guid.CreateVersion7();
        var writes = new GraphWriteWitness();
        await using var services = BuildServices(providerFixture, tenant, userId, writes);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var publisher = await SeedOwnerAsync(db, tenant.TenantId, userId);
        await AssertProviderAsync(providerFixture, db);
        var request = DraftRequest(publisher.LocationId, mixed: true, explicitReferences: false) with
        {
            OrganizationId = publisher.OrganizationId,
            EventStatusId = (int)EventStatusEnum.Published
        };
        var response = await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { EventDto = request }, default);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Errors!.Any(error =>
            error.Contains("Sessions[0].StartTime", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(writes.GraphWriteAttempted).IsFalse();
        await using var fresh = providerFixture.CreateTenantContext(tenant.TenantId);
        await AssertEmptyGraphAsync(fresh);
    }

    [Test]
    public async Task NativeDraftCreation_PreCommitPublicationFailureRollsBackWrittenGraphAndOutbox()
    {
        var providerFixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        await providerFixture.PrepareAsync();
        var tenant = new GraphTenant(Guid.CreateVersion7());
        var userId = Guid.CreateVersion7();
        var failure = new PublicationWriteFailure(tenant.TenantId);
        await using var services = BuildServices(providerFixture, tenant, userId, failure);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var publisher = await SeedOwnerAsync(db, tenant.TenantId, userId);
        await AssertProviderAsync(providerFixture, db);
        var draft = DraftRequest(publisher.LocationId, mixed: true, explicitReferences: false)
            with
        { OrganizationId = publisher.OrganizationId };
        var request = draft with
        {
            EventStatusId = (int)EventStatusEnum.Published,
            Sessions =
            [
                draft.Sessions[0] with
                {
                    StartTime = new DateTimeOffset(2040, 7, 21, 7, 0, 0, TimeSpan.Zero),
                    EndTime = new DateTimeOffset(2040, 7, 21, 8, 0, 0, TimeSpan.Zero)
                },
                draft.Sessions[1]
            ]
        };
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>>>();
        await Assert.ThrowsAsync<NativeGraphWriteFailureException>(() =>
            handler.ExecuteAsync(new() { EventDto = request }, default));
        await Assert.That(failure.ChildrenWritten).IsEqualTo(2);
        await Assert.That(failure.OwnerWritten).IsTrue();
        await Assert.That(failure.OutboxWritten).IsTrue();
        await Assert.That(failure.Injected).IsTrue();
        await using var fresh = providerFixture.CreateTenantContext(tenant.TenantId);
        await AssertEmptyGraphAsync(fresh);
        await Assert.That(await fresh.OutboxMessages.CountAsync(message => message.AggregateId == failure.RootId)).IsEqualTo(0);
        await Assert.That(await fresh.PdsSyncOutbox.CountAsync(message => message.SourceEntityId == failure.RootId)).IsEqualTo(0);
    }

    [Test]
    public async Task NativeDraftCreation_RealChildSaveFailureRollsBackRootChildrenOwnerAndOutbox()
    {
        var providerFixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        await providerFixture.PrepareAsync();
        var tenant = new GraphTenant(Guid.CreateVersion7());
        var userId = Guid.CreateVersion7();
        var failure = new ChildWriteFailure();
        await using var services = BuildServices(providerFixture, tenant, userId, failure);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var publisher = await SeedOwnerAsync(db, tenant.TenantId, userId);
        await AssertProviderAsync(providerFixture, db);
        var request = DraftRequest(publisher.LocationId, mixed: false, explicitReferences: true)
            with
        { OrganizationId = publisher.OrganizationId };
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>>>();
        await Assert.ThrowsAsync<NativeGraphWriteFailureException>(() =>
            handler.ExecuteAsync(new() { EventDto = request }, default));
        await Assert.That(failure.Injected).IsTrue();
        await Assert.That(failure.RootWritten).IsTrue();
        await Assert.That(failure.OwnerWritten).IsTrue();
        await Assert.That(failure.ChildWritten).IsTrue();
        await using var fresh = providerFixture.CreateTenantContext(tenant.TenantId);
        await AssertEmptyGraphAsync(fresh);
        await Assert.That(await fresh.LocationRooms.CountAsync()).IsEqualTo(0);
        await Assert.That(await fresh.OutboxMessages.CountAsync(message => message.AggregateId == failure.RootId)).IsEqualTo(0);
    }

    private static async Task AssertEmptyGraphAsync(ExploreDbContext db)
    {
        await Assert.That(await db.Events.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.EventSessions.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.EventDays.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.EventLocations.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.EventRoleAssignments.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.EventParticipationConfigurations.CountAsync()).IsEqualTo(0);
    }

    private static async Task AssertProviderAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture, ExploreDbContext db)
    {
        var expected = fixture.Provider switch
        {
            PrimaryDatabaseProvider.PostgreSql => "Npgsql.EntityFrameworkCore.PostgreSQL",
            PrimaryDatabaseProvider.Sqlite => "Microsoft.EntityFrameworkCore.Sqlite",
            PrimaryDatabaseProvider.SqlServer => "Microsoft.EntityFrameworkCore.SqlServer",
            PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb => "Microting.EntityFrameworkCore.MySql",
            _ => throw new InvalidOperationException($"Unexpected graph runtime provider {fixture.Provider}.")
        };
        await Assert.That(db.Database.ProviderName).IsEqualTo(expected);
        Console.WriteLine($"Native creation graph runtime provider: {fixture.Provider}; {db.Database.ProviderName}.");
    }

    private static CreateEventDto DraftRequest(Guid locationId, bool mixed, bool explicitReferences)
    {
        var prepared = new Dictionary<string, object?>
        {
            ["title"] = "Prepared native session",
            ["description"] = "Prepared description",
            ["slug"] = "prepared-native-session",
            ["maxAudienceAttendees"] = 37,
            ["sortOrder"] = 7,
            ["locationId"] = locationId
        };
        if (explicitReferences)
        {
            prepared["dayTempKey"] = "prepared-day";
            prepared["roomTempKey"] = "prepared-room";
        }
        var second = new Dictionary<string, object?>
        {
            ["title"] = mixed ? "Scheduled native session" : "Another prepared native session",
            ["sortOrder"] = 8,
            ["locationId"] = locationId
        };
        if (mixed)
        {
            second["startTime"] = "2040-07-21T09:00:00+02:00";
            second["endTime"] = "2040-07-21T10:00:00+02:00";
        }
        var request = new Dictionary<string, object?>
        {
            ["title"] = $"Native provider graph {Guid.CreateVersion7():N}",
            ["eventTimeZoneId"] = "Europe/Brussels",
            ["participationConfiguration"] = new
            {
                participationHandlingModeId = (int)ParticipationHandlingModeEnum.InformationOnly,
                advanceRegistrationObligationId = (int)AdvanceRegistrationObligationEnum.NotApplicable
            },
            ["sessions"] = new[] { prepared, second }
        };
        if (explicitReferences)
        {
            request["days"] = new[] { new { tempKey = "prepared-day", localDate = "2040-07-21", label = "Prepared day", isPublished = false } };
            request["rooms"] = new[] { new { tempKey = "prepared-room", locationId, name = "Prepared room", capacity = 37 } };
        }
        return JsonSerializer.Deserialize<CreateEventDto>(JsonSerializer.Serialize(request, JsonOptions), JsonOptions)!;
    }

    private static ServiceProvider BuildServices(
        PrimaryDatabaseProviderBehaviorFixture fixture, GraphTenant tenant, Guid userId, IInterceptor? interceptor = null)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Authentication:Provider"] = "Local",
                ["SecretProvider:Provider"] = "Environment"
            }).Build();
        var services = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Production
        }).Services;
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHybridCache();
        services.AddMetrics();
        services.AddSingleton<ProjectionMetrics>();
        services.AddSingleton<BusinessMetrics>();
        services.Configure<InstanceOperatorIdentityOptions>(options =>
        {
            options.OperatorId = Guid.CreateVersion7();
            options.PublicName = "Native graph fixture operator";
            options.LegalName = "Native graph fixture ASBL";
            options.OfficialOrigin = "https://example.test";
            options.OperatorKindCode = "registered_organization";
            options.JurisdictionCountryCode = "BE";
            options.RegistrationIdentifier = "BE 0123.456.789";
            options.PublicContactEmail = "contact@example.test";
            options.WebsiteUrl = "https://example.test";
            options.LegalNoticeUrl = "https://example.test/legal";
            options.TermsUrl = "https://example.test/terms";
            options.PrivacyUrl = "https://example.test/privacy";
        });
        services.ConfigureApplicationServices(configuration);
        services.ConfigureInfrastructureServices(configuration);
        services.AddSecretProvider(configuration);
        services.AddSecretResolution();
        services.ConfigurePersistenceServices(configuration, skipLookupCacheInitializer: true);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "fixture"))
            }
        });
        services.AddScoped(provider =>
        {
            var context = interceptor is null
                ? fixture.CreateTenantContext(tenant.TenantId)
                : fixture.CreateTenantContext(tenant.TenantId, interceptor);
            context.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
            return context;
        });
        return services.BuildIsolatedServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<(Guid LocationId, Guid OrganizationId)> SeedOwnerAsync(
        ExploreDbContext db, Guid tenantId, Guid userId)
    {
        var actorId = Guid.CreateVersion7();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Slug = $"native-graph-{tenantId:N}",
            FullName = "Native graph tenant",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        });
        db.Users.Add(new User
        {
            Id = userId,
            Pii = new UserPii { Email = string.Empty, FirstName = "Native", LastName = "Organizer" }
        });
        db.Actors.Add(new Actor
        {
            Id = actorId,
            UserId = userId,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Native organizer" }
        });
        db.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            ActorId = actorId,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            FullName = "Prepared venue",
            City = "Brussels",
            Country = "Belgium",
            Timezone = "Europe/Brussels"
        };
        location.ClassifyAs(LocationKindEnum.CommercialVenue);
        location.SetManualAddress("Prepared street", "1000");
        db.Locations.Add(location);
        var organizationId = Guid.CreateVersion7();
        var organization = new Organization
        {
            Id = organizationId,
            Pii = new OrganizationPii { OrganizationId = organizationId, FullName = "Native graph publisher" },
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var participation = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            OrganizationId = organizationId,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsVisible = true,
            IsOrganizerEligible = true,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        db.Organizations.Add(organization);
        db.OrganizationTenants.Add(participation);
        db.Actors.Add(new Actor
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ActorTypeId = (int)ActorTypeEnum.Organization,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Native graph publisher" }
        });
        db.OrganizationMembers.Add(new OrganizationMember
        {
            Id = Guid.CreateVersion7(),
            OrganizationTenantId = participation.Id,
            OrganizationTenant = participation,
            UserId = userId,
            User = null!,
            TenantId = tenantId,
            Tenant = null!,
            RoleId = (int)RoleEnum.OrgAdmin,
            Role = null!,
            CreatedAt = DateTime.UtcNow
        });
        var permissionId = await db.Permissions
            .Where(permission => permission.MasterCode == PermissionCodes.EventCreate)
            .Select(permission => permission.Id).SingleAsync();
        if (!await db.RolePermissions.AnyAsync(grant =>
                grant.RoleId == (int)RoleEnum.OrgAdmin && grant.PermissionId == permissionId))
        {
            db.RolePermissions.Add(new RolePermission
            {
                RoleId = (int)RoleEnum.OrgAdmin,
                Role = null!,
                PermissionId = permissionId,
                Permission = null!,
                GrantedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (location.Id, organizationId);
    }

    private sealed record GraphTenant(Guid TenantId) : ITenantContext;

    private sealed class GraphWriteWitness : SaveChangesInterceptor
    {
        public bool GraphWriteAttempted { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } db)
                GraphWriteAttempted |= db.ChangeTracker.Entries().Any(entry =>
                    entry.State == EntityState.Added
                    && entry.Entity is Explore.Domain.Event or EventSession or EventRoleAssignment or OutboxMessage);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ChildWriteFailure : SaveChangesInterceptor
    {
        public Guid RootId { get; private set; }
        public bool RootWritten { get; private set; }
        public bool OwnerWritten { get; private set; }
        public bool ChildWritten { get; private set; }
        public bool Injected { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Injected || eventData.Context is not ExploreDbContext db || db.Database.CurrentTransaction is null)
                return result;
            var child = db.ChangeTracker.Entries<EventSession>().FirstOrDefault();
            if (child is null)
                return result;
            RootId = child.Entity.EventId;
            RootWritten = await db.Events.AnyAsync(root => root.Id == RootId, cancellationToken);
            ChildWritten = await db.EventSessions.AnyAsync(session => session.Id == child.Entity.Id, cancellationToken);
            OwnerWritten = await db.EventRoleAssignments.AnyAsync(grant => grant.EventId == RootId
                && grant.RoleId == (int)RoleEnum.EventOwner, cancellationToken);
            if (!RootWritten || !ChildWritten || !OwnerWritten)
                throw new InvalidOperationException("The provider seam did not observe the persisted native graph.");
            Injected = true;
            throw new NativeGraphWriteFailureException();
        }
    }

    private sealed class PublicationWriteFailure(Guid tenantId) : SaveChangesInterceptor
    {
        public Guid RootId { get; private set; }
        public int ChildrenWritten { get; private set; }
        public bool OwnerWritten { get; private set; }
        public bool OutboxWritten { get; private set; }
        public bool Injected { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Injected || eventData.Context is not ExploreDbContext db || db.Database.CurrentTransaction is null)
                return result;
            var rootId = await db.Events.Where(root => root.TenantId == tenantId)
                .Select(root => (Guid?)root.Id).SingleOrDefaultAsync(cancellationToken);
            if (rootId is null)
                return result;
            RootId = rootId.Value;
            OutboxWritten = await db.OutboxMessages.AnyAsync(message => message.AggregateId == RootId, cancellationToken);
            if (!OutboxWritten)
                return result;
            ChildrenWritten = await db.EventSessions.CountAsync(session => session.EventId == RootId, cancellationToken);
            OwnerWritten = await db.EventRoleAssignments.AnyAsync(grant => grant.EventId == RootId
                && grant.RoleId == (int)RoleEnum.EventOwner, cancellationToken);
            if (ChildrenWritten != 2 || !OwnerWritten || !OutboxWritten)
                throw new InvalidOperationException("The provider write seam did not observe the persisted published graph and outbox.");
            Injected = true;
            throw new NativeGraphWriteFailureException();
        }
    }

    private sealed class NativeGraphWriteFailureException : Exception;
}
