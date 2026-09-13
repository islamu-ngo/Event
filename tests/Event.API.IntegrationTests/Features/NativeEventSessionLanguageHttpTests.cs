using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
using Explore.Application.Operations;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Hybrid;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed partial class NativeEventSessionLanguageHttpTests
{
    [Test]
    public async Task PublicReads_RespectPublicationAndTenantVisibility()
    {
        await using var factory = await LanguageFactory.CreateAsync(seedAssignments: true);
        using var anonymous = factory.CreateClient();
        var publicBody = await CollectionAsync(anonymous, Public(factory.PublicSessionId));
        await Assert.That(Items(publicBody).Select(item => item.GetProperty("id").GetInt32())).IsEquivalentTo(new[] { 101, 102 });
        var item = Items(publicBody).Single(item => item.GetProperty("id").GetInt32() == 101);
        await Assert.That(item.GetProperty("languageId").GetInt32()).IsEqualTo(1);
        await Assert.That(item.GetProperty("languageFullName").GetString()).IsNotNull();
        await Assert.That(item.GetProperty("_links").TryGetProperty("edit", out _)).IsFalse();
        await Assert.That(publicBody.GetProperty("_links").TryGetProperty("self", out _)).IsTrue();
        foreach (var hidden in new[] { factory.PrivateSessionId, factory.DraftSessionId, factory.ForeignSessionId, Guid.CreateVersion7() })
        {
            await Assert.That(Items(await CollectionAsync(anonymous, Public(hidden))).Length).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ManagedReads_RequirePersistedRolesAndParentSessionBinding()
    {
        await using var factory = await LanguageFactory.CreateAsync(seedAssignments: true);
        using var anonymous = factory.CreateClient();
        using var owner = factory.Client(factory.OwnerId);
        using var outsider = factory.Client(factory.OutsiderId);
        using var unauthenticated = await anonymous.GetAsync(Managed(factory.PrivateEventId, factory.PrivateSessionId));
        await Assert.That(unauthenticated.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var denied = await outsider.GetAsync(Managed(factory.PrivateEventId, factory.PrivateSessionId));
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(denied.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using var foreign = await owner.GetAsync(Managed(factory.ForeignEventId, factory.ForeignSessionId));
        await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        var managed = await CollectionAsync(owner, Managed(factory.PrivateEventId, factory.PrivateSessionId));
        await Assert.That(Items(managed).Single().GetProperty("languageId").GetInt32()).IsEqualTo(2);
        await Assert.That(Items(await CollectionAsync(owner, Managed(factory.PublicEventId, factory.PrivateSessionId))).Length).IsEqualTo(0);
        await Assert.That(Items(await CollectionAsync(owner, Managed(factory.PublicEventId, factory.ForeignSessionId))).Length).IsEqualTo(0);
        await Assert.That(Items(managed).Single().GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();
    }

    [Test]
    public async Task Writes_PreserveManualValidationConcurrencyReassignmentAndDurability()
    {
        await using var factory = await LanguageFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        using var outsider = factory.Client(factory.OutsiderId);
        using var anonymous = factory.CreateClient();
        using (var denied = await anonymous.PostAsJsonAsync("/api/eventsessionlanguage", Input(factory.PublicSessionId, 1)))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using (var denied = await outsider.PostAsJsonAsync("/api/eventsessionlanguage", Input(factory.PublicSessionId, 1)))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using (var invalid = await owner.PostAsJsonAsync("/api/eventsessionlanguage", Input(factory.PublicSessionId, int.MaxValue)))
            await ProblemDetailsAssertions.AssertProblemDetailsAsync(invalid, HttpStatusCode.BadRequest, "Program validation failed");
        using (var foreign = await owner.PostAsJsonAsync("/api/eventsessionlanguage", Input(factory.ForeignSessionId, 1)))
            await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        var id = await CreateAsync(owner, factory.PublicSessionId, 1);
        var before = await factory.DetailAsync(id) ?? throw new InvalidOperationException("Expected durable assignment.");
        await Assert.That(before.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(before.ConcurrencyStamp).IsNotEqualTo(Guid.Empty);
        using (var missingHeader = await owner.PatchAsJsonAsync($"/api/eventsessionlanguage/{id}", new { language = new { languageId = 2 } }))
        {
            await ProblemDetailsAssertions.AssertProblemDetailsAsync(missingHeader, HttpStatusCode.BadRequest, "Program validation failed");
            using var problem = await ProblemDetailsAssertions.ReadAsJsonAsync(missingHeader);
            await Assert.That(problem.RootElement.GetProperty("errors").GetProperty("If-Match").GetArrayLength()).IsEqualTo(1);
        }
        using (var empty = await PatchAsync(owner, id, new { }, before.ConcurrencyStamp))
            await Assert.That(empty.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using (var invalid = await PatchAsync(owner, id, new { language = new { languageId = int.MaxValue } }, before.ConcurrencyStamp))
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using (var stale = await PatchAsync(owner, id, new { language = new { languageId = 2 } }, Guid.CreateVersion7()))
        {
            await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
            await Assert.That((await JsonAsync(stale)).GetProperty("type").GetString()).IsEqualTo("/problems/concurrent_update");
        }
        using (var denied = await PatchAsync(outsider, id, new { language = new { languageId = 2 } }, before.ConcurrencyStamp))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using (var denied = await outsider.DeleteAsync($"/api/eventsessionlanguage/{id}"))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        foreach (var target in new[] { factory.PrivateSessionId, factory.ForeignSessionId, Guid.CreateVersion7() })
        {
            using var reassignment = await PatchAsync(owner, id, new { session = new { eventSessionId = target } }, before.ConcurrencyStamp);
            await Assert.That(reassignment.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        await CreateAsync(owner, factory.DraftSessionId, 2);
        using (var duplicate = await PatchAsync(owner, id,
            new { session = new { eventSessionId = factory.DraftSessionId }, language = new { languageId = 2 } }, before.ConcurrencyStamp))
            await Assert.That(duplicate.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await factory.DetailAsync(id))!.ConcurrencyStamp).IsEqualTo(before.ConcurrencyStamp);
        var cache = factory.Services.GetRequiredService<HybridCache>();
        var detailKey = $"event:detail:{factory.PublicEventId}";
        var listKey = $"native-session-language-list:{factory.PublicEventId}";
        await cache.SetAsync(detailKey, "before");
        await cache.SetAsync(listKey, "before", tags: [CacheTags.EventListByTenant(PlatformDefaults.DefaultTenantId)]);
        using (var changed = await PatchAsync(owner, id,
            new { session = new { eventSessionId = factory.DraftSessionId }, language = new { languageId = 3 } }, before.ConcurrencyStamp))
            await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await cache.GetOrCreateAsync<string>(detailKey, _ => ValueTask.FromResult("after"))).IsEqualTo("after");
        await Assert.That(await cache.GetOrCreateAsync<string>(listKey, _ => ValueTask.FromResult("after"))).IsEqualTo("after");
        var after = (await factory.DetailAsync(id))!;
        await Assert.That(after.EventSessionId).IsEqualTo(factory.DraftSessionId);
        await Assert.That(after.LanguageId).IsEqualTo(3);
        await Assert.That(after.ConcurrencyStamp).IsNotEqualTo(before.ConcurrencyStamp);
        await Assert.That(Items(await CollectionAsync(owner, Managed(factory.PublicEventId, factory.DraftSessionId)))
            .Select(item => item.GetProperty("languageId").GetInt32())).IsEquivalentTo(new[] { 2, 3 });
        using (var missingUpdate = await PatchAsync(owner, int.MaxValue, new { language = new { languageId = 2 } }, before.ConcurrencyStamp))
            await Assert.That(missingUpdate.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using (var deleted = await owner.DeleteAsync($"/api/eventsessionlanguage/{id}"))
        {
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(await deleted.Content.ReadAsStringAsync()).IsEmpty();
        }
        await Assert.That(await factory.DetailAsync(id)).IsNull();
        using var missingDelete = await owner.DeleteAsync($"/api/eventsessionlanguage/{id}");
        await ProblemDetailsAssertions.AssertProblemDetailsAsync(missingDelete, HttpStatusCode.NotFound, "Event session language not found");
    }

    [Test]
    public async Task ScopedPorts_PreserveNullableDetailPagingTenantIsolationAndNativeComposition()
    {
        await using var factory = await LanguageFactory.CreateAsync(seedAssignments: true);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var list = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSessionLanguageListQuery, PaginatedResult<EventSessionLanguageListDto>>>();
        await Assert.That(list is AuthorizationQueryHandlerDecorator<GetEventSessionLanguageListQuery, PaginatedResult<EventSessionLanguageListDto>>).IsTrue();
        var page = await list.QueryAsync(new() { PageNumber = 1, PageSize = 1 }, default);
        await Assert.That(page.TotalCount).IsEqualTo(4);
        await Assert.That(page.Items.Single().Id).IsEqualTo(104);
        await Assert.That((await list.QueryAsync(new() { PageNumber = 2, PageSize = 1 }, default)).Items.Single().Id).IsEqualTo(103);
        var normalized = await list.QueryAsync(new() { PageNumber = 0, PageSize = 0 }, default);
        await Assert.That(normalized.PageNumber).IsEqualTo(1);
        await Assert.That(normalized.PageSize).IsEqualTo(1);
        await Assert.That((await list.QueryAsync(new(), default)).PageSize).IsEqualTo(20);
        await Assert.That((await list.QueryAsync(new() { PageSize = 101 }, default)).PageSize).IsEqualTo(100);
        await Assert.That((await list.QueryAsync(new() { PageNumber = 99 }, default)).Items).IsEmpty();
        var detail = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSessionLanguageDetailsQuery, EventSessionLanguageDto?>>();
        await Assert.That(await detail.QueryAsync(new() { Id = int.MaxValue }, default)).IsNull();
        await Assert.That(await detail.QueryAsync(new() { Id = factory.ForeignAssignmentId }, default)).IsNull();
        await Assert.That((await detail.QueryAsync(new() { Id = 101 }, default))!.EventId).IsEqualTo(factory.PublicEventId);
        await Assert.That(scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventSessionLanguageCommand, BaseCommandResponse<int>>>()
            is AuthorizationCommandHandlerDecorator<CreateEventSessionLanguageCommand, BaseCommandResponse<int>>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventSessionLanguageCommand, BaseCommandResponse<int>>>()
            is AuthorizationCommandHandlerDecorator<UpdateEventSessionLanguageCommand, BaseCommandResponse<int>>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteEventSessionLanguageCommand, bool>>()
            is AuthorizationCommandHandlerDecorator<DeleteEventSessionLanguageCommand, bool>).IsTrue();
        await Assert.That(detail is AuthorizationQueryHandlerDecorator<GetEventSessionLanguageDetailsQuery, EventSessionLanguageDto?>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IQueryHandler<GetLanguagesBySessionQuery, List<EventSessionLanguageListDto>>>()
            is AuthorizationQueryHandlerDecorator<GetLanguagesBySessionQuery, List<EventSessionLanguageListDto>>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IQueryHandler<GetManagedLanguagesBySessionQuery, List<EventSessionLanguageListDto>>>()
            is AuthorizationQueryHandlerDecorator<GetManagedLanguagesBySessionQuery, List<EventSessionLanguageListDto>>).IsTrue();
        await factory.Services.ValidateNativeOperationsDeepAsync();
    }

    [Test]
    public async Task HttpContract_MatchesShippedOpenApiWithoutNewPublicShapes()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        // Match the shipped generator's provider-neutral security configuration.
        factory.AdditionalConfiguration["Keycloak:Authority"] = "";
        factory.AdditionalConfiguration["Keycloak:AuthorizationUrl"] = "";
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Explore.slnx")))
            root = root.Parent;
        await using var stream = File.OpenRead(Path.Combine(root?.FullName
            ?? throw new InvalidOperationException("Repository root not found."), "schemas", "openapi_islamu-event.json"));
        using var shipped = await JsonDocument.ParseAsync(stream);
        foreach (var path in shipped.RootElement.GetProperty("paths").EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/eventsessionlanguage", StringComparison.Ordinal)))
        {
            var actual = document.RootElement.GetProperty("paths").GetProperty(path.Name);
            if (!JsonElement.DeepEquals(actual, path.Value))
                throw new InvalidOperationException($"OpenAPI mismatch at {path.Name}. Actual: {actual.GetRawText()} Expected: {path.Value.GetRawText()}");
        }
        foreach (var schema in shipped.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject()
            .Where(schema => schema.Name.Contains("EventSessionLanguage", StringComparison.Ordinal)))
        {
            await Assert.That(JsonElement.DeepEquals(document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema.Name), schema.Value)).IsTrue();
        }
    }

    private static string Public(Guid session) => $"/api/eventsessionlanguage/by-session/{session}";
    private static string Managed(Guid parent, Guid session) => $"/api/eventsessionlanguage/management/by-event/{parent}/by-session/{session}";
    private static CreateEventSessionLanguageDto Input(Guid session, int language) => new() { EventSessionId = session, LanguageId = language };
    private static async Task<int> CreateAsync(HttpClient client, Guid session, int language)
    {
        using var response = await client.PostAsJsonAsync("/api/eventsessionlanguage", Input(session, language));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(response.Headers.Location!.AbsolutePath).IsEqualTo(Public(session));
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }
    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, int id, object input, Guid stamp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/eventsessionlanguage/{id}") { Content = JsonContent.Create(input) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        return await client.SendAsync(request);
    }
    private static async Task<JsonElement> CollectionAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await JsonAsync(response);
    }
    private static JsonElement[] Items(JsonElement body) => body.GetProperty("_embedded").GetProperty("items").EnumerateArray().ToArray();
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private sealed class LanguageFactory : AuthenticatedWebApplicationFactory
    {
        private readonly AssignmentInsertBarrier _insertBarrier = new();
        public void SynchronizeAssignmentInserts() => _insertBarrier.Enabled = true;
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"native-session-language-{Guid.CreateVersion7():N}.db");
        public Guid OwnerId { get; private set; }
        public Guid OutsiderId { get; private set; }
        public Guid PublicEventId { get; private set; }
        public Guid PrivateEventId { get; private set; }
        public Guid ForeignEventId { get; private set; }
        public Guid PublicSessionId { get; private set; }
        public Guid DraftSessionId { get; private set; }
        public Guid PrivateSessionId { get; private set; }
        public Guid ForeignSessionId { get; private set; }
        public int ForeignAssignmentId { get; private set; }

        public static async Task<LanguageFactory> CreateAsync(bool seedAssignments = false)
        {
            var factory = new LanguageFactory();
            try
            {
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                factory.ConfigureDatabase(options);
                await using (var db = new ExploreDbContext(options.Options))
                {
                    await db.Database.EnsureCreatedAsync();
                    await SqliteDatabaseInitializer.InitializeAsync(db, default);
                }
                using var scope = factory.Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                var owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
                var outsider = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
                var foreign = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(context);
                factory.OwnerId = owner.UserId;
                factory.OutsiderId = outsider.UserId;
                var publicEvent = Parent(owner, VisibilityTypeEnum.Public);
                var privateEvent = Parent(owner, VisibilityTypeEnum.Private);
                var foreignEvent = Parent(foreign, VisibilityTypeEnum.Public);
                var published = Session(publicEvent, EventSessionStatusEnum.Published);
                var draft = Session(publicEvent, EventSessionStatusEnum.Draft);
                draft.AssignEventLocation(published.EventLocation!);
                var privateSession = Session(privateEvent, EventSessionStatusEnum.Published);
                var foreignSession = Session(foreignEvent, EventSessionStatusEnum.Published);
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                tenant.SetTenant(owner.TenantId);
                context.Events.AddRange(publicEvent, privateEvent);
                context.EventSessions.AddRange(published, draft, privateSession);
                foreach (var parent in new[] { publicEvent, privateEvent })
                    context.EventRoleAssignments.Add(EventRoleAssignment.Create(owner.TenantId, parent.Id, owner.UserId,
                        (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddDays(-1), null, owner.UserId));
                if (seedAssignments)
                {
                    context.EventSessionLanguages.AddRange(
                        Assignment(101, published, 1), Assignment(102, published, 2),
                        Assignment(103, privateSession, 2), Assignment(104, draft, 1));
                }
                await context.SaveChangesAsync();
                tenant.SetTenant(foreign.TenantId);
                context.Events.Add(foreignEvent);
                context.EventSessions.Add(foreignSession);
                var assignment = new EventSessionLanguage { TenantId = foreign.TenantId, EventSessionId = foreignSession.Id,
                    LanguageId = 1, EventSession = foreignSession, Language = null!, Tenant = null! };
                context.EventSessionLanguages.Add(assignment);
                await context.SaveChangesAsync();
                factory.PublicEventId = publicEvent.Id;
                factory.PrivateEventId = privateEvent.Id;
                factory.ForeignEventId = foreignEvent.Id;
                factory.PublicSessionId = published.Id;
                factory.DraftSessionId = draft.Id;
                factory.PrivateSessionId = privateSession.Id;
                factory.ForeignSessionId = foreignSession.Id;
                factory.ForeignAssignmentId = assignment.Id;
                return factory;
            }
            catch { await factory.DisposeAsync(); throw; }
        }
        private static EventSessionLanguage Assignment(int id, EventSession session, int languageId) => new()
        {
            Id = id, TenantId = session.TenantId, EventSessionId = session.Id, EventSession = session,
            LanguageId = languageId, Language = null!, Tenant = null!
        };
        private static Explore.Domain.Event Parent(TenantScenarioSeed.TenantScenarioResult owner, VisibilityTypeEnum visibility) =>
            new EventBuilder().WithActorId(owner.ActorId).WithTenantId(owner.TenantId)
                .WithStatus(EventStatusEnum.Published).WithVisibility(visibility).Build();
        private static EventSession Session(Explore.Domain.Event parent, EventSessionStatusEnum status)
        {
            var session = new EventSession(status) { Id = Guid.CreateVersion7(), EventId = parent.Id, Event = parent,
                TenantId = parent.TenantId, Tenant = null!, Title = "Language session", EventSessionKindId = (int)EventSessionKindEnum.Talk,
                RegistrationModeId = (int)RegistrationModeEnum.Open };
            var start = new DateTimeOffset(2027, 1, 1, 10, 0, 0, TimeSpan.Zero);
            session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());
            session.AssignEventLocation(EventLocation.CreateToBeAnnounced(parent.TenantId, parent.Id, Guid.CreateVersion7(), DateTime.UtcNow));
            return session;
        }
        public HttpClient Client(Guid userId)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
            return client;
        }
        public async Task<EventSessionLanguageDto?> DetailAsync(int id)
        {
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
            return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSessionLanguageDetailsQuery, EventSessionLanguageDto?>>()
                .QueryAsync(new() { Id = id }, default);
        }
        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            { Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = _path });
            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(_insertBarrier);
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
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
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            ConfigureDatabase(options);
            await using var db = new ExploreDbContext(options.Options);
            SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
            File.Delete(_path);
            File.Delete(_path + "-wal");
            File.Delete(_path + "-shm");
        }
    }
}
