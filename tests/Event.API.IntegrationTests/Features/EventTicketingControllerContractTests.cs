using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.DTOs.OrganizerPaymentConnections;
using Explore.Application.Features.OrganizerPaymentConnections.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Phase43Ticketing)]
public sealed class EventTicketingControllerContractTests
{
    [Test]
    public async Task TicketingActions_UseNamedRoutesAndProblemDetailsFailures()
    {
        var expected = new Dictionary<string, string>
        {
            [nameof(EventTicketingController.Get)] = RouteNames.GetEventTicketCatalogManagement,
            [nameof(EventTicketingController.CreateDraft)] = RouteNames.CreateEventTicketCatalogDraft,
            [nameof(EventTicketingController.CloneDraft)] = RouteNames.CloneEventTicketCatalogDraft,
            [nameof(EventTicketingController.CreateType)] = RouteNames.CreateEventTicketType,
            [nameof(EventTicketingController.UpdateType)] = RouteNames.UpdateEventTicketType,
            [nameof(EventTicketingController.DeleteType)] = RouteNames.DeleteEventTicketType,
            [nameof(EventTicketingController.CreatePool)] = RouteNames.CreateEventCapacityPool,
            [nameof(EventTicketingController.UpdatePool)] = RouteNames.UpdateEventCapacityPool,
            [nameof(EventTicketingController.DeletePool)] = RouteNames.DeleteEventCapacityPool,
            [nameof(EventTicketingController.Preflight)] = RouteNames.GetPaidEventPublicationPreflight,
            [nameof(EventTicketingController.UpdateCommercialDisclosures)] = RouteNames.UpdateEventTicketCatalogCommercialDisclosures,
            [nameof(EventTicketingController.GetPaymentConnection)] = RouteNames.GetEventOrganizerPaymentConnection,
            [nameof(EventTicketingController.StartPaymentOnboarding)] = RouteNames.StartEventOrganizerPaymentOnboarding,
            [nameof(EventTicketingController.ReturnPaymentOnboarding)] = RouteNames.ReturnEventOrganizerPaymentOnboarding,
            [nameof(EventTicketingController.RefreshPaymentOnboarding)] = RouteNames.RefreshEventOrganizerPaymentOnboarding,
            [nameof(EventTicketingController.Publish)] = RouteNames.PublishEventTicketCatalog
        };
        var conflictCapableActions = new HashSet<string>
        {
            nameof(EventTicketingController.CreateDraft),
            nameof(EventTicketingController.CloneDraft),
            nameof(EventTicketingController.UpdateType),
            nameof(EventTicketingController.CreatePool),
            nameof(EventTicketingController.UpdatePool),
            nameof(EventTicketingController.Publish)
        };

        foreach ((string actionName, string routeName) in expected)
        {
            MethodInfo action = typeof(EventTicketingController).GetMethod(actionName)
                ?? throw new InvalidOperationException($"Action {actionName} was not found.");
            HttpMethodAttribute route = action.GetCustomAttributes<HttpMethodAttribute>().Single();

            await Assert.That(route.Name).IsEqualTo(routeName);

            foreach (int statusCode in new[]
                     {
                         StatusCodes.Status400BadRequest,
                         StatusCodes.Status401Unauthorized,
                         StatusCodes.Status403Forbidden,
                         StatusCodes.Status404NotFound
                     })
            {
                ProducesResponseTypeAttribute response = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
                    .Single(attribute => attribute.StatusCode == statusCode);
                await Assert.That(response.Type).IsEqualTo(typeof(ProblemDetails));
            }

            ProducesResponseTypeAttribute[] responses = action.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
            bool hasConflictResponse = responses.Any(response => response.StatusCode == StatusCodes.Status409Conflict);
            await Assert.That(hasConflictResponse).IsEqualTo(conflictCapableActions.Contains(actionName));
            if (hasConflictResponse)
            {
                await Assert.That(responses.Single(response => response.StatusCode == StatusCodes.Status409Conflict).Type)
                    .IsEqualTo(typeof(ProblemDetails));
            }
        }
    }

    [Test]
    public async Task ManagementRead_UsesHalResponseAndPrivateNoStore()
    {
        MethodInfo action = typeof(EventTicketingController).GetMethod(nameof(EventTicketingController.Get))
            ?? throw new InvalidOperationException("The ticketing management read action was not found.");

        ProducesResponseTypeAttribute response = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == StatusCodes.Status200OK);

