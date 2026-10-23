using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventCustomPropertyProjections.Handlers.Commands;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Handlers.Commands;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Commands;
using Explore.Application.Features.LocationRooms.Handlers.Commands;
using Explore.Application.Features.LocationRooms.Requests.Commands;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    public static IEnumerable<(int Operation, string Change)> ProjectionRoomTerminalCases()
    {
        // Operations 2 and 3 exercise the same drain owner against both real updaters.
        for (int operation = 0; operation < 7; operation++)
        {
            foreach (string change in new[]
            {
                "unchanged_instance", "unchanged_tenant", "stamp", "binding", "admin", "admin_tenant",
                "request", "source", "credential_after_reader"
            })
                yield return (operation, change);
            if (operation is 4 or 5)
                yield return (operation, "persisted_parent");
        }
    }

    [Test]
    [MethodDataSource(nameof(ProjectionRoomTerminalCases))]
    public async Task ProjectionAndRoomBodiesRequireOriginalAuthorityThroughPhysicalCommit(int operation, string change)
    {
        await using var fixture = await ProjectionRoomFixture.CreateAsync();
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var services = scope.ServiceProvider;
        var request = (AdministrativeRequest)scope;
        var db = services.GetRequiredService<ExploreDbContext>();
        Guid userId = fixture.Data.OwnerId;
        bool session = operation % 2 == 1;
        var projection = operation < 6
            ? await SeedProjectionMaintenance(db, fixture.Data.PublicId, session)
            : (ParentId: Guid.Empty, ValueId: Guid.Empty);
        var room = operation == 6 ? await SeedRoomMutation(db) : default;
        object command = operation == 6
            ? RoomMutation(room.Room, room.Destination.Id)
            : ProjectionCommand(operation, db.TenantContext!.TenantId, projection.ParentId);
        bool platform = change is not ("unchanged_tenant" or "admin_tenant");
        await GrantFooterAdministrator(db, userId, platform ? 1 : 2);
        Guid grantId = platform
            ? await db.PlatformUserRoles.Where(row => row.UserId == userId).Select(row => row.Id).SingleAsync()
            : await db.TenantUserRoleGrants.Where(row => row.TenantUser.UserId == userId).Select(row => row.Id).SingleAsync();
        var identity = await fixture.Local.ReadIdentityAsync();
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        int bodies = 0;
        bool readerCompleted = false;
        var physicalOwner = new ProjectionRoomAfterBodyOwner(
            (ISerializableAttemptOwner)new EfCoreUnitOfWork(db), async token =>
        {
            bodies++;
            await AssertProjectionRoomEffects(db, operation, projection, room, true, token);
            switch (change)
            {
                case "stamp":
                    await db.Set<LocalIdentityUser>().Where(row => row.Id == userId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                            Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "binding":
                    await db.UserExternalLogins.Where(row => row.Id == fixture.Local.Binding.ExternalLoginId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey,
                            Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "admin":
                    await db.PlatformUserRoles.Where(row => row.Id == grantId).ExecuteDeleteAsync(token);
                    break;
                case "admin_tenant":
                    await db.TenantUserRoleGrants.Where(row => row.Id == grantId).ExecuteDeleteAsync(token);
                    break;
                case "request":
                    request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
                    break;
                case "source":
                    await db.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                    break;
                case "persisted_parent":
                    if (session)
                        await db.EventSessions.Where(row => row.Id == projection.ParentId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true), token);
                    else
                        await db.Events.Where(row => row.Id == projection.ParentId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true), token);
                    break;
                case "credential_after_reader":
                    services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                    {
                        // Registered after the action's own source readers. Observe real
                        // stored body effects before crossing the actual credential horizon.
                        await AssertProjectionRoomEffects(db, operation, projection, room, true, finalToken);
                        readerCompleted = true;
                        var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                            .ObserveAsync(request.Context.User, finalToken))!;
                        var expired = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
                        fixture.Local.Clock.Advance(expired - fixture.Local.Clock.GetUtcNow());
                    }, _ => { });
                    break;
            }
        });
        bool accepted = change is "unchanged_instance" or "unchanged_tenant";
        async Task Invoke() => await Assert.That(
            await InvokeProjectionRoomOwner(services, command, deadline.Token, physicalOwner)).IsTrue();
        if (accepted) await Invoke();
        else if (change == "persisted_parent") await Assert.ThrowsAsync<AuthorizationException>(Invoke);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(Invoke);
        await Assert.That(bodies).IsEqualTo(1);
        await Assert.That(readerCompleted).IsEqualTo(change == "credential_after_reader");
        await Assert.That(db.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Local.Native.CreateDatabase();
        observer.TenantContext = new AuthorityTenant(PlatformDefaults.DefaultTenantId);
        await AssertProjectionRoomEffects(observer, operation, projection, room, accepted, deadline.Token);
        await Assert.That(await observer.Set<LocalIdentityUser>().AsNoTracking().Where(row => row.Id == userId)
            .Select(row => row.SecurityStamp).SingleAsync(deadline.Token)).IsEqualTo(identity.SecurityStamp);
        await Assert.That(await observer.UserExternalLogins.AsNoTracking()
            .Where(row => row.Id == fixture.Local.Binding.ExternalLoginId)
            .Select(row => row.ProviderKey).SingleAsync(deadline.Token)).IsEqualTo(userId.ToString("D"));
        await Assert.That(platform
            ? await observer.PlatformUserRoles.AnyAsync(row => row.Id == grantId, deadline.Token)
            : await observer.TenantUserRoleGrants.AnyAsync(row => row.Id == grantId && row.RevokedAt == null, deadline.Token)).IsTrue();
        await Assert.That(await observer.SystemSettings.Where(row =>
            row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
            .Select(row => row.Value).SingleAsync(deadline.Token)).IsEqualTo("\"local\"");
        if (operation is 4 or 5)
            await Assert.That(session
                ? await observer.EventSessions.AnyAsync(row => row.Id == projection.ParentId && !row.IsDeleted, deadline.Token)
                : await observer.Events.AnyAsync(row => row.Id == projection.ParentId && !row.IsDeleted, deadline.Token)).IsTrue();
    }

    private static async Task AssertProjectionRoomEffects(
        ExploreDbContext db, int operation, (Guid ParentId, Guid ValueId) projection,
        (Explore.Domain.LocationRoom Room, Explore.Domain.Location Destination) room,
        bool applied, CancellationToken token)
    {
        if (operation == 6)
        {
            var current = await db.LocationRooms.AsNoTracking().SingleAsync(row => row.Id == room.Room.Id, token);
            await Assert.That(current.Name).IsEqualTo(applied ? "Changed room" : room.Room.Name);
            await Assert.That(current.LocationId).IsEqualTo(applied ? room.Destination.Id : room.Room.LocationId);
            if (applied)
                await Assert.That(current.ConcurrencyStamp).IsNotEqualTo(room.Room.ConcurrencyStamp);
            else
                await Assert.That(current.ConcurrencyStamp).IsEqualTo(room.Room.ConcurrencyStamp);
            return;
        }
        bool session = operation % 2 == 1;
        await Assert.That(await ProjectionValue(db, session, projection.ValueId)).IsEqualTo(applied ? "maintenance" : null);
        var dirty = await db.CustomPropertyProjectionDirtyScopes.AsNoTracking()
            .SingleAsync(row => row.ScopeId == projection.ParentId, token);
        await Assert.That(dirty.DrainedAt != null).IsEqualTo(applied && operation < 4);
        string name = session ? IEventSessionCustomPropertyProjectionUpdater.ProjectionName
            : IEventCustomPropertyProjectionUpdater.ProjectionName;
        var status = await db.CustomPropertyProjectionStatuses.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ProjectionName == name
                && row.TenantId == PlatformDefaults.DefaultTenantId, token);
        if (operation < 2 && applied)
        {
            await Assert.That(status).IsNotNull();
            await Assert.That(status!.State).IsEqualTo(CustomPropertyProjectionState.Idle);
            await Assert.That(status.RowsProcessed).IsGreaterThan(0);
            await Assert.That(status.RowsFailed).IsEqualTo(0);
            await Assert.That(status.LastRebuildCompletedAt).IsNotNull();
        }
        else await Assert.That(status).IsNull();
    }

    private static object ProjectionCommand(int operation, Guid tenantId, Guid parentId) => operation switch
    {
        0 => new RebuildEventCustomPropertyProjectionCommand { RequestDto = new() { TenantId = tenantId } },
        1 => new RebuildEventSessionCustomPropertyProjectionCommand { RequestDto = new() { TenantId = tenantId } },
        2 or 3 => new DrainCustomPropertyProjectionDirtyScopesCommand
        {
            RequestDto = new()
            {
                TenantId = tenantId,
                ProjectionName = operation == 3
                    ? IEventSessionCustomPropertyProjectionUpdater.ProjectionName : IEventCustomPropertyProjectionUpdater.ProjectionName
            }
        },
        4 => new RebuildSingleEventCustomPropertyProjectionCommand { EventId = parentId },
        5 => new RebuildSingleEventSessionCustomPropertyProjectionCommand { EventSessionId = parentId },
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static async Task<bool> InvokeProjectionRoomOwner(
        IServiceProvider services, object command, CancellationToken token, ISerializableAttemptOwner? owner = null)
    {
        T Handler<T>() where T : class => owner is null
            ? ActivatorUtilities.CreateInstance<T>(services)
            : ActivatorUtilities.CreateInstance<T>(services, owner);
        return command switch
        {
            RebuildEventCustomPropertyProjectionCommand request =>
                (await Handler<RebuildEventCustomPropertyProjectionCommandHandler>().ExecuteAsync(request, token)).IsSuccess,
            RebuildEventSessionCustomPropertyProjectionCommand request =>
                (await Handler<RebuildEventSessionCustomPropertyProjectionCommandHandler>().ExecuteAsync(request, token)).IsSuccess,
            DrainCustomPropertyProjectionDirtyScopesCommand request =>
                (await Handler<DrainCustomPropertyProjectionDirtyScopesCommandHandler>().ExecuteAsync(request, token)).IsSuccess,
            RebuildSingleEventCustomPropertyProjectionCommand request =>
                (await Handler<RebuildSingleEventCustomPropertyProjectionCommandHandler>().ExecuteAsync(request, token)).IsSuccess,
            RebuildSingleEventSessionCustomPropertyProjectionCommand request =>
                (await Handler<RebuildSingleEventSessionCustomPropertyProjectionCommandHandler>().ExecuteAsync(request, token)).IsSuccess,
            UpdateLocationRoomCommand request =>
                (await Handler<UpdateLocationRoomCommandHandler>().ExecuteAsync(request, token)).IsSuccess,
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
    }

    private sealed class ProjectionRoomAfterBodyOwner(
        ISerializableAttemptOwner inner, Func<CancellationToken, Task> afterBody) : ISerializableAttemptOwner
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(async token =>
            {
                var result = await operation(token);
                await afterBody(token);
                return result;
            }, cancellationToken);
    }
}
