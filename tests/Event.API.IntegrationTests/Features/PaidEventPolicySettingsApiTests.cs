using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.PaidEventPolicies.Requests.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[Category("Phase43Ticketing")]
public sealed class PaidEventPolicySettingsApiTests
{
    [Test]
    public async Task Controllers_UseProtectedClassificationsAndCanonicalRoutes()
    {
        await AssertController(typeof(InstancePaidEventPolicySettingsController), EndpointClass.Admin, "api/instance/settings/paid-event-policy");
        await AssertController(typeof(TenantPaidEventPolicySettingsController), EndpointClass.Authenticated, "api/tenants/{tenantId:guid}/settings/paid-event-policy");

        await AssertAction(typeof(InstancePaidEventPolicySettingsController), nameof(InstancePaidEventPolicySettingsController.Get), RouteNames.GetInstancePaidEventPolicySettings, HttpMethods.Get, typeof(HalResource<PaidEventPolicyDto>), expectWritePolicy: false);
        await AssertAction(typeof(InstancePaidEventPolicySettingsController), nameof(InstancePaidEventPolicySettingsController.Update), RouteNames.UpdateInstancePaidEventPolicySettings, HttpMethods.Put, typeof(BaseCommandResponse<Guid>), expectWritePolicy: true);
        await AssertAction(typeof(TenantPaidEventPolicySettingsController), nameof(TenantPaidEventPolicySettingsController.Get), RouteNames.GetTenantPaidEventPolicySettings, HttpMethods.Get, typeof(HalResource<TenantPaidEventPolicyConfigurationDto>), expectWritePolicy: false);
        await AssertAction(typeof(TenantPaidEventPolicySettingsController), nameof(TenantPaidEventPolicySettingsController.Update), RouteNames.UpdateTenantPaidEventPolicySettings, HttpMethods.Put, typeof(BaseCommandResponse<Guid>), expectWritePolicy: true);
    }

