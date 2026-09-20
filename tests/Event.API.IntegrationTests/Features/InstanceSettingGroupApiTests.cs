using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class InstanceSettingGroupApiTests
{
    [Test]
    public async Task ResetInstanceSetting_PreservesScopeAndRejectsUnknownKeys()
    {
        var ports = new SettingsPorts();
        ports.Reset.ExecuteAsync(Arg.Any<ResetSettingCommand>(), Arg.Any<CancellationToken>())
            .Returns(SuccessfulResponse());
        var controller = CreateController(ports, Substitute.For<IAdminContext>());
        var key = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;

        var result = await controller.ResetInstanceSetting(key, CancellationToken.None);
        var unknown = await controller.ResetInstanceSetting("outside.instance.capability", CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
        await Assert.That((unknown.Result as ObjectResult)!.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        await ports.Reset.Received(1).ExecuteAsync(
            Arg.Is<ResetSettingCommand>(command => command.Key == key && command.Scope == SettingScope.Instance),
            Arg.Any<CancellationToken>());
        await ports.Reset.DidNotReceive().ExecuteAsync(
            Arg.Is<ResetSettingCommand>(command => command.Key == "outside.instance.capability"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetInstanceSettings_WhenInstanceAdmin_ReturnsAuthorizedHalResource()
    {
        var ports = new SettingsPorts();
        var adminContext = Substitute.For<IAdminContext>();
        var assembler = Substitute.For<IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto>>();
        var settings = CreateAtprotoSettings();
        var resource = new HalResource<SettingGroupResponseDto>(settings);
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        ports.Query.QueryAsync(Arg.Any<ResolveSettingGroupQuery>(), Arg.Any<CancellationToken>()).Returns(settings);
        assembler.ToResource(settings, Arg.Any<HttpContext>()).Returns(resource);
        var controller = CreateController(ports, adminContext, assembler);

        var result = await controller.GetInstanceSettings(CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(resource);
        await ports.Query.Received(1).QueryAsync(
            Arg.Is<ResolveSettingGroupQuery>(query =>
                query.Category == AtprotoFederationSettingDefinitions.Category
                && query.Scope == SettingScope.Instance
                && query.IncludedKeys != null
                && query.IncludedKeys.SequenceEqual(AtprotoFederationSettingDefinitions.AdministratorKeys)),
            Arg.Any<CancellationToken>());
        await assembler.Received(1).ToResource(settings, Arg.Any<HttpContext>());
    }

    [Test]
    public async Task GetInstanceSettings_WhenNotInstanceAdmin_ReturnsForbiddenWithoutReadingSettings()
    {
        var ports = new SettingsPorts();
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        var controller = CreateController(ports, adminContext);

        var result = await controller.GetInstanceSettings(CancellationToken.None);

        var forbidden = result.Result as ObjectResult;
        await Assert.That(forbidden).IsNotNull();
        await Assert.That(forbidden!.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await ports.Query.DidNotReceive().QueryAsync(Arg.Any<ResolveSettingGroupQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateInstanceSetting_DispatchesNativeCommandAtInstanceScope()
    {
        var ports = new SettingsPorts();
        ports.Update.ExecuteAsync(Arg.Any<UpdateSettingCommand>(), Arg.Any<CancellationToken>())
            .Returns(SuccessfulResponse());
        var controller = CreateController(ports, Substitute.For<IAdminContext>());

        var result = await controller.UpdateInstanceSetting(
            GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
            new UpdateSettingValueDto { Value = "true" },
            CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
        await ports.Update.Received(1).ExecuteAsync(
            Arg.Is<UpdateSettingCommand>(command =>
                command.Key == GovernanceSettingKeys.Federation.AtprotoEventsEnabled
                && command.Value == "true"
                && command.Scope == SettingScope.Instance),
            Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments("federation.atproto_events_backfill_enabled", "true")]
    [Arguments("federation.atproto_events_backfill_mode", "full")]
    public async Task BackfillSettingUpdate_UsesNativeInstanceCommand(string key, string value)
    {
        var ports = new SettingsPorts();
        ports.Update.ExecuteAsync(Arg.Any<UpdateSettingCommand>(), Arg.Any<CancellationToken>())
            .Returns(SuccessfulResponse());
        var controller = CreateController(ports, Substitute.For<IAdminContext>());

        var result = await controller.UpdateInstanceSetting(
            key,
            new UpdateSettingValueDto { Value = value },
            CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
        await ports.Update.Received(1).ExecuteAsync(
            Arg.Is<UpdateSettingCommand>(command =>
                command.Key == key
                && command.Value == value
                && command.Scope == SettingScope.Instance),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LockAndUnlockInstanceSetting_DispatchNativeCommandsAtInstanceScope()
    {
        var ports = new SettingsPorts();
        ports.Lock.ExecuteAsync(Arg.Any<LockSettingCommand>(), Arg.Any<CancellationToken>()).Returns(SuccessfulResponse());
        ports.Unlock.ExecuteAsync(Arg.Any<UnlockSettingCommand>(), Arg.Any<CancellationToken>()).Returns(SuccessfulResponse());
        var controller = CreateController(ports, Substitute.For<IAdminContext>());
        var key = GovernanceSettingKeys.Federation.AtprotoEventValidationProfile;

        var lockResult = await controller.LockInstanceSetting(key, CancellationToken.None);
        var unlockResult = await controller.UnlockInstanceSetting(key, CancellationToken.None);

        await Assert.That(lockResult.Result).IsTypeOf<OkObjectResult>();
        await Assert.That(unlockResult.Result).IsTypeOf<OkObjectResult>();
        await ports.Lock.Received(1).ExecuteAsync(
            Arg.Is<LockSettingCommand>(command => command.Key == key && command.Scope == SettingScope.Instance),
            Arg.Any<CancellationToken>());
        await ports.Unlock.Received(1).ExecuteAsync(
            Arg.Is<UnlockSettingCommand>(command => command.Key == key && command.Scope == SettingScope.Instance),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Mutations_WhenKeyIsOutsideAtprotoAdministratorAllowlist_ReturnNotFoundWithoutDispatch()
    {
        var ports = new SettingsPorts();
        var controller = CreateController(ports, Substitute.For<IAdminContext>());
        const string unrelatedKey = "storage.provider";

        var update = await controller.UpdateInstanceSetting(
            unrelatedKey,
            new UpdateSettingValueDto { Value = "s3" },
            CancellationToken.None);
        var lockResult = await controller.LockInstanceSetting(unrelatedKey, CancellationToken.None);
        var unlockResult = await controller.UnlockInstanceSetting(unrelatedKey, CancellationToken.None);

        await Assert.That((update.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        await Assert.That((lockResult.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        await Assert.That((unlockResult.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        await ports.Update.DidNotReceive().ExecuteAsync(Arg.Any<UpdateSettingCommand>(), Arg.Any<CancellationToken>());
        await ports.Lock.DidNotReceive().ExecuteAsync(Arg.Any<LockSettingCommand>(), Arg.Any<CancellationToken>());
        await ports.Unlock.DidNotReceive().ExecuteAsync(Arg.Any<UnlockSettingCommand>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AtprotoInstanceSettingsLinks_ExposeOnlyCurrentLockTransition()
    {
        var policy = new AtprotoInstanceSettingGroupLinkPolicy();

        var links = policy.GetLinks(CreateAtprotoSettings(), user: null).ToArray();

        await Assert.That(links.Single(link => link.Rel == LinkRelations.Self).RouteName)
            .IsEqualTo(RouteNames.GetInstanceAtprotoFederationSettings);
        await Assert.That(links.Any(link => link.Rel == $"update-{GovernanceSettingKeys.Federation.AtprotoEventsEnabled}"))
            .IsTrue();
        await Assert.That(links.Any(link => link.Rel == $"unlock-{GovernanceSettingKeys.Federation.AtprotoEventsEnabled}"))
            .IsTrue();
        await Assert.That(links.Any(link => link.Rel == $"lock-{GovernanceSettingKeys.Federation.AtprotoEventsEnabled}"))
            .IsFalse();
        await Assert.That(links.Any(link => link.Rel == $"update-{GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled}"))
            .IsTrue();
        await Assert.That(links.Any(link => link.Rel == $"update-{GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode}"))
            .IsTrue();
        await Assert.That(links.All(link => link.PermissionResourceKind == ResourceKinds.InstanceSetting))
            .IsTrue();
    }

    private static InstanceAtprotoSettingsController CreateController(
        SettingsPorts ports,
        IAdminContext adminContext,
        IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto>? assembler = null) =>
        new(
            ports.Query,
            ports.Update,
            ports.Reset,
            ports.Lock,
            ports.Unlock,
            adminContext,
            assembler ?? Substitute.For<IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private sealed class SettingsPorts
    {
        public IQueryHandler<ResolveSettingGroupQuery, SettingGroupResponseDto> Query { get; } =
            Substitute.For<IQueryHandler<ResolveSettingGroupQuery, SettingGroupResponseDto>>();
        public ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>> Update { get; } =
            Substitute.For<ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>>();
        public ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>> Reset { get; } =
            Substitute.For<ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>>>();
        public ICommandHandler<LockSettingCommand, BaseCommandResponse<Guid>> Lock { get; } =
            Substitute.For<ICommandHandler<LockSettingCommand, BaseCommandResponse<Guid>>>();
        public ICommandHandler<UnlockSettingCommand, BaseCommandResponse<Guid>> Unlock { get; } =
            Substitute.For<ICommandHandler<UnlockSettingCommand, BaseCommandResponse<Guid>>>();
    }

    private static SettingGroupResponseDto CreateAtprotoSettings() =>
        new()
        {
            Category = AtprotoFederationSettingDefinitions.Category,
            Settings =
            [
                new EffectiveSettingDto
                {
                    Key = GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
                    Value = "true",
                    SettingValueTypeCode = "boolean",
                    SettingValueTypeName = "Boolean",
                    CanEdit = true,
                    IsLockable = true,
                    IsLocked = true
                },
                new EffectiveSettingDto
                {
                    Key = GovernanceSettingKeys.Federation.AtprotoEventValidationProfile,
                    Value = "\"community_lexicon\"",
                    SettingValueTypeCode = "string",
                    SettingValueTypeName = "String",
                    CanEdit = true,
                    IsLockable = true,
                    IsLocked = false
                },
                new EffectiveSettingDto
                {
                    Key = GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled,
                    Value = "false",
                    SettingValueTypeCode = "boolean",
                    SettingValueTypeName = "Boolean",
                    CanEdit = true,
                    IsLockable = true,
                    IsLocked = false
                },
                new EffectiveSettingDto
                {
                    Key = GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode,
                    Value = "\"downtime_only\"",
                    SettingValueTypeCode = "string",
                    SettingValueTypeName = "String",
                    CanEdit = true,
                    IsLockable = true,
                    IsLocked = false
                }
            ]
        };

    private static BaseCommandResponse<Guid> SuccessfulResponse() =>
        BaseCommandResponse.Success(Guid.Empty);
}
