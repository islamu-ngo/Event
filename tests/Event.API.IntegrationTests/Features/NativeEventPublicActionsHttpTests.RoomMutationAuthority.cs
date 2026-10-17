using Event.Api.IntegrationTests.Fixtures;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessions.Handlers.Commands;
using Explore.Application.Features.LocationRooms.Handlers.Commands;
using Explore.Application.Features.LocationRooms.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    public async Task RoomUpdateRetainsCurrentHumanAdministration(int grant, bool move)
    {
        await using var fixture = await ProjectionRoomFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var (room, target) = await SeedRoomMutation(db);
        await GrantFooterAdministrator(db, data.OwnerId, grant);
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);

        var result = await ActivatorUtilities.CreateInstance<UpdateLocationRoomCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(RoomMutation(room, move ? target.Id : null), default);

        await Assert.That(result.IsSuccess).IsTrue();
        var current = await db.LocationRooms.AsNoTracking().SingleAsync(row => row.Id == room.Id);
        await Assert.That(current.LocationId).IsEqualTo(move ? target.Id : room.LocationId);
        await Assert.That(current.Name).IsEqualTo("Changed room");
        await Assert.That(current.ConcurrencyStamp).IsNotEqualTo(room.ConcurrencyStamp);
    }

    [Test]
    [Arguments("local", ExternalApiKeyOwnerType.Tenant, ExternalApiKeyScopes.AdminTenant, true)]
    [Arguments("local", ExternalApiKeyOwnerType.User, ExternalApiKeyScopes.AdminTenant, true)]
    [Arguments("local", ExternalApiKeyOwnerType.InstanceAdmin, ExternalApiKeyScopes.AdminInstance, false)]
    [Arguments("cerbos", ExternalApiKeyOwnerType.InstanceAdmin, ExternalApiKeyScopes.AdminInstance, true)]
    [Arguments("cerbos", ExternalApiKeyOwnerType.Tenant, ExternalApiKeyScopes.AdminTenant, true)]
    [Arguments("local", ExternalApiKeyOwnerType.Tenant, ExternalApiKeyScopes.EventsWrite, false)]
    [Arguments("cerbos", ExternalApiKeyOwnerType.Tenant, ExternalApiKeyScopes.EventsWrite, false)]
    public async Task RoomMachineMutationPreservesSelectedProviderScopeIntersection(
        string provider, ExternalApiKeyOwnerType owner, string scopes, bool allowed)
    {
        await using var factory = new NativeEventSeriesFactory
        {
            InstanceCerbosClient = provider == "cerbos" ? AllowingMachinePolicyTransport() : null
        };
        factory.AdditionalConfiguration["Authorization:Provider"] = provider;
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var (room, target) = await SeedRoomMutation(db);
        if (owner == ExternalApiKeyOwnerType.User) await GrantFooterAdministrator(db, data.OwnerId, 2);
        if (owner == ExternalApiKeyOwnerType.InstanceAdmin) await GrantTemplateAdministrator(db, data.OwnerId);
        await AuthenticateNativeMachine(scope, db,
            owner == ExternalApiKeyOwnerType.Tenant ? db.TenantContext!.TenantId : data.OwnerId, owner, scopes: scopes);
        var handler = scope.ServiceProvider.GetRequiredService<UpdateLocationRoomCommandHandler>();

        if (allowed)
            await Assert.That((await handler.ExecuteAsync(RoomMutation(room, target.Id), default)).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(RoomMutation(room, target.Id), default));

        var current = await db.LocationRooms.AsNoTracking().SingleAsync(row => row.Id == room.Id);
        await Assert.That(current.LocationId).IsEqualTo(allowed ? target.Id : room.LocationId);
        if (allowed) await Assert.That(current.UpdatedBy).IsNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IAdminContext>().UserId).IsNull();
    }

    [Test]
    [Arguments("revoked")]
    [Arguments("scope")]
    [Arguments("owner")]
    [Arguments("expired")]
    public async Task RoomMutationRechecksStoredKeyAfterAuthentication(string drift)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var (room, target) = await SeedRoomMutation(db);
        var key = await AuthenticateNativeMachine(scope, db, db.TenantContext!.TenantId,
            ExternalApiKeyOwnerType.Tenant, scopes: ExternalApiKeyScopes.AdminTenant);
        var query = db.ExternalApiKeys.Where(row => row.Id == key.Id);
        switch (drift)
        {
            case "revoked": await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExternalApiKeyStatusId, (int)ExternalApiKeyStatusEnum.Revoked)); break;
            case "scope": await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Scopes, ExternalApiKeyScopes.EventsRead)); break;
            case "owner": await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.OwnerId, data.ForeignTenantId)); break;
            case "expired": await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExpiresAt, (DateTime?)DateTime.UtcNow.AddDays(-1))); break;
            default: throw new ArgumentOutOfRangeException(nameof(drift));
        }

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            scope.ServiceProvider.GetRequiredService<UpdateLocationRoomCommandHandler>()
                .ExecuteAsync(RoomMutation(room, target.Id), default));

        await Assert.That((await db.LocationRooms.AsNoTracking().SingleAsync(row => row.Id == room.Id)).LocationId).IsEqualTo(room.LocationId);
    }

    [Test]
    public async Task RoomUpdatePreservesConcurrencyAndRejectsTrackedParentSubstitution()
    {
        await using var fixture = await ProjectionRoomFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var (room, target) = await SeedRoomMutation(db);
        await GrantFooterAdministrator(db, data.OwnerId, 2);
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);
        var handler = ActivatorUtilities.CreateInstance<UpdateLocationRoomCommandHandler>(scope.ServiceProvider);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            handler.ExecuteAsync(RoomMutation(room, target.Id) with { ExpectedConcurrencyStamp = Guid.CreateVersion7() }, default));
        using var fresh = fixture.CreateScope();
        await fixture.AuthenticateAsync(fresh);
        var freshDb = fresh.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tracked = await freshDb.LocationRooms.SingleAsync(row => row.Id == room.Id);
        tracked.LocationId = target.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ActivatorUtilities.CreateInstance<UpdateLocationRoomCommandHandler>(fresh.ServiceProvider)
                .ExecuteAsync(RoomMutation(room, null), default));

        await Assert.That((await db.LocationRooms.AsNoTracking().SingleAsync(row => row.Id == room.Id)).LocationId).IsEqualTo(room.LocationId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RoomUpdateRechecksCommittedSourceParentAndAdministratorDrift(bool revoke)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await ProjectionRoomFixture.CreateAsync(interceptor: boundary);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var (room, target) = await SeedRoomMutation(db);
        await GrantFooterAdministrator(db, data.OwnerId, 2);
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);
        var command = RoomMutation(room, target.Id);
        await scope.ServiceProvider.GetRequiredService<RequestAuthorization<UpdateLocationRoomCommand>>()
            .AuthorizeAsync(command, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default);
        boundary.Arm(db, async token =>
        {
            await using var changed = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<ExploreDbContext>>()
                .CreateDbContextAsync(token);
            changed.TenantContext = new AuthorityTenant(db.TenantContext!.TenantId);
            if (revoke)
            {
                await changed.TenantUserRoleGrants.Where(row => row.TenantUser.UserId == data.OwnerId)
                    .ExecuteDeleteAsync(token);
                return;
            }
            await new EfCoreUnitOfWork(changed).ExecuteSerializableAsync(async inner =>
            {
                var repository = new LocationRoomRepository(changed);
                var current = await changed.LocationRooms.SingleAsync(row => row.Id == room.Id, inner);
                var destination = await changed.Locations.SingleAsync(row => row.Id == target.Id, inner);
                await repository.MoveToLocationAsync(current, destination, "Concurrent move", current.ConcurrencyStamp, inner);
                await repository.Update(current);
                return true;
            }, token);
        });
        Task Change() => ActivatorUtilities.CreateInstance<UpdateLocationRoomCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(command, default);

        if (revoke) await Assert.ThrowsAsync<UnauthorizedAccessException>(Change);
        else await Assert.ThrowsAsync<ConcurrencyConflictException>(Change);

        await Assert.That(boundary.Applied).IsTrue();
        var current = await db.LocationRooms.AsNoTracking().SingleAsync(row => row.Id == room.Id);
        await Assert.That(current.LocationId).IsEqualTo(revoke ? room.LocationId : target.Id);
        await Assert.That(current.Name).IsEqualTo(revoke ? room.Name : "Concurrent move");
    }

    [Test]
    public async Task RoomRelocationRetainsScheduleReferenceGuardInsideItsTransaction()
    {
        await using var fixture = await ProjectionRoomFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var (room, target) = await SeedRoomMutation(db);
        await PrepareSessionParent(db, data.PublicId);
        var session = await ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new()
            {
                EventSessionDto = SessionInput(data.PublicId) with { LocationId = room.LocationId, RoomId = room.Id }
            }, default);
        await Assert.That(session.IsSuccess).IsTrue();
        await GrantFooterAdministrator(db, data.OwnerId, 2);
        using var updateScope = fixture.CreateScope();
        await fixture.AuthenticateAsync(updateScope);
        await updateScope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);

        var result = await ActivatorUtilities.CreateInstance<UpdateLocationRoomCommandHandler>(updateScope.ServiceProvider)
            .ExecuteAsync(RoomMutation(room, target.Id), default);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That((await db.LocationRooms.AsNoTracking().SingleAsync(row => row.Id == room.Id)).LocationId).IsEqualTo(room.LocationId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RoomUpdateHonorsSelectedProviderRefusalAndOutage(bool unavailable)
    {
        var client = Substitute.For<ICerbosClient>();
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(call =>
        {
            if (unavailable) throw new RpcException(new Status(StatusCode.Unavailable, "Unavailable test transport."));
            var request = call.Arg<CheckResourcesRequest>()!.ToCheckResourcesRequest();
            var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
            foreach (var resource in request.Resources)
            {
                var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                {
                    Resource = new() { Id = resource.Resource.Id, Kind = resource.Resource.Kind }
                };
                foreach (string action in resource.Actions) result.Actions.Add(action, Cerbos.Api.V1.Effect.Effect.Deny);
                response.Results.Add(result);
            }
            return Task.FromResult(new CheckResourcesResponse(response));
        });
        await using var factory = new NativeEventSeriesFactory { InstanceCerbosClient = client };
        factory.AdditionalConfiguration["Authorization:Provider"] = "cerbos";
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var (room, target) = await SeedRoomMutation(db);
        await AuthenticateNativeMachine(scope, db, db.TenantContext!.TenantId, ExternalApiKeyOwnerType.Tenant,
            scopes: ExternalApiKeyScopes.AdminTenant);
        Task Change() => scope.ServiceProvider.GetRequiredService<UpdateLocationRoomCommandHandler>()
            .ExecuteAsync(RoomMutation(room, target.Id), default);

        if (unavailable) await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(Change);
        else await Assert.ThrowsAsync<AuthorizationException>(Change);

        await Assert.That((await db.LocationRooms.AsNoTracking().SingleAsync(row => row.Id == room.Id)).LocationId).IsEqualTo(room.LocationId);
    }

    private static async Task<(LocationRoom Room, Location Destination)> SeedRoomMutation(ExploreDbContext db)
    {
        Guid tenantId = db.TenantContext!.TenantId;
        var source = new Location { Id = Guid.CreateVersion7(), TenantId = tenantId, FullName = "Room source", City = "Brussels", Country = "BE" };
        var destination = new Location { Id = Guid.CreateVersion7(), TenantId = tenantId, FullName = "Room destination", City = "Brussels", Country = "BE" };
        var room = new LocationRoom
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            LocationId = source.Id,
            Location = source,
            Name = "Original room",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        db.AddRange(source, destination, room);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (room, destination);
    }

    private static UpdateLocationRoomCommand RoomMutation(LocationRoom room, Guid? destination) => new()
    {
        LocationRoomId = room.Id,
        ExpectedConcurrencyStamp = room.ConcurrencyStamp,
        UpdateLocationRoomDto = new()
        {
            Name = new() { Value = "Changed room" },
            Location = destination is { } id ? new() { LocationId = id } : null
        }
    };
}
