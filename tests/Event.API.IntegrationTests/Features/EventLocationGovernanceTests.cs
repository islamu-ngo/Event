using System.Reflection;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventLocationGovernanceTests
{
    [Test]
    public async Task TenantWrite_UsesExistingSettingsCommandAtTenantScope()
    {
        var handler = Substitute.For<ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>>();
        handler.ExecuteAsync(Arg.Any<UpdateSettingCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(Guid.CreateVersion7()));
        var controller = CreateController(handler);

        var result = await controller.UpdateTenantSetting(
            GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations,
            new UpdateSettingValueDto { Value = "false" },
            Substitute.For<Microsoft.AspNetCore.OutputCaching.IOutputCacheStore>(),
            CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
        await handler.Received(1).ExecuteAsync(
            Arg.Is<UpdateSettingCommand>(command =>
                command.Key == GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations
                && command.Value == "false"
                && command.Scope == SettingScope.Tenant),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SettingsController_RemainsAuthenticatedForLocationGovernanceWrites()
    {
        AuthorizeAttribute? authorize = typeof(SettingsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        await Assert.That(authorize).IsNotNull();
        await Assert.That(typeof(SettingsController).GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
    }

    private static SettingsController CreateController(ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>> handler) => new(
        Substitute.For<IMediator>(),
        Substitute.For<IQueryHandler<ResolveSettingGroupQuery, SettingGroupResponseDto>>(),
        handler,
        Substitute.For<ICommandHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto>>(),
        Substitute.For<ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>>>(),
        Substitute.For<ICommandHandler<LockSettingCommand, BaseCommandResponse<Guid>>>(),
        Substitute.For<ICommandHandler<UnlockSettingCommand, BaseCommandResponse<Guid>>>(),
        Substitute.For<IAdminContext>(),
        Substitute.For<IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto>>())
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        }
    };
}
