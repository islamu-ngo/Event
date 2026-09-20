using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Requests.Commands;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeEventSessionSpeakerHttpTests
{
    [Test]
    public async Task EventOwner_AssignsReassignsRejectsDuplicatesAndDeletesThroughRealHttp()
    {
        await using var factory = new SpeakerFactory();
        var data = await SeedAsync(factory);
        using var client = AuthenticatedClient(factory, data.OwnerId);

        using var create = await client.PostAsJsonAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionAId:D}",
            new { actorId = data.AlphaActorId, eventSessionId = data.ForeignSessionId });
        await Assert.That(create.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var assignmentId = (await JsonAsync(create)).GetProperty("id").GetGuid();
        await Assert.That(assignmentId).IsNotEqualTo(Guid.Empty);

        using var firstRead = await client.GetAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionAId:D}");
        await Assert.That(firstRead.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var first = (await ItemsAsync(firstRead)).Single();
        await Assert.That(first.GetProperty("id").GetGuid()).IsEqualTo(assignmentId);
        await Assert.That(first.GetProperty("actorId").GetGuid()).IsEqualTo(data.AlphaActorId);
        await Assert.That(first.GetProperty("actorDisplayName").GetString()).IsEqualTo("Alpha Speaker");
        await Assert.That(first.GetProperty("eventSessionId").GetGuid()).IsEqualTo(data.SessionAId);
        await Assert.That(first.GetProperty("eventId").GetGuid()).IsEqualTo(data.EventId);
        await Assert.That(first.GetProperty("tenantId").GetGuid()).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(first.GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();
        await Assert.That(first.GetProperty("_links").TryGetProperty("delete", out _)).IsTrue();
        var originalStamp = first.GetProperty("concurrencyStamp").GetGuid();

        using (var duplicate = await client.PostAsJsonAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionAId:D}",
            new { actorId = data.AlphaActorId, eventSessionId = data.SessionAId }))
        {
            await AssertProblemAsync(duplicate, HttpStatusCode.BadRequest, "validation_failed");
        }

        using (var createWithoutPii = await client.PostAsJsonAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionBId:D}",
            new { actorId = data.BetaActorId, eventSessionId = data.SessionBId }))
        {
            await Assert.That(createWithoutPii.StatusCode).IsEqualTo(HttpStatusCode.Created);
        }

        using (var move = await PatchAsync(client, assignmentId, originalStamp,
            new { session = new { eventSessionId = data.SessionBId } }))
        {
            await Assert.That(move.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        using (var emptyOldSession = await client.GetAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionAId:D}"))
        {
            await Assert.That((await ItemsAsync(emptyOldSession)).Length).IsEqualTo(0);
        }

        using var movedRead = await client.GetAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionBId:D}");
        var movedItems = await ItemsAsync(movedRead);
        await Assert.That(movedItems.Length).IsEqualTo(2);
        var moved = movedItems.Single(item => item.GetProperty("id").GetGuid() == assignmentId);
        var beta = movedItems.Single(item => item.GetProperty("actorId").GetGuid() == data.BetaActorId);
        await Assert.That(moved.GetProperty("eventSessionTitle").GetString()).IsEqualTo("Private Session B");
        await Assert.That(beta.TryGetProperty("actorDisplayName", out _)).IsFalse();
        var currentStamp = moved.GetProperty("concurrencyStamp").GetGuid();
        await Assert.That(currentStamp).IsNotEqualTo(originalStamp);

        using (var stale = await PatchAsync(client, assignmentId, originalStamp,
            new { actor = new { actorId = data.BetaActorId } }))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, "concurrent_update");
        }
        using (var duplicateUpdate = await PatchAsync(client, assignmentId, currentStamp,
            new { actor = new { actorId = data.BetaActorId } }))
        {
            await AssertProblemAsync(duplicateUpdate, HttpStatusCode.BadRequest, "validation_failed");
        }

        using (var wrongParent = await client.DeleteAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionAId:D}/{assignmentId:D}"))
        {
            await AssertProblemAsync(wrongParent, HttpStatusCode.NotFound, "resource_not_found");
        }
        using (var delete = await client.DeleteAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionBId:D}/{assignmentId:D}"))
        {
            await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }
        using (var deleteAgain = await client.DeleteAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionBId:D}/{assignmentId:D}"))
        {
            await AssertProblemAsync(deleteAgain, HttpStatusCode.NotFound, "resource_not_found");
        }

        using var finalRead = await client.GetAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionBId:D}");
        var finalItems = await ItemsAsync(finalRead);
        await Assert.That(finalItems.Length).IsEqualTo(1);
        await Assert.That(finalItems[0].GetProperty("actorId").GetGuid()).IsEqualTo(data.BetaActorId);
    }

    [Test]
    public async Task ManagementBoundary_PreservesTenantPrivacyActorValidationAndRealRoles()
    {
        await using var factory = new SpeakerFactory();
        var data = await SeedAsync(factory);
        using var owner = AuthenticatedClient(factory, data.OwnerId);
        using var member = AuthenticatedClient(factory, data.MemberId);
        using var anonymous = factory.CreateClient();

        using (var anonymousManagement = await anonymous.GetAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionAId:D}"))
        {
            await Assert.That(anonymousManagement.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        using (var unprivilegedManagement = await member.GetAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionAId:D}"))
        {
            await Assert.That(unprivilegedManagement.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
        using (var foreign = await owner.GetAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.ForeignSessionId:D}"))
        {
            await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(await foreign.Content.ReadAsStringAsync()).DoesNotContain("Foreign Speaker");
        }
        using (var deletedSession = await owner.GetAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.DeletedSessionId:D}"))
        {
            await Assert.That(deletedSession.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
        using (var ownerCreate = await owner.PostAsJsonAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionAId:D}",
            new { actorId = data.AlphaActorId, eventSessionId = data.SessionAId }))
        {
            await Assert.That(ownerCreate.StatusCode).IsEqualTo(HttpStatusCode.Created);
        }
        using (var missingActor = await owner.PostAsJsonAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionAId:D}",
            new { actorId = Guid.CreateVersion7(), eventSessionId = data.SessionAId }))
        {
            await AssertProblemAsync(missingActor, HttpStatusCode.BadRequest, "validation_failed");
        }
        using (var deletedActor = await owner.PostAsJsonAsync(
            $"/api/eventsessionspeaker/management/by-session/{data.SessionAId:D}",
            new { actorId = data.DeletedActorId, eventSessionId = data.SessionAId }))
        {
            await AssertProblemAsync(deletedActor, HttpStatusCode.BadRequest, "validation_failed");
        }
        using (var obsoletePublicPath = await anonymous.GetAsync("/api/eventsessionspeaker"))
        {
            await Assert.That(obsoletePublicPath.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        await Assert.That(await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .EventSessionSpeakers.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task NativeQueries_PreserveTenantFilteringNullableDisclosurePagingAndDecoratedPorts()
    {
        await using var factory = new SpeakerFactory();
        var data = await SeedAsync(factory);
        var localAssignments = await SeedLocalAssignmentsAsync(factory, data);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var details = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetEventSessionSpeakerDetailsQuery, EventSessionSpeakerDto?>>();
        var list = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetEventSessionSpeakerListQuery, PaginatedResult<EventSessionSpeakerListDto>>>();
        var sessions = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetSessionsByActorQuery, List<EventSessionSpeakerListDto>>>();
        var bySession = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetSpeakersBySessionQuery, List<EventSessionSpeakerListDto>>>();
        var create = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<CreateEventSessionSpeakerCommand, BaseCommandResponse<Guid>>>();
        var update = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<UpdateEventSessionSpeakerCommand, BaseCommandResponse<Guid>>>();
        var delete = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<DeleteEventSessionSpeakerCommand, bool>>();

        await Assert.That(details).IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventSessionSpeakerDetailsQuery, EventSessionSpeakerDto?>>();
        await Assert.That(list).IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventSessionSpeakerListQuery, PaginatedResult<EventSessionSpeakerListDto>>>();
        await Assert.That(sessions).IsTypeOf<AuthorizationQueryHandlerDecorator<GetSessionsByActorQuery, List<EventSessionSpeakerListDto>>>();
        await Assert.That(bySession).IsTypeOf<AuthorizationQueryHandlerDecorator<GetSpeakersBySessionQuery, List<EventSessionSpeakerListDto>>>();
        await Assert.That(create).IsTypeOf<AuthorizationCommandHandlerDecorator<CreateEventSessionSpeakerCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(update).IsTypeOf<AuthorizationCommandHandlerDecorator<UpdateEventSessionSpeakerCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(delete).IsTypeOf<AuthorizationCommandHandlerDecorator<DeleteEventSessionSpeakerCommand, bool>>();

        var page = await list.QueryAsync(new GetEventSessionSpeakerListQuery { PageNumber = 1, PageSize = 1 }, default);
        await Assert.That(page.TotalCount).IsEqualTo(2);
        await Assert.That(page.Items.Count).IsEqualTo(1);
        await Assert.That((await details.QueryAsync(new GetEventSessionSpeakerDetailsQuery(localAssignments.Alpha), default))?.ActorDisplayName)
            .IsEqualTo("Alpha Speaker");
        await Assert.That((await details.QueryAsync(new GetEventSessionSpeakerDetailsQuery(localAssignments.Beta), default))?.ActorDisplayName)
            .IsNull();
        await Assert.That(await details.QueryAsync(new GetEventSessionSpeakerDetailsQuery(Guid.CreateVersion7()), default)).IsNull();
        await Assert.That(await details.QueryAsync(new GetEventSessionSpeakerDetailsQuery(data.ForeignAssignmentId), default)).IsNull();
        await Assert.That((await sessions.QueryAsync(new GetSessionsByActorQuery { ActorId = data.AlphaActorId }, default))
            .Select(item => item.EventSessionId)).IsEquivalentTo(new[] { data.SessionAId });
        await Assert.That(await sessions.QueryAsync(new GetSessionsByActorQuery { ActorId = data.ForeignActorId }, default)).IsEmpty();
    }

    [Test]
    public async Task OpenApi_PreservesTheFourManagedRoutesAndGeneratedContractParity()
    {
        await using var factory = new SpeakerFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var runtime = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        await using var canonicalStream = File.OpenRead(Path.Combine(RepositoryRoot(), "schemas", "openapi_islamu-event.json"));
        using var canonical = await JsonDocument.ParseAsync(canonicalStream);
        (string Path, string Verb, string Operation)[] expected =
        [
            ("/api/eventsessionspeaker/management/by-session/{eventSessionId}", "get", "GetEventSessionSpeakersBySession"),
            ("/api/eventsessionspeaker/management/by-session/{eventSessionId}", "post", "CreateEventSessionSpeaker"),
            ("/api/eventsessionspeaker/management/{id}", "patch", "UpdateEventSessionSpeaker"),
            ("/api/eventsessionspeaker/management/by-session/{eventSessionId}/{id}", "delete", "DeleteEventSessionSpeaker")
        ];
        var paths = runtime.RootElement.GetProperty("paths");
        var canonicalPaths = canonical.RootElement.GetProperty("paths");
        foreach (var route in expected)
        {
            var operation = paths.GetProperty(route.Path).GetProperty(route.Verb);
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(route.Operation);
            await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Authenticated");
            await Assert.That(JsonElement.DeepEquals(
                paths.GetProperty(route.Path), canonicalPaths.GetProperty(route.Path))).IsTrue();
        }
    }

    private static HttpClient AuthenticatedClient(SpeakerFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Session speaker manager"));
        return client;
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, Guid stamp, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch,
            $"/api/eventsessionspeaker/management/{id:D}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement[]> ItemsAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("_embedded").GetProperty("items")
            .EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        await Assert.That(contentType is "application/problem+json" or "application/json").IsTrue();
        var problem = await JsonAsync(response);
        await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo((int)status);
        await Assert.That(problem.GetProperty("code").GetString()).IsEqualTo(code);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static async Task<LocalAssignments> SeedLocalAssignmentsAsync(SpeakerFactory factory, SeedData data)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var alpha = new EventSessionSpeaker
        {
            Id = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            EventSessionId = data.SessionAId,
            EventSession = null!,
            ActorId = data.AlphaActorId,
            Actor = null!
        };
        var beta = new EventSessionSpeaker
        {
            Id = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            EventSessionId = data.SessionBId,
            EventSession = null!,
            ActorId = data.BetaActorId,
            Actor = null!
        };
        db.EventSessionSpeakers.AddRange(alpha, beta);
        await db.SaveChangesAsync();
        return new LocalAssignments(alpha.Id, beta.Id);
    }

    private static async Task<SeedData> SeedAsync(SpeakerFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var defaultTenant = new TenantBuilder().WithId(PlatformDefaults.DefaultTenantId)
            .WithFullName("Native speaker tenant").WithSlug("native-speakers").Build();
        var foreignTenant = new TenantBuilder().WithFullName("Foreign speaker tenant")
            .WithSlug($"foreign-speakers-{Guid.CreateVersion7():N}").Build();
        db.Tenants.AddRange(defaultTenant, foreignTenant);

        var owner = new UserBuilder().WithEmail($"speaker-owner-{Guid.CreateVersion7():N}@example.test").Build();
        var member = new UserBuilder().WithEmail($"speaker-member-{Guid.CreateVersion7():N}@example.test").Build();
        var alphaUser = new UserBuilder().WithEmail($"alpha-speaker-{Guid.CreateVersion7():N}@example.test").Build();
        var betaUser = new UserBuilder().WithEmail($"beta-speaker-{Guid.CreateVersion7():N}@example.test").Build();
        var deletedUser = new UserBuilder().WithEmail($"deleted-speaker-{Guid.CreateVersion7():N}@example.test").Build();
        var foreignUser = new UserBuilder().WithEmail($"foreign-speaker-{Guid.CreateVersion7():N}@example.test").Build();
        var ownerActor = new ActorBuilder().WithUserId(owner.Id).WithDisplayName("Event Owner").Build();
        var alphaActor = new ActorBuilder().WithUserId(alphaUser.Id).WithDisplayName("Alpha Speaker").Build();
        var betaActor = new ActorBuilder().WithUserId(betaUser.Id).WithDisplayName("Removed PII Speaker").Build();
        var deletedActor = new ActorBuilder().WithUserId(deletedUser.Id).WithDisplayName("Deleted Speaker").Build();
        deletedActor.IsDeleted = true;
        deletedActor.DeletedAt = DateTime.UtcNow;
        var foreignActor = new ActorBuilder().WithUserId(foreignUser.Id).WithDisplayName("Foreign Speaker").Build();
        db.Users.AddRange(owner, member, alphaUser, betaUser, deletedUser, foreignUser);
        db.Actors.AddRange(ownerActor, alphaActor, betaActor, deletedActor, foreignActor);

        var localEvent = new EventBuilder().WithTitle("Private Draft Event").WithActorId(ownerActor.Id)
            .WithTenantId(defaultTenant.Id).WithStatus(EventStatusEnum.Draft)
            .WithVisibility(VisibilityTypeEnum.Private).Build();
        localEvent.Actor = ownerActor;
        localEvent.Tenant = defaultTenant;
        var sessionA = Session(localEvent, defaultTenant, "Private Session A");
        var sessionB = Session(localEvent, defaultTenant, "Private Session B");
        var deletedSession = Session(localEvent, defaultTenant, "Deleted Session");
        deletedSession.IsDeleted = true;
        deletedSession.DeletedAt = DateTime.UtcNow;
        localEvent.Sessions.Add(sessionA);
        localEvent.Sessions.Add(sessionB);
        localEvent.Sessions.Add(deletedSession);

        var foreignEvent = new EventBuilder().WithTitle("Foreign Event").WithActorId(foreignActor.Id)
            .WithTenantId(foreignTenant.Id).WithStatus(EventStatusEnum.Published).Build();
        foreignEvent.Actor = foreignActor;
        foreignEvent.Tenant = foreignTenant;
        var foreignSession = Session(foreignEvent, foreignTenant, "Foreign Session");
        foreignEvent.Sessions.Add(foreignSession);
        db.Events.AddRange(localEvent, foreignEvent);
        var foreignAssignment = new EventSessionSpeaker
        {
            Id = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            TenantId = foreignTenant.Id,
            Tenant = foreignTenant,
            EventSessionId = foreignSession.Id,
            EventSession = foreignSession,
            ActorId = foreignActor.Id,
            Actor = foreignActor
        };
        db.EventSessionSpeakers.Add(foreignAssignment);
        db.EventRoleAssignments.Add(EventRoleAssignment.Create(
            defaultTenant.Id, localEvent.Id, owner.Id, (int)RoleEnum.EventOwner,
            EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddMinutes(-1), null, owner.Id));
        await db.SaveChangesAsync();

        db.Set<ActorPii>().Remove(betaActor.Pii);
        await db.SaveChangesAsync();

        return new SeedData(owner.Id, member.Id, localEvent.Id, sessionA.Id, sessionB.Id,
            deletedSession.Id, foreignSession.Id, alphaActor.Id, betaActor.Id, deletedActor.Id,
            foreignActor.Id, foreignAssignment.Id);
    }

    private static EventSession Session(Explore.Domain.Event parent, Tenant tenant, string title) =>
        new(EventSessionStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            EventId = parent.Id,
            Event = parent,
            TenantId = tenant.Id,
            Tenant = tenant,
            Title = title,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

    private sealed record LocalAssignments(Guid Alpha, Guid Beta);

    private sealed record SeedData(
        Guid OwnerId,
        Guid MemberId,
        Guid EventId,
        Guid SessionAId,
        Guid SessionBId,
        Guid DeletedSessionId,
        Guid ForeignSessionId,
        Guid AlphaActorId,
        Guid BetaActorId,
        Guid DeletedActorId,
        Guid ForeignActorId,
        Guid ForeignAssignmentId);

    private sealed class SpeakerFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"native-speakers-{Guid.CreateVersion7():N}.db");

        public SpeakerFactory()
        {
            AdditionalConfiguration["Authorization:Provider"] = "local";
            AdditionalConfiguration["Testing:DisableDeploymentModeCache"] = "true";
            AdditionalConfiguration["Keycloak:Authority"] = "";
            AdditionalConfiguration["Keycloak:AuthorizationUrl"] = "";
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                ConfigureDatabase(options);
                using (var db = new ExploreDbContext(options.Options))
                {
                    db.Database.EnsureCreated();
                }
                services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
                services.AddScoped(provider =>
                {
                    var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    db.TenantContext = provider.GetRequiredService<ITenantContext>();
                    db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                    return db;
                });
            });
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = _databasePath
            });
            options.UseSnakeCaseNamingConvention();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            ConfigureDatabase(options);
            await using var db = new ExploreDbContext(options.Options);
            SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
            File.Delete(_databasePath);
            File.Delete(_databasePath + "-wal");
            File.Delete(_databasePath + "-shm");
        }
    }
}
