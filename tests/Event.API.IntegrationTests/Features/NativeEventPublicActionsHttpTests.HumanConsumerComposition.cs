using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Application.Features.LocationRooms.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    public async Task NativeMachineRoomBranchDoesNotConstructUnusedHumanPorts()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var (room, target) = await SeedRoomMutation(db);
        await AuthenticateNativeMachine(scope, db, db.TenantContext!.TenantId,
            ExternalApiKeyOwnerType.Tenant, scopes: ExternalApiKeyScopes.AdminTenant);
        var command = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<UpdateLocationRoomCommand, BaseCommandResponse<Guid>>>();

        await Assert.That((await command.ExecuteAsync(RoomMutation(room, target.Id), default)).IsSuccess).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IAdminContext>().UserId).IsNull();
        await Assert.That((await db.LocationRooms.AsNoTracking().SingleAsync(row => row.Id == room.Id)).LocationId)
            .IsEqualTo(target.Id);
    }

    [Test]
    public async Task NativeKeyQueryAndCommandConstructHumanPortsOnOriginalTenantOwner()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ExploreDbContext>();
        await GrantTemplateAdministrator(db, data.OutsiderId);
        var key = ManagedKey(db.TenantContext!.TenantId, data.OwnerId, ExternalApiKeyOwnerType.User);
        db.ExternalApiKeys.Add(key);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var query = services.GetRequiredService<IQueryHandler<GetExternalApiKeyDetailsRequest, ExternalApiKeyListDto?>>();
        var revoke = services.GetRequiredService<ICommandHandler<RevokeExternalApiKeyCommand, bool>>();
        var details = await query.QueryAsync(new() { Id = key.Id }, default);
        await Assert.That(details).IsNotNull();
        await Assert.That(details!.Id).IsEqualTo(key.Id);
        await Assert.That(await revoke.ExecuteAsync(new(key.Id), default)).IsTrue();
        await Assert.That(services.GetRequiredService<ExploreDbContext>()).IsSameReferenceAs(db);
        await Assert.That((await query.QueryAsync(new() { Id = key.Id }, default))!.ExternalApiKeyStatusId)
            .IsEqualTo((int)ExternalApiKeyStatusEnum.Revoked);
    }

    [Test]
    public async Task NativeCustomPropertyCommandConstructsBoundAdministratorDependencies()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantTemplateAdministrator(db, data.OutsiderId);
        Guid definition = await SeedOwnedDefinition(db, data.PublicId, 0);
        var command = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<DeleteCustomPropertyDefinitionCommand, bool>>();

        await Assert.That(await command.ExecuteAsync(new() { Id = definition }, default)).IsTrue();
        await Assert.That(await DefinitionStillActive(db, 0, definition)).IsFalse();
        await Assert.That(scope.ServiceProvider.GetRequiredService<ExploreDbContext>()).IsSameReferenceAs(db);
    }

    [Test]
    public async Task NativeTenantUsageQueryBindsTheExactSelectedReportOwner()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantTemplateAdministrator(db, data.OutsiderId);
        var query = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetExternalApiKeyUsageReportRequest, List<ExternalApiKeyUsageReportDto>>>();

        var result = await query.QueryAsync(new()
        {
            TenantId = db.TenantContext!.TenantId,
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2026, 1, 2)
        }, default);

        await Assert.That(result).IsEmpty();
        await Assert.That(scope.ServiceProvider.GetRequiredService<ExploreDbContext>()).IsSameReferenceAs(db);
    }
}
