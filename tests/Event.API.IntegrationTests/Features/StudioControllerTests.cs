using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Studio;
using Explore.Application.Features.Studio.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class StudioControllerTests
{
    [Test]
    public async Task GetContext_ForwardsActorHintAndReturnsPrivateHalResource()
    {
        var actorId = Guid.CreateVersion7();
        var context = new StudioContextDto { SelectedActorId = actorId };
        var mediator = Substitute.For<IMediator>();
        var assembler = Substitute.For<IResourceAssembler<StudioContextDto, StudioContextDto>>();
        mediator.Send(Arg.Any<GetStudioContextQuery>(), Arg.Any<CancellationToken>()).Returns(context);
        assembler.ToResource(Arg.Any<StudioContextDto>(), Arg.Any<HttpContext>())
            .Returns(new HalResource<StudioContextDto>(context));
        var controller = new StudioController(mediator, assembler)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        ActionResult<HalResource<StudioContextDto>> response = await controller.GetContext(actorId);

        var result = (ObjectResult)response.Result!;
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(result.ContentTypes).Contains(HateoasConstants.HalJsonMediaType);
        await mediator.Received(1).Send(
            Arg.Is<GetStudioContextQuery>(query => query.ActorId == actorId),
            Arg.Any<CancellationToken>());
    }
}
