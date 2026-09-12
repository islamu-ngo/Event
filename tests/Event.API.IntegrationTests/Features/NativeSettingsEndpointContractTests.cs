using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Event.Api.IntegrationTests.Features;

public sealed class NativeSettingsEndpointContractTests
{
    [Test]
    public async Task UserCapability_DeclaresOnlyItsFourClosedNativePorts()
    {
        Type[] expected =
        [
            typeof(IQueryHandler<ResolveSettingGroupQuery, SettingGroupResponseDto>),
            typeof(ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto>),
            typeof(ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>>)
        ];
        var actual = typeof(UserSettingsController).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task UserCapability_PreservesRoutesNamesAndAuthenticationMetadata()
    {
        var controller = typeof(UserSettingsController);
        await Assert.That(controller.GetCustomAttribute<RouteAttribute>()!.Template).IsEqualTo("api/settings");
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<EndpointClassificationAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<TagsAttribute>()!.Tags.SequenceEqual(["Settings"])).IsTrue();

        (string Method, string Verb, string Template, string Name)[] routes =
        [
            (nameof(UserSettingsController.GetUserSettings), "GET", "user/{category}", RouteNames.GetUserSettings),
            (nameof(UserSettingsController.UpdateUserSettingsBatch), "PUT", "user/{category}", RouteNames.UpdateUserSettingsBatch),
            (nameof(UserSettingsController.UpdateUserSetting), "PUT", "user/keys/{key}", RouteNames.UpdateUserSetting),
            (nameof(UserSettingsController.ResetUserSetting), "DELETE", "user/keys/{key}", RouteNames.ResetUserSetting)
        ];
        await Assert.That(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length)
            .IsEqualTo(routes.Length);
        foreach (var route in routes)
        {
            var attribute = controller.GetMethod(route.Method)!.GetCustomAttribute<HttpMethodAttribute>()!;
            await Assert.That(attribute.HttpMethods.Single()).IsEqualTo(route.Verb);
            await Assert.That(attribute.Template).IsEqualTo(route.Template);
            await Assert.That(attribute.Name).IsEqualTo(route.Name);
        }
    }
}
