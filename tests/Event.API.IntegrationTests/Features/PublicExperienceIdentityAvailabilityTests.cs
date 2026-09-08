namespace Event.Api.IntegrationTests.Features;

using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.PublicExperience.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

public sealed class PublicExperienceIdentityAvailabilityTests
{
    [Test]
    public async Task GetSettings_UnavailableIdentityReturnsNonCacheable503Problem()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Any<GetPublicExperienceSettingsQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(new PublicExperienceSettingsDto
            {
                IsAvailable = false,
                UnavailableCode = "tenant_identity_unavailable"
            });
        PublicExperienceController controller = CreateController(mediator);

        ActionResult<PublicExperienceSettingsDto> response =
            await controller.GetSettings(CancellationToken.None);

        ObjectResult result = (ObjectResult)response.Result!;
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        ProblemDetails problem = (ProblemDetails)result.Value!;
        await Assert.That(problem.Extensions["code"]?.ToString())
            .IsEqualTo("tenant_identity_unavailable");
        await Assert.That(controller.Response.Headers.CacheControl.ToString())
            .IsEqualTo("no-store");
    }

    [Test]
    public async Task GetShell_UnavailableIdentityReturnsNonCacheable503Problem()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Any<GetPublicExperienceShellQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(new PublicExperienceShellDto
            {
                IsAvailable = false,
                UnavailableCode = "tenant_identity_unavailable"
            });
        PublicExperienceController controller = CreateController(mediator);

        ActionResult<PublicExperienceShellDto> response =
            await controller.GetShell(CancellationToken.None);

        ObjectResult result = (ObjectResult)response.Result!;
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        ProblemDetails problem = (ProblemDetails)result.Value!;
        await Assert.That(problem.Extensions["code"]?.ToString())
            .IsEqualTo("tenant_identity_unavailable");
        await Assert.That(controller.Response.Headers.CacheControl.ToString())
            .IsEqualTo("no-store");
    }

    private static PublicExperienceController CreateController(IMediator mediator)
    {
        var controller = new PublicExperienceController(
            mediator,
            Substitute.For<ILinkPolicy<EventDiscoveryItemDto>>(),
            Substitute.For<IHateoasLinkGenerator>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        return controller;
    }
}
