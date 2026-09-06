// ABOUTME: Controller contract tests for anonymous capability-scoped and authenticated registration-order APIs.
// ABOUTME: Verifies canonical routes, PublicTransactional safeguards, and token-safe HTTP transport.

using System.Reflection;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.API.OpenApi;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Application.Features.Promotions.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class RegistrationOrderControllerTests
{
    private const string CapabilityHeader = "X-Registration-Order-Capability";

    [Test]
    public async Task GuestRoutes_UseCapabilityScopedPublicTransactionalContracts()
    {
        var controller = typeof(GuestRegistrationOrderController);

        await AssertRoute<GuestRegistrationOrderController, HttpPostAttribute>(
            nameof(GuestRegistrationOrderController.StartGuest), "guest", RouteNames.StartGuestRegistrationOrder,
            EndpointClass.PublicTransactional, requiresIdempotency: true);
        await AssertRoute<GuestRegistrationOrderController, HttpGetAttribute>(
            nameof(GuestRegistrationOrderController.GetGuest), "guest/{orderId:guid}", RouteNames.GetGuestRegistrationOrder,
            EndpointClass.Public, requiresIdempotency: false);
        await AssertRoute<GuestRegistrationOrderController, HttpPostAttribute>(
            nameof(GuestRegistrationOrderController.ContinueGuest), "guest/{orderId:guid}/continue", RouteNames.ContinueGuestRegistrationOrder,
            EndpointClass.PublicTransactional, requiresIdempotency: true);
        await AssertRoute<GuestRegistrationOrderController, HttpPostAttribute>(
            nameof(GuestRegistrationOrderController.ApplyGuestPromotion), "guest/{orderId:guid}/promotion", RouteNames.ApplyGuestRegistrationOrderPromotion,
            EndpointClass.PublicTransactional, requiresIdempotency: true);
        await AssertRoute<GuestRegistrationOrderController, HttpDeleteAttribute>(
            nameof(GuestRegistrationOrderController.RemoveGuestPromotion), "guest/{orderId:guid}/promotion", RouteNames.RemoveGuestRegistrationOrderPromotion,
            EndpointClass.PublicTransactional, requiresIdempotency: true);
        await AssertRoute<GuestRegistrationOrderController, HttpPostAttribute>(
            nameof(GuestRegistrationOrderController.FinalizeGuest), "guest/{orderId:guid}/finalize", RouteNames.FinalizeGuestRegistrationOrder,
            EndpointClass.PublicTransactional, requiresIdempotency: true);
        await AssertRoute<GuestRegistrationOrderController, HttpDeleteAttribute>(
            nameof(GuestRegistrationOrderController.CancelGuest), "guest/{orderId:guid}", RouteNames.CancelGuestRegistrationOrder,
            EndpointClass.PublicTransactional, requiresIdempotency: true);

        await AssertRoute<GuestRegistrationOrderController, HttpPostAttribute>(
            nameof(GuestRegistrationOrderController.ClaimGuest), "guest/{orderId:guid}/claim", RouteNames.ClaimGuestRegistrationOrder,
            EndpointClass.Authenticated, requiresIdempotency: true);

        MethodInfo guestRead = controller.GetMethod(nameof(GuestRegistrationOrderController.GetGuest))!;
        await Assert.That(guestRead.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
        ParameterInfo capability = guestRead.GetParameters()
            .Single(parameter => parameter.GetCustomAttribute<FromHeaderAttribute>()?.Name == CapabilityHeader);
        await Assert.That(capability.ParameterType).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task AuthenticatedRoutes_UseCurrentAccountContracts()
    {
        await AssertRoute<AuthenticatedRegistrationOrderController, HttpPostAttribute>(
            nameof(AuthenticatedRegistrationOrderController.StartAuthenticated), string.Empty, RouteNames.StartAuthenticatedRegistrationOrder,
            EndpointClass.Authenticated, requiresIdempotency: false);
        await AssertRoute<AuthenticatedRegistrationOrderController, HttpGetAttribute>(
            nameof(AuthenticatedRegistrationOrderController.GetCurrent), "{orderId:guid}", RouteNames.GetCurrentRegistrationOrder,
            EndpointClass.Authenticated, requiresIdempotency: false);
        await AssertRoute<AuthenticatedRegistrationOrderController, HttpPostAttribute>(
            nameof(AuthenticatedRegistrationOrderController.ContinueAuthenticated), "{orderId:guid}/continue", RouteNames.ContinueAuthenticatedRegistrationOrder,
            EndpointClass.Authenticated, requiresIdempotency: false);
        await AssertRoute<AuthenticatedRegistrationOrderController, HttpPostAttribute>(
            nameof(AuthenticatedRegistrationOrderController.ApplyAuthenticatedPromotion), "{orderId:guid}/promotion", RouteNames.ApplyAuthenticatedRegistrationOrderPromotion,
            EndpointClass.Authenticated, requiresIdempotency: true);
        await AssertRoute<AuthenticatedRegistrationOrderController, HttpDeleteAttribute>(
            nameof(AuthenticatedRegistrationOrderController.RemoveAuthenticatedPromotion), "{orderId:guid}/promotion", RouteNames.RemoveAuthenticatedRegistrationOrderPromotion,
            EndpointClass.Authenticated, requiresIdempotency: true);
        await AssertRoute<AuthenticatedRegistrationOrderController, HttpPostAttribute>(
            nameof(AuthenticatedRegistrationOrderController.FinalizeAuthenticated), "{orderId:guid}/finalize", RouteNames.FinalizeAuthenticatedRegistrationOrder,
            EndpointClass.Authenticated, requiresIdempotency: false);
        await AssertRoute<AuthenticatedRegistrationOrderController, HttpDeleteAttribute>(
            nameof(AuthenticatedRegistrationOrderController.CancelAuthenticated), "{orderId:guid}", RouteNames.CancelAuthenticatedRegistrationOrder,
            EndpointClass.Authenticated, requiresIdempotency: false);
        await AssertRoute<AuthenticatedRegistrationOrderController, HttpGetAttribute>(
            nameof(AuthenticatedRegistrationOrderController.GetAuthenticatedParticipants), "{orderId:guid}/participants", RouteNames.GetAuthenticatedRegistrationOrderParticipants,
            EndpointClass.Authenticated, requiresIdempotency: false);
        await AssertRoute<AuthenticatedRegistrationOrderController, HttpPostAttribute>(
            nameof(AuthenticatedRegistrationOrderController.AddAuthenticatedParticipant), "{orderId:guid}/participants", RouteNames.AddAuthenticatedRegistrationOrderParticipant,
            EndpointClass.Authenticated, requiresIdempotency: true);
    }

    [Test]
    public async Task NativeSubmissionRoutes_ExposeAuthenticatedAndGuestTransactionalContracts()
    {
        await AssertNativeSubmissionRoute("LaunchAuthenticatedNativeAttempt", "{orderId:guid}/attempts", EndpointClass.Authenticated);
        await AssertNativeSubmissionRoute("SubmitAuthenticatedNativeAttempt", "{orderId:guid}/attempts/{attemptId:guid}/submissions", EndpointClass.Authenticated);
        await AssertNativeSubmissionRoute("LaunchGuestNativeAttempt", "guest/{orderId:guid}/attempts", EndpointClass.PublicTransactional);
        await AssertNativeSubmissionRoute("SubmitGuestNativeAttempt", "guest/{orderId:guid}/attempts/{attemptId:guid}/submissions", EndpointClass.PublicTransactional);
    }

    [Test]
    public async Task AttemptLaunchRequests_ForwardOptionalSupersededAttemptForExplicitRestart()
    {
        var mediator = Substitute.For<IMediator>();
        Guid oldAttemptId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        Guid requirementId = Guid.CreateVersion7();
        Guid channelId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        Guid versionId = Guid.CreateVersion7();
        Guid bindingId = Guid.CreateVersion7();
        mediator.Send(Arg.Any<LaunchAuthenticatedNativeRegistrationAttemptCommand>(), Arg.Any<CancellationToken>())
            .Returns(new NativeRegistrationAttemptResult(true, Guid.CreateVersion7(), requirementId, channelId, formId, versionId,
                DateTime.UtcNow.AddMinutes(10), new NativeRegistrationFormDefinitionDto(versionId, 1, "en", null, [], []), [],
                new NativeRegistrationRequirementProgressDto(0, 0, 0, 0, false), false, "raw-token"));
        mediator.Send(Arg.Any<LaunchAuthenticatedRegistrationProviderAttemptCommand>(), Arg.Any<CancellationToken>())
            .Returns(new RegistrationProviderAttemptResult(true, Guid.CreateVersion7(), new NativeRegistrationProviderLaunchDescriptorDto(
                Guid.CreateVersion7(), requirementId, channelId, bindingId, formId, versionId, "redirect", true,
                "https://forms.example.test/start", "Provider registration", true, "manual", "ok", [],
                new NativeRegistrationRequirementProgressDto(0, 0, 0, 0, false))));
        var controller = CreateController<AuthenticatedRegistrationOrderController>(mediator);

        await controller.LaunchAuthenticatedNativeAttempt(eventId, orderId, "idem", new LaunchNativeRegistrationAttemptRequest(
            requirementId, channelId, formId, versionId, null, oldAttemptId));
        await controller.LaunchAuthenticatedProviderAttempt(eventId, orderId, new LaunchRegistrationProviderAttemptRequest(
            requirementId, channelId, bindingId, formId, versionId, oldAttemptId));

        _ = mediator.Received(1).Send(Arg.Is<LaunchAuthenticatedNativeRegistrationAttemptCommand>(command =>
            command.SupersededAttemptId == oldAttemptId), Arg.Any<CancellationToken>());
        _ = mediator.Received(1).Send(Arg.Is<LaunchAuthenticatedRegistrationProviderAttemptCommand>(command =>
            command.SupersededAttemptId == oldAttemptId), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GuestParticipantRoutes_KeepCapabilityInHeaderAndWritesTransactional()
    {
        await AssertRoute<GuestRegistrationOrderController, HttpGetAttribute>(
            nameof(GuestRegistrationOrderController.GetGuestParticipants), "guest/{orderId:guid}/participants", RouteNames.GetGuestRegistrationOrderParticipants,
            EndpointClass.Public, requiresIdempotency: false);
        await AssertRoute<GuestRegistrationOrderController, HttpPostAttribute>(
            nameof(GuestRegistrationOrderController.AddGuestParticipant), "guest/{orderId:guid}/participants", RouteNames.AddGuestRegistrationOrderParticipant,
            EndpointClass.PublicTransactional, requiresIdempotency: true);

        MethodInfo read = typeof(GuestRegistrationOrderController).GetMethod(nameof(GuestRegistrationOrderController.GetGuestParticipants))!;
        ParameterInfo capability = read.GetParameters()
            .Single(parameter => parameter.GetCustomAttribute<FromHeaderAttribute>()?.Name == CapabilityHeader);
        await Assert.That(capability.ParameterType).IsEqualTo(typeof(string));
        await Assert.That(read.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
    }

    [Test]
    public async Task EventOrderCollectionRoute_UsesPrivateEventScopedOrganizerContract()
    {
        MethodInfo? action = typeof(RegistrationOrderController).GetMethod("GetEventOrders");

        await Assert.That(action).IsNotNull();
        var route = action!.GetCustomAttribute<HttpGetAttribute>();
        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo(string.Empty);
        await Assert.That(route.Name).IsEqualTo(RouteNames.GetEventRegistrationOrders);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(action.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == StatusCodes.Status200OK &&
                              attribute.Type == typeof(HalCollectionResource<RegistrationOrderDto>))).IsTrue();
    }

    [Test]
    public async Task GetEventOrders_DispatchesEventScopedQueryAndAssemblesHalCollection()
    {
        var mediator = Substitute.For<IMediator>();
        var assembler = Substitute.For<IResourceAssembler<RegistrationOrderDto, RegistrationOrderDto>>();
        var eventId = Guid.CreateVersion7();
        IReadOnlyList<RegistrationOrderDto> orders = [new RegistrationOrderDto { Id = Guid.CreateVersion7(), EventId = eventId }];
        var collection = new HalCollectionResource<RegistrationOrderDto>();
        mediator.Send(Arg.Any<GetEventRegistrationOrdersQuery>(), Arg.Any<CancellationToken>()).Returns(orders);
        assembler.ToCollectionResource(
                Arg.Any<IEnumerable<RegistrationOrderDto>>(),
                RouteNames.GetEventRegistrationOrders,
                Arg.Any<object?>(),
                Arg.Any<HttpContext>())
            .Returns(collection);
        var controller = CreateController<RegistrationOrderController>(mediator, assembler);

        ActionResult<HalCollectionResource<RegistrationOrderDto>> result = await controller.GetEventOrders(eventId);

        await Assert.That((result.Result as OkObjectResult)?.Value).IsEqualTo(collection);
        _ = mediator.Received(1).Send(
            Arg.Is<GetEventRegistrationOrdersQuery>(query => query.EventId == eventId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GuestPostOpenApiMetadata_RequiresIdempotencyKey()
    {
        foreach (string actionName in new[]
                 {
                     nameof(GuestRegistrationOrderController.StartGuest),
                     nameof(GuestRegistrationOrderController.ContinueGuest),
                     nameof(GuestRegistrationOrderController.FinalizeGuest)
                 })
        {
            MethodInfo action = RegistrationFamilyAction(actionName)!;
            var operation = new OpenApiOperation();

            bool applied = EndpointClassificationTransformer.ApplyIdempotencyKeyRequirement(
                operation,
                action.GetCustomAttributes().Cast<object>());

            await Assert.That(applied).IsTrue();
            await Assert.That(operation.Extensions).ContainsKey("x-idempotency-key-required");
        }
    }

    [Test]
    public async Task StartGuest_EmitsCapabilityOnlyInResponseHeader()
    {
        var token = "opaque-guest-capability";
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<StartGuestRegistrationOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(GuestRegistrationOrderStartDto.Success(
                Guid.CreateVersion7(),
                message: null,
                guestCapabilityToken: token)));
        var controller = CreateController<GuestRegistrationOrderController>(mediator);
        var request = CreateStartRequest(platformContributionBasisPoints: 500);
        var eventId = Guid.CreateVersion7();

        ActionResult<GuestRegistrationOrderStartDto> result = await controller.StartGuest(eventId, request);

        var created = result.Result as CreatedAtRouteResult;
        await Assert.That(created).IsNotNull();
        await Assert.That(controller.Response.Headers[CapabilityHeader].ToString()).IsEqualTo(token);
        await Assert.That(JsonSerializer.Serialize(created!.Value)).DoesNotContain(token);
        _ = mediator.Received(1).Send(
            Arg.Is<StartGuestRegistrationOrderCommand>(command => command.PlatformContributionBasisPoints == 500),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GuestAccessFailures_ReturnGenericNotFoundWithoutCapabilityDisclosure()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetGuestRegistrationOrderQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GuestRegistrationOrderDto?>(null));
        var controller = CreateController<GuestRegistrationOrderController>(mediator);

        ActionResult<HalResource<GuestRegistrationOrderDto>> result = await controller.GetGuest(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "guessed-capability");

        var notFound = result.Result as ObjectResult;
        await Assert.That(notFound?.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        await Assert.That(JsonSerializer.Serialize(notFound?.Value)).DoesNotContain("guessed-capability");
    }

    [Test]
    public async Task PromotionRoutes_DispatchCapabilityAndCurrentAccountWrappersAndGenericFailures()
    {
        var mediator = Substitute.For<IMediator>();
        var eventId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        mediator.Send(Arg.Any<ApplyGuestPromotionCodeToRegistrationOrderCommand>(), Arg.Any<CancellationToken>()).Returns(PromotionFailure(orderId));
        mediator.Send(Arg.Any<RemoveGuestPromotionFromRegistrationOrderCommand>(), Arg.Any<CancellationToken>()).Returns(PromotionFailure(orderId));
        mediator.Send(Arg.Any<ApplyAuthenticatedPromotionCodeToRegistrationOrderCommand>(), Arg.Any<CancellationToken>()).Returns(PromotionSuccess(orderId));
        mediator.Send(Arg.Any<RemoveAuthenticatedPromotionFromRegistrationOrderCommand>(), Arg.Any<CancellationToken>()).Returns(PromotionSuccess(orderId));
        // Guest and authenticated promotion now live on their own capability controllers; this test asserts
        // both doors behave identically, so it drives both.
        var guestController = CreateController<GuestRegistrationOrderController>(mediator);
        var authenticatedController = CreateController<AuthenticatedRegistrationOrderController>(mediator);

        var guestApply = await guestController.ApplyGuestPromotion(eventId, orderId, "guest-capability", new PromotionCodeRequest("SAVE10"), Guid.CreateVersion7().ToString("N"));
        var guestRemove = await guestController.RemoveGuestPromotion(eventId, orderId, "guest-capability", Guid.CreateVersion7().ToString("N"));
        var authenticatedApply = await authenticatedController.ApplyAuthenticatedPromotion(eventId, orderId, new PromotionCodeRequest("SAVE10"), Guid.CreateVersion7().ToString("N"));
        var authenticatedRemove = await authenticatedController.RemoveAuthenticatedPromotion(eventId, orderId, Guid.CreateVersion7().ToString("N"));

        await Assert.That((guestApply.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        await Assert.That(JsonSerializer.Serialize((guestRemove.Result as ObjectResult)?.Value)).DoesNotContain("guest-capability");
        await Assert.That((authenticatedApply.Result as OkObjectResult)?.Value).IsTypeOf<PromotionRedemptionResponseDto>();
        await Assert.That((authenticatedRemove.Result as OkObjectResult)?.Value).IsTypeOf<PromotionRedemptionResponseDto>();
        _ = mediator.Received(1).Send(Arg.Is<ApplyGuestPromotionCodeToRegistrationOrderCommand>(command => command.EventId == eventId && command.OrderId == orderId && command.CapabilityToken == "guest-capability" && command.Code == "SAVE10"), Arg.Any<CancellationToken>());
        _ = mediator.Received(1).Send(Arg.Is<RemoveGuestPromotionFromRegistrationOrderCommand>(command => command.CapabilityToken == "guest-capability"), Arg.Any<CancellationToken>());
        _ = mediator.Received(1).Send(Arg.Is<ApplyAuthenticatedPromotionCodeToRegistrationOrderCommand>(command => command.EventId == eventId && command.OrderId == orderId && command.Code == "SAVE10"), Arg.Any<CancellationToken>());
        _ = mediator.Received(1).Send(Arg.Is<RemoveAuthenticatedPromotionFromRegistrationOrderCommand>(command => command.EventId == eventId && command.OrderId == orderId), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ParticipantHalMutationRelationsFollowServerManageDecision()
    {
        var mediator = Substitute.For<IMediator>();
        var eventId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var viewOnly = new RegistrationOrderParticipantsDto(orderId, [], []);
        var manageable = viewOnly with { CanManage = true };
        mediator.Send(Arg.Any<GetAuthenticatedRegistrationOrderParticipantsQuery>(), Arg.Any<CancellationToken>())
            .Returns(viewOnly, manageable);
        var controller = CreateController<AuthenticatedRegistrationOrderController>(mediator);

        var organizerResult = await controller.GetAuthenticatedParticipants(eventId, orderId);
        var ownerResult = await controller.GetAuthenticatedParticipants(eventId, orderId);
        var organizerResource = (organizerResult.Result as OkObjectResult)?.Value as HalResource<RegistrationOrderParticipantsDto>;
        var ownerResource = (ownerResult.Result as OkObjectResult)?.Value as HalResource<RegistrationOrderParticipantsDto>;

        await Assert.That(organizerResource!.Links.Keys).IsEquivalentTo([LinkRelations.Self]);
        await Assert.That(ownerResource!.Links.Keys).Contains(LinkRelations.AddParticipant);
        await Assert.That(ownerResource.Links.Keys).Contains(LinkRelations.UpdateParticipant);
        await Assert.That(ownerResource.Links.Keys).Contains(LinkRelations.AssignTickets);
        await Assert.That(ownerResource.Links.Keys).Contains(LinkRelations.DeferTickets);
    }

    [Test]
    public async Task ContinueGuest_ForwardsContributionSelectionWithoutClientAmount()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ContinueGuestRegistrationOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(GuestRegistrationOrderLifecycleResponseDto.Success(
                Guid.CreateVersion7(),
                message: null,
                order: null));
        var controller = CreateController<GuestRegistrationOrderController>(mediator);

        await controller.ContinueGuest(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "guest-token",
            new ContinueRegistrationOrderRequest { PlatformContributionBasisPoints = 500 });

        _ = mediator.Received(1).Send(
            Arg.Is<ContinueGuestRegistrationOrderCommand>(command => command.PlatformContributionBasisPoints == 500),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AccountRequiredGuestStart_ReturnsAuthenticationRequiredWithoutCreatingAnAccount()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<StartGuestRegistrationOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(GuestRegistrationOrderStartDto.Failure(
                BaseCommandResponse.Failure<Guid>(
                    "registration_order_identity_required",
                    "Registration order requires an authenticated account.",
                    id: Guid.CreateVersion7()))));
        var controller = CreateController<GuestRegistrationOrderController>(mediator);

        ActionResult<GuestRegistrationOrderStartDto> result = await controller.StartGuest(
            Guid.CreateVersion7(), CreateStartRequest());

        var unauthorized = result.Result as ObjectResult;
        await Assert.That(unauthorized?.StatusCode).IsEqualTo(StatusCodes.Status401Unauthorized);
        await Assert.That(JsonSerializer.Serialize(unauthorized?.Value)).DoesNotContain("registration_order_identity_required");
    }

    [Test]
    public async Task AuthenticatedReadAndLifecycle_AssembleHalResources()
    {
        var mediator = Substitute.For<IMediator>();
        var assembler = Substitute.For<IResourceAssembler<RegistrationOrderDto, RegistrationOrderDto>>();
        var order = CreateOrder();
        var resource = new HalResource<RegistrationOrderDto>(order);
        mediator.Send(Arg.Any<GetCurrentRegistrationOrderQuery>(), Arg.Any<CancellationToken>()).Returns(order);
        mediator.Send(Arg.Any<ContinueAuthenticatedRegistrationOrderCommand>(), Arg.Any<CancellationToken>()).Returns(
            RegistrationOrderLifecycleResponseDto.Success(order.Id, message: null, order: order));
        mediator.Send(Arg.Any<FinalizeAuthenticatedRegistrationOrderCommand>(), Arg.Any<CancellationToken>()).Returns(
            RegistrationOrderLifecycleResponseDto.Success(order.Id, message: null, order: order));
        mediator.Send(Arg.Any<CancelAuthenticatedRegistrationOrderCommand>(), Arg.Any<CancellationToken>()).Returns(
            RegistrationOrderLifecycleResponseDto.Success(order.Id, message: null, order: order));
        assembler.ToResource(order, Arg.Any<HttpContext>()).Returns(resource);
        var controller = CreateController<AuthenticatedRegistrationOrderController>(mediator, assembler);

        var current = await controller.GetCurrent(order.EventId, order.Id);
        var continued = await controller.ContinueAuthenticated(order.EventId, order.Id);
        var finalized = await controller.FinalizeAuthenticated(order.EventId, order.Id);
        var cancelled = await controller.CancelAuthenticated(order.EventId, order.Id);

        foreach (ActionResult<HalResource<RegistrationOrderDto>> result in new[] { current, continued, finalized, cancelled })
        {
            var objectResult = result.Result as ObjectResult;
            await Assert.That(objectResult?.Value).IsEqualTo(resource);
            await Assert.That(objectResult?.ContentTypes).Contains(HateoasConstants.HalJsonMediaType);
        }

        await assembler.Received(4).ToResource(order, Arg.Any<HttpContext>());
    }

    [Test]
    public async Task GuestRead_ReturnsCapabilityScopedHalWithoutAuthenticatedAssembler()
    {
        var mediator = Substitute.For<IMediator>();
        var guestOrder = new GuestRegistrationOrderDto { Id = Guid.CreateVersion7(), EventId = Guid.CreateVersion7() };
        mediator.Send(Arg.Any<GetGuestRegistrationOrderQuery>(), Arg.Any<CancellationToken>()).Returns(guestOrder);
        var controller = CreateGuestController(mediator, TimeProvider.System);

        ActionResult<HalResource<GuestRegistrationOrderDto>> result = await controller.GetGuest(
            guestOrder.EventId, guestOrder.Id, "opaque-capability");

        var resource = (result.Result as OkObjectResult)?.Value as HalResource<GuestRegistrationOrderDto>;
        await Assert.That(resource?.Data).IsEqualTo(guestOrder);
        await Assert.That(resource?.Links).ContainsKey(LinkRelations.Self);
        await Assert.That(resource?.Links).ContainsKey(LinkRelations.ClaimRegistrationOrder);
        await Assert.That(resource?.Links).DoesNotContainKey(LinkRelations.ApplyPromotion);
        await Assert.That(resource?.Links).DoesNotContainKey(LinkRelations.RemovePromotion);
        await Assert.That(JsonSerializer.Serialize(resource)).DoesNotContain("opaque-capability");
    }

    [Test]
    public async Task GuestReadyForCheckoutHal_WithoutPromotionExposesApplyOnlyWithoutCapabilityInUrls()
    {
        var mediator = Substitute.For<IMediator>();
        var order = new GuestRegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            StatusId = (int)RegistrationOrderStatusEnum.ReadyForCheckout,
            StatusCode = "READY_FOR_CHECKOUT"
        };
        mediator.Send(Arg.Any<GetGuestRegistrationOrderQuery>(), Arg.Any<CancellationToken>()).Returns(order);
        var controller = CreateController<GuestRegistrationOrderController>(mediator);

        ActionResult<HalResource<GuestRegistrationOrderDto>> result = await controller.GetGuest(
            order.EventId, order.Id, "opaque-capability");

        var resource = (result.Result as OkObjectResult)?.Value as HalResource<GuestRegistrationOrderDto>;
        await Assert.That(resource?.Links).ContainsKey(LinkRelations.ApplyPromotion);
        await Assert.That(resource?.Links).DoesNotContainKey(LinkRelations.RemovePromotion);
        await Assert.That(JsonSerializer.Serialize(resource?.Links)).DoesNotContain("opaque-capability");
    }

    [Test]
    public async Task GuestReadyForCheckoutHal_WithPromotionExposesRemoveOnlyWithoutCapabilityInUrls()
    {
        var mediator = Substitute.For<IMediator>();
        var order = new GuestRegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            StatusId = (int)RegistrationOrderStatusEnum.ReadyForCheckout,
            StatusCode = "READY_FOR_CHECKOUT",
            AppliedPromotionDisplayLabel = "Launch discount"
        };
        mediator.Send(Arg.Any<GetGuestRegistrationOrderQuery>(), Arg.Any<CancellationToken>()).Returns(order);
        var controller = CreateController<GuestRegistrationOrderController>(mediator);

        ActionResult<HalResource<GuestRegistrationOrderDto>> result = await controller.GetGuest(
            order.EventId, order.Id, "opaque-capability");

        var resource = (result.Result as OkObjectResult)?.Value as HalResource<GuestRegistrationOrderDto>;
        await Assert.That(resource?.Links).ContainsKey(LinkRelations.RemovePromotion);
        await Assert.That(resource?.Links).DoesNotContainKey(LinkRelations.ApplyPromotion);
        await Assert.That(JsonSerializer.Serialize(resource?.Links)).DoesNotContain("opaque-capability");
    }

    [Test]
    public async Task GuestAwaitingPaymentHal_WhenExpired_OmitsStartPayment()
    {
        DateTime utcNow = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        var mediator = Substitute.For<IMediator>();
        var order = new GuestRegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            StatusId = (int)RegistrationOrderStatusEnum.AwaitingPayment,
            StatusCode = "AWAITING_PAYMENT",
            TotalDueMinor = 1_000,
            ExpiresAt = utcNow.AddSeconds(-1)
        };
        mediator.Send(Arg.Any<GetGuestRegistrationOrderQuery>(), Arg.Any<CancellationToken>()).Returns(order);
        var controller = CreateGuestController(mediator, new FixedTimeProvider(utcNow));

        ActionResult<HalResource<GuestRegistrationOrderDto>> result = await controller.GetGuest(
            order.EventId, order.Id, "opaque-capability");

        var resource = (result.Result as OkObjectResult)?.Value as HalResource<GuestRegistrationOrderDto>;
        await Assert.That(resource?.Links).DoesNotContainKey(LinkRelations.StartPayment);
        await Assert.That(resource?.Links).ContainsKey(LinkRelations.PaymentStatus);
    }

    [Test]
    public async Task ClaimGuest_DispatchesCapabilityScopedCommandAndMapsConflict()
    {
        var mediator = Substitute.For<IMediator>();
        var orderId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        mediator.Send(Arg.Any<ClaimGuestRegistrationOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                "registration_order_already_linked",
                "Registration order is already linked to another account.",
                id: orderId));
        var controller = CreateController<GuestRegistrationOrderController>(mediator);

        ActionResult<BaseCommandResponse<Guid>> result = await controller.ClaimGuest(eventId, orderId, "guest-token");

        var conflict = result.Result as ConflictObjectResult;
        await Assert.That(conflict).IsNotNull();
        _ = mediator.Received(1).Send(
            Arg.Is<ClaimGuestRegistrationOrderCommand>(command =>
                command.EventId == eventId && command.OrderId == orderId && command.CapabilityToken == "guest-token"),
            Arg.Any<CancellationToken>());
    }

    private static TController CreateController<TController>(
        IMediator mediator,
        IResourceAssembler<RegistrationOrderDto, RegistrationOrderDto>? assembler = null)
        where TController : ControllerBase
    {
        IResourceAssembler<RegistrationOrderDto, RegistrationOrderDto> effectiveAssembler =
            assembler ?? Substitute.For<IResourceAssembler<RegistrationOrderDto, RegistrationOrderDto>>();
        object[] arguments = typeof(TController) == typeof(GuestRegistrationOrderController)
            ? [mediator, TimeProvider.System]
            : [mediator, effectiveAssembler];
        var controller = (TController)Activator.CreateInstance(typeof(TController), arguments)!;
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var url = Substitute.For<IUrlHelper>();
        url.Link(Arg.Any<string>(), Arg.Any<object>()).Returns(call => $"/api/routes/{call.ArgAt<string>(0)}");
        controller.Url = url;

        return controller;
    }

    private static GuestRegistrationOrderController CreateGuestController(IMediator mediator, TimeProvider timeProvider)
    {
        var controller = new GuestRegistrationOrderController(mediator, timeProvider);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var url = Substitute.For<IUrlHelper>();
        url.Link(Arg.Any<string>(), Arg.Any<object>()).Returns(call => $"/api/routes/{call.ArgAt<string>(0)}");
        controller.Url = url;
        return controller;
    }

    private static RegistrationOrderDto CreateOrder() => new()
    {
        Id = Guid.CreateVersion7(),
        EventId = Guid.CreateVersion7()
    };

    private static PromotionRedemptionResponseDto PromotionSuccess(Guid orderId) =>
        PromotionRedemptionResponseDto.Success(
            orderId,
            message: null,
            appliedPromotionDisplayLabel: null,
            promotionDiscountTotalMinor: 0,
            totalDueMinor: 0,
            platformFeeTotalMinor: 0,
            platformContributionTotalMinor: 0);

    private static PromotionRedemptionResponseDto PromotionFailure(Guid orderId) =>
        PromotionRedemptionResponseDto.Failure(BaseCommandResponse.Failure<Guid>(
            PromotionRedemptionFailureCodes.Unavailable,
            "Promotion cannot be changed for this order.",
            id: orderId));

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private static async Task AssertNativeSubmissionRoute(
        string actionName,
        string routeTemplate,
        EndpointClass endpointClass)
    {
        MethodInfo? action = RegistrationFamilyAction(actionName);
        await Assert.That(action).IsNotNull();
        var route = action!.GetCustomAttribute<HttpPostAttribute>();
        await Assert.That(route?.Template).IsEqualTo(routeTemplate);
        await Assert.That(action.GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(endpointClass);
        await Assert.That(action.GetCustomAttribute<RequireIdempotencyKeyAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>() is not null)
            .IsEqualTo(endpointClass == EndpointClass.Authenticated);
    }

    private static StartRegistrationOrderRequest CreateStartRequest(int? platformContributionBasisPoints = null) => new()
    {
        TicketCatalogVersionId = Guid.CreateVersion7(),
        BookingPartyType = BookingPartyTypeEnum.Individual,
        Lines = [new RegistrationOrderLineSelection(Guid.CreateVersion7(), 1, null)],
        PlatformContributionBasisPoints = platformContributionBasisPoints
    };

    private static async Task AssertRoute<TController, TAttribute>(
        string actionName,
        string template,
        string routeName,
        EndpointClass endpointClass,
        bool requiresIdempotency)
        where TController : ControllerBase
        where TAttribute : HttpMethodAttribute
    {
        MethodInfo action = typeof(TController).GetMethod(actionName)!;
        var route = action.GetCustomAttribute<TAttribute>();

        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo(template);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(action.GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(endpointClass);

        if (endpointClass == EndpointClass.PublicTransactional)
        {
            await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
            await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
                .IsEqualTo(RateLimitingExtensions.PublicTransactionalPolicy);
        }
        else if (endpointClass == EndpointClass.Authenticated)
        {
            await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        }
        else
        {
            await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        }

        await Assert.That(action.GetCustomAttribute<RequireIdempotencyKeyAttribute>() is not null)
            .IsEqualTo(requiresIdempotency);
        await Assert.That(action.GetCustomAttributes<ProducesResponseTypeAttribute>()).IsNotEmpty();
    }

    /// <summary>
    /// Finds an action across the controllers the original RegistrationOrderController was partitioned into.
    /// Guest and authenticated checkout are separate surfaces now, but these contracts still apply to both.
    /// </summary>
    private static MethodInfo? RegistrationFamilyAction(string actionName) => new[]
    {
        typeof(RegistrationOrderController),
        typeof(GuestRegistrationOrderController),
        typeof(AuthenticatedRegistrationOrderController),
    }.Select(type => type.GetMethod(actionName)).FirstOrDefault(method => method is not null);
}
