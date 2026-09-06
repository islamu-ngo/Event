// ABOUTME: Pins the payment start/status/retry HTTP contract for guest, account, and Studio callers.
// ABOUTME: Verifies transactional safeguards, private caching, and named route stability before implementation.

using System.Reflection;
using System.Runtime.CompilerServices;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class RegistrationPaymentContractTests
{
    [Test]
    public async Task AuthenticatedPaymentRequestsEnforceTheSamePermissionsAsHal()
    {
        await AssertSecureRequest<StartAuthenticatedRegistrationPaymentCommand>(
            AuthorizationActions.RegistrationOrders.Continue);
        await AssertSecureRequest<RetryAuthenticatedRegistrationPaymentCommand>(
            AuthorizationActions.RegistrationOrders.Continue);
        await AssertSecureRequest<GetAuthenticatedPaidOrderAcceptanceQuery>(
            AuthorizationActions.RegistrationOrders.Continue);
        await AssertSecureRequest<GetAuthenticatedRegistrationPaymentQuery>(
            AuthorizationActions.RegistrationOrders.View);
        await AssertSecureRequest<GetAuthenticatedRegistrationPaymentCheckoutTargetQuery>(
            AuthorizationActions.RegistrationOrders.View);
        await AssertSecureRequest<RequestAuthenticatedRegistrationRefundCommand>(
            AuthorizationActions.RegistrationOrders.RequestRefund);
        await AssertSecureRequest<RespondAuthenticatedRegistrationMaterialChangeCommand>(
            AuthorizationActions.RegistrationOrders.RespondMaterialChange);
        AuthorizeResourceAttribute studioRefund = typeof(CreateStudioRegistrationRefundCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>()!;
        await Assert.That(studioRefund.Resource).IsEqualTo(ResourceKinds.Event);
        await Assert.That(studioRefund.Action).IsEqualTo(AuthorizationActions.Events.ManagePaidEventCommerce);
    }

    [Test]
    public async Task PaidAcceptanceMarksEveryExactValueAndCollectionAsRequired()
    {
        string[] requiredProperties =
        [
            "OrganizerMerchant",
            "TenantDirectoryOperator",
            "InstanceOperator",
            "PaymentOperations",
            "DeliveryStartsAtUtc",
            "DeliveryEndsAtUtc",
            "CurrencyMinorUnitDigits",
            "OrganizerAmountMinor",
            "PlatformFeeMinor",
            "PlatformContributionMinor",
            "TotalMinor",
            "RefundPolicyVersion",
            "Lines"
        ];
        foreach (string propertyName in requiredProperties)
        {
            PropertyInfo property = typeof(PaidOrderAcceptanceDisclosureDto).GetProperty(propertyName)!;
            await Assert.That(property.GetCustomAttribute<RequiredMemberAttribute>()).IsNotNull();
        }

        foreach (PropertyInfo property in typeof(PaidOrderAcceptanceLineDto).GetProperties())
        {
            await Assert.That(property.GetCustomAttribute<RequiredMemberAttribute>()).IsNotNull();
        }

        PropertyInfo officialInstance = typeof(PaidOrderAcceptanceInstanceOperatorDto)
            .GetProperty(nameof(PaidOrderAcceptanceInstanceOperatorDto.IsOfficialInstance))!;
        await Assert.That(officialInstance.GetCustomAttribute<RequiredMemberAttribute>()).IsNotNull();
    }

    [Test]
    public async Task GuestPaymentWritesAreCapabilityScopedPublicTransactionalAndIdempotent()
    {
        await AssertEndpoint<GuestRegistrationOrderPaymentController, HttpPostAttribute>(
            nameof(GuestRegistrationOrderPaymentController.Start), "guest/{orderId:guid}/payment", RouteNames.StartGuestRegistrationPayment, EndpointClass.PublicTransactional, true);
        await AssertEndpoint<GuestRegistrationOrderPaymentController, HttpPostAttribute>(
            nameof(GuestRegistrationOrderPaymentController.Retry), "guest/{orderId:guid}/payment/retry", RouteNames.RetryGuestRegistrationPayment, EndpointClass.PublicTransactional, true);
        await AssertEndpoint<GuestRegistrationOrderPaymentController, HttpGetAttribute>(
            nameof(GuestRegistrationOrderPaymentController.GetStatus), "guest/{orderId:guid}/payment", RouteNames.GetGuestRegistrationPayment, EndpointClass.Public, false);
        await AssertEndpoint<GuestRegistrationOrderPaymentController, HttpGetAttribute>(
            nameof(GuestRegistrationOrderPaymentController.GetAcceptance), "guest/{orderId:guid}/payment/acceptance", RouteNames.GetGuestPaidOrderAcceptance, EndpointClass.Public, false);
    }

    [Test]
    public async Task AccountAndStudioPaymentReadsStayPrivateAndExplicitlyClassified()
    {
        await AssertEndpoint<AuthenticatedRegistrationOrderPaymentController, HttpPostAttribute>(
            nameof(AuthenticatedRegistrationOrderPaymentController.Start), "{orderId:guid}/payment", RouteNames.StartAuthenticatedRegistrationPayment, EndpointClass.Authenticated, true);
        await AssertEndpoint<AuthenticatedRegistrationOrderPaymentController, HttpPostAttribute>(
            nameof(AuthenticatedRegistrationOrderPaymentController.Retry), "{orderId:guid}/payment/retry", RouteNames.RetryAuthenticatedRegistrationPayment, EndpointClass.Authenticated, true);
        await AssertEndpoint<AuthenticatedRegistrationOrderPaymentController, HttpGetAttribute>(
            nameof(AuthenticatedRegistrationOrderPaymentController.GetStatus), "{orderId:guid}/payment", RouteNames.GetAuthenticatedRegistrationPayment, EndpointClass.Authenticated, false);
        await AssertEndpoint<AuthenticatedRegistrationOrderPaymentController, HttpGetAttribute>(
            nameof(AuthenticatedRegistrationOrderPaymentController.GetAcceptance), "{orderId:guid}/payment/acceptance", RouteNames.GetAuthenticatedPaidOrderAcceptance, EndpointClass.Authenticated, false);
        await AssertEndpoint<StudioRegistrationOrderPaymentController, HttpGetAttribute>(
            nameof(StudioRegistrationOrderPaymentController.GetStatus), "{orderId:guid}/payment/studio", RouteNames.GetStudioRegistrationPayment, EndpointClass.Authenticated, false);
        await AssertEndpoint<AuthenticatedRegistrationOrderPaymentController, HttpPostAttribute>(
            nameof(AuthenticatedRegistrationOrderPaymentController.RequestRefund), "{orderId:guid}/payment/refunds", RouteNames.RequestAuthenticatedRegistrationRefund, EndpointClass.Authenticated, true);
        await AssertEndpoint<AuthenticatedRegistrationOrderPaymentController, HttpPostAttribute>(
            nameof(AuthenticatedRegistrationOrderPaymentController.RespondMaterialChange), "{orderId:guid}/payment/material-change-choice", RouteNames.RespondAuthenticatedRegistrationMaterialChange, EndpointClass.Authenticated, true);
        await AssertEndpoint<StudioRegistrationOrderPaymentController, HttpPostAttribute>(
            nameof(StudioRegistrationOrderPaymentController.CreateRefund), "{orderId:guid}/payment/studio/refunds", RouteNames.CreateStudioRegistrationRefund, EndpointClass.Authenticated, true);
        await AssertEndpoint<RefundCampaignController, HttpGetAttribute>(
            nameof(RefundCampaignController.GetList), null!, RouteNames.GetRefundCampaigns, EndpointClass.Authenticated, false);
        await AssertEndpoint<RefundCampaignController, HttpPostAttribute>(
            nameof(RefundCampaignController.Resume), "{campaignId:guid}/resume", RouteNames.ResumeRefundCampaign, EndpointClass.Authenticated, true);
    }

    private static async Task AssertEndpoint<TController, THttpAttribute>(
        string methodName,
        string template,
        string routeName,
        EndpointClass endpointClass,
        bool requiresIdempotency)
        where THttpAttribute : HttpMethodAttribute
    {
        MethodInfo method = typeof(TController).GetMethod(methodName)!;
        THttpAttribute route = method.GetCustomAttribute<THttpAttribute>()!;
        await Assert.That(route.Template).IsEqualTo(template);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(method.GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(endpointClass);
        await Assert.That(method.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<RequireIdempotencyKeyAttribute>() is not null).IsEqualTo(requiresIdempotency);

        if (endpointClass == EndpointClass.PublicTransactional)
        {
            await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
            await Assert.That(method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
                .IsEqualTo(RateLimitingExtensions.PublicTransactionalPolicy);
        }
        else if (endpointClass == EndpointClass.Authenticated)
        {
            await Assert.That(method.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        }
    }

    private static async Task AssertSecureRequest<TRequest>(string action)
        where TRequest : ISecureRequest
    {
        AuthorizeResourceAttribute? requirement =
            typeof(TRequest).GetCustomAttribute<AuthorizeResourceAttribute>();
        await Assert.That(requirement).IsNotNull();
        await Assert.That(requirement!.Resource).IsEqualTo(ResourceKinds.RegistrationOrder);
        await Assert.That(requirement.Action).IsEqualTo(action);
    }
}
