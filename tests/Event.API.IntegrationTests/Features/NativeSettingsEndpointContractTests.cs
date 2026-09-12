using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.Settings;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class NativeSettingsEndpointContractTests
{
    [Test]
    public async Task EmailCapability_UsesTwoNativeCommandsIncludingConfirmationIssuance()
    {
        Type[] expected =
        [
            typeof(ICommandHandler<PreviewEmailDeliveryDisableCommand, BaseCommandResponse<EmailDeliveryDisablePreviewDto>>),
            typeof(ICommandHandler<DisableEmailDeliveryCommand, BaseCommandResponse<Guid>>)
        ];
        var tenantPorts = typeof(EmailDeliverySettingsController).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        await Assert.That(tenantPorts.SequenceEqual(expected)).IsTrue();
        var instancePorts = typeof(InstanceMessagingSettingsController).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        await Assert.That(expected.All(instancePorts.Contains)).IsTrue();
    }

    [Test]
    public async Task EmailCapability_PreservesPrivateConfirmationRoutesAndPolicies()
    {
        var controller = typeof(EmailDeliverySettingsController);
        await Assert.That(controller.GetCustomAttribute<RouteAttribute>()!.Template).IsEqualTo("api/settings");
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<TagsAttribute>()!.Tags.SequenceEqual(["Settings"])).IsTrue();
        (string Method, string Template, string Name, int[] Statuses)[] routes =
        [
            (nameof(EmailDeliverySettingsController.PreviewEmailDeliveryDisable), "email-delivery/disable-preview",
                RouteNames.PreviewTenantSmtpDisable, [200, 400, 401, 403, 404]),
            (nameof(EmailDeliverySettingsController.DisableEmailDelivery), "email-delivery/disable",
                RouteNames.DisableTenantSmtp, [200, 400, 401, 403, 404, 409])
        ];
        await Assert.That(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length)
            .IsEqualTo(routes.Length);
        foreach (var route in routes)
        {
            var method = controller.GetMethod(route.Method)!;
            var attribute = method.GetCustomAttribute<HttpMethodAttribute>()!;
            await Assert.That(attribute.HttpMethods.Single()).IsEqualTo("POST");
            await Assert.That(attribute.Template).IsEqualTo(route.Template);
            await Assert.That(attribute.Name).IsEqualTo(route.Name);
            await Assert.That(method.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
            await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
            await Assert.That(method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName)
                .IsEqualTo(Explore.API.Extensions.RateLimitingExtensions.WritePolicy);
            var statuses = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Select(response => response.StatusCode).Order().ToArray();
            await Assert.That(statuses.SequenceEqual(route.Statuses)).IsTrue();
        }
    }

    [Test]
    public async Task InstanceCapability_DeclaresOnlyNativeSettingsAuthorityAndHalPorts()
    {
        Type[] expected =
        [
            typeof(IQueryHandler<ResolveSettingGroupQuery, SettingGroupResponseDto>),
            typeof(ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<LockSettingCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<UnlockSettingCommand, BaseCommandResponse<Guid>>),
            typeof(IAdminContext),
            typeof(IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto>)
        ];
        var actual = typeof(InstanceAtprotoSettingsController).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task InstanceCapability_PreservesRoutesNamesAndWriteRateLimiting()
    {
        var controller = typeof(InstanceAtprotoSettingsController);
        await Assert.That(controller.GetCustomAttribute<RouteAttribute>()!.Template).IsEqualTo("api/settings");
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<TagsAttribute>()!.Tags.SequenceEqual(["Settings"])).IsTrue();
        (string Method, string Verb, string Template, string Name)[] routes =
        [
            (nameof(InstanceAtprotoSettingsController.GetInstanceSettings), "GET", "instance/atproto-federation", RouteNames.GetInstanceAtprotoFederationSettings),
            (nameof(InstanceAtprotoSettingsController.UpdateInstanceSetting), "PUT", "instance/atproto-federation/{key}", RouteNames.UpdateInstanceAtprotoFederationSetting),
            (nameof(InstanceAtprotoSettingsController.ResetInstanceSetting), "DELETE", "instance/atproto-federation/{key}", RouteNames.ResetInstanceAtprotoFederationSetting),
            (nameof(InstanceAtprotoSettingsController.LockInstanceSetting), "POST", "instance/atproto-federation/{key}/lock", RouteNames.LockInstanceAtprotoFederationSetting),
            (nameof(InstanceAtprotoSettingsController.UnlockInstanceSetting), "DELETE", "instance/atproto-federation/{key}/lock", RouteNames.UnlockInstanceAtprotoFederationSetting)
        ];
        await Assert.That(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length)
            .IsEqualTo(routes.Length);
        foreach (var route in routes)
        {
            var method = controller.GetMethod(route.Method)!;
            var attribute = method.GetCustomAttribute<HttpMethodAttribute>()!;
            await Assert.That(attribute.HttpMethods.Single()).IsEqualTo(route.Verb);
            await Assert.That(attribute.Template).IsEqualTo(route.Template);
            await Assert.That(attribute.Name).IsEqualTo(route.Name);
            await Assert.That(method.GetCustomAttribute<EndpointClassificationAttribute>()).IsNotNull();
            int[] expectedStatuses = route.Verb == "GET" ? [200, 401, 403] : [200, 400, 401, 403];
            var statuses = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Select(response => response.StatusCode).Order().ToArray();
            await Assert.That(statuses.SequenceEqual(expectedStatuses)).IsTrue();
            if (route.Verb != "GET")
                await Assert.That(method.GetCustomAttribute<EnableRateLimitingAttribute>()).IsNotNull();
        }
    }

    [Test]
    public async Task TenantCapability_DeclaresOnlyNativeSettingsAndHalPorts()
    {
        Type[] expected =
        [
            typeof(IQueryHandler<ResolveSettingGroupQuery, SettingGroupResponseDto>),
            typeof(ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto>),
            typeof(ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<LockSettingCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<UnlockSettingCommand, BaseCommandResponse<Guid>>),
            typeof(IResourceAssembler<SettingGroupResponseDto, SettingGroupResponseDto>)
        ];
        var actual = typeof(TenantSettingsController).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task TenantCapability_PreservesRoutesNamesAndPrivateReadMetadata()
    {
        var controller = typeof(TenantSettingsController);
        await Assert.That(controller.GetCustomAttribute<RouteAttribute>()!.Template).IsEqualTo("api/settings");
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<EndpointClassificationAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<TagsAttribute>()!.Tags.SequenceEqual(["Settings"])).IsTrue();
        await Assert.That(controller.GetMethod(nameof(TenantSettingsController.GetTenantSettings))!
            .GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();

        (string Method, string Verb, string Template, string Name)[] routes =
        [
            (nameof(TenantSettingsController.GetTenantSettings), "GET", "tenant/{category}", RouteNames.GetTenantScopedSettings),
            (nameof(TenantSettingsController.UpdateTenantSettingsBatch), "PUT", "tenant/{category}", RouteNames.UpdateTenantSettingsBatch),
            (nameof(TenantSettingsController.UpdateTenantSetting), "PUT", "tenant/keys/{key}", RouteNames.UpdateTenantSetting),
            (nameof(TenantSettingsController.ResetTenantSetting), "DELETE", "tenant/keys/{key}", RouteNames.ResetTenantSetting),
            (nameof(TenantSettingsController.LockTenantSetting), "POST", "tenant/keys/{key}/lock", RouteNames.LockTenantSetting),
            (nameof(TenantSettingsController.UnlockTenantSetting), "DELETE", "tenant/keys/{key}/lock", RouteNames.UnlockTenantSetting)
        ];
        await Assert.That(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length)
            .IsEqualTo(routes.Length);
        foreach (var route in routes)
        {
            var method = controller.GetMethod(route.Method)!;
            var attribute = method.GetCustomAttribute<HttpMethodAttribute>()!;
            await Assert.That(attribute.HttpMethods.Single()).IsEqualTo(route.Verb);
            await Assert.That(attribute.Template).IsEqualTo(route.Template);
            await Assert.That(attribute.Name).IsEqualTo(route.Name);
            int[] expectedStatuses = route.Verb == "GET"
                ? [200, 401, 403]
                : [200, 400, 401, 403, 409];
            var statuses = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Select(response => response.StatusCode).Order().ToArray();
            await Assert.That(statuses.SequenceEqual(expectedStatuses)).IsTrue();
        }
    }

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
