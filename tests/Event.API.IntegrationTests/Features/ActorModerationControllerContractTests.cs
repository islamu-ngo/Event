using System.Net;
using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Actor;
using Explore.Application.Features.Actors.Requests.Commands;
using Explore.Application.Features.Actors.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

public sealed class ActorModerationControllerContractTests
{
    private const string BaseUrl = "/api/actor";

    [Test]
    public async Task GlobalModerationRequestDto_ContainsOnlyReasonCode()
    {
        var properties = typeof(GlobalModerationRequestDto).GetProperties();

        await Assert.That(properties.Length).IsEqualTo(1);
        await Assert.That(properties[0].Name).IsEqualTo(nameof(GlobalModerationRequestDto.ReasonCode));
    }

    [Test]
    public async Task ModerationRoutes_DeclareRequiredContractMetadata()
    {
        var expectedRoutes = new Dictionary<string, (string Template, string Name)>
        {
            [nameof(ActorController.SuspendActor)] = (
                "{actorId:guid}/moderation/suspend",
                RouteNames.SuspendActor),
            [nameof(ActorController.ReinstateActor)] = (
                "{actorId:guid}/moderation/reinstate",
                RouteNames.ReinstateActor),
            [nameof(ActorController.SuspendAtprotoIdentity)] = (
                "atproto-identities/{identityId:guid}/moderation/suspend",
                RouteNames.SuspendAtprotoIdentity),
            [nameof(ActorController.ReinstateAtprotoIdentity)] = (
                "atproto-identities/{identityId:guid}/moderation/reinstate",
                RouteNames.ReinstateAtprotoIdentity)
        };

        foreach (var expectedRoute in expectedRoutes)
        {
            var method = typeof(ActorController).GetMethod(expectedRoute.Key)!;
            var post = method.GetCustomAttribute<HttpPostAttribute>();
            var classification = method.GetCustomAttribute<EndpointClassificationAttribute>();
            var rateLimit = method.GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>();
            var responseTypes = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();

            await Assert.That(post).IsNotNull();
            await Assert.That(post!.Template).IsEqualTo(expectedRoute.Value.Template);
            await Assert.That(post.Name).IsEqualTo(expectedRoute.Value.Name);
            await Assert.That(method.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>())
                .IsNotNull();
            await Assert.That(classification?.Class).IsEqualTo(EndpointClass.Authenticated);
            await Assert.That(method.GetCustomAttributes()
                .Any(attribute => attribute.GetType().Name == "EndpointSummaryAttribute")).IsTrue();
            await Assert.That(method.GetCustomAttributes()
                .Any(attribute => attribute.GetType().Name == "EndpointDescriptionAttribute")).IsTrue();
            await Assert.That(method.GetCustomAttribute<ConsumesAttribute>()?.ContentTypes)
                .Contains("application/json");
            await Assert.That(rateLimit?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);

            await Assert.That(responseTypes.Any(response =>
                response.StatusCode == StatusCodes.Status200OK &&
                response.Type == typeof(BaseCommandResponse<Guid>))).IsTrue();
            foreach (var expectedResponse in new[]
                     {
                         (StatusCode: StatusCodes.Status400BadRequest, Type: typeof(ValidationProblemDetails)),
                        (StatusCode: StatusCodes.Status401Unauthorized, Type: typeof(ProblemDetails)),
                        (StatusCode: StatusCodes.Status403Forbidden, Type: typeof(ProblemDetails)),
                        (StatusCode: StatusCodes.Status429TooManyRequests, Type: typeof(ProblemDetails)),
                        (StatusCode: StatusCodes.Status404NotFound, Type: typeof(ProblemDetails)),
                        (StatusCode: StatusCodes.Status409Conflict, Type: typeof(ProblemDetails))
                      })
            {
                await Assert.That(responseTypes.Any(response =>
                    response.StatusCode == expectedResponse.StatusCode &&
                    response.Type == expectedResponse.Type)).IsTrue();
            }
        }
    }

    [Test]
    [Arguments("{0}/moderation/suspend", "actor", "Suspend")]
    [Arguments("{0}/moderation/reinstate", "actor", "Reinstate")]
    [Arguments("atproto-identities/{0}/moderation/suspend", "identity", "Suspend")]
    [Arguments("atproto-identities/{0}/moderation/reinstate", "identity", "Reinstate")]
    public async Task AuthenticatedRoute_DispatchesServerSelectedAction(
        string routeFormat,
        string targetType,
        string expectedAction)
    {
        var handler = new ModerationCapturingHandler();
        var controller = CreateController(handler, handler);
        var targetId = Guid.CreateVersion7();
        var requestDto = new GlobalModerationRequestDto { ReasonCode = "policy-violation" };

        ActionResult<BaseCommandResponse<Guid>> response = routeFormat switch
        {
            "{0}/moderation/suspend" => await controller.SuspendActor(targetId, requestDto),
            "{0}/moderation/reinstate" => await controller.ReinstateActor(targetId, requestDto),
            "atproto-identities/{0}/moderation/suspend" => await controller.SuspendAtprotoIdentity(targetId, requestDto),
            "atproto-identities/{0}/moderation/reinstate" => await controller.ReinstateAtprotoIdentity(targetId, requestDto),
            _ => throw new ArgumentOutOfRangeException(nameof(routeFormat))
        };

        var okResult = response.Result as OkObjectResult;
        await Assert.That(okResult).IsNotNull();
        await Assert.That(okResult!.StatusCode).IsEqualTo(StatusCodes.Status200OK);

        if (targetType == "actor")
        {
            await Assert.That(handler.LastActorCommand).IsNotNull();
            await Assert.That(handler.LastActorCommand!.ActorId).IsEqualTo(targetId);
            await Assert.That(handler.LastActorCommand.Moderation!.Action.ToString()).IsEqualTo(expectedAction);
            await Assert.That(handler.LastActorCommand.Moderation.ReasonCode).IsEqualTo("policy-violation");
        }
        else
        {
            await Assert.That(handler.LastIdentityCommand).IsNotNull();
            await Assert.That(handler.LastIdentityCommand!.AtprotoIdentityId).IsEqualTo(targetId);
            await Assert.That(handler.LastIdentityCommand.Moderation!.Action.ToString()).IsEqualTo(expectedAction);
            await Assert.That(handler.LastIdentityCommand.Moderation.ReasonCode).IsEqualTo("policy-violation");
        }
    }

