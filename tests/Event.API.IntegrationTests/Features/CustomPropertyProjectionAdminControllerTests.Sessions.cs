using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class CustomPropertyProjectionAdminControllerTests
{
    [Test]
    public async Task SessionNativePortsAreProtectedAndScopedInTheActualHost()
    {
        await using var factory = await FactoryAsync();
        using var scope = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        await Assert.That(services.GetRequiredService<IQueryHandler<GetEventSessionCustomPropertyProjectionStatusQuery, BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>>>())
            .IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventSessionCustomPropertyProjectionStatusQuery, BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>>>();
        await Assert.That(services.GetRequiredService<IQueryHandler<GetEventSessionCustomPropertyProjectionsForSessionQuery, BaseCommandResponse<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>>>())
            .IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventSessionCustomPropertyProjectionsForSessionQuery, BaseCommandResponse<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>>>();
        await Assert.That(services.GetRequiredService<ICommandHandler<RebuildEventSessionCustomPropertyProjectionCommand, BaseCommandResponse<RebuildProjectionResponseDto>>>())
            .IsTypeOf<AuthorizationCommandHandlerDecorator<RebuildEventSessionCustomPropertyProjectionCommand, BaseCommandResponse<RebuildProjectionResponseDto>>>();
        var single = services.GetRequiredService<ICommandHandler<RebuildSingleEventSessionCustomPropertyProjectionCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(single).IsTypeOf<AuthorizationCommandHandlerDecorator<RebuildSingleEventSessionCustomPropertyProjectionCommand, BaseCommandResponse<Guid>>>();
        foreach (var port in new[]
        {
            typeof(IQueryHandler<GetEventSessionCustomPropertyProjectionStatusQuery, BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>>),
            typeof(IQueryHandler<GetEventSessionCustomPropertyProjectionsForSessionQuery, BaseCommandResponse<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>>),
            typeof(ICommandHandler<RebuildEventSessionCustomPropertyProjectionCommand, BaseCommandResponse<RebuildProjectionResponseDto>>),
            typeof(ICommandHandler<RebuildSingleEventSessionCustomPropertyProjectionCommand, BaseCommandResponse<Guid>>)
        })
        {
            await Assert.That(ReferenceEquals(services.GetRequiredService(port), services.GetRequiredService(port))).IsTrue();
            await Assert.That(ReferenceEquals(services.GetRequiredService(port), second.ServiceProvider.GetRequiredService(port))).IsFalse();
        }
    }

    [Test]
    public async Task SessionOperationsRejectForgedAdminClaimsAndForeignPersistedOwners()
    {
        await using var factory = await FactoryAsync();
        var data = await SeedAsync(factory);
        using var anonymous = factory.CreateClient();
        using var member = Client(factory, data.MemberId);
        using var admin = Client(factory, data.AdminId);
        foreach (var (client, tenantId, sessionId, status) in new[]
        {
            (anonymous, PlatformDefaults.DefaultTenantId, data.SessionId, HttpStatusCode.Unauthorized),
            (member, PlatformDefaults.DefaultTenantId, data.SessionId, HttpStatusCode.Forbidden),
            (admin, data.ForeignTenantId, data.ForeignSessionId, HttpStatusCode.Forbidden)
        })
        {
            foreach (var route in new[] { $"sessions/status?tenantId={tenantId}", $"sessions/{sessionId}?exposureCeiling=Internal" })
            {
                using var response = await client.GetAsync($"{Root}/{route}");
                await ProblemAsync(response, status);
                await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain("session-secret-internal");
            }
            using var batch = await client.PostAsJsonAsync($"{Root}/sessions/rebuild", new { tenantId });
            await ProblemAsync(batch, status);
            using var single = await client.PostAsJsonAsync($"{Root}/sessions/rebuild-single", new { eventSessionId = sessionId });
            await ProblemAsync(single, status);
        }
        await Assert.That((await SessionRowsAsync(admin, data.SessionId)).Count).IsEqualTo(0);
        await Assert.That((await SessionDirtyAsync(admin)).Length).IsEqualTo(1);
        await Assert.That((await DirtyAsync(admin)).Length).IsEqualTo(2);
    }

    [Test]
    public async Task SessionRebuildCommitsValuesStatusAndOnlySessionBacklog()
    {
        await using var factory = await FactoryAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        var result = await PostAsync<RebuildProjectionResponseDto>(client, "sessions/rebuild", new { tenantId = PlatformDefaults.DefaultTenantId, batchSize = 1 });
        await Assert.That(result.LockAcquired).IsTrue();
        await Assert.That(result.RowsProcessed).IsEqualTo(1);
        await Assert.That(result.RowsFailed).IsEqualTo(0);
        await Assert.That(result.DrainedDirtyScopes).IsEqualTo(1);
        await Assert.That((await SessionRowsAsync(client, data.SessionId)).Select(row => row.TextValue))
            .IsEquivalentTo(new string?[] { "session-safe-public", "session-secret-internal", "session-admin-only", "session-organizer-only" });
        await Assert.That((await SessionDirtyAsync(client)).Length).IsEqualTo(0);
        await Assert.That((await DirtyAsync(client)).Length).IsEqualTo(2);
        using var status = await client.GetAsync($"{Root}/sessions/status?tenantId={PlatformDefaults.DefaultTenantId}");
        await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        var row = Embedded(json).Single();
        await Assert.That(row.GetProperty("projectionName").GetString()).IsEqualTo(SessionProjection);
        await Assert.That(row.GetProperty("rowsProcessed").GetInt64()).IsEqualTo(1);
        await Assert.That(row.GetProperty("rowsFailed").GetInt64()).IsEqualTo(0);
        await Assert.That(row.GetProperty("pendingDirtyScopeCount").GetInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task SingleSessionRebuildPreservesExposureCeilingsAndDoesNotSettleWork()
    {
        await using var factory = await FactoryAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        using var forged = await client.PostAsJsonAsync($"{Root}/sessions/rebuild-single", new { eventSessionId = data.SessionId, tenantId = data.ForeignTenantId });
        await ProblemAsync(forged, HttpStatusCode.BadRequest);
        await Assert.That((await SessionRowsAsync(client, data.SessionId)).Count).IsEqualTo(0);
        var id = await PostAsync<Guid>(client, "sessions/rebuild-single", new { eventSessionId = data.SessionId });
        await Assert.That(id).IsEqualTo(data.SessionId);
        await Assert.That((await SessionRowsAsync(client, data.SessionId, "Public")).Select(row => row.Key)).IsEquivalentTo(new[] { "public" });
        await Assert.That((await SessionRowsAsync(client, data.SessionId, "TenantAdminOnly")).Select(row => row.Key)).IsEquivalentTo(new[] { "public", "admin" });
        await Assert.That((await SessionRowsAsync(client, data.SessionId, "OrganizerOnly")).Select(row => row.Key)).IsEquivalentTo(new[] { "public", "admin", "organizer" });
        await Assert.That((await SessionRowsAsync(client, data.SessionId, "Internal")).Count).IsEqualTo(4);
        await Assert.That((await SessionDirtyAsync(client)).Length).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailedSessionRebuildRollsBackReplacementsAndCanRetry(bool batch)
    {
        await using var factory = await FactoryAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        await PostAsync<Guid>(client, "sessions/rebuild-single", new { eventSessionId = data.SessionId });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var value = await db.EventSessionCustomPropertyValues.SingleAsync(row => row.EventSessionId == data.SessionId && row.TextValue == "session-safe-public");
            value.TextValue = "session-changed-public";
            await db.SaveChangesAsync();
        }
        factory.Reads.Fail = true;
        var route = batch ? "sessions/rebuild" : "sessions/rebuild-single";
        object body = batch ? new { tenantId = PlatformDefaults.DefaultTenantId } : new { eventSessionId = data.SessionId };
        using var failed = await client.PostAsJsonAsync($"{Root}/{route}", body);
        await ProblemAsync(failed, HttpStatusCode.InternalServerError);
        factory.Reads.Fail = false;
        await Assert.That((await SessionRowsAsync(client, data.SessionId, "Public")).Single().TextValue).IsEqualTo("session-safe-public");
        await Assert.That((await SessionRowsAsync(client, data.SessionId)).Count).IsEqualTo(4);
        await Assert.That((await SessionDirtyAsync(client)).Length).IsEqualTo(1);
        using var retry = await client.PostAsJsonAsync($"{Root}/{route}", body);
        await Assert.That(retry.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await SessionRowsAsync(client, data.SessionId, "Public")).Single().TextValue).IsEqualTo("session-changed-public");
        await Assert.That((await SessionDirtyAsync(client)).Length).IsEqualTo(batch ? 0 : 1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelledSessionRebuildRollsBackAndRetainsPendingWork(bool batch)
    {
        await using var factory = await FactoryAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        await PostAsync<Guid>(client, "sessions/rebuild-single", new { eventSessionId = data.SessionId });
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", data.AdminId.ToString())], "Test"))
            };
            using var cancellation = new CancellationTokenSource();
            factory.Reads.Block = true;
            var entered = factory.Reads.Entered.Task;
            Task execution = batch
                ? scope.ServiceProvider.GetRequiredService<ICommandHandler<RebuildEventSessionCustomPropertyProjectionCommand, BaseCommandResponse<RebuildProjectionResponseDto>>>()
                    .ExecuteAsync(new() { RequestDto = new() { TenantId = PlatformDefaults.DefaultTenantId } }, cancellation.Token)
                : scope.ServiceProvider.GetRequiredService<ICommandHandler<RebuildSingleEventSessionCustomPropertyProjectionCommand, BaseCommandResponse<Guid>>>()
                    .ExecuteAsync(new() { EventSessionId = data.SessionId }, cancellation.Token);
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
        await Assert.That((await SessionRowsAsync(client, data.SessionId)).Count).IsEqualTo(4);
        await Assert.That((await SessionDirtyAsync(client)).Length).IsEqualTo(1);
        await Assert.That((await DirtyAsync(client)).Length).IsEqualTo(2);
    }

    private static async Task<IReadOnlyList<EventSessionCustomPropertyProjectionDto>> SessionRowsAsync(HttpClient client, Guid sessionId, string? ceiling = null)
    {
        using var response = await client.GetAsync($"{Root}/sessions/{sessionId}" + (ceiling is null ? "" : $"?exposureCeiling={ceiling}"));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<BaseCommandResponse<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>>(JsonOptions))!.Id!;
    }

    private static async Task<JsonElement[]> SessionDirtyAsync(HttpClient client)
    {
        using var response = await client.GetAsync($"{Root}/dirty-scopes?tenantId={PlatformDefaults.DefaultTenantId}&projectionName={SessionProjection}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Embedded(json).Select(row => row.Clone()).ToArray();
    }
}
