using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    public async Task OrdinaryNativeSelfPreferenceDoesNotRequireAdministratorAndCannotWriteAnotherUser()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<ExploreDbContext>();
        await context.PlatformUserRoles.Where(row => row.UserId == data.OwnerId).ExecuteDeleteAsync();
        await context.TenantUserRoleGrants.Where(row => row.TenantUser.UserId == data.OwnerId).ExecuteDeleteAsync();
        var command = services.GetRequiredService<ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>>();
        var result = await command.ExecuteAsync(new()
        {
            Key = "event_list.page_size",
            Value = "12",
            Scope = SettingScope.User
        }, default);

        await Assert.That(result.IsSuccess).IsTrue();
        var preferences = services.GetRequiredService<IUserPreferenceRepository>();
        await Assert.That((await preferences.GetByUserAndKey(
            PlatformDefaults.DefaultTenantId, data.OwnerId, "event_list.page_size"))!.Value).IsEqualTo("12");
        await Assert.That(await preferences.GetByUserAndKey(
            PlatformDefaults.DefaultTenantId, data.OutsiderId, "event_list.page_size")).IsNull();
    }
}
