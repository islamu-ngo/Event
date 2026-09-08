using System.Reflection;
using System.Security.Claims;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class ReportingIntakeSettingsApiTests
{
    private readonly Guid _tenantId = Guid.CreateVersion7();
    private readonly Guid _actorUserId = Guid.CreateVersion7();

    [Test]
    public async Task Controller_UsesCurrentTenantAuthenticatedPrivateContract()
    {
        Type controller = typeof(TenantReportingIntakeSettingsController);
        RouteAttribute route = controller.GetCustomAttribute<RouteAttribute>()!;

        await Assert.That(route.Template).IsEqualTo("api/tenant/settings/reporting-intake");
        await Assert.That(controller.IsDefined(typeof(AuthorizeAttribute), inherit: true)).IsTrue();
        await Assert.That(controller.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Authenticated);

        await AssertAction(
            nameof(TenantReportingIntakeSettingsController.Get),
            HttpMethods.Get,
            RouteNames.GetTenantReportingIntakePolicy,
            typeof(HalResource<TenantReportingIntakePolicyDto>),
            RequestTimeoutExtensions.LookupPolicy,
            expectWriteLimiter: false,
            expectedProblemStatuses:
            [
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status403Forbidden
            ]);
        await AssertAction(
            nameof(TenantReportingIntakeSettingsController.Update),
            HttpMethods.Put,
            RouteNames.UpdateTenantReportingIntakePolicy,
            typeof(BaseCommandResponse<Guid>),
            RequestTimeoutExtensions.DefaultPolicy,
            expectWriteLimiter: true,
            expectedProblemStatuses:
            [
                StatusCodes.Status400BadRequest,
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status403Forbidden,
                StatusCodes.Status409Conflict,
                StatusCodes.Status429TooManyRequests
            ]);
    }

    [Test]
    public async Task UpdateBody_ContainsOnlyCallerOwnedEnabledState()
    {
        PropertyInfo[] properties = typeof(UpdateTenantReportingIntakePolicyDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        await Assert.That(properties.Select(property => property.Name)).IsEquivalentTo([
            nameof(UpdateTenantReportingIntakePolicyDto.Enabled)
        ]);
        await Assert.That(properties.Single().PropertyType).IsEqualTo(typeof(bool));
    }

    [Test]
    public async Task Get_DerivesTenantFromAmbientContextAndAssemblesHal()
    {
        var mediator = Substitute.For<IMediator>();
        var assembler = Substitute.For<IResourceAssembler<TenantReportingIntakePolicyDto, TenantReportingIntakePolicyDto>>();
        TenantReportingIntakePolicyDto policy = Policy();
        HalResource<TenantReportingIntakePolicyDto> resource = new(policy);
        mediator.Send(Arg.Any<GetTenantReportingIntakePolicyQuery>(), Arg.Any<CancellationToken>())
            .Returns(policy);
        assembler.ToResource(policy, Arg.Any<HttpContext>()).Returns(resource);
        TenantReportingIntakeSettingsController controller = CreateController(mediator, assembler);

        var result = await controller.Get(CancellationToken.None);

        ObjectResult response = (ObjectResult)result.Result!;
        await Assert.That(response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(response.Value).IsSameReferenceAs(resource);
        await mediator.Received(1).Send(
            Arg.Is<GetTenantReportingIntakePolicyQuery>(query => query.TenantId == _tenantId),
            CancellationToken.None);
        await assembler.Received(1).ToResource(policy, controller.HttpContext);
    }

    [Test]
    public async Task Update_DerivesTenantAndActorOnServerAndDispatchesOnlyEnabledState()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpdateTenantReportingIntakePolicyCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(_tenantId));
        TenantReportingIntakeSettingsController controller = CreateController(
            mediator,
            Substitute.For<IResourceAssembler<TenantReportingIntakePolicyDto, TenantReportingIntakePolicyDto>>());
        var body = new UpdateTenantReportingIntakePolicyDto { Enabled = false };

        var result = await controller.Update(body, CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
        await mediator.Received(1).Send(
            Arg.Is<UpdateTenantReportingIntakePolicyCommand>(command =>
                command.TenantId == _tenantId
                && command.ActorUserId == _actorUserId
                && command.Policy == body
                && !command.Policy.Enabled),
            CancellationToken.None);
    }

    [Test]
    [Arguments("event_reporting_intake_policy_invalid", StatusCodes.Status400BadRequest)]
    [Arguments("tenant_context_mismatch", StatusCodes.Status403Forbidden)]
    [Arguments("event_reporting_policy_locked", StatusCodes.Status409Conflict)]
    [Arguments("event_reporting_intake_unsafe_publication_policy", StatusCodes.Status409Conflict)]
    public async Task Update_UsesImmutableRfc7807FailurePolicy(string failureCode, int expectedStatus)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpdateTenantReportingIntakePolicyCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                failureCode,
                "Reporting intake policy was not changed.",
                [failureCode]));
        TenantReportingIntakeSettingsController controller = CreateController(
            mediator,
            Substitute.For<IResourceAssembler<TenantReportingIntakePolicyDto, TenantReportingIntakePolicyDto>>());

        var result = await controller.Update(
            new UpdateTenantReportingIntakePolicyDto { Enabled = false },
            CancellationToken.None);

        ObjectResult problemResult = (ObjectResult)result.Result!;
        await Assert.That(problemResult.StatusCode).IsEqualTo(expectedStatus);
        await Assert.That(problemResult.Value).IsAssignableTo<ProblemDetails>();
        ProblemDetails problem = (ProblemDetails)problemResult.Value!;
        await Assert.That(problem.Status).IsEqualTo(expectedStatus);
        if (expectedStatus is StatusCodes.Status400BadRequest or StatusCodes.Status409Conflict)
        {
            await Assert.That(problem.Extensions["code"]).IsEqualTo(failureCode);
        }
    }

    [Test]
    public async Task HalPolicy_EmitsCanonicalSelfAndEditPermissionFactsWhenUnlocked()
    {
        var policy = new TenantReportingIntakePolicyLinkPolicy();

        LinkDefinition[] links = policy.GetLinks(Policy(isLocked: false, canDisable: true), user: null).ToArray();

        LinkDefinition self = links.Single(link => link.Rel == LinkRelations.Self);
        LinkDefinition edit = links.Single(link => link.Rel == LinkRelations.Edit);
        await AssertPermissionLink(
            self,
            RouteNames.GetTenantReportingIntakePolicy,
            HttpMethods.Get,
            AuthorizationActions.TenantSettings.View,
            isLocked: false);
        await AssertPermissionLink(
            edit,
            RouteNames.UpdateTenantReportingIntakePolicy,
            HttpMethods.Put,
            AuthorizationActions.TenantSettings.Update,
            isLocked: false);
        await Assert.That(links.Any(link =>
            link.Rel is LinkRelations.ExportConfigurationOverrides
                or LinkRelations.ExportConfigurationPortable)).IsFalse();
    }

    [Test]
    public async Task HalPolicy_WhenInstanceLocked_EmitsSelfAndOmitsEdit()
    {
        var policy = new TenantReportingIntakePolicyLinkPolicy();

        LinkDefinition[] links = policy.GetLinks(Policy(isLocked: true, canDisable: false), user: null).ToArray();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.Self)).IsTrue();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.Edit)).IsFalse();
        LinkDefinition self = links.Single(link => link.Rel == LinkRelations.Self);
        await AssertPermissionLink(
            self,
            RouteNames.GetTenantReportingIntakePolicy,
            HttpMethods.Get,
            AuthorizationActions.TenantSettings.View,
            isLocked: true);
    }

    [Test]
    public async Task DtoSettingSource_IsCoveredByTheOpenApiStringEnumCatalog()
    {
        PropertyInfo sourceProperty = typeof(TenantReportingIntakePolicyDto)
            .GetProperty(nameof(TenantReportingIntakePolicyDto.Source))!;
        Type catalog = typeof(TenantReportingIntakeSettingsController).Assembly
            .GetType("Explore.API.OpenApi.OpenApiStringEnumSchemaCatalog")!;
        var enumTypes = (IReadOnlyCollection<Type>)catalog
            .GetProperty("EnumTypes", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        await Assert.That(sourceProperty.PropertyType).IsEqualTo(typeof(SettingSource));
        await Assert.That(enumTypes).Contains(typeof(SettingSource));
    }

    private TenantReportingIntakeSettingsController CreateController(
        IMediator mediator,
        IResourceAssembler<TenantReportingIntakePolicyDto, TenantReportingIntakePolicyDto> assembler)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);
        var userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(_actorUserId);
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(userContext)
            .BuildServiceProvider();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("internal_user_id", _actorUserId.ToString("D"))],
            authenticationType: "Test"));

        return new TenantReportingIntakeSettingsController(mediator, tenant, assembler)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services,
                    User = principal
                }
            }
        };
    }

    private TenantReportingIntakePolicyDto Policy(bool isLocked = false, bool canDisable = false) => new()
    {
        TenantId = _tenantId,
        Enabled = true,
        Source = isLocked ? SettingSource.SystemLocked : SettingSource.TenantOverride,
        IsLockedByInstance = isLocked,
        CanDisable = canDisable,
        ReasonCode = canDisable
            ? "event_reporting_intake_protected_by_approval"
            : "event_reporting_intake_unsafe_publication_policy",
        Reason = canDisable
            ? "Publication is protected by approval."
            : "Reporting intake cannot be disabled while an ordinary submission path is open."
    };

    private static async Task AssertAction(
        string actionName,
        string method,
        string routeName,
        Type successType,
        string timeoutPolicy,
        bool expectWriteLimiter,
        int[] expectedProblemStatuses)
    {
        MethodInfo action = typeof(TenantReportingIntakeSettingsController).GetMethod(actionName)
            ?? throw new InvalidOperationException($"Action {actionName} was not found.");
        HttpMethodAttribute route = action.GetCustomAttributes<HttpMethodAttribute>().Single();
        ProducesResponseTypeAttribute[] responses = action.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();

        await Assert.That(route.Template).IsEqualTo(string.Empty);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(route.HttpMethods).HasSingleItem().And.Contains(method);
        await Assert.That(action.IsDefined(typeof(PrivateNoStoreAttribute), inherit: true)).IsTrue();
        await Assert.That(action.GetCustomAttribute<RequestTimeoutAttribute>()?.PolicyName)
            .IsEqualTo(timeoutPolicy);
        await Assert.That(responses.Single(response => response.StatusCode == StatusCodes.Status200OK).Type)
            .IsEqualTo(successType);

        EnableRateLimitingAttribute? limiter = action.GetCustomAttribute<EnableRateLimitingAttribute>();
        if (expectWriteLimiter)
            await Assert.That(limiter?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
        else
            await Assert.That(limiter).IsNull();

        foreach (int status in expectedProblemStatuses)
        {
            await Assert.That(responses.Single(response => response.StatusCode == status).Type)
                .IsEqualTo(typeof(ProblemDetails));
        }
    }

    private async Task AssertPermissionLink(
        LinkDefinition link,
        string routeName,
        string method,
        string action,
        bool isLocked)
    {
        await Assert.That(link.RouteName).IsEqualTo(routeName);
        await Assert.That(link.Method).IsEqualTo(method);
        await Assert.That(link.RequiresAuth).IsTrue();
        await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.TenantSetting);
        await Assert.That(link.PermissionAction).IsEqualTo(action);
        await Assert.That(link.PermissionResourceId)
            .IsEqualTo(GovernanceSettingKeys.EventReporting.IntakeEnabled);
        await Assert.That(link.PermissionFacts).IsEqualTo(new TenantSettingAuthorizationFacts(
            _tenantId,
            GovernanceSettingKeys.EventReporting.IntakeEnabled,
            isLocked));
        await Assert.That(link.PermissionScope?.TenantId).IsEqualTo(_tenantId.ToString());
    }
}
