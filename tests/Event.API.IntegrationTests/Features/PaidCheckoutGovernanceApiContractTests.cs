using System.Reflection;
using Explore.API.Controllers;
using Explore.API.Filters;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Payments;
using Explore.Application.Features.PaidCheckoutGovernance.Commands;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class PaidCheckoutGovernanceApiContractTests
{
    [Test]
    public async Task BrowserMutationDtosCannotSetOfficialActivationOperatorOrCredentialFacts()
    {
        string[] propertyNames = typeof(PaidCheckoutSaleControlMutationDto).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(PaidCheckoutSaleControlMutationDto).Namespace &&
                           (type.Name.Contains("PaidCheckout", StringComparison.Ordinal) || type.Name.Contains("PaidSales", StringComparison.Ordinal)))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property.Name)
            .ToArray();

        await Assert.That(propertyNames).DoesNotContain("IsOfficialInstance");
        await Assert.That(propertyNames).DoesNotContain("ActivationStatus");
        await Assert.That(propertyNames).DoesNotContain("OperatorId");
        await Assert.That(propertyNames).DoesNotContain("CredentialOwner");
        await Assert.That(propertyNames).DoesNotContain("ExternalAccountId");
    }

    [Test]
    public async Task SaleControlGetEmitsOnlyCurrentAuthorizedTransitionAndEveryEndpointIsNoStore()
    {
        Guid tenantId = Guid.CreateVersion7();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetPaidCheckoutSaleControlQuery>(), Arg.Any<CancellationToken>()).Returns(new PaidCheckoutSaleControlDto
        {
            TenantId = tenantId,
            IsStopped = true,
            ResumeReviewPending = false,
            Version = 2
        });
        var authorization = Substitute.For<IAuthorizationProvider>();
        authorization.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationDecision.Allow(AuthorizationProviderMetadata.Runtime));
        var controller = new PaidCheckoutGovernanceController(mediator, authorization)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            Url = RouteUrl()
        };

        ActionResult<HalResource<PaidCheckoutSaleControlDto>> response =
            await controller.GetSaleControl(tenantId, null, CancellationToken.None);
        var resource = (HalResource<PaidCheckoutSaleControlDto>)((OkObjectResult)response.Result!).Value!;

        await Assert.That(resource.Links.Keys).Contains(LinkRelations.RequestPaidSalesResume);
        await Assert.That(resource.Links.Keys).DoesNotContain(LinkRelations.StopPaidSales);
        await Assert.That(typeof(PaidCheckoutGovernanceController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
            .All(method => method.GetCustomAttribute<PrivateNoStoreAttribute>() is not null)).IsTrue();
    }

    private static IUrlHelper RouteUrl()
    {
        var url = Substitute.For<IUrlHelper>();
        url.Link(Arg.Any<string>(), Arg.Any<object>()).Returns(call => $"/api/routes/{call.ArgAt<string>(0)}");
        return url;
    }
}