        await Assert.That(response.Type).IsEqualTo(typeof(HalResource<EventTicketCatalogManagementDto>));
        await Assert.That(action.IsDefined(typeof(PrivateNoStoreAttribute), inherit: true)).IsTrue();
    }

    [Test]
    public async Task PaidPublicationAndPaymentReads_UseHalResponsesAndPrivateNoStore()
    {
        var expected = new Dictionary<string, Type>
        {
            [nameof(EventTicketingController.Preflight)] = typeof(HalResource<PaidEventPublicationPreflightDto>),
            [nameof(EventTicketingController.GetPaymentConnection)] = typeof(HalResource<EventOrganizerPaymentConnectionManagementDto>)
        };

        foreach ((string actionName, Type responseType) in expected)
        {
            MethodInfo action = typeof(EventTicketingController).GetMethod(actionName)
                ?? throw new InvalidOperationException($"Action {actionName} was not found.");
            ProducesResponseTypeAttribute response = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Single(attribute => attribute.StatusCode == StatusCodes.Status200OK);

            await Assert.That(response.Type).IsEqualTo(responseType);
            await Assert.That(action.IsDefined(typeof(PrivateNoStoreAttribute), inherit: true)).IsTrue();
        }
    }

    [Test]
    public async Task PaymentOnboardingWrite_UsesCommandResponseContract()
    {
        MethodInfo action = typeof(EventTicketingController).GetMethod(nameof(EventTicketingController.StartPaymentOnboarding))
            ?? throw new InvalidOperationException("The payment onboarding action was not found.");
        ProducesResponseTypeAttribute response = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == StatusCodes.Status200OK);

        await Assert.That(response.Type).IsEqualTo(typeof(BaseCommandResponse<OrganizerPaymentOnboardingLinkResult>));
        await Assert.That(action.IsDefined(typeof(PrivateNoStoreAttribute), inherit: true)).IsTrue();
        await Assert.That(action.GetParameters().Any(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() is not null)).IsFalse();
    }

    [Test]
    public async Task PaymentOnboardingNavigation_UsesGetPrivateNoStoreAndFixedStudioRedirect()
    {
        foreach (string actionName in new[] { nameof(EventTicketingController.ReturnPaymentOnboarding), nameof(EventTicketingController.RefreshPaymentOnboarding) })
        {
            MethodInfo action = typeof(EventTicketingController).GetMethod(actionName)
                ?? throw new InvalidOperationException($"Action {actionName} was not found.");
            HttpMethodAttribute route = action.GetCustomAttributes<HttpMethodAttribute>().Single();

            await Assert.That(route.HttpMethods).IsEquivalentTo([HttpMethods.Get]);
            await Assert.That(action.IsDefined(typeof(PrivateNoStoreAttribute), inherit: true)).IsTrue();
        }

        var controller = CreateController(Substitute.For<IMediator>());
        Guid eventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000003");

        var returned = (RedirectResult)controller.ReturnPaymentOnboarding(eventId);
        var refreshed = (RedirectResult)controller.RefreshPaymentOnboarding(eventId);

        await Assert.That(returned.Url).IsEqualTo($"/studio/events/{eventId:D}/tickets");
        await Assert.That(refreshed.Url).IsEqualTo($"/studio/events/{eventId:D}/tickets");
    }

    [Test]
    public async Task PaymentOnboarding_UrlGenerationFailureSkipsMediator()
    {
        IMediator mediator = Substitute.For<IMediator>();
        EventTicketingController controller = CreateController(mediator);
        IUrlHelper url = Substitute.For<IUrlHelper>();
        url.RouteUrl(Arg.Any<UrlRouteContext>()).Returns((string?)null);
        controller.Url = url;

        ActionResult<BaseCommandResponse<OrganizerPaymentOnboardingLinkResult>> result = await controller.StartPaymentOnboarding(Guid.CreateVersion7(), CancellationToken.None);

        await mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
        await Assert.That(result.Result).IsTypeOf<ObjectResult>();
        await Assert.That(((ObjectResult)result.Result!).StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    private static EventTicketingController CreateController(IMediator mediator)
    {
        var controller = new EventTicketingController(
            mediator,
            Substitute.For<IResourceAssembler<EventTicketCatalogManagementDto, EventTicketCatalogManagementDto>>(),
            Substitute.For<IResourceAssembler<PaidEventPublicationPreflightDto, PaidEventPublicationPreflightDto>>(),
            Substitute.For<IResourceAssembler<EventOrganizerPaymentConnectionManagementDto, EventOrganizerPaymentConnectionManagementDto>>());
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.ControllerContext.HttpContext.Request.Scheme = "https";
        controller.ControllerContext.HttpContext.Request.Host = new HostString("api.example");
        return controller;
    }
}
