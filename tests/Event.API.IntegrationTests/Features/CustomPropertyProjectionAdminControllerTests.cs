using System.Net;
using System.Security.Claims;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class CustomPropertyProjectionAdminControllerTests
{
    private const string Root = "/api/admin/custom-property-projections";
    private const string EventProjection = IEventCustomPropertyProjectionUpdater.ProjectionName;
    private const string SessionProjection = IEventSessionCustomPropertyProjectionUpdater.ProjectionName;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    public async Task NativePorts_AreProtectedAndScopedInTheActualHost()
    {
        await using var factory = await FactoryAsync();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await Assert.That(services.GetRequiredService<IQueryHandler<GetEventCustomPropertyProjectionStatusQuery, BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>>>())
            .IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventCustomPropertyProjectionStatusQuery, BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>>>();
        await Assert.That(services.GetRequiredService<IQueryHandler<GetCustomPropertyProjectionDirtyScopesQuery, PaginatedResult<ProjectionDirtyScopeDto>>>())
            .IsTypeOf<AuthorizationQueryHandlerDecorator<GetCustomPropertyProjectionDirtyScopesQuery, PaginatedResult<ProjectionDirtyScopeDto>>>();
        await Assert.That(services.GetRequiredService<IQueryHandler<GetEventCustomPropertyProjectionsForEventQuery, BaseCommandResponse<IReadOnlyList<EventCustomPropertyProjectionDto>>>>())
            .IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventCustomPropertyProjectionsForEventQuery, BaseCommandResponse<IReadOnlyList<EventCustomPropertyProjectionDto>>>>();
        await Assert.That(services.GetRequiredService<ICommandHandler<RebuildEventCustomPropertyProjectionCommand, BaseCommandResponse<RebuildProjectionResponseDto>>>())
            .IsTypeOf<AuthorizationCommandHandlerDecorator<RebuildEventCustomPropertyProjectionCommand, BaseCommandResponse<RebuildProjectionResponseDto>>>();
        await Assert.That(services.GetRequiredService<ICommandHandler<RebuildSingleEventCustomPropertyProjectionCommand, BaseCommandResponse<Guid>>>())
            .IsTypeOf<AuthorizationCommandHandlerDecorator<RebuildSingleEventCustomPropertyProjectionCommand, BaseCommandResponse<Guid>>>();
        var drain = services.GetRequiredService<ICommandHandler<DrainCustomPropertyProjectionDirtyScopesCommand, BaseCommandResponse<DrainDirtyScopesResponseDto>>>();
        await Assert.That(drain).IsTypeOf<AuthorizationCommandHandlerDecorator<DrainCustomPropertyProjectionDirtyScopesCommand, BaseCommandResponse<DrainDirtyScopesResponseDto>>>();
        await Assert.That(ReferenceEquals(drain, services.GetRequiredService<ICommandHandler<DrainCustomPropertyProjectionDirtyScopesCommand, BaseCommandResponse<DrainDirtyScopesResponseDto>>>())).IsTrue();
        using var second = factory.Services.CreateScope();
        await Assert.That(ReferenceEquals(drain, second.ServiceProvider.GetRequiredService<ICommandHandler<DrainCustomPropertyProjectionDirtyScopesCommand, BaseCommandResponse<DrainDirtyScopesResponseDto>>>())).IsFalse();
    }

    [Test]
    public async Task DeniedCallersAndForeignRequestFactsCannotReadOrMutateProjections()
    {
        await using var factory = await FactoryAsync();
        var data = await SeedAsync(factory);
        using var anonymous = factory.CreateClient();
        using var unauthorized = await anonymous.GetAsync($"{Root}/events/{data.EventId}");
        await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var member = Client(factory, data.MemberId);
        using var admin = Client(factory, data.AdminId);
        foreach (var (client, tenantId, eventId) in new[]
        {
            (member, PlatformDefaults.DefaultTenantId, data.EventId),
            (admin, data.ForeignTenantId, data.ForeignEventId)
        })
        {
            foreach (var url in new[] { $"status?tenantId={tenantId}",
                $"dirty-scopes?tenantId={tenantId}&projectionName={EventProjection}", $"events/{eventId}" })
            {
                using var response = await client.GetAsync($"{Root}/{url}");
                await ProblemAsync(response, HttpStatusCode.Forbidden);
                await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain("secret-internal");
            }
            using var rebuild = await client.PostAsJsonAsync($"{Root}/rebuild", new { tenantId });
            await ProblemAsync(rebuild, HttpStatusCode.Forbidden);
            using var single = await client.PostAsJsonAsync($"{Root}/rebuild-single-event", new { eventId });
            await ProblemAsync(single, HttpStatusCode.Forbidden);
            using var drain = await client.PostAsJsonAsync($"{Root}/drain-dirty-scopes", new { tenantId, projectionName = EventProjection });
            await ProblemAsync(drain, HttpStatusCode.Forbidden);
        }
        await Assert.That((await RowsAsync(admin, data.EventId)).Count).IsEqualTo(0);
        await Assert.That((await DirtyAsync(admin)).Length).IsEqualTo(2);
    }

    [Test]
    public async Task RebuildAndBothDrainPathsCommitRowsAndSettleTheirOwnBacklog()
    {
        await using var factory = await FactoryAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        var rebuilt = await PostAsync<RebuildProjectionResponseDto>(client, "rebuild", new { tenantId = PlatformDefaults.DefaultTenantId, batchSize = 1 });
        await Assert.That(rebuilt.LockAcquired).IsTrue();
        await Assert.That(rebuilt.RowsFailed).IsEqualTo(0);
        await Assert.That(rebuilt.DrainedDirtyScopes).IsEqualTo(2);
        await Assert.That((await DirtyAsync(client)).Length).IsEqualTo(0);
        await Assert.That((await RowsAsync(client, data.EventId)).Select(row => row.TextValue))
            .IsEquivalentTo(new string?[] { "safe-public", "secret-internal", "admin-only", "organizer-only" });
        using (var status = await client.GetAsync($"{Root}/status?tenantId={PlatformDefaults.DefaultTenantId}"))
        {
            await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
            var row = Embedded(json)[0];
            await Assert.That(row.GetProperty("rowsFailed").GetInt64()).IsEqualTo(0);
            await Assert.That(row.GetProperty("pendingDirtyScopeCount").GetInt32()).IsEqualTo(0);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var value = await db.EventCustomPropertyValues.SingleAsync(row => row.Id == data.PublicValueId);
            value.TextValue = "changed-public";
            AddDirty(db, PlatformDefaults.DefaultTenantId, data.EventId, EventProjection, CustomPropertyProjectionScopeType.Event);
            await db.SaveChangesAsync();
        }
        var drained = await PostAsync<DrainDirtyScopesResponseDto>(client, "drain-dirty-scopes", new { tenantId = PlatformDefaults.DefaultTenantId, projectionName = EventProjection });
        await Assert.That(drained.DrainedCount).IsEqualTo(1);
        await Assert.That((await RowsAsync(client, data.EventId, "Public"))[0].TextValue).IsEqualTo("changed-public");
        await Assert.That((await DirtyAsync(client)).Length).IsEqualTo(0);
        var again = await PostAsync<DrainDirtyScopesResponseDto>(client, "drain-dirty-scopes", new { tenantId = PlatformDefaults.DefaultTenantId, projectionName = EventProjection });
        await Assert.That(again.DrainedCount).IsEqualTo(0);
        var sessionDrain = await PostAsync<DrainDirtyScopesResponseDto>(client, "drain-dirty-scopes", new { tenantId = PlatformDefaults.DefaultTenantId, projectionName = SessionProjection });
        await Assert.That(sessionDrain.DrainedCount).IsEqualTo(1);
        using var sessions = await client.GetAsync($"{Root}/sessions/{data.SessionId}?exposureCeiling=Public");
        await Assert.That(sessions.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var sessionRows = (await sessions.Content.ReadFromJsonAsync<BaseCommandResponse<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>>(JsonOptions))!.Id!;
        await Assert.That(sessionRows.Select(row => row.TextValue)).IsEquivalentTo(new string?[] { "session-safe-public" });
        await Assert.That(await sessions.Content.ReadAsStringAsync()).DoesNotContain("session-secret-internal");
    }

    [Test]
    public async Task SingleRebuildAndExposureCeilingsUsePersistedValuesNotCallerAuthority()
    {
        await using var factory = await FactoryAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        using var forged = await client.PostAsJsonAsync($"{Root}/rebuild-single-event", new { eventId = data.EventId, tenantId = data.ForeignTenantId });
        await ProblemAsync(forged, HttpStatusCode.BadRequest);
        await Assert.That((await RowsAsync(client, data.EventId)).Count).IsEqualTo(0);
        await PostAsync<Guid>(client, "rebuild-single-event", new { eventId = data.EventId });
        await Assert.That((await RowsAsync(client, data.EventId, "Public")).Select(row => row.Key)).IsEquivalentTo(new[] { "public" });
        await Assert.That((await RowsAsync(client, data.EventId, "TenantAdminOnly")).Select(row => row.Key)).IsEquivalentTo(new[] { "public", "admin" });
        await Assert.That((await RowsAsync(client, data.EventId, "OrganizerOnly")).Select(row => row.Key)).IsEquivalentTo(new[] { "public", "admin", "organizer" });
        await Assert.That((await RowsAsync(client, data.EventId, "Internal")).Count).IsEqualTo(4);
        // A single refresh does not claim to have drained tenant-owned work.
        await Assert.That((await DirtyAsync(client)).Length).IsEqualTo(2);
    }

    [Test]
    public async Task InvalidRequestsPreserveProblemDetailsAndDoNotSettleWork()
    {
        await using var factory = await FactoryAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        using var invalid = await client.PostAsJsonAsync($"{Root}/rebuild", new { tenantId = PlatformDefaults.DefaultTenantId, batchSize = 0 });
        await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        using var unknown = await client.PostAsJsonAsync($"{Root}/drain-dirty-scopes", new { tenantId = PlatformDefaults.DefaultTenantId, projectionName = "unknown" });
        await ProblemAsync(unknown, HttpStatusCode.BadRequest);
        using var quota = await client.PostAsJsonAsync($"{Root}/rebuild", new { tenantId = PlatformDefaults.DefaultTenantId, batchSize = int.MaxValue });
        await ProblemAsync(quota, HttpStatusCode.UnprocessableEntity);
        await Assert.That((await DirtyAsync(client)).Length).IsEqualTo(2);
        await Assert.That((await RowsAsync(client, data.EventId)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task DirtyScopePagesAreDistinctAndReadsDoNotSettleWork()
    {
        await using var factory = await FactoryAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        var first = await DirtyAsync(client, 1, 1);
        var second = await DirtyAsync(client, 2, 1);
        await Assert.That(first.Length).IsEqualTo(1);
        await Assert.That(second.Length).IsEqualTo(1);
        await Assert.That(second[0].GetProperty("id").GetInt64()).IsGreaterThan(first[0].GetProperty("id").GetInt64());
        await Assert.That((await DirtyAsync(client, 3, 1)).Length).IsEqualTo(0);
        await Assert.That((await DirtyAsync(client)).Length).IsEqualTo(2);
    }

    [Test]
    public async Task FailedDrainRollsBackDeletesAndLeavesWorkForRetry()
    {
        await using var factory = await FactoryAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        await PostAsync<Guid>(client, "rebuild-single-event", new { eventId = data.EventId });
        factory.Reads.Fail = true;
        using var failed = await client.PostAsJsonAsync($"{Root}/drain-dirty-scopes", new { tenantId = PlatformDefaults.DefaultTenantId, projectionName = EventProjection });
        await ProblemAsync(failed, HttpStatusCode.InternalServerError);
        factory.Reads.Fail = false;
        await Assert.That((await RowsAsync(client, data.EventId)).Count).IsEqualTo(4);
        await Assert.That((await DirtyAsync(client)).Length).IsEqualTo(2);
        var retried = await PostAsync<DrainDirtyScopesResponseDto>(client, "drain-dirty-scopes", new { tenantId = PlatformDefaults.DefaultTenantId, projectionName = EventProjection });
        await Assert.That(retried.DrainedCount).IsEqualTo(2);
        await Assert.That((await DirtyAsync(client)).Length).IsEqualTo(0);
    }

    [Test]
    public async Task CancellationDuringDrainRollsBackAndKeepsPendingWork()
    {
        await using var factory = await FactoryAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        await PostAsync<Guid>(client, "rebuild-single-event", new { eventId = data.EventId });
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", data.AdminId.ToString())], "Test"))
            };
            var port = scope.ServiceProvider.GetRequiredService<ICommandHandler<DrainCustomPropertyProjectionDirtyScopesCommand, BaseCommandResponse<DrainDirtyScopesResponseDto>>>();
            using var cancellation = new CancellationTokenSource();
            factory.Reads.Block = true;
            var entered = factory.Reads.Entered.Task;
            var execution = port.ExecuteAsync(new DrainCustomPropertyProjectionDirtyScopesCommand
            {
                RequestDto = new() { TenantId = PlatformDefaults.DefaultTenantId, ProjectionName = EventProjection }
            }, cancellation.Token);
            try
            {
                await entered.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                await cancellation.CancelAsync();
            }
            await Assert.That(async () => await execution.WaitAsync(TimeSpan.FromSeconds(10))).Throws<OperationCanceledException>();
            factory.Reads.Block = false;
        }
        await Assert.That((await RowsAsync(client, data.EventId)).Count).IsEqualTo(4);
        await Assert.That((await DirtyAsync(client)).Length).IsEqualTo(2);
    }

    [Test]
    public async Task EventAndRetainedSessionStatusValidationUseProblemDetails()
    {
        await using var factory = await FactoryAsync();
        // Substitute the external PDP only to reach the handlers' empty-ID validation.
        factory.AuthorizationProviderOverride = new StubAuthorizationProvider();
        using var client = Client(factory, Guid.CreateVersion7());
        foreach (var (path, errorKey) in new[]
        {
            ("status", "customPropertyProjection"),
            ("sessions/status", "eventSessionCustomPropertyProjection")
        })
        {
            using var response = await client.GetAsync($"{Root}/{path}?tenantId={Guid.Empty}");
            await ProblemAsync(response, HttpStatusCode.BadRequest);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo("validation_failed");
            var errors = json.RootElement.GetProperty("errors");
            await Assert.That(errors.EnumerateObject().Single().Name).IsEqualTo(errorKey);
            await Assert.That(errors.GetProperty(errorKey).GetArrayLength()).IsEqualTo(1);
        }
    }

    private static async Task<NativeCustomPropertyGovernanceFactory> FactoryAsync()
    {
        var factory = await NativeCustomPropertyGovernanceFactory.CreateAsync();
        factory.AdditionalConfiguration["Authorization:Provider"] = "local";
        factory.AdditionalConfiguration["Testing:DisableDeploymentModeCache"] = "true";
        factory.AdditionalConfiguration["Keycloak:Authority"] = "";
        factory.AdditionalConfiguration["Keycloak:AuthorizationUrl"] = "";
        return factory;
    }

    private static HttpClient Client(NativeCustomPropertyGovernanceFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        // Even the ordinary member claims admin: only persisted grants may authorize.
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(userId, PlatformDefaults.DefaultTenantId));
        return client;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string route, object body)
    {
        using var response = await client.PostAsJsonAsync($"{Root}/{route}", body);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<BaseCommandResponse<T>>(JsonOptions))!;
        await Assert.That(result.IsSuccess).IsTrue();
        return result.Id!;
    }

    private static async Task<IReadOnlyList<EventCustomPropertyProjectionDto>> RowsAsync(HttpClient client, Guid eventId, string? ceiling = null)
    {
        using var response = await client.GetAsync($"{Root}/events/{eventId}" + (ceiling is null ? "" : $"?exposureCeiling={ceiling}"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BaseCommandResponse<IReadOnlyList<EventCustomPropertyProjectionDto>>>(JsonOptions))!.Id!;
    }

    private static async Task<JsonElement[]> DirtyAsync(HttpClient client, int page = 1, int pageSize = 20)
    {
        using var response = await client.GetAsync($"{Root}/dirty-scopes?tenantId={PlatformDefaults.DefaultTenantId}&projectionName={EventProjection}&pageNumber={page}&pageSize={pageSize}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Embedded(json).Select(row => row.Clone()).ToArray();
    }

    private static JsonElement[] Embedded(JsonDocument json) => json.RootElement.GetProperty("_embedded").EnumerateObject().Single().Value.EnumerateArray().ToArray();

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        // Existing controller content negotiation also permits application/json for ProblemDetails.
        await Assert.That(response.Content.Headers.ContentType!.MediaType is "application/problem+json" or "application/json").IsTrue();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)status);
        await Assert.That(json.RootElement.TryGetProperty("title", out _)).IsTrue();
    }

    private static async Task<SeedData> SeedAsync(NativeCustomPropertyGovernanceFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var admin = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var member = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var foreign = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
        var membership = await db.TenantUsers.Include(row => row.Tenant).SingleAsync(row => row.UserId == admin.UserId);
        db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(), TenantId = membership.TenantId, Tenant = membership.Tenant,
            TenantUserId = membership.Id, TenantUser = membership, RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant
        });
        var own = await EventScenarioSeed.SeedPublishedEventAsync(db, admin.ActorId, admin.TenantId);
        var other = await EventScenarioSeed.SeedPublishedEventAsync(db, foreign.ActorId, foreign.TenantId);
        var session = await db.EventSessions.SingleAsync(row => row.EventId == own.EventId);
        Guid publicValueId = default;
        foreach (var (key, exposure, text) in new[]
        {
            ("public", ExposureLevel.Public, "safe-public"), ("internal", ExposureLevel.Internal, "secret-internal"),
            ("admin", ExposureLevel.TenantAdminOnly, "admin-only"), ("organizer", ExposureLevel.OrganizerOnly, "organizer-only")
        })
        {
            var definition = new EventCustomPropertyDefinition
            {
                Id = Guid.CreateVersion7(), TenantId = admin.TenantId, EventId = own.EventId,
                Namespace = "tenant.custom", Key = key, DisplayName = key, IsActive = true,
                PropertyType = PropertyType.Text, ExposureLevel = exposure, ConcurrencyStamp = Guid.CreateVersion7()
            };
            var value = new EventCustomPropertyValue
            {
                Id = Guid.CreateVersion7(), TenantId = admin.TenantId, EventId = own.EventId,
                EventCustomPropertyDefinitionId = definition.Id, TextValue = text, ConcurrencyStamp = Guid.CreateVersion7()
            };
            db.AddRange(definition, value);
            if (exposure == ExposureLevel.Public) publicValueId = value.Id;
        }
        foreach (var (key, exposure, text) in new[] { ("public", ExposureLevel.Public, "session-safe-public"), ("internal", ExposureLevel.Internal, "session-secret-internal") })
        {
            var definition = new EventSessionCustomPropertyDefinition
            {
                Id = Guid.CreateVersion7(), TenantId = admin.TenantId, EventSessionId = session.Id,
                Namespace = "tenant.custom", Key = key, DisplayName = key, IsActive = true,
                PropertyType = PropertyType.Text, ExposureLevel = exposure, ConcurrencyStamp = Guid.CreateVersion7()
            };
            db.AddRange(definition, new EventSessionCustomPropertyValue
            {
                Id = Guid.CreateVersion7(), TenantId = admin.TenantId, EventSessionId = session.Id,
                EventSessionCustomPropertyDefinitionId = definition.Id, TextValue = text, ConcurrencyStamp = Guid.CreateVersion7()
            });
        }
        AddDirty(db, admin.TenantId, own.EventId, EventProjection, CustomPropertyProjectionScopeType.Event);
        AddDirty(db, admin.TenantId, own.EventId, EventProjection, CustomPropertyProjectionScopeType.Event, Guid.CreateVersion7());
        AddDirty(db, admin.TenantId, session.Id, SessionProjection, CustomPropertyProjectionScopeType.EventSession);
        await db.SaveChangesAsync();
        return new(admin.UserId, member.UserId, own.EventId, session.Id, foreign.TenantId, other.EventId, publicValueId);
    }

    private static void AddDirty(ExploreDbContext db, Guid tenantId, Guid id, string projection, CustomPropertyProjectionScopeType type, Guid? definitionId = null)
        => db.CustomPropertyProjectionDirtyScopes.Add(new CustomPropertyProjectionDirtyScope
        {
            ProjectionName = projection, ProjectionVersion = 1, TenantId = tenantId, ScopeId = id,
            ScopeType = type, DefinitionId = definitionId, Reason = "rebuild_in_progress", CreatedAt = DateTimeOffset.UtcNow
        });

    private sealed record SeedData(Guid AdminId, Guid MemberId, Guid EventId, Guid SessionId, Guid ForeignTenantId, Guid ForeignEventId, Guid PublicValueId);
}