    [Test]
    public async Task TenantUpdate_UsesRouteTenantIdRatherThanBodyOwnedIdentity()
    {
        var tenantId = Guid.CreateVersion7();
        var body = CreateRevisionDto();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ReviseTenantPaidEventPolicyCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(Guid.CreateVersion7()));
        var controller = new TenantPaidEventPolicySettingsController(
            mediator,
            Substitute.For<IResourceAssembler<TenantPaidEventPolicyConfigurationDto, TenantPaidEventPolicyConfigurationDto>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Update(tenantId, body, CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
        await mediator.Received(1).Send(
            Arg.Is<ReviseTenantPaidEventPolicyCommand>(command => command.TenantId == tenantId && command.Policy == body),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LinkPolicies_UsePaidPolicySettingPermissionMetadata()
    {
        var tenantId = Guid.CreateVersion7();

        LinkDefinition[] instanceLinks = new InstancePaidEventPolicyLinkPolicy().GetLinks(CreatePolicyDto(), null).ToArray();
        LinkDefinition instanceSelf = instanceLinks.Single(link => link.Rel == LinkRelations.Self);
        LinkDefinition instanceEdit = instanceLinks.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(instanceSelf.RouteName).IsEqualTo(RouteNames.GetInstancePaidEventPolicySettings);
        await Assert.That(instanceSelf.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(instanceEdit.RouteName).IsEqualTo(RouteNames.UpdateInstancePaidEventPolicySettings);
        await Assert.That(instanceEdit.Method).IsEqualTo(HttpMethods.Put);
        await Assert.That(instanceEdit.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
        await Assert.That(instanceLinks.All(link => link.PermissionResourceKind == ResourceKinds.InstanceSetting)).IsTrue();
        await Assert.That(instanceLinks.All(link => link.PermissionResourceId == "paid-event-policy")).IsTrue();

        LinkDefinition[] tenantLinks = new TenantPaidEventPolicyConfigurationLinkPolicy().GetLinks(CreateConfigurationDto(tenantId), null).ToArray();
        LinkDefinition tenantSelf = tenantLinks.Single(link => link.Rel == LinkRelations.Self);
        LinkDefinition tenantEdit = tenantLinks.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(tenantSelf.RouteName).IsEqualTo(RouteNames.GetTenantPaidEventPolicySettings);
        await Assert.That(tenantSelf.PermissionAction).IsEqualTo(AuthorizationActions.TenantSettings.View);
        await Assert.That(tenantEdit.RouteName).IsEqualTo(RouteNames.UpdateTenantPaidEventPolicySettings);
        await Assert.That(tenantEdit.Method).IsEqualTo(HttpMethods.Put);
        await Assert.That(tenantEdit.PermissionAction).IsEqualTo(AuthorizationActions.TenantSettings.Update);
        await Assert.That(tenantLinks.All(link => link.PermissionResourceKind == ResourceKinds.TenantSetting)).IsTrue();
        await Assert.That(tenantLinks.All(link => link.PermissionResourceId == $"{tenantId}:paid-event-policy")).IsTrue();
        await Assert.That(tenantLinks.All(link => link.PermissionScope?.TenantId == tenantId.ToString())).IsTrue();
        await Assert.That(tenantLinks.All(link =>
            Equals(link.PermissionFacts, new TenantSettingAuthorizationFacts(tenantId)))).IsTrue();
    }

    private static async Task AssertController(Type controller, EndpointClass endpointClass, string routeTemplate)
    {
        await Assert.That(controller.IsDefined(typeof(AuthorizeAttribute), true)).IsTrue();
        await Assert.That(controller.GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(endpointClass);
        await Assert.That(controller.GetCustomAttribute<RouteAttribute>()?.Template).IsEqualTo(routeTemplate);
    }

    private static async Task AssertAction(Type controller, string actionName, string routeName, string method, Type successType, bool expectWritePolicy)
    {
        MethodInfo action = controller.GetMethod(actionName)
            ?? throw new InvalidOperationException($"Action {actionName} was not found.");
        HttpMethodAttribute route = action.GetCustomAttributes<HttpMethodAttribute>().Single();
        ProducesResponseTypeAttribute success = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == StatusCodes.Status200OK);

        await Assert.That(route.Template).IsEqualTo(string.Empty);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(route.HttpMethods.Single()).IsEqualTo(method);
        await Assert.That(success.Type).IsEqualTo(successType);
        await Assert.That(action.IsDefined(typeof(PrivateNoStoreAttribute), inherit: true)).IsTrue();

        var rateLimit = action.GetCustomAttribute<EnableRateLimitingAttribute>();
        if (expectWritePolicy)
        {
            await Assert.That(rateLimit?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
            await AssertProducesProblem(action, StatusCodes.Status429TooManyRequests);
        }
        else
        {
            await Assert.That(rateLimit).IsNull();
        }

        foreach (int statusCode in new[]
                 {
                     StatusCodes.Status400BadRequest,
                     StatusCodes.Status401Unauthorized,
                     StatusCodes.Status403Forbidden,
                     StatusCodes.Status404NotFound
                 })
        {
            await AssertProducesProblem(action, statusCode);
        }
    }

    private static async Task AssertProducesProblem(MethodInfo action, int statusCode)
    {
        ProducesResponseTypeAttribute response = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == statusCode);
        await Assert.That(response.Type).IsEqualTo(typeof(ProblemDetails));
    }

    private static TenantPaidEventPolicyConfigurationDto CreateConfigurationDto(Guid tenantId)
    {
        PaidEventPolicyDto instance = CreatePolicyDto();
        return new TenantPaidEventPolicyConfigurationDto
        {
            TenantId = tenantId,
            ActiveInstanceCeiling = instance,
            EffectivePolicy = instance
        };
    }

    private static PaidEventPolicyDto CreatePolicyDto() => new()
    {
        Id = Guid.CreateVersion7(),
        IsActive = true,
        AllowedOrganizerKindIds = [(int)ActorTypeEnum.Organization],
        AllowedCurrencyCodes = ["USD"],
        RefundProtectionIds = [1]
    };

    private static RevisePaidEventPolicyDto CreateRevisionDto() => new()
    {
        IsPaymentsEnabled = true,
        AllowedOrganizerKindIds = [(int)ActorTypeEnum.Organization],
        AllowedCurrencyCodes = ["USD"],
        RefundProtectionIds = [1]
    };
}