    [Test]
    public async Task ModerationValidationFailure_ReturnsValidationProblemDetails()
    {
        var handler = new ModerationCapturingHandler
        {
            Response = BaseCommandResponse.Validation(
                ["ReasonCode must not be empty."],
                "Actor moderation failed validation.",
                Guid.CreateVersion7())
        };
        var controller = CreateController(moderateActor: handler);
        var response = await controller.SuspendActor(
            Guid.CreateVersion7(),
            new GlobalModerationRequestDto { ReasonCode = string.Empty });

        var badRequest = response.Result as ObjectResult;
        await Assert.That(badRequest).IsNotNull();
        await Assert.That(badRequest!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        var problem = badRequest.Value as ValidationProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Title).IsEqualTo("Global actor moderation validation failed");
    }

    [Test]
    public async Task UnresolvedApplicationUser_ReturnsAuthenticationRequiredProblemDetails()
    {
        var handler = new ModerationCapturingHandler
        {
            Response = BaseCommandResponse.Authentication<Guid>(
                "Authenticated instance administrator context is required.")
        };
        var controller = CreateController(moderateActor: handler);
        var response = await controller.SuspendActor(
            Guid.CreateVersion7(),
            new GlobalModerationRequestDto { ReasonCode = "policy-violation" });

        var unauthorized = response.Result as ObjectResult;
        await Assert.That(unauthorized).IsNotNull();
        await Assert.That(unauthorized!.StatusCode).IsEqualTo(StatusCodes.Status401Unauthorized);
        var problem = unauthorized.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Title).IsEqualTo("User ID not found in token");
        await Assert.That(problem.Detail).IsEqualTo("Authenticated instance administrator context is required.");
    }

    [Test]
    public async Task InstanceAdminFailure_ReturnsForbiddenProblemDetails()
    {
        var handler = new ModerationCapturingHandler
        {
            Response = BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can moderate global actors.")
        };
        var controller = CreateController(moderateActor: handler);
        var response = await controller.SuspendActor(
            Guid.CreateVersion7(),
            new GlobalModerationRequestDto { ReasonCode = "policy-violation" });

        var forbidden = response.Result as ObjectResult;
        await Assert.That(forbidden).IsNotNull();
        await Assert.That(forbidden!.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        var problem = forbidden.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Title).IsEqualTo("Forbidden");
        await Assert.That(problem.Detail).IsEqualTo("Only instance administrators can moderate global actors.");
    }

    private static ActorController CreateController(
        ICommandHandler<ModerateActorCommand, BaseCommandResponse<Guid>>? moderateActor = null,
        ICommandHandler<ModerateAtprotoIdentityCommand, BaseCommandResponse<Guid>>? moderateIdentity = null)
    {
        return new ActorController(
            Substitute.For<IQueryHandler<GetActorListRequest, PaginatedResult<ActorListDto>>>(),
            Substitute.For<IQueryHandler<GetActorDetailsRequest, ActorDto?>>(),
            Substitute.For<IQueryHandler<GetActorByDidRequest, ActorDto?>>(),
            Substitute.For<IQueryHandler<GetActorsByTenantRequest, List<ActorListDto>>>(),
            moderateActor ?? Substitute.For<ICommandHandler<ModerateActorCommand, BaseCommandResponse<Guid>>>(),
            moderateIdentity ?? Substitute.For<ICommandHandler<ModerateAtprotoIdentityCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<IResourceAssembler<ActorDto, ActorListDto>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private sealed class ModerationCapturingHandler :
        ICommandHandler<ModerateActorCommand, BaseCommandResponse<Guid>>,
        ICommandHandler<ModerateAtprotoIdentityCommand, BaseCommandResponse<Guid>>
    {
        public BaseCommandResponse<Guid> Response { get; init; } =
            BaseCommandResponse.Success(Guid.CreateVersion7(), "Moderation updated.");

        public ModerateActorCommand? LastActorCommand { get; private set; }
        public ModerateAtprotoIdentityCommand? LastIdentityCommand { get; private set; }

        public Task<BaseCommandResponse<Guid>> ExecuteAsync(
            ModerateActorCommand command,
            CancellationToken cancellationToken = default)
        {
            LastActorCommand = command;
            return Task.FromResult(Response);
        }

        public Task<BaseCommandResponse<Guid>> ExecuteAsync(
            ModerateAtprotoIdentityCommand command,
            CancellationToken cancellationToken = default)
        {
            LastIdentityCommand = command;
            return Task.FromResult(Response);
        }
    }
}
